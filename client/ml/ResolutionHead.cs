using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;

namespace Wayfinders.Client.Ml;

/// <summary>
/// Phase 6 L3 deliverable: the resolution-head MLP loaded as a standalone
/// ONNX session on the C# side of the Wayfinders client.
///
/// <para>
/// <b>Input contract.</b> Three <c>(batch=1, 384) float32</c> tensors named
/// <c>char_vec</c>, <c>action_vec</c>, <c>context_vec</c>. These are the
/// L2-normalised mean-pooled MiniLM embeddings produced by
/// <see cref="MiniLmEncoder"/> (or, in tests, by the Python reference dump).
/// The cos similarity between <c>char_vec</c> and <c>action_vec</c> and the
/// concatenation that produces the internal 1153-dim representation are both
/// computed <i>inside</i> the ONNX graph, so the C# caller never sees them.
/// One contract, three vectors -- that's it.
/// </para>
///
/// <para>
/// <b>Output contract.</b> A single <c>(batch=1, 1) float32</c> tensor named
/// <c>delta</c> in the range <c>[-5, +5]</c>. The bound is enforced by an
/// in-graph <c>tanh * 5</c> on the final layer; the C# side does not clamp,
/// because clamping here would mask a contract bug rather than surface it.
/// </para>
///
/// <para>
/// <b>Why a plain class, not an autoload.</b> Same reasoning as L2's
/// <see cref="MiniLmEncoder"/>: L4 will compose encoder + head into the
/// <see cref="IClassifier"/> implementation that owns lifecycle and bridges
/// to the autoload. Keeping the head a plain class keeps the L3 surface
/// minimal and the L4 swap clean.
/// </para>
///
/// <para>
/// <b>Why <see cref="IDisposable"/>.</b> <see cref="InferenceSession"/> wraps
/// native ORT memory; the .NET GC does not reclaim native handles on its own
/// schedule. xUnit reuses one process across many test methods, so leaking a
/// session per test would eventually surface as OS file-handle pressure that
/// looks like flakiness. Same lesson Godot teaches with <c>Node.QueueFree</c>:
/// when a managed runtime bridges to native code, <i>who frees it</i> is your
/// job, not the runtime's.
/// </para>
///
/// <para>
/// <b>Synchronous API at L3.</b> Pure CPU work, sub-millisecond per call on
/// the head's tiny MLP (1153 -> 256 -> 64 -> 1). The async surface lives one
/// layer up at <see cref="IClassifier"/>; L4 wraps this in a Task.
/// </para>
/// </summary>
public sealed class ResolutionHead : IDisposable
{
    private const int EmbedDim = 384;

    private readonly InferenceSession _session;
    private bool _disposed;

    /// <summary>
    /// Construct the head. The ONNX file is required at the given path;
    /// missing files throw at construction time so failures surface during
    /// scene-load (or test setup), not on first inference.
    /// </summary>
    /// <param name="onnxPath">
    /// Absolute path to <c>resolution-head-v0.1.onnx</c>. For L3 tests this
    /// resolves under <c>artifacts/onnx/</c>; L4 will decide whether to copy
    /// the head into <c>client/ml/</c> alongside the encoder.
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="onnxPath"/> does not exist.
    /// </exception>
    public ResolutionHead(string onnxPath)
    {
        if (!File.Exists(onnxPath))
        {
            throw new FileNotFoundException(
                $"Resolution-head ONNX not found at '{onnxPath}'. " +
                "Run `uv run python -m wayfinders.ml.export_head` to regenerate.",
                onnxPath);
        }

        // CPUExecutionProvider only at L3 -- the head MLP is sub-millisecond
        // on CPU and the A2000 CUDA EP would force a CUDA-runtime DLL chain
        // we still don't need at this layer. ORT_ENABLE_ALL turns on every
        // graph optimisation pass (constant folding, redundant-node removal,
        // layout reordering). Sequential execution mode matches L2.
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        _session = new InferenceSession(onnxPath, sessionOptions);
    }

    /// <summary>
    /// Run a single inference. Each input must be exactly 384 floats; the
    /// graph reshapes them to <c>(1, 384)</c> internally.
    /// </summary>
    /// <param name="charVec">L2-normalised character embedding, length 384.</param>
    /// <param name="actionVec">L2-normalised action embedding, length 384.</param>
    /// <param name="contextVec">L2-normalised context embedding, length 384.</param>
    /// <returns>
    /// The scalar &#916; in <c>[-5, +5]</c> produced by the head's tanh*5 output.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown if <see cref="Dispose"/> has been called.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any input is null or not exactly <c>EmbedDim</c> (384) floats.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the ONNX graph returns an unexpected output shape (a contract
    /// bug, not a caller bug).
    /// </exception>
    public float RunHead(float[] charVec, float[] actionVec, float[] contextVec)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ResolutionHead));
        }
        ValidateEmbedding(charVec, nameof(charVec));
        ValidateEmbedding(actionVec, nameof(actionVec));
        ValidateEmbedding(contextVec, nameof(contextVec));

        long[] shape = new long[] { 1L, EmbedDim };
        using var charTensor = OrtValue.CreateTensorValueFromMemory(charVec, shape);
        using var actionTensor = OrtValue.CreateTensorValueFromMemory(actionVec, shape);
        using var contextTensor = OrtValue.CreateTensorValueFromMemory(contextVec, shape);

        // Named keys, not positional. The cos similarity inside the graph is
        // computed between char_vec and action_vec specifically -- swapping
        // any pair would silently produce a wrong delta, so we lean on the
        // names rather than argument order to stay safe.
        var inputs = new Dictionary<string, OrtValue>
        {
            ["char_vec"] = charTensor,
            ["action_vec"] = actionTensor,
            ["context_vec"] = contextTensor,
        };

        using var runOptions = new RunOptions();
        using var outputs = _session.Run(runOptions, inputs, new[] { "delta" });

        var deltaTensor = outputs[0];
        var dims = deltaTensor.GetTensorTypeAndShape().Shape;
        if (dims.Length != 2 || dims[0] != 1 || dims[1] != 1)
        {
            throw new InvalidOperationException(
                $"Unexpected head output shape: [{string.Join(", ", dims)}], expected [1, 1].");
        }

        ReadOnlySpan<float> outSpan = deltaTensor.GetTensorDataAsSpan<float>();
        if (outSpan.Length != 1)
        {
            throw new InvalidOperationException(
                $"Unexpected head output element count: {outSpan.Length}, expected 1.");
        }
        return outSpan[0];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }

    private static void ValidateEmbedding(float[] vec, string paramName)
    {
        if (vec is null)
        {
            throw new ArgumentException($"{paramName} must not be null.", paramName);
        }
        if (vec.Length != EmbedDim)
        {
            throw new ArgumentException(
                $"{paramName} must have length {EmbedDim}, got {vec.Length}.",
                paramName);
        }
    }
}
