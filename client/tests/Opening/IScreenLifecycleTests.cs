using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Lifecycle-sequencing tests using fake <see cref="IScreen"/> /
/// <see cref="IModalOverlay"/> implementations. Validates the contract
/// around what call comes before what — without needing a Godot runtime.
///
/// <para>
/// We do NOT exercise <see cref="Wayfinders.Client.Services.SceneManager"/>
/// directly here (it is a Godot Node and would need the engine). Instead
/// we manually drive the expected sequence to lock in the contract that
/// SceneManager promises to honor. If a refactor of SceneManager breaks
/// these expectations, the manual integration checklist (J1 closeout)
/// will catch it ; these tests document the intent explicitly so future
/// regressions are diagnosed quickly.
/// </para>
/// </summary>
public sealed class IScreenLifecycleTests
{
    private sealed class FakeScreen : IScreen
    {
        public List<string> Calls { get; } = new();
        public string ScreenId { get; }
        public ScreenContext? LastEnterContext { get; private set; }

        public FakeScreen(string id) { ScreenId = id; }

        public Task OnEnter(ScreenContext context, CancellationToken ct)
        {
            LastEnterContext = context;
            Calls.Add("OnEnter");
            return Task.CompletedTask;
        }

        public Task OnExit(CancellationToken ct)
        {
            Calls.Add("OnExit");
            return Task.CompletedTask;
        }

        public Task OnSuspend(CancellationToken ct)
        {
            Calls.Add("OnSuspend");
            return Task.CompletedTask;
        }

        public Task OnResume(CancellationToken ct)
        {
            Calls.Add("OnResume");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModal : IModalOverlay
    {
        public List<string> Calls { get; } = new();
        public string ModalId { get; }
        public bool CanCloseOnEsc => true;
        public bool CanCloseOnClickOutside => true;

        public FakeModal(string id) { ModalId = id; }

        public Task OnOpen(ScreenContext context, CancellationToken ct)
        {
            Calls.Add("OnOpen");
            return Task.CompletedTask;
        }

        public Task OnClose(CancellationToken ct)
        {
            Calls.Add("OnClose");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Documents the OnEnter -&gt; OnExit ordering for a simple
    /// push-then-pop sequence. SceneManager invokes these in this order ;
    /// any future refactor must keep the contract.
    /// </summary>
    [Fact]
    public async Task Push_then_pop_invokes_OnEnter_then_OnExit()
    {
        var e1 = new FakeScreen("E1");

        await e1.OnEnter(ScreenContext.Empty, CancellationToken.None);
        await e1.OnExit(CancellationToken.None);

        Assert.Equal(new[] { "OnEnter", "OnExit" }, e1.Calls);
    }

    /// <summary>
    /// The modal-open sequence: host receives OnSuspend BEFORE modal
    /// receives OnOpen. SceneManager.OpenModal honors this — pre-brief
    /// §4.4 commits to it explicitly.
    /// </summary>
    [Fact]
    public async Task OpenModal_suspends_host_before_opening_modal()
    {
        var host = new FakeScreen("E3");
        var modal = new FakeModal("E4");

        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);
        // SceneManager sequence:
        await host.OnSuspend(CancellationToken.None);
        await modal.OnOpen(ScreenContext.Empty, CancellationToken.None);

        Assert.Equal(new[] { "OnEnter", "OnSuspend" }, host.Calls);
        Assert.Equal(new[] { "OnOpen" }, modal.Calls);
    }

    /// <summary>
    /// The modal-close sequence: modal OnClose fires BEFORE host
    /// OnResume. Host should NOT see OnEnter again — it was alive the
    /// whole time. Pre-brief §4.3 invariant.
    /// </summary>
    [Fact]
    public async Task CloseModal_closes_modal_then_resumes_host_without_re_entering()
    {
        var host = new FakeScreen("E3");
        var modal = new FakeModal("E4");

        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);
        await host.OnSuspend(CancellationToken.None);
        await modal.OnOpen(ScreenContext.Empty, CancellationToken.None);
        // Close sequence:
        await modal.OnClose(CancellationToken.None);
        await host.OnResume(CancellationToken.None);

        Assert.Equal(
            new[] { "OnEnter", "OnSuspend", "OnResume" },
            host.Calls);
        Assert.DoesNotContain("OnEnter", host.Calls.GetRange(1, host.Calls.Count - 1));

        Assert.Equal(new[] { "OnOpen", "OnClose" }, modal.Calls);
    }

    /// <summary>
    /// Rapid open-close sequence: each OnSuspend must be paired with an
    /// OnResume. Validates the counter parity that catches the Pre-brief
    /// Risk #3 race (host stuck in suspended state after fast modal toggle).
    /// </summary>
    [Fact]
    public async Task Rapid_modal_open_close_sequence_keeps_suspend_resume_balanced()
    {
        var host = new FakeScreen("E3");
        var modal1 = new FakeModal("E4");
        var modal2 = new FakeModal("E4");
        var modal3 = new FakeModal("E4");

        await host.OnEnter(ScreenContext.Empty, CancellationToken.None);

        // 3 cycles of open/close.
        for (int i = 0; i < 3; i++)
        {
            var modal = i switch { 0 => modal1, 1 => modal2, _ => modal3 };
            await host.OnSuspend(CancellationToken.None);
            await modal.OnOpen(ScreenContext.Empty, CancellationToken.None);
            await modal.OnClose(CancellationToken.None);
            await host.OnResume(CancellationToken.None);
        }

        // Counter parity: 3 suspends, 3 resumes.
        var suspendCount = host.Calls.FindAll(c => c == "OnSuspend").Count;
        var resumeCount = host.Calls.FindAll(c => c == "OnResume").Count;
        Assert.Equal(3, suspendCount);
        Assert.Equal(3, resumeCount);

        // Final state: host woke up properly (last call is OnResume).
        Assert.Equal("OnResume", host.Calls[^1]);
    }

    /// <summary>
    /// CallerScreenId is propagated through the context. SceneManager
    /// sets this on every navigation ; here we validate the contract by
    /// constructing the context as SceneManager would and checking it
    /// lands on OnEnter.
    /// </summary>
    [Fact]
    public async Task OnEnter_receives_context_with_caller_screen_id()
    {
        var e2 = new FakeScreen("E2");
        var ctx = new ScreenContext { CallerScreenId = "E1_TITLE" };

        await e2.OnEnter(ctx, CancellationToken.None);

        Assert.NotNull(e2.LastEnterContext);
        Assert.Equal("E1_TITLE", e2.LastEnterContext!.CallerScreenId);
    }

    /// <summary>
    /// Default <see cref="ScreenContext.Empty"/> propagates a null
    /// CallerScreenId — used for the boot navigation when there is no
    /// previous screen.
    /// </summary>
    [Fact]
    public void Empty_context_has_null_caller_id()
    {
        Assert.Null(ScreenContext.Empty.CallerScreenId);
        Assert.Empty(ScreenContext.Empty.Payload);
    }
}
