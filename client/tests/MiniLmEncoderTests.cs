using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Wayfinders.Client.Ml;
using Xunit.Abstractions;

namespace Wayfinders.Client.Tests;

/// <summary>
/// Phase 6 L2 parity tests. Validates that the C#
/// <see cref="MiniLmEncoder"/> reproduces the Python torch+ONNX-Runtime
/// reference embeddings within <c>atol=1e-3</c> on the first two cases
/// of the canonical parity fixture (Tess M1 v0.1 handoff §5.1).
///
/// <para>
/// Reference embeddings are produced by
/// <c>scripts/dump_parity_embeddings.py</c> from the same encoder ONNX
/// (sha256 pinned in the JSON header) and committed to
/// <c>parity/encoder_embeddings_v0.1.json</c>. If the encoder ONNX ever
/// changes, the dump script must be re-run.
/// </para>
///
/// <para>
/// <b>Tolerance choice.</b> The contract gate (Tess M1 handoff §4, ONNX
/// Export Contract §6) is element-wise <c>max_abs_diff &lt;= 1e-3</c>
/// on encoder output. Cosine and L2-norm sanity checks are softer floors
/// chosen to flag direction drift without false-failing on the natural
/// fp16 numerical noise floor of ~1e-4 per element across 384 dims.
/// </para>
/// </summary>
public sealed class MiniLmEncoderTests : IDisposable
{
    private const string FixtureRelativePath = "parity/encoder_embeddings_v0.1.json";
    private const string EncoderOnnxRelativePath = "client/ml/encoder.onnx";
    private const string VocabRelativePath = "client/ml/vocab.txt";

    /// <summary>
    /// Element-wise atol per ONNX Export Contract §6. This is the hard
    /// gate -- failing this means C# inference does not match Python.
    /// </summary>
    private const float Atol = 1e-3f;

    /// <summary>
    /// L2-norm tolerance. The encoder graph internally L2-normalises,
    /// but fp16 quantisation drifts the norm by ~5e-4 per the dump-script
    /// trace (Python norms ranged 0.9996-1.0003 across the 6 reference
    /// vectors). 1e-3 is loose enough to absorb that drift without hiding
    /// a real bug.
    /// </summary>
    private const float NormTolerance = 1e-3f;

    /// <summary>
    /// Cosine-similarity floor. With max_abs_diff at 1e-3 across 384
    /// dimensions and L2-normed vectors, the natural cosine floor is
    /// roughly 1 - 0.5 * sum(diff^2). Using 0.998 leaves a safety margin
    /// while still catching direction-flip bugs (a tokenisation error
    /// usually drops cos below 0.95). NOT the contract gate -- atol is.
    /// </summary>
    private const float CosineMin = 0.998f;

    private readonly string _repoRoot;
    private readonly MiniLmEncoder _encoder;
    private readonly JsonElement _fixture;
    private readonly ITestOutputHelper _output;

    public MiniLmEncoderTests(ITestOutputHelper output)
    {
        _output = output;
        _repoRoot = FindRepoRoot();

        var encoderPath = Path.Combine(_repoRoot, EncoderOnnxRelativePath);
        var vocabPath = Path.Combine(_repoRoot, VocabRelativePath);
        _encoder = new MiniLmEncoder(encoderPath, vocabPath);

        var fixturePath = Path.Combine(_repoRoot, FixtureRelativePath);
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Parity reference fixture not found at '{fixturePath}'. " +
                "Run `uv run python scripts/dump_parity_embeddings.py` to generate.",
                fixturePath);
        }
        _fixture = JsonDocument.Parse(File.ReadAllText(fixturePath)).RootElement;
    }

    public void Dispose() => _encoder.Dispose();

    [Fact]
    public void Encode_KiraStealthRidge_MatchesPythonReference()
    {
        AssertParityForCase("kira_stealth_ridge");
    }

    [Fact]
    public void Encode_KiraStealthFestival_MatchesPythonReference()
    {
        AssertParityForCase("kira_stealth_festival");
    }

    [Fact]
    public void Tokenize_FrenchAccents_MatchesPythonTokenIds()
    {
        // Verifies the BERT-uncased NFD + accent-strip path is exact.
        // Python reference (sentence-transformers, bert-base-uncased) maps
        // the accented strings to identical token IDs as the ASCII
        // equivalents. C# must do the same -- otherwise prose with French
        // names or proper nouns would silently shift the embedding.
        var refRecords = _fixture.GetProperty("accent_token_ids_reference");
        Assert.True(refRecords.GetArrayLength() >= 4,
            "fixture missing accent reference records");

        foreach (var rec in refRecords.EnumerateArray())
        {
            var text = rec.GetProperty("text").GetString()!;
            var expectedIds = rec.GetProperty("token_ids")
                .EnumerateArray()
                .Select(e => e.GetInt32())
                .ToArray();
            var actualIds = _encoder.TokenizeForTest(text).ToArray();

            _output.WriteLine($"text: {text}");
            _output.WriteLine($"  expected ({expectedIds.Length}): [{string.Join(",", expectedIds.Take(12))}{(expectedIds.Length > 12 ? ",..." : "")}]");
            _output.WriteLine($"  actual   ({actualIds.Length}): [{string.Join(",", actualIds.Take(12))}{(actualIds.Length > 12 ? ",..." : "")}]");

            Assert.Equal(expectedIds, actualIds);
        }
    }

    private void AssertParityForCase(string caseId)
    {
        var embeddings = _fixture.GetProperty("embeddings");
        var caseRecords = embeddings
            .EnumerateArray()
            .Where(rec => rec.GetProperty("id").GetString() == caseId)
            .ToList();
        Assert.Equal(3, caseRecords.Count); // character, action, context

        // Run all three docs first, accumulate failures, then assert.
        // This keeps the test output informative even when one doc fails.
        var failures = new List<string>();
        foreach (var rec in caseRecords)
        {
            var doc = rec.GetProperty("doc").GetString()!;
            var prose = rec.GetProperty("prose").GetString()!;
            var expectedTokenCount = rec.GetProperty("token_count").GetInt32();
            var expected = rec.GetProperty("embedding")
                .EnumerateArray()
                .Select(e => (float)e.GetDouble())
                .ToArray();
            Assert.Equal(384, expected.Length);

            // Sanity: tokenizer agrees on token count before encoder runs.
            // Mismatch here is an immediate red flag (NFD/accent path bug,
            // pre-tokenizer split bug, special-token bug).
            var tokenIds = _encoder.TokenizeForTest(prose);
            if (tokenIds.Count != expectedTokenCount)
            {
                failures.Add(
                    $"{caseId}/{doc}: token_count={tokenIds.Count}, expected={expectedTokenCount}");
                continue;
            }

            var actual = _encoder.Encode(prose);
            Assert.Equal(384, actual.Length);

            // Element-wise diff stats.
            float maxAbsDiff = 0f;
            for (int i = 0; i < 384; i++)
            {
                var d = MathF.Abs(actual[i] - expected[i]);
                if (d > maxAbsDiff) maxAbsDiff = d;
            }

            // Cosine similarity (both L2-normalised so cos == dot).
            float cos = 0f;
            for (int i = 0; i < 384; i++) cos += actual[i] * expected[i];

            // L2 norm of actual.
            float norm = 0f;
            for (int i = 0; i < 384; i++) norm += actual[i] * actual[i];
            norm = MathF.Sqrt(norm);

            _output.WriteLine(
                $"{caseId}/{doc}: max_abs_diff={maxAbsDiff:E3} cos={cos:F6} norm={norm:F6} tokens={tokenIds.Count}");

            if (maxAbsDiff > Atol)
                failures.Add($"{caseId}/{doc}: max_abs_diff={maxAbsDiff:E3} > atol={Atol:E1}");
            if (cos < CosineMin)
                failures.Add($"{caseId}/{doc}: cosine={cos:F6} < floor={CosineMin}");
            if (MathF.Abs(norm - 1.0f) > NormTolerance)
                failures.Add($"{caseId}/{doc}: |norm - 1.0|={MathF.Abs(norm - 1.0f):E3} > tol={NormTolerance:E1}");
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join("\n", failures));
        }
    }

    /// <summary>
    /// Walk up from the test assembly directory to find the repo root,
    /// identified by the presence of <c>client/Wayfinders.Client.sln</c>.
    /// Avoids hard-coded paths and works under both `dotnet test` and CI.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "client", "Wayfinders.Client.sln")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir || parent == null) break;
            dir = parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (looking for client/Wayfinders.Client.sln). " +
            $"Started from {AppContext.BaseDirectory}.");
    }
}
