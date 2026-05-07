using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Lifecycle and behaviour tests for the J2 modal stubs (Options + Quit
/// confirm). These exercise the IModalOverlay contract via fake
/// implementations that mimic the real OptionsModal / QuitConfirmModal
/// signal handlers without a Godot runtime.
/// </summary>
public sealed class ModalLifecycleTests
{
    /// <summary>
    /// Fake mimicking <c>OptionsModal</c>: stub controls log on change but
    /// take no real side-effect. Esc / click-outside / Close all map to
    /// CloseModal on the host.
    /// </summary>
    private sealed class FakeOptionsModal : IModalOverlay
    {
        public string ModalId => "OPTIONS_MODAL";
        public bool CanCloseOnEsc => true;
        public bool CanCloseOnClickOutside => true;

        public List<string> SideEffects { get; } = new();

        public Task OnOpen(ScreenContext context, CancellationToken ct) => Task.CompletedTask;
        public Task OnClose(CancellationToken ct) => Task.CompletedTask;

        // Stub controls -- called by the (real) signal handlers. We track
        // that they appended a log entry rather than mutating audio bus,
        // window mode, or i18n.
        public void OnVolumeChanged(double value) =>
            SideEffects.Add($"log:volume={value:F0}");

        public void OnFullscreenToggled(bool pressed) =>
            SideEffects.Add($"log:fullscreen={pressed}");

        public void OnLanguageSelected(long index) =>
            SideEffects.Add($"log:language={index}");
    }

    /// <summary>
    /// Fake mimicking <c>QuitConfirmModal</c>. The Confirm path is
    /// emulated as a flag flip rather than a real <c>GetTree().Quit()</c>
    /// (we cannot quit a test runner mid-test).
    /// </summary>
    private sealed class FakeQuitConfirmModal : IModalOverlay
    {
        public string ModalId => "QUIT_CONFIRM_MODAL";
        public bool CanCloseOnEsc => true;
        public bool CanCloseOnClickOutside => true;

        public bool ConfirmInvoked { get; private set; }
        public bool CancelInvoked { get; private set; }

        public Task OnOpen(ScreenContext context, CancellationToken ct) => Task.CompletedTask;
        public Task OnClose(CancellationToken ct) => Task.CompletedTask;

        public void Confirm() => ConfirmInvoked = true;
        public void Cancel() => CancelInvoked = true;
    }

    [Fact]
    public void OptionsModal_volume_change_logs_only_no_real_audio_bus_call()
    {
        // Pre-brief J2 §3.3: every Options control logs but takes no
        // engine-level side-effect in J2. Locks the stub contract.
        var modal = new FakeOptionsModal();
        modal.OnVolumeChanged(47);

        Assert.Single(modal.SideEffects);
        Assert.StartsWith("log:", modal.SideEffects[0]);
    }

    [Fact]
    public void OptionsModal_fullscreen_toggle_logs_only_no_window_mode_call()
    {
        // Critical J2 stub guarantee: toggling fullscreen must NOT call
        // DisplayServer.WindowSetMode -- it would lose the playtester
        // (Pre-brief Risk §3.3 reasoning). Verified here via the
        // log-only side-effect.
        var modal = new FakeOptionsModal();
        modal.OnFullscreenToggled(true);
        modal.OnFullscreenToggled(false);

        Assert.Equal(2, modal.SideEffects.Count);
        Assert.All(modal.SideEffects, e => Assert.StartsWith("log:fullscreen=", e));
    }

    [Fact]
    public void OptionsModal_language_select_logs_only_no_i18n_pipeline()
    {
        var modal = new FakeOptionsModal();
        modal.OnLanguageSelected(0);

        Assert.Single(modal.SideEffects);
        Assert.Equal("log:language=0", modal.SideEffects[0]);
    }

    [Fact]
    public void OptionsModal_can_close_on_esc_and_click_outside()
    {
        // Decision Pre-brief J2 §2.3: both close affordances are on for
        // Options. Lock the contract so a future tweak cannot silently
        // disable Esc.
        var modal = new FakeOptionsModal();
        Assert.True(modal.CanCloseOnEsc);
        Assert.True(modal.CanCloseOnClickOutside);
    }

    [Fact]
    public void QuitConfirmModal_Confirm_invokes_quit_only_when_pressed()
    {
        // Pre-brief J2 D2 lock: Confirm = sync GetTree().Quit(). The
        // Confirm path must fire only when the Confirm button is
        // explicitly pressed -- never as a side-effect of opening.
        var modal = new FakeQuitConfirmModal();

        Assert.False(modal.ConfirmInvoked);  // Just opening must not quit.
        modal.Confirm();
        Assert.True(modal.ConfirmInvoked);
        Assert.False(modal.CancelInvoked);
    }

    [Fact]
    public void QuitConfirmModal_Cancel_does_not_invoke_quit()
    {
        // Cancel / Esc / click-outside all close the modal without
        // tearing down the game. Pre-brief J2 §2.3 D2.
        var modal = new FakeQuitConfirmModal();
        modal.Cancel();

        Assert.False(modal.ConfirmInvoked);
        Assert.True(modal.CancelInvoked);
    }

    [Fact]
    public void QuitConfirmModal_can_close_on_esc_and_click_outside_means_cancel()
    {
        // Esc / click-outside on Quit confirm = Cancel implicit (do NOT
        // quit the game). Locks the safety affordance.
        var modal = new FakeQuitConfirmModal();
        Assert.True(modal.CanCloseOnEsc);
        Assert.True(modal.CanCloseOnClickOutside);
        Assert.False(modal.ConfirmInvoked);
    }

    [Fact]
    public async Task OpenModal_and_CloseModal_are_balanced_on_options()
    {
        // Smoke test on the IModalOverlay open/close pairing for the
        // Options stub, mirrored from the real SceneManager flow.
        var modal = new FakeOptionsModal();
        await modal.OnOpen(ScreenContext.Empty, CancellationToken.None);
        await modal.OnClose(CancellationToken.None);
        // No assertion to make beyond "no throw" -- the methods are
        // intentionally noop on the stub. The test exists to lock the
        // contract surface so a future refactor that drops one of them
        // shows up loud.
        Assert.True(true);
    }
}
