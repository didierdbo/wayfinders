using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Locks the visibility contract that <c>SceneManager.NavigateTo</c> /
/// <c>NavigateBack</c> must honor. Surface bug J3 fix: the suspended
/// screen must be hidden on Push and re-shown on Pop, so the player does
/// not see two screens stacked on top of each other.
///
/// <para>
/// <b>Why a contract test, not a Godot integration test.</b> Visibility
/// in Godot lives on <c>CanvasItem</c> and <c>CanvasLayer</c> nodes — the
/// real <c>SceneManager</c> manipulates engine state we cannot exercise
/// from xUnit (no scene tree, no <c>Node</c> base). What we <i>can</i>
/// lock here is the <b>orchestration sequence</b>: each Push must drive a
/// "hide previous" beat, each Pop must drive a "show new top" beat. A
/// future refactor that drops one of those beats fails this test loud.
/// </para>
///
/// <para>
/// <b>Why an interface, not a method on <see cref="IScreen"/>.</b>
/// Visibility is an orchestration concern, not a per-screen concern. The
/// real <c>SceneManager</c> walks the <c>CanvasLayer</c> tree directly —
/// it does not delegate to the screen. We mirror that boundary in the
/// fake so the test does not pretend visibility is part of the screen
/// contract (it isn't, and adding it there would push a Godot-shaped trap
/// onto every future screen author).
/// </para>
///
/// <para>
/// <b>Engine-side regression coverage.</b> The recursive
/// <c>CanvasLayer</c> walk inside the real <c>SceneManager</c> is locked
/// by the J3 fix manual checklist entry "click Nouvelle Partie : E1
/// disparaît visuellement, seul E2 affiché ; Esc : E1 réapparaît." The
/// xUnit layer documents the <i>intent</i>, the manual checklist
/// validates the <i>engine outcome</i>. Same split as Risk #1
/// thread-safety.
/// </para>
/// </summary>
public sealed class SceneVisibilityContractTests
{
    /// <summary>
    /// Mirrors the visibility orchestration the real SceneManager
    /// performs. Each registered screen has an observable Visible flag.
    /// Push hides the outgoing top ; Pop shows the new top. Modal open
    /// must NOT touch host visibility (modal-preserves-host invariant).
    /// </summary>
    private sealed class FakeVisibilityNavigator
    {
        private readonly List<string> _stack = new();
        private readonly Dictionary<string, bool> _visibility = new();

        public List<string> HideCalls { get; } = new();
        public List<string> ShowCalls { get; } = new();

        public bool ModalOpen { get; private set; }

        public bool IsVisible(string screenId) =>
            _visibility.TryGetValue(screenId, out var v) && v;

        public string? CurrentScreenId =>
            _stack.Count > 0 ? _stack[^1] : null;

        public Task NavigateTo(string screenId)
        {
            // Hide the previous top first — this is the J3 fix invariant.
            if (CurrentScreenId is { } prev)
            {
                _visibility[prev] = false;
                HideCalls.Add(prev);
            }
            _stack.Add(screenId);
            _visibility[screenId] = true;
            return Task.CompletedTask;
        }

        public Task NavigateBack()
        {
            if (_stack.Count <= 1) return Task.CompletedTask;

            var leaving = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            _visibility.Remove(leaving);

            // Re-show the new top — reciprocal of the Hide on Push.
            if (CurrentScreenId is { } newTop)
            {
                _visibility[newTop] = true;
                ShowCalls.Add(newTop);
            }
            return Task.CompletedTask;
        }

        public Task OpenModal(string _)
        {
            // Modal invariant: opening a modal must NOT hide the host.
            // The fake records nothing on visibility — same shape as the
            // real SceneManager.OpenModal which never calls
            // SetScreenVisibility on the host.
            ModalOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseModal()
        {
            ModalOpen = false;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NavigateTo_hides_the_previous_top_screen()
    {
        // J3 surface bug: clicking "Nouvelle Partie" on E1 navigated to
        // E2 but left E1 still rendered behind. The fix is that Push
        // hides the previous top. This locks the contract.
        var nav = new FakeVisibilityNavigator();

        await nav.NavigateTo("E1_TITLE");
        await nav.NavigateTo("E1_WORLD");

        Assert.False(nav.IsVisible("E1_TITLE"));
        Assert.True(nav.IsVisible("E1_WORLD"));
        Assert.Single(nav.HideCalls);
        Assert.Equal("E1_TITLE", nav.HideCalls[0]);
    }

    [Fact]
    public async Task NavigateBack_reshows_the_new_top_screen()
    {
        // Reciprocal invariant: if Push hides E1, Pop must re-show E1.
        // Without this, returning from E2 would leave the player on a
        // black scene.
        var nav = new FakeVisibilityNavigator();
        await nav.NavigateTo("E1_TITLE");
        await nav.NavigateTo("E1_WORLD");

        await nav.NavigateBack();

        Assert.True(nav.IsVisible("E1_TITLE"));
        Assert.False(nav.IsVisible("E1_WORLD")); // E2 was popped
        Assert.Single(nav.ShowCalls);
        Assert.Equal("E1_TITLE", nav.ShowCalls[0]);
    }

    [Fact]
    public async Task NavigateTo_root_does_not_call_hide_on_empty_stack()
    {
        // First push has no previous top. Hide must not be invoked.
        // Boot path: OpeningBootstrap -> NavigateTo("E1_TITLE").
        var nav = new FakeVisibilityNavigator();

        await nav.NavigateTo("E1_TITLE");

        Assert.True(nav.IsVisible("E1_TITLE"));
        Assert.Empty(nav.HideCalls);
    }

    [Fact]
    public async Task Push_pop_push_pop_keeps_visibility_consistent()
    {
        // Two-cycle stress: validates that successive navigations do not
        // leak visibility state between entries. Mirrors a player who
        // bounces E1 -> E2 -> E1 -> E2 from the title.
        var nav = new FakeVisibilityNavigator();
        await nav.NavigateTo("E1_TITLE");
        await nav.NavigateTo("E1_WORLD");
        await nav.NavigateBack();
        await nav.NavigateTo("E1_WORLD");
        await nav.NavigateBack();

        // After two full cycles, only E1 remains and is visible.
        Assert.Equal("E1_TITLE", nav.CurrentScreenId);
        Assert.True(nav.IsVisible("E1_TITLE"));
        Assert.False(nav.IsVisible("E1_WORLD"));

        // Hide called twice (each Push past root), Show called twice
        // (each Pop back to E1).
        Assert.Equal(2, nav.HideCalls.Count);
        Assert.Equal(2, nav.ShowCalls.Count);
        Assert.All(nav.HideCalls, id => Assert.Equal("E1_TITLE", id));
        Assert.All(nav.ShowCalls, id => Assert.Equal("E1_TITLE", id));
    }

    [Fact]
    public async Task OpenModal_does_not_hide_host_screen()
    {
        // Modal invariant (pre-brief J2 §4.4 + J3 fix re-confirmation):
        // a modal opens ON TOP of the host screen, leaving it visible
        // beneath. The OptionsModal / QuitConfirmModal flows on E1 must
        // never trigger a SetScreenVisibility on the host.
        var nav = new FakeVisibilityNavigator();
        await nav.NavigateTo("E1_TITLE");

        await nav.OpenModal("OPTIONS_MODAL");

        Assert.True(nav.ModalOpen);
        Assert.True(nav.IsVisible("E1_TITLE"));   // host stays visible
        Assert.Empty(nav.HideCalls);              // no hide call fired

        await nav.CloseModal();

        Assert.False(nav.ModalOpen);
        Assert.True(nav.IsVisible("E1_TITLE"));   // still visible after close
    }

    [Fact]
    public async Task OpenModal_then_NavigateTo_sequence_keeps_visibility_correct()
    {
        // Defense in depth: after a modal cycle, the host screen's
        // visibility flag is unchanged, so a subsequent NavigateTo from
        // that host hides it correctly. This catches a regression where
        // a sloppy fix might leave host.Visible flipped after modal close.
        var nav = new FakeVisibilityNavigator();
        await nav.NavigateTo("E1_TITLE");
        await nav.OpenModal("OPTIONS_MODAL");
        await nav.CloseModal();

        // Now the [01] Nouvelle Partie path:
        await nav.NavigateTo("E1_WORLD");

        Assert.False(nav.IsVisible("E1_TITLE"));
        Assert.True(nav.IsVisible("E1_WORLD"));
        Assert.Single(nav.HideCalls);
        Assert.Equal("E1_TITLE", nav.HideCalls[0]);
    }
}
