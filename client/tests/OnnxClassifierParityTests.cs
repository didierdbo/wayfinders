// Phase 6 L4 parity tests: end-to-end OnnxClassifier (C# encoder + C# head)
// vs the regenerated Python ORT-fp16-CPU reference (Tess 2026-05-06, fixture
// key delta_onnx_e2e_v0_1). The L4 contract gate is atol=5e-2 across all 20
// records (Pax L4 Parity Gate Decision §1, "third gate"). This file owns the
// gate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Ml;
using Wayfinders.Client.Tests.Doubles;
using Xunit.Abstractions;

namespace Wayfinders.Client.Tests;

/// <summary>
/// Phase 6 L4 parity tests. Validates that the production
/// <see cref="OnnxClassifier"/> reproduces the Python ORT-fp16-CPU
/// end-to-end reference Δ within <c>atol=5e-2</c> on every record of the
/// canonical parity fixture.
///
/// <para>
/// <b>What this class tests.</b>
/// <list type="bullet">
/// <item>End-to-end parity over all 20 records of
/// <c>parity/python_reference_v0.1.json</c>: encoder + head composed by
/// <see cref="OnnxClassifier"/>, asserted at the gate the L4 brief
/// commits to (<c>atol=5e-2</c>).</item>
/// <item>Defensive lifecycle: post-dispose call throws, pre-cancelled
/// token throws, missing-asset construction throws.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this class deliberately does not test.</b>
/// <list type="bullet">
/// <item>Encoder per-element parity (<c>atol=1e-3</c>) -- already covered
/// by <see cref="MiniLmEncoderTests"/>. No copy here.</item>
/// <item>Head-only parity (<c>atol=1e-4</c>) -- already covered by
/// <see cref="ResolutionHeadTests"/>. No copy here.</item>
/// <item>The <c>ClassifierService</c> autoload -- requires a live Godot
/// runtime, deferred to L5 if a smoke test is wanted at all (the existing
/// TacticalScene log line is the L4 acceptance surface).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Tolerance choice.</b> The contract gate is <c>atol=5e-2</c> from
/// the fixture header (<c>expected_e2e_atol</c>). Tess's regen showed
/// max Python-vs-old-torch-baseline diff at 3.64e-2, so the gate has
/// comfortable headroom for the additional fp16 / ORT-version wobble we
/// expect on the C# side. A failure here likely means a real bug
/// (tokeniser drift, prose truncation, encoder-head shape mismatch),
/// not tolerance pressure -- escalate before loosening the gate.
/// </para>
/// </summary>
public sealed class OnnxClassifierParityTests : IClassFixture<OnnxClassifierFixture>
{
    private readonly OnnxClassifierFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OnnxClassifierParityTests(OnnxClassifierFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// One xUnit test case per parity record. The
    /// <see cref="OnnxClassifierFixture.RecordIds"/> generator pulls the
    /// 20 IDs from the fixture file at discovery time, so a failed record
    /// surfaces in the xUnit pane with its real <c>record_id</c>, not an
    /// opaque index.
    /// </summary>
    [Theory]
    [MemberData(nameof(OnnxClassifierFixture.RecordIds), MemberType = typeof(OnnxClassifierFixture))]
    public async Task ResolveDeltaAsync_MatchesPythonReference(string recordId)
    {
        var record = _fixture.Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null)
        {
            throw new InvalidOperationException(
                $"Fixture mismatch: record id '{recordId}' was emitted by " +
                "MemberData but is not present in the loaded fixture. " +
                "Did the fixture change between discovery and execution?");
        }

        // Warm-up call: amortises ORT graph optimisation and allocator
        // pool warmup so the measured latency reflects steady-state, not
        // first-call overhead. The fixture is class-scoped so for the
        // first test of the run we pay the cold-start cost; for subsequent
        // tests the warm-up is essentially free.
        var warm = await _fixture.Classifier
            .ResolveDeltaAsync(record.CharacterProse, record.ActionProse, record.ContextProse)
            .ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var actual = await _fixture.Classifier
            .ResolveDeltaAsync(record.CharacterProse, record.ActionProse, record.ContextProse)
            .ConfigureAwait(false);
        sw.Stop();
        var totalLatencyMs = sw.Elapsed.TotalMilliseconds;

        var diff = MathF.Abs(actual - record.ExpectedDelta);

        _output.WriteLine(
            $"{recordId}: actual={actual:F6} expected={record.ExpectedDelta:F6} " +
            $"diff={diff:E3} total_ms={totalLatencyMs:F2} warm={warm:F6}");

        Assert.True(diff <= _fixture.EndToEndAtol,
            $"{recordId}: |actual - expected| = {diff:E3} > atol = {_fixture.EndToEndAtol:E2} " +
            $"(actual={actual:F6}, expected={record.ExpectedDelta:F6}). " +
            "Diagnostic: char/action/context prose lengths = " +
            $"{record.CharacterProse.Length}/{record.ActionProse.Length}/{record.ContextProse.Length}.");
    }

    /// <summary>
    /// Aggregate roll-up across all 20 records: prints
    /// <c>max_abs_diff</c>, <c>mean_abs_diff</c>, average per-record
    /// latency. Not strictly necessary for the gate (the per-record
    /// theory above already enforces it) but the closeout brief asks for
    /// these aggregates and putting them in a single test means
    /// <c>dotnet test</c> output carries them without manual collation.
    /// </summary>
    [Fact]
    public async Task ResolveDeltaAsync_AggregateDiagnostics_AcrossAllRecords()
    {
        var diffs = new List<float>(_fixture.Records.Count);
        var totalLatencies = new List<double>(_fixture.Records.Count);

        // Warm-up once on the first record so cold-start does not
        // contaminate the latency average. The remaining 20 calls (one
        // per record) report warm latency only.
        var first = _fixture.Records[0];
        await _fixture.Classifier
            .ResolveDeltaAsync(first.CharacterProse, first.ActionProse, first.ContextProse)
            .ConfigureAwait(false);

        foreach (var record in _fixture.Records)
        {
            var sw = Stopwatch.StartNew();
            var actual = await _fixture.Classifier
                .ResolveDeltaAsync(record.CharacterProse, record.ActionProse, record.ContextProse)
                .ConfigureAwait(false);
            sw.Stop();

            diffs.Add(MathF.Abs(actual - record.ExpectedDelta));
            totalLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var maxDiff = diffs.Max();
        var meanDiff = diffs.Average();
        var meanLatency = totalLatencies.Average();
        var p50Latency = totalLatencies.OrderBy(x => x).ElementAt(totalLatencies.Count / 2);
        var maxLatency = totalLatencies.Max();

        _output.WriteLine($"=== Aggregate L4 parity (n={_fixture.Records.Count}) ===");
        _output.WriteLine($"max_abs_diff   = {maxDiff:E3}  (gate atol = {_fixture.EndToEndAtol:E2})");
        _output.WriteLine($"mean_abs_diff  = {meanDiff:E3}");
        _output.WriteLine($"latency_mean_ms= {meanLatency:F2}");
        _output.WriteLine($"latency_p50_ms = {p50Latency:F2}");
        _output.WriteLine($"latency_max_ms = {maxLatency:F2}");

        // Defensive aggregate gate (mirrors per-record gate but on the
        // worst record). If the per-record [Theory] pass and this fails,
        // it indicates a fixture-vs-test divergence, not a real bug.
        Assert.True(maxDiff <= _fixture.EndToEndAtol,
            $"Aggregate max_abs_diff {maxDiff:E3} exceeds gate {_fixture.EndToEndAtol:E2}.");
    }

    // -------------------------------------------------------------------
    // Defensive tests: lifecycle, cancellation, missing-asset construction.
    // These do not share the class fixture (they construct their own
    // throw-away classifier or a deliberately broken one) so they sit
    // alongside the parity tests rather than in a separate file.
    // -------------------------------------------------------------------

    [Fact]
    public async Task ResolveDeltaAsync_AfterDispose_Throws()
    {
        // Build a fresh classifier just for this test -- we cannot dispose
        // the shared fixture mid-run.
        var encoderPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "encoder.onnx");
        var vocabPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "vocab.txt");
        var headPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "resolution-head-v0.1.onnx");

        var classifier = new OnnxClassifier(encoderPath, vocabPath, headPath);
        classifier.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await classifier.ResolveDeltaAsync("char", "action", "context").ConfigureAwait(false));
    }

    [Fact]
    public async Task ResolveDeltaAsync_PreCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _fixture.Classifier
                .ResolveDeltaAsync("char", "action", "context", cts.Token)
                .ConfigureAwait(false));
    }

    [Fact]
    public async Task ResolveDeltaAsync_RejectsHeadDimMismatchInternally()
    {
        // The head's dim-mismatch guard fires when its three input vectors
        // are not 384 floats long. OnnxClassifier always produces 384-dim
        // vectors via MiniLmEncoder, so the only path to this guard from
        // the OnnxClassifier surface is via a contract bug in the encoder
        // -- not directly testable from the public surface. We leave the
        // direct dim-mismatch coverage to ResolutionHeadTests (already
        // green at L3) and assert the boundary contract here: a normal
        // call returns a finite float in [-5, +5].
        var record = _fixture.Records[0];
        var actual = await _fixture.Classifier
            .ResolveDeltaAsync(record.CharacterProse, record.ActionProse, record.ContextProse)
            .ConfigureAwait(false);
        Assert.True(float.IsFinite(actual), $"Expected finite delta, got {actual}.");
        Assert.InRange(actual, -5f, 5f);
    }

    [Fact]
    public void Construct_MissingHeadOnnx_Throws()
    {
        var encoderPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "encoder.onnx");
        var vocabPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "vocab.txt");
        var bogusHeadPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "does-not-exist.onnx");

        // FileNotFoundException surfaces from ResolutionHead's constructor.
        // Critical: the encoder construction must succeed first, so the
        // catch-and-dispose in OnnxClassifier's ctor is what protects us
        // from leaking the encoder session on this code path. Hard to
        // assert directly; the absence of a leak warning across a 20-test
        // run is the practical signal.
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxClassifier(encoderPath, vocabPath, bogusHeadPath));
    }

    [Fact]
    public void Construct_MissingEncoderOnnx_Throws()
    {
        var bogusEncoderPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "does-not-exist.onnx");
        var vocabPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "vocab.txt");
        var headPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "resolution-head-v0.1.onnx");

        Assert.Throws<FileNotFoundException>(() =>
            new OnnxClassifier(bogusEncoderPath, vocabPath, headPath));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var encoderPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "encoder.onnx");
        var vocabPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "vocab.txt");
        var headPath = Path.Combine(_fixture.RepoRoot, "client", "ml", "resolution-head-v0.1.onnx");

        var classifier = new OnnxClassifier(encoderPath, vocabPath, headPath);
        classifier.Dispose();
        // Second Dispose() must not throw -- the IClassifier autoload's
        // _ExitTree may dispose during a teardown that Godot also tears
        // down via GC, and the convention for Dispose() is "safe to call
        // any number of times."
        var ex = Record.Exception(() => classifier.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task MockClassifier_StillReturnsZero_AsTestDouble()
    {
        // Sanity test for the test double itself: confirms the L1
        // contract still holds after the move from client/ml/ to
        // client/tests/Doubles/. If a future scene-level integration test
        // reaches for MockClassifier as an injected double, this is the
        // contract it relies on.
        var mock = new MockClassifier();
        var result = await mock.ResolveDeltaAsync("char", "action", "context").ConfigureAwait(false);
        Assert.Equal(0f, result);
    }
}
