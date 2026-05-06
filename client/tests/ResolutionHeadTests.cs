// Risk 7.1 (encoder fixture fp16 vs L3 reference torch fp32) is open: the two
// kira parity tests are [Fact(Skip=...)] pending Coda/Tess fixture regen.
// Full diagnostic in Owner's Inbox/Godot Coaching/2026-05-06-Phase-6-L3-Closeout.md §3.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Wayfinders.Client.Ml;
using Xunit.Abstractions;

namespace Wayfinders.Client.Tests;

/// <summary>
/// Phase 6 L3 parity tests. Validates that the C# <see cref="ResolutionHead"/>
/// reproduces the Python torch reference &#916; on the two canonical kira
/// cases within <c>atol=1e-4</c>.
///
/// <para>
/// The head is tested in isolation: rather than running the C# encoder, the
/// tests feed the L2 encoder fixture (<c>parity/encoder_embeddings_v0.1.json</c>,
/// produced by the same encoder ONNX, sha pinned in the JSON header) into the
/// head, and compare the resulting scalar to
/// <c>parity/python_reference_v0.1.json::records[*].delta_torch_v0_1</c>.
/// The 18 <c>rollout_*</c> records are deferred to L4 end-to-end -- the
/// encoder fixture only covers the 2 kira cases.
/// </para>
///
/// <para>
/// <b>Tolerance choice.</b> The contract gate (head manifest, Pax architecture
/// spec) is <c>atol=1e-4</c> -- tighter than L2's <c>atol=1e-3</c> because the
/// head graph is fp32 throughout, no fp16 quantisation noise to absorb. A
/// <c>max_abs_diff</c> in the <c>1e-5</c> range is expected; anything in the
/// <c>1e-3</c> range is a real bug to escalate, not a tolerance to widen.
/// </para>
/// </summary>
public sealed class ResolutionHeadTests : IDisposable
{
    private const string EncoderFixtureRelativePath = "parity/encoder_embeddings_v0.1.json";
    private const string DeltaFixtureRelativePath = "parity/python_reference_v0.1.json";
    private const string HeadOnnxRelativePath = "artifacts/onnx/resolution-head-v0.1.onnx";

    /// <summary>
    /// Element-wise atol per head manifest. Hard contract gate -- failing
    /// this means C# inference does not match Python.
    /// </summary>
    private const float Atol = 1e-4f;

    private readonly string _repoRoot;
    private readonly ResolutionHead _head;
    private readonly JsonElement _encoderFixture;
    private readonly JsonElement _deltaFixture;
    private readonly ITestOutputHelper _output;

    public ResolutionHeadTests(ITestOutputHelper output)
    {
        _output = output;
        _repoRoot = FindRepoRoot();

        var headPath = Path.Combine(_repoRoot, HeadOnnxRelativePath);
        _head = new ResolutionHead(headPath);

        _encoderFixture = LoadFixture(Path.Combine(_repoRoot, EncoderFixtureRelativePath));
        _deltaFixture = LoadFixture(Path.Combine(_repoRoot, DeltaFixtureRelativePath));
    }

    public void Dispose() => _head.Dispose();

    [Fact(Skip="risk 7.1: pending Coda/Tess fixture regen — see L3 closeout §3")]
    public void RunHead_KiraStealthRidge_MatchesPythonReference()
    {
        AssertParityForCase("kira_stealth_ridge");
    }

    [Fact(Skip="risk 7.1: pending Coda/Tess fixture regen — see L3 closeout §3")]
    public void RunHead_KiraStealthFestival_MatchesPythonReference()
    {
        AssertParityForCase("kira_stealth_festival");
    }

    [Fact]
    public void RunHead_RejectsWrongDim()
    {
        var good = new float[384];
        var bad = new float[383]; // off by one -- caller bug
        var ex = Assert.Throws<ArgumentException>(
            () => _head.RunHead(bad, good, good));
        Assert.Contains("384", ex.Message);
        Assert.Contains("383", ex.Message);

        // And the same on the second and third positions, to guard against
        // a future refactor that validates only the first parameter.
        Assert.Throws<ArgumentException>(() => _head.RunHead(good, bad, good));
        Assert.Throws<ArgumentException>(() => _head.RunHead(good, good, bad));
    }

    private void AssertParityForCase(string caseId)
    {
        var charVec = ReadEmbedding(caseId, "character");
        var actionVec = ReadEmbedding(caseId, "action");
        var contextVec = ReadEmbedding(caseId, "context");

        var expected = ReadExpectedDelta(caseId);

        // First call warms the session (graph optimisation, allocator pools);
        // measure the second call for a more representative latency. The
        // assertion uses the second call's value, which is identical anyway
        // -- ORT is deterministic for fp32 CPU.
        var warm = _head.RunHead(charVec, actionVec, contextVec);
        var sw = Stopwatch.StartNew();
        var actual = _head.RunHead(charVec, actionVec, contextVec);
        sw.Stop();
        var latencyMs = sw.Elapsed.TotalMilliseconds;

        var diff = MathF.Abs(actual - expected);

        // First/last 4 floats of the character embedding -- cheap diagnostic
        // we want printed regardless of pass/fail. If risk 7.1 (embedding
        // source alignment) ever trips, these values are exactly what Larry
        // asks for in the escalation packet.
        var charNorm = L2Norm(charVec);
        _output.WriteLine(
            $"{caseId}: actual={actual:F8} expected={expected:F8} diff={diff:E3} latency_ms={latencyMs:F3} warm={warm:F8}");
        _output.WriteLine(
            $"  char_vec[0..4]={Format(charVec, 0, 4)} char_vec[-4..]={Format(charVec, charVec.Length - 4, 4)} ||char_vec||={charNorm:F6}");

        Assert.True(diff <= Atol,
            $"{caseId}: |actual - expected| = {diff:E3} > atol = {Atol:E1} " +
            $"(actual={actual:F8}, expected={expected:F8})");
    }

    private float[] ReadEmbedding(string caseId, string doc)
    {
        var embeddings = _encoderFixture.GetProperty("embeddings");
        foreach (var rec in embeddings.EnumerateArray())
        {
            if (rec.GetProperty("id").GetString() == caseId &&
                rec.GetProperty("doc").GetString() == doc)
            {
                var arr = rec.GetProperty("embedding")
                    .EnumerateArray()
                    .Select(e => (float)e.GetDouble())
                    .ToArray();
                if (arr.Length != 384)
                {
                    throw new InvalidOperationException(
                        $"Encoder fixture record {caseId}/{doc} has length {arr.Length}, expected 384.");
                }
                return arr;
            }
        }
        throw new InvalidOperationException(
            $"Encoder fixture missing record id={caseId}, doc={doc}.");
    }

    private float ReadExpectedDelta(string caseId)
    {
        var records = _deltaFixture.GetProperty("records");
        foreach (var rec in records.EnumerateArray())
        {
            if (rec.GetProperty("id").GetString() == caseId)
            {
                return (float)rec.GetProperty("delta_torch_v0_1").GetDouble();
            }
        }
        throw new InvalidOperationException(
            $"Delta fixture missing record id={caseId}.");
    }

    private static JsonElement LoadFixture(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Parity fixture not found at '{path}'.", path);
        }
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static float L2Norm(float[] vec)
    {
        float sum = 0f;
        for (int i = 0; i < vec.Length; i++) sum += vec[i] * vec[i];
        return MathF.Sqrt(sum);
    }

    private static string Format(float[] vec, int start, int count)
    {
        var pieces = new List<string>(count);
        for (int i = start; i < start + count && i < vec.Length; i++)
        {
            pieces.Add(vec[i].ToString("F6"));
        }
        return "[" + string.Join(", ", pieces) + "]";
    }

    /// <summary>
    /// Walk up from the test assembly directory to find the repo root,
    /// identified by the presence of <c>client/Wayfinders.Client.sln</c>.
    /// Same helper as <c>MiniLmEncoderTests</c>; not factored out because
    /// the test project deliberately stays small and dependency-free.
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
