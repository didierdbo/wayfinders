using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace Wayfinders.Client.Ml;

/// <summary>
/// Phase 6 L2 deliverable: MiniLM-L6-v2 encoder on the C# side of the
/// Wayfinders client.
///
/// <para>
/// Tokenizes an English prose string with the BERT-uncased WordPiece
/// tokenizer (NFD + accent strip + lowercase, per Tess M1 handoff §3),
/// runs the encoder ONNX session shipped at
/// <c>client/ml/encoder.onnx</c>, and returns the 384-dim mean-pooled,
/// L2-normalised sentence embedding as a <see cref="float"/> array
/// (the encoder ONNX outputs <c>float16</c>; this class casts to
/// <c>float</c> before return -- matches the head's input contract).
/// </para>
///
/// <para>
/// <b>Why a plain class, not an autoload.</b> L2 is encoder-only. The
/// scene graph does not call the encoder at L2; only test code does.
/// The eventual <c>OnnxClassifier</c> (L4) will own the lifecycle and
/// bridge to the <see cref="IClassifier"/> autoload. Keeping
/// <see cref="MiniLmEncoder"/> as a plain class keeps the L2 surface
/// minimal and the L4 swap clean.
/// </para>
///
/// <para>
/// <b>Why <see cref="IDisposable"/>.</b> <see cref="InferenceSession"/>
/// holds native memory; C# GC does not reclaim it -- the same lesson
/// Godot teaches with <c>Node.QueueFree</c>. Forgetting to dispose leaks
/// across test runs. The test harness wraps every instance in a
/// <c>using</c>; production code at L4 will own a single long-lived
/// instance bound to <c>OnnxClassifier</c>'s lifetime.
/// </para>
///
/// <para>
/// <b>Synchronous API at L2.</b> No <c>async</c> here -- the encoder is
/// pure CPU work, no I/O once the session is loaded. Tests call it
/// synchronously. The async surface lives one layer up in
/// <see cref="IClassifier"/>; L4 will await this from a Task wrapper.
/// </para>
/// </summary>
public sealed class MiniLmEncoder : IDisposable
{
    private const int ExpectedEmbeddingDim = 384;
    private const int MaxSeqLen = 256;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private bool _disposed;

    /// <summary>
    /// Construct the encoder. Both files are required at the given
    /// paths; missing files throw at construction time so failures
    /// surface during scene-load, not on first inference.
    /// </summary>
    /// <param name="onnxPath">
    /// Absolute path to <c>minilm-l6-v2-fp16.onnx</c>. Typically
    /// <c>res://ml/encoder.onnx</c> resolved through Godot's
    /// <c>ProjectSettings.GlobalizePath</c>; for tests, just the file
    /// system path.
    /// </param>
    /// <param name="vocabPath">
    /// Absolute path to <c>vocab.txt</c> from the
    /// sentence-transformers/all-MiniLM-L6-v2 HuggingFace snapshot
    /// (30522 lines, the standard BERT-uncased vocabulary).
    /// </param>
    public MiniLmEncoder(string onnxPath, string vocabPath)
    {
        if (!File.Exists(onnxPath))
        {
            throw new FileNotFoundException(
                $"Encoder ONNX not found at '{onnxPath}'. " +
                "Fetch from the GitHub release asset and place under client/ml/.",
                onnxPath);
        }
        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException(
                $"vocab.txt not found at '{vocabPath}'. " +
                "Copy from the sentence-transformers HF snapshot to client/ml/vocab.txt.",
                vocabPath);
        }

        // CPUExecutionProvider only at L2 -- the A2000 CUDA EP would add a
        // CUDA-runtime DLL chain we do not need yet. Tess handoff §4 says
        // CPU is ~5-15 ms per encode warm, well within budget.
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        _session = new InferenceSession(onnxPath, sessionOptions);

        // BERT-uncased + NFD + accent-strip pipeline.
        // RemoveNonSpacingMarks=true is the Microsoft.ML.Tokenizers flag for
        // the standard BERT NFD-then-strip-Mn behaviour. LowerCaseBeforeTokenization
        // matches the Python `do_lower_case=True` default for bert-base-uncased.
        // ApplyBasicTokenization=true is also required (it is the punctuation
        // splitter + whitespace cleaner that runs before WordPiece).
        var bertOptions = new BertOptions
        {
            LowerCaseBeforeTokenization = true,
            ApplyBasicTokenization = true,
            RemoveNonSpacingMarks = true,
            SplitOnSpecialTokens = true,
            IndividuallyTokenizeCjk = false,
        };
        _tokenizer = BertTokenizer.Create(vocabPath, bertOptions);
    }

    /// <summary>
    /// Encode a single prose string into the 384-dim sentence embedding.
    ///
    /// <para>
    /// Pipeline: tokenize -> int64 input_ids/attention_mask/token_type_ids
    /// -> ONNX encoder session -> float16 (1, 384) -> cast to float[].
    /// The encoder graph already applies mean-pooling and L2-normalisation
    /// internally; the C# side does not re-normalise.
    /// </para>
    /// </summary>
    /// <param name="text">Non-null English prose. Empty string is allowed
    /// but produces a degenerate (CLS-only) embedding.</param>
    /// <returns>Newly-allocated <see cref="float"/> array of length 384.</returns>
    public float[] Encode(string text)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MiniLmEncoder));
        }
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<int> ids = TokenizeForTest(text);
        int seqLen = ids.Count;

        // Build the three int64 tensors. token_type_ids is all-zeros for
        // single-sentence input but MUST be present (Tess handoff §6 step 2).
        // attention_mask is all-1 because we do not pad in single-string Encode().
        var inputIds = new long[seqLen];
        var attentionMask = new long[seqLen];
        var tokenTypeIds = new long[seqLen]; // all zeros by default

        for (int i = 0; i < seqLen; i++)
        {
            inputIds[i] = ids[i];
            attentionMask[i] = 1L;
            // tokenTypeIds[i] = 0L  -- already zero from new long[]
        }

        long[] shape = new long[] { 1, seqLen };
        using var inputIdsTensor = OrtValue.CreateTensorValueFromMemory(inputIds, shape);
        using var attentionMaskTensor = OrtValue.CreateTensorValueFromMemory(attentionMask, shape);
        using var tokenTypeIdsTensor = OrtValue.CreateTensorValueFromMemory(tokenTypeIds, shape);

        var inputs = new Dictionary<string, OrtValue>
        {
            ["input_ids"] = inputIdsTensor,
            ["attention_mask"] = attentionMaskTensor,
            ["token_type_ids"] = tokenTypeIdsTensor,
        };

        // Run inference. Output is "sentence_embedding" with dtype float16
        // and shape (1, 384).
        using var runOptions = new RunOptions();
        using var outputs = _session.Run(runOptions, inputs, new[] { "sentence_embedding" });

        var sentenceEmbedding = outputs[0];
        var typeAndShape = sentenceEmbedding.GetTensorTypeAndShape();
        var dims = typeAndShape.Shape;
        if (dims.Length != 2 || dims[1] != ExpectedEmbeddingDim)
        {
            throw new InvalidOperationException(
                $"Unexpected encoder output shape: [{string.Join(", ", dims)}], " +
                $"expected [1, {ExpectedEmbeddingDim}].");
        }

        // Cast fp16 -> fp32. ONNX Runtime ships its own Float16 wrapper
        // (Microsoft.ML.OnnxRuntime.Float16) -- NOT the .NET 5+ System.Half --
        // because ORT's binary layout is fixed and predates Half. The struct
        // exposes a ToFloat() method for the upcast.
        ReadOnlySpan<Float16> fp16Span = sentenceEmbedding.GetTensorDataAsSpan<Float16>();
        if (fp16Span.Length != ExpectedEmbeddingDim)
        {
            throw new InvalidOperationException(
                $"Unexpected encoder output element count: {fp16Span.Length}, " +
                $"expected {ExpectedEmbeddingDim}.");
        }

        var result = new float[ExpectedEmbeddingDim];
        for (int i = 0; i < ExpectedEmbeddingDim; i++)
        {
            result[i] = fp16Span[i].ToFloat();
        }
        return result;
    }

    /// <summary>
    /// For test inspection: return the integer token IDs for a given input.
    /// Production callers do not need this; tests use it to validate NFD +
    /// accent-strip parity vs the Python reference at the tokenizer level.
    /// </summary>
    /// <remarks>
    /// Marked <c>internal</c> so it is callable from the in-tree test
    /// project (which compiles MiniLmEncoder.cs directly) but not from
    /// external consumers of the client assembly.
    /// </remarks>
    internal IReadOnlyList<int> TokenizeForTest(string text)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MiniLmEncoder));
        ArgumentNullException.ThrowIfNull(text);
        // EncodeToIds(text, addSpecialTokens, considerPreTokenization, considerNormalization)
        // addSpecialTokens=true        -> prepend [CLS] (101), append [SEP] (102)
        // considerPreTokenization=true -> apply BasicTokenizer (whitespace + punctuation split)
        // considerNormalization=true   -> apply lowercase + NFD + accent strip
        IReadOnlyList<int> ids = _tokenizer.EncodeToIds(
            text,
            addSpecialTokens: true,
            considerPreTokenization: true,
            considerNormalization: true);
        if (ids.Count > MaxSeqLen)
        {
            ids = ids.Take(MaxSeqLen).ToList();
        }
        return ids;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }
}
