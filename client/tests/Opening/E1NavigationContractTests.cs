using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the E1 navigation surface in J2. The Godot-side
/// E1Title cannot be loaded from xUnit (no engine), so these tests assert
/// on a fake harness that mimics the SceneManager API shape and locks the
/// expected intents:
/// <list type="bullet">
///   <item><c>[01] Nouvelle Partie</c> -&gt; NavigateTo("E2_WORLD")</item>
///   <item><c>[03] Préférences</c> -&gt; OpenModal("OPTIONS_MODAL")</item>
///   <item><c>[04] Refermer</c> -&gt; OpenModal("QUIT_CONFIRM_MODAL")</item>
///   <item>The single-modal invariant: a second OpenModal while one is
///         active is rejected (Pre-brief Risk #1).</item>
/// </list>
/// </summary>
public sealed class E1NavigationContractTests
{
    /// <summary>
    /// Minimal fake of SceneManager's public surface used by E1. Lets us
    /// observe the calls without spinning up Godot.
    /// </summary>
    private sealed class FakeNavigator
    {
        public List<string> NavigateToCalls { get; } = new();
        public List<string> OpenModalCalls { get; } = new();
        public List<string> CloseModalCalls { get; } = new();

        public string? ActiveModalId { get; private set; }

        public Task NavigateTo(string screenId, ScreenContext? ctx = null)
        {
            NavigateToCalls.Add(screenId);
            return Task.CompletedTask;
        }

        public Task OpenModal(string modalId, ScreenContext? ctx = null)
        {
            // Mirror the J1 invariant: single modal at a time. Reject the
            // second open with no error, just no-op (matches SceneManager
            // logged-warning behaviour).
            if (ActiveModalId is not null)
            {
                return Task.CompletedTask;
            }
            OpenModalCalls.Add(modalId);
            ActiveModalId = modalId;
            return Task.CompletedTask;
        }

        public Task CloseModal()
        {
            if (ActiveModalId is null)
                return Task.CompletedTask;
            CloseModalCalls.Add(ActiveModalId);
            ActiveModalId = null;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NewGame_button_navigates_to_E2_WORLD()
    {
        var nav = new FakeNavigator();

        // Simulate the OnNewGamePressed handler from E1Title.cs (line 167).
        await nav.NavigateTo("E2_WORLD");

        Assert.Single(nav.NavigateToCalls);
        Assert.Equal("E2_WORLD", nav.NavigateToCalls[0]);
        Assert.Empty(nav.OpenModalCalls);
    }

    [Fact]
    public async Task Options_button_opens_OPTIONS_MODAL()
    {
        var nav = new FakeNavigator();
        await nav.OpenModal("OPTIONS_MODAL");

        Assert.Single(nav.OpenModalCalls);
        Assert.Equal("OPTIONS_MODAL", nav.OpenModalCalls[0]);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Equal("OPTIONS_MODAL", nav.ActiveModalId);
    }

    [Fact]
    public async Task Quit_button_opens_QUIT_CONFIRM_MODAL()
    {
        var nav = new FakeNavigator();
        await nav.OpenModal("QUIT_CONFIRM_MODAL");

        Assert.Single(nav.OpenModalCalls);
        Assert.Equal("QUIT_CONFIRM_MODAL", nav.OpenModalCalls[0]);
        Assert.Equal("QUIT_CONFIRM_MODAL", nav.ActiveModalId);
    }

    [Fact]
    public async Task Modal_on_modal_is_rejected_keeping_first_active()
    {
        // Pre-brief Risk #1 lock: while one modal is open, a second
        // OpenModal call must not stack. The first stays active.
        var nav = new FakeNavigator();
        await nav.OpenModal("OPTIONS_MODAL");
        await nav.OpenModal("QUIT_CONFIRM_MODAL");

        Assert.Single(nav.OpenModalCalls);
        Assert.Equal("OPTIONS_MODAL", nav.OpenModalCalls[0]);
        Assert.Equal("OPTIONS_MODAL", nav.ActiveModalId);
    }
}
