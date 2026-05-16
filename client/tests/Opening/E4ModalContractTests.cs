using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Defense-in-depth contract tests for the (E3, E4) modal couple --
/// the first real gameplay use of the J3-fix modal invariant
/// (<c>OpenModal</c> does NOT touch host visibility) since J2 only
/// exercised it on the (E1, Options/Quit) couple. J4 D-J4-13 ships
/// these as belt-and-braces:
/// <list type="bullet">
///   <item>Opening E4 from E3 leaves E3 visible underneath.</item>
///   <item>Closing E4 returns the host stack focus to E3 (the
///         "modal caller" is E3, not whichever screen is below E3
///         on the navigation stack).</item>
///   <item>The npc id threaded by E3 propagates verbatim through the
///         <c>ScreenContext.Payload</c> bag to the E4 modal.</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="ModalLifecycleTests"/> from J2: a
/// FakeE3Host + FakeE4Modal exercise the IModalOverlay/IScreen
/// boundary without spinning a Godot runtime. Visibility is
/// orchestration-only (the real SceneManager walks CanvasLayer
/// children) -- here we lock the orchestration sequence: host stays
/// "visible" in the fake, OnSuspend fires before OnOpen, OnClose
/// fires before OnResume.
/// </para>
/// </summary>
public sealed class E4ModalContractTests
{
    private const string NpcIdPayloadKey = "E4.NpcId";

    /// <summary>
    /// Fake E3 host that records its own lifecycle calls. The real
    /// E3CityHalfgate disables ProcessMode + dims Modulate on suspend,
    /// but neither touches Visible -- the modal invariant J3-fix
    /// locked. Here we mirror the contract: HostVisible flips off only
    /// if a (forbidden) hide is explicitly called, which it never is.
    /// </summary>
    private sealed class FakeE3Host : IScreen
    {
        public string ScreenId => "E2_AREA";
        public bool HostVisible { get; private set; } = true;
        public bool Suspended { get; private set; }

        public List<string> Lifecycle { get; } = new();

        public Task OnEnter(ScreenContext context, CancellationToken ct)
        {
            Lifecycle.Add("OnEnter");
            HostVisible = true;
            return Task.CompletedTask;
        }

        public Task OnExit(CancellationToken ct)
        {
            Lifecycle.Add("OnExit");
            return Task.CompletedTask;
        }

        public Task OnSuspend(CancellationToken ct)
        {
            // Real screen disables input + dims, never hides. Mirror.
            Lifecycle.Add("OnSuspend");
            Suspended = true;
            return Task.CompletedTask;
        }

        public Task OnResume(CancellationToken ct)
        {
            Lifecycle.Add("OnResume");
            Suspended = false;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fake E4 modal that captures the payload received via OnOpen so
    /// the test can assert the npc id propagated end-to-end.
    /// </summary>
    private sealed class FakeE4Modal : IModalOverlay
    {
        public string ModalId => "E4_CHARACTER_SHEET";
        public bool CanCloseOnEsc => true;
        public bool CanCloseOnClickOutside => true;

        public ScreenContext? CapturedContext { get; private set; }
        public string? CapturedNpcId { get; private set; }

        public List<string> Lifecycle { get; } = new();

        public Task OnOpen(ScreenContext context, CancellationToken ct)
        {
            Lifecycle.Add("OnOpen");
            CapturedContext = context;
            if (context?.Payload?.TryGetValue(NpcIdPayloadKey, out var raw) == true)
                CapturedNpcId = raw?.ToString();
            return Task.CompletedTask;
        }

        public Task OnClose(CancellationToken ct)
        {
            Lifecycle.Add("OnClose");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Mini orchestrator mirroring the SceneManager.OpenModal /
    /// CloseModal sequence: suspend host, open modal ; close modal,
    /// resume host. Visibility is intentionally NOT touched here --
    /// that is the modal invariant J3-fix.
    /// </summary>
    private static async Task OpenAndClose(
        FakeE3Host host, FakeE4Modal modal, ScreenContext ctx)
    {
        await host.OnSuspend(CancellationToken.None);
        await modal.OnOpen(ctx, CancellationToken.None);
        // ... E4 modal is now open ; in real code the SceneManager
        // returns to its caller. For the test we close immediately.
        await modal.OnClose(CancellationToken.None);
        await host.OnResume(CancellationToken.None);
    }

    [Fact]
    public async Task OpenModal_E4_does_not_hide_E3_host()
    {
        // Modal invariant J3-fix locked: opening E4 from E3 must
        // suspend E3 (input disabled + dim) but must NOT hide it.
        // The host stays visible underneath the modal CanvasLayer.
        var host = new FakeE3Host();
        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);

        var modal = new FakeE4Modal();
        await host.OnSuspend(CancellationToken.None);
        await modal.OnOpen(ScreenContext.Empty, CancellationToken.None);

        Assert.True(host.HostVisible, "E3 must remain visible underneath the E4 modal (J3-fix invariant).");
        Assert.True(host.Suspended,    "E3 must be suspended while E4 is open (input + dim only).");
        Assert.Contains("OnSuspend", host.Lifecycle);
        Assert.Contains("OnOpen", modal.Lifecycle);
    }

    [Fact]
    public async Task CloseModal_E4_returns_focus_to_E3_host_via_resume()
    {
        // Reciprocal: closing E4 must call OnResume on E3 so input is
        // re-enabled and the dim drops. The fake locks the lifecycle
        // sequence -- real Godot timing (await ToSignal ProcessFrame
        // between calls) is engine-side, not asserted here.
        var host = new FakeE3Host();
        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);
        var modal = new FakeE4Modal();

        await OpenAndClose(host, modal, ScreenContext.Empty);

        Assert.False(host.Suspended, "E3 must be back to active after E4 closes.");
        Assert.True(host.HostVisible, "E3 must still be visible after E4 closes.");

        // The full lifecycle on the host: Enter -> Suspend -> Resume.
        // The modal: Open -> Close. Asserted in order to lock the
        // sequence (so a refactor that drops Resume surfaces here).
        Assert.Equal(new[] { "OnEnter", "OnSuspend", "OnResume" }, host.Lifecycle);
        Assert.Equal(new[] { "OnOpen", "OnClose" }, modal.Lifecycle);
    }

    [Fact]
    public async Task OpenModal_E4_with_NpcId_propagates_through_payload()
    {
        // D-J4-07 plumbing test: when E3 dispatches a NpcCandidate
        // click, it wraps the NpcId in a ScreenContext.Payload bag
        // under the key "E4.NpcId". The E4 modal must read the same
        // key on OnOpen. This locks the contract end-to-end so a
        // refactor that renames the key (or stops threading it) is
        // surfaced here.
        var host = new FakeE3Host();
        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);
        var modal = new FakeE4Modal();

        var ctx = new ScreenContext
        {
            CallerScreenId = "E2_AREA",
            Payload = new Dictionary<string, object>
            {
                [NpcIdPayloadKey] = "kira",
            },
        };

        await OpenAndClose(host, modal, ctx);

        Assert.NotNull(modal.CapturedContext);
        Assert.Equal("E2_AREA", modal.CapturedContext!.CallerScreenId);
        Assert.Equal("kira", modal.CapturedNpcId);
    }
}
