using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Wayfinders.Client.Ml;

namespace Wayfinders.Client.Tests;

/// <summary>
/// xUnit class-scoped fixture: builds one <see cref="OnnxClassifier"/> and
/// loads the parity reference fixture once, then reuses both across every
/// test method in <see cref="OnnxClassifierParityTests"/>.
///
/// <para>
/// <b>Why a fixture, not constructor-per-test.</b> Each
/// <see cref="OnnxClassifier"/> spins up two ORT
/// <c>InferenceSession</c>s; the encoder cold-start alone is around
/// 200 ms and the head adds a few more. A 20-record <c>[Theory]</c>
/// rebuilding the classifier per case would burn ~4 seconds of test-suite
/// latency for zero correctness gain (the sessions are deterministic and
/// stateless across calls). xUnit's <see cref="IClassFixture{TFixture}"/>
/// constructs the fixture once before any test in the class runs and
/// disposes it once after the class finishes -- exactly the lifetime we
/// want for a heavyweight native resource.
/// </para>
///
/// <para>
/// <b>Required asset paths.</b> The fixture resolves the encoder ONNX,
/// vocab, and head ONNX relative to the repo root (located by walking
/// up to <c>client/Wayfinders.Client.sln</c>). The encoder asset is
/// expected to be already present at <c>client/ml/encoder.onnx</c>
/// (manual fetch step, see top-level README -- same convention L2 set).
/// The head ONNX ships in-repo at <c>client/ml/resolution-head-v0.1.onnx</c>
/// (Phase 6 L4 commit). The parity fixture lives at
/// <c>parity/python_reference_v0.1.json</c>.
/// </para>
/// </summary>
public sealed class OnnxClassifierFixture : IDisposable
{
    private const string EncoderOnnxRelativePath = "client/ml/encoder.onnx";
    private const string VocabRelativePath = "client/ml/vocab.txt";
    private const string HeadOnnxRelativePath = "client/ml/resolution-head-v0.1.onnx";
    private const string ParityFixtureRelativePath = "parity/python_reference_v0.1.json";

    /// <summary>
    /// The shared classifier under test. Constructed once per test class.
    /// </summary>
    public OnnxClassifier Classifier { get; }

    /// <summary>
    /// All 20 parity records loaded from the regenerated fixture. Order
    /// preserved from the JSON so failure messages match the reference
    /// row order.
    /// </summary>
    public IReadOnlyList<ParityRecord> Records { get; }

    /// <summary>
    /// End-to-end atol gate published in the fixture header
    /// (<c>expected_e2e_atol</c>, currently 0.05 per Pax L4 Parity Gate
    /// Decision §1). Read from the fixture rather than hard-coded so a
    /// future tightening (Pax flagged 1e-2 at v0.2) needs only a fixture
    /// regen, not a test edit.
    /// </summary>
    public float EndToEndAtol { get; }

    /// <summary>
    /// Repo root, exposed so the test class can include it in diagnostic
    /// messages if a parity assertion fails.
    /// </summary>
    public string RepoRoot { get; }

    public OnnxClassifierFixture()
    {
        RepoRoot = FindRepoRoot();

        var encoderPath = Path.Combine(RepoRoot, EncoderOnnxRelativePath);
        var vocabPath = Path.Combine(RepoRoot, VocabRelativePath);
        var headPath = Path.Combine(RepoRoot, HeadOnnxRelativePath);
        var fixturePath = Path.Combine(RepoRoot, ParityFixtureRelativePath);

        Classifier = new OnnxClassifier(encoderPath, vocabPath, headPath);

        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Parity fixture not found at '{fixturePath}'.", fixturePath);
        }

        var json = JsonDocument.Parse(File.ReadAllText(fixturePath)).RootElement;

        EndToEndAtol = (float)json.GetProperty("expected_e2e_atol").GetDouble();

        Records = json.GetProperty("records")
            .EnumerateArray()
            .Select(rec => new ParityRecord(
                Id: rec.GetProperty("id").GetString()!,
                CharacterProse: rec.GetProperty("prose_character").GetString()!,
                ActionProse: rec.GetProperty("prose_action").GetString()!,
                ContextProse: rec.GetProperty("prose_context").GetString()!,
                ExpectedDelta: (float)rec.GetProperty("delta_onnx_e2e_v0_1").GetDouble()))
            .ToList();

        if (Records.Count != 20)
        {
            throw new InvalidOperationException(
                $"Parity fixture has {Records.Count} records, expected 20. " +
                "Tess regen may have drifted -- escalate before running L4 gate.");
        }
    }

    public void Dispose() => Classifier.Dispose();

    /// <summary>
    /// xUnit <see cref="MemberDataAttribute"/> source: emits one
    /// <c>{ recordId }</c> tuple per fixture record. Pulls from the same
    /// JSON the fixture instance loads, so test discovery works without
    /// instantiating the heavyweight fixture (xUnit calls this static
    /// method during enumeration). The fixture instance reads the same
    /// file again at construction time -- two reads, both cheap, both
    /// kept independent so this method stays static-callable.
    /// </summary>
    public static IEnumerable<object[]> RecordIds()
    {
        var fixturePath = Path.Combine(FindRepoRoot(), ParityFixtureRelativePath);
        if (!File.Exists(fixturePath))
        {
            // Discovery-time: do not throw, let the tests themselves fail
            // with a clear file-not-found message. Throwing here would
            // hide the real path from the xUnit error pane.
            yield break;
        }

        var json = JsonDocument.Parse(File.ReadAllText(fixturePath)).RootElement;
        foreach (var rec in json.GetProperty("records").EnumerateArray())
        {
            yield return new object[] { rec.GetProperty("id").GetString()! };
        }
    }

    /// <summary>
    /// Walk up from the test assembly directory to find the repo root,
    /// identified by <c>client/Wayfinders.Client.sln</c>. Same helper as
    /// the L2 / L3 test classes; not factored into a shared utility on
    /// purpose -- the test project deliberately stays small and
    /// dependency-free.
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

/// <summary>
/// One parity reference record. Field names mirror the JSON fixture
/// (<c>id</c>, <c>prose_character</c>, <c>prose_action</c>,
/// <c>prose_context</c>, <c>delta_onnx_e2e_v0_1</c>) but rendered in C#
/// PascalCase. Immutable record so the fixture's <c>Records</c> list is
/// safe to expose as <see cref="System.Collections.Generic.IReadOnlyList{T}"/>.
/// </summary>
public sealed record ParityRecord(
    string Id,
    string CharacterProse,
    string ActionProse,
    string ContextProse,
    float ExpectedDelta);
