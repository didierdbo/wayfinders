using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton that owns the single live tooltip Control on the
/// scene tree, schedules the 600ms hover delay, and runs fade in/out
/// tweens. Consumed primarily by the E2 World Map POI buttons in J3 ;
/// extended later for E3 quartiers / E4 caviardages / E5 ambient PNJ
/// tags as P4 onboarding-pops grow.
///
/// <para>
/// <b>J3 scope (D-J3-02 implementation, slot inerte respected).</b>
/// This is a <i>minimal</i> tooltip controller, not the full Varn §4 P4
/// onboarding-pops system. Three behaviours only:
/// <list type="number">
///   <item><see cref="RequestTooltip"/> stores the pending text/position
///         and starts a 600ms <see cref="SceneTreeTimer"/>. If a request
///         was already pending, it cancels the previous one (no
///         queueing — last hover wins).</item>
///   <item>When the timer fires, the tooltip Control's text and
///         position are set, and a fade-in tween runs to alpha = 1.</item>
///   <item><see cref="CancelTooltip"/> kills any pending timer and runs
///         a fade-out tween to alpha = 0.</item>
/// </list>
/// Cool-downs, hierarchy of importance, opt-in acknowledgement, log
/// persistence — all post-MVP in the full
/// <see cref="OnboardingPops"/> system.
/// </para>
///
/// <para>
/// <b>E2.2 step 2 extension (2026-05-17).</b> The simple Label tooltip
/// (used by E1 POI, E2 cell, E2 NPC portrait, E3 POI, legacy E2 POI) is
/// preserved verbatim. A parallel mission-tooltip path is added :
/// <see cref="RequestMissionTooltip"/> renders a PanelContainer
/// (<c>MissionTooltip.tscn</c>) with one <see cref="Button"/> per
/// <see cref="MissionTooltipRow"/>. Each row carries a hover-highlight
/// (Button's built-in <c>theme/styles/hover</c>), a pointing-hand cursor
/// (<see cref="Control.MouseDefaultCursorShape"/> =
/// <see cref="Control.CursorShape.PointingHand"/>), and dispatches its
/// <see cref="MissionTooltipRow.MissionId"/> through a caller-supplied
/// <c>onRowClicked</c> delegate. Step 2 wires the delegate to a no-op
/// (the click is logged) ; step 3 will swap in the mission-panel
/// slide-in.
/// </para>
///
/// <para>
/// <b>Sticky bridge.</b> The simple tooltip auto-cancels on POI
/// hover-out. The mission tooltip must stay visible while the cursor is
/// on the tooltip itself (so the player can hover individual rows). The
/// bridge state machine has two booleans :
/// <list type="bullet">
///   <item><see cref="_anchorHovered"/> — true while the POI marker
///         under the tooltip is hovered. Flipped by the caller via
///         <see cref="RequestMissionTooltip"/> (hover-in) and
///         <see cref="CancelMissionTooltip"/> (hover-out).</item>
///   <item><see cref="_tooltipHovered"/> — true while the cursor is on
///         the mission tooltip panel OR on any of its row Buttons. The
///         Buttons are wired explicitly because a Stop-filter Button
///         child <i>steals</i> the parent Panel's mouse_exited (Godot 4
///         quirk : a child Stop-filter Control covering a pixel makes
///         the parent's mouse_exited fire even though the cursor is
///         still inside the parent's rect).</item>
/// </list>
/// The tooltip stays visible while EITHER flag is true. When both go
/// false, the fade-out runs. This is the "POI ↔ tooltip sticky bridge"
/// the spec calls out.
/// </para>
///
/// <para>
/// <b>Stale-timer discipline (E2.2 step 2 bugfix 2026-05-17).</b>
/// <see cref="SceneTreeTimer"/> has no <c>Cancel()</c> in Godot 4 — once
/// scheduled, it WILL fire. The earlier shape stored the latest timer
/// in a private field and bailed when that field was null, but the test
/// missed the case where a NEW request had replaced the field with a
/// NEW timer instance. The old timer would then fire, see the field
/// non-null, and proceed to mutate the panel with the STALE payload
/// (bug 1 — tooltip A stays visible after switching to POI B ; bug 3 —
/// new tooltip B never renders because the stale fire nulled the field
/// the new fire was checking). The fix is a monotonically incrementing
/// <see cref="_missionRequestGeneration"/> counter captured by each
/// scheduled lambda ; only the lambda whose captured token equals the
/// current generation is allowed to run.
/// </para>
///
/// <para>
/// <b>Risk #3 mitigation (tooltip orphelin if E2 freed mid-hover).</b>
/// E1WorldMap's <c>_ExitTree</c> calls
/// <see cref="CancelTooltip"/>. Without that, the autoload (which
/// outlives E2) would still own a scheduled timer that fires after E2
/// is gone, briefly showing a tooltip on the next screen. Cancellation
/// is the discipline. <see cref="CancelMissionTooltip"/> with
/// <c>force = true</c> performs the same role for the mission tooltip
/// (forces both bridge flags to false so the fade actually fires even
/// if the cursor happened to be on the tooltip when E2 freed).
/// </para>
///
/// <para>
/// <b>Why a Node not a RefCounted.</b> The autoload contract requires a
/// <see cref="Node"/>. The tooltip Control lives inside an internal
/// <see cref="CanvasLayer"/> wrapper (<see cref="_tooltipCanvasLayer"/>,
/// layer = 100) so it always renders <i>above</i> every screen-local
/// CanvasLayer (DecorationLayer = 2, ChromeLayer = 3, etc.) and is
/// immune to the world Camera2D's canvas_transform — anchors are
/// therefore expressed in <b>screen-space pixels</b>, not world coords.
/// Callsites must pass
/// <c>hotspot.GetGlobalTransformWithCanvas().Origin</c> for Node2D
/// hotspots, never the raw <c>GlobalPosition</c>.
/// </para>
/// </summary>
public partial class HoverTooltipController : Node
{
    private const string TooltipScenePath = "res://scenes/ui/HoverTooltip.tscn";
    private const string MissionTooltipScenePath = "res://scenes/ui/MissionTooltip.tscn";

    /// <summary>Hover delay before a tooltip surfaces (Varn §3.2.3 spec).</summary>
    private const float HoverDelaySeconds = 0.6f;

    /// <summary>Fade-in duration once the tooltip becomes visible.</summary>
    private const float FadeInSeconds = 0.2f;

    /// <summary>Fade-out duration on hover-exit or cancel.</summary>
    private const float FadeOutSeconds = 0.1f;

    /// <summary>
    /// Pixel offset from the requested anchor to where the tooltip's
    /// top-left corner lands. Negative Y so the tooltip floats <i>above</i>
    /// the POI, not on top of it.
    /// </summary>
    private static readonly Vector2 TooltipOffset = new(-8, -56);

    /// <summary>
    /// CanvasLayer the tooltip is parented into. Set high enough
    /// (= 100) to stack above every screen's chrome layers (currently
    /// max layer = 3 for ChromeLayer across E1/E2/E3). A CanvasLayer's
    /// canvas_transform is identity by default — coordinates here are
    /// raw viewport pixels, which matches what
    /// <c>Node2D.GetGlobalTransformWithCanvas().Origin</c> returns.
    /// </summary>
    private const int TooltipCanvasLayerIndex = 100;

    // -- E2.2 step 2 typography (Didier-locked 2026-05-17) ---------------
    // Black font + white outline. Applied via a shared C#-built Theme
    // attached to the mission tooltip root so every Label + Button inside
    // inherits without repeating the override on each node. Locked at
    // outline_size = 4 px ; thinner reads as a halo, thicker overwhelms
    // the small text size.
    private const int TooltipTypoOutlineSizePx = 4;
    private static readonly Color TooltipTypoFontColor = new(0f, 0f, 0f, 1f);
    private static readonly Color TooltipTypoFontOutlineColor = new(1f, 1f, 1f, 1f);

    // -- E2.2 step 2 mission-row hover highlight (Didier choice 2026-05-17).
    // Slight terracotta wash on hover. The button's flat=true keeps the
    // normal state transparent so the parchment panel reads through ;
    // hover swaps in this StyleBoxFlat. Kept as a constant so a future
    // tweak is one-line.
    private static readonly Color RowHoverBgColor = new(0.78f, 0.55f, 0.38f, 0.35f);

    private CanvasLayer? _tooltipCanvasLayer;

    // -- Simple Label tooltip (E1 POI, E2 cell, E2 NPC, E3 POI, legacy E2)
    private PanelContainer? _tooltipPanel;
    private Label? _tooltipLabel;
    private SceneTreeTimer? _pendingTimer;
    private Tween? _fadeTween;

    // -- E2.2 step 2 mission tooltip (PanelContainer + VBox + per-row Button)
    private PanelContainer? _missionTooltipPanel;
    private VBoxContainer? _missionTooltipVbox;
    private Tween? _missionFadeTween;
    private Theme? _missionTooltipTheme;

    /// <summary>
    /// Monotonically incrementing token. Each
    /// <see cref="RequestMissionTooltip"/> call bumps this AND captures
    /// the new value into the timer lambda's closure. When the timer
    /// fires, it compares its captured value against the current value :
    /// stale timers (from a previous request that was superseded) silently
    /// no-op. This is the canonical workaround for Godot 4's
    /// <see cref="SceneTreeTimer"/> having no <c>Cancel()</c>.
    /// </summary>
    private int _missionRequestGeneration;

    /// <summary>
    /// Sticky bridge -- true while the anchoring POI is hovered. Flipped
    /// by <see cref="RequestMissionTooltip"/> (true) and
    /// <see cref="CancelMissionTooltip"/> (false).
    /// </summary>
    private bool _anchorHovered;

    /// <summary>
    /// Sticky bridge -- true while the cursor is on the mission tooltip
    /// panel OR on any of its row Buttons. Flipped by both the Panel's
    /// MouseEntered/MouseExited and by each row Button's
    /// MouseEntered/MouseExited (the Buttons must opt in explicitly --
    /// a Stop-filter child Control steals the parent's mouse_exited).
    /// </summary>
    private bool _tooltipHovered;

    /// <summary>
    /// Caller-supplied callback fired when a row Button is pressed.
    /// Receives the row's <see cref="MissionTooltipRow.MissionId"/>. Null
    /// when no mission tooltip is live. Captured at
    /// <see cref="RequestMissionTooltip"/> time so a stale callback from
    /// a previous request never fires.
    /// </summary>
    private Action<string>? _onRowClicked;

    /// <summary>
    /// Per-row captured handlers, kept so <see cref="ClearMissionRows"/>
    /// disconnects with the EXACT lambdas wired at build time. Mirrors
    /// the Risk #1 captured-reference discipline used by every other
    /// hover-handler dict in this codebase (E2 markers, E2 cells, NPC
    /// portraits). Carries the Pressed handler AND the
    /// MouseEntered/MouseExited handlers (the latter feed the sticky
    /// bridge -- see <see cref="_tooltipHovered"/>). Also carries the
    /// MinimumSizeChanged handler on the row's inner VBox so we can
    /// disconnect it cleanly at teardown -- see
    /// <see cref="BuildMissionTooltipChildren"/> for the
    /// min-size-propagation rationale.
    /// </summary>
    private readonly List<RowHandlers> _rowHandlers = new();

    private readonly record struct RowHandlers(
        Button Button,
        VBoxContainer InnerVbox,
        Action Pressed,
        Action MouseEntered,
        Action MouseExited,
        Action InnerMinSizeChanged);

    public override void _Ready()
    {
        _tooltipCanvasLayer = new CanvasLayer
        {
            Name = "TooltipCanvasLayer",
            Layer = TooltipCanvasLayerIndex,
        };
        AddChild(_tooltipCanvasLayer);

        // -- Simple tooltip path (kept verbatim from J3) ------------------
        var simpleScene = GD.Load<PackedScene>(TooltipScenePath);
        if (simpleScene is null)
        {
            GD.PushError($"[HoverTooltipController] failed to load {TooltipScenePath} — simple tooltips disabled");
        }
        else
        {
            _tooltipPanel = simpleScene.Instantiate<PanelContainer>();
            _tooltipLabel = _tooltipPanel.GetNode<Label>("Label");
            _tooltipPanel.Modulate = new Color(1, 1, 1, 0);
            _tooltipPanel.Visible = false;
            _tooltipCanvasLayer.AddChild(_tooltipPanel);
        }

        // -- E2.2 step 2 mission tooltip path ---------------------------
        _missionTooltipTheme = BuildMissionTooltipTheme();
        var missionScene = GD.Load<PackedScene>(MissionTooltipScenePath);
        if (missionScene is null)
        {
            GD.PushError($"[HoverTooltipController] failed to load {MissionTooltipScenePath} — mission tooltips disabled");
        }
        else
        {
            _missionTooltipPanel = missionScene.Instantiate<PanelContainer>();
            _missionTooltipVbox = _missionTooltipPanel.GetNode<VBoxContainer>("VBox");
            _missionTooltipPanel.Modulate = new Color(1, 1, 1, 0);
            _missionTooltipPanel.Visible = false;
            // The mission panel needs to STOP mouse events (so the sticky
            // bridge MouseEntered/MouseExited fire on it). The simple
            // tooltip uses IGNORE because it has no interactivity ; we
            // diverge here deliberately. The VBox child stays at Pass so
            // hover events that land on the VBox empty band (between
            // rows, in the separation gap) propagate up to the Panel and
            // keep the bridge alive.
            _missionTooltipPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            if (_missionTooltipVbox is not null)
            {
                _missionTooltipVbox.MouseFilter = Control.MouseFilterEnum.Pass;
            }
            _missionTooltipPanel.Theme = _missionTooltipTheme;
            _missionTooltipPanel.MouseEntered += OnMissionTooltipMouseEntered;
            _missionTooltipPanel.MouseExited += OnMissionTooltipMouseExited;
            _tooltipCanvasLayer.AddChild(_missionTooltipPanel);
        }

        GD.Print(
            $"[HoverTooltipController] mounted in CanvasLayer layer={TooltipCanvasLayerIndex}, " +
            $"simple_panel={_tooltipPanel is not null}, " +
            $"mission_panel={_missionTooltipPanel is not null}, " +
            $"theme_built={_missionTooltipTheme is not null}");
    }

    /// <summary>
    /// Schedule a (simple Label) tooltip to appear after
    /// <see cref="HoverDelaySeconds"/>. If another request is pending, it
    /// is cancelled — only the most recent hover wins. Used by E1 POI,
    /// E2 cell, E2 NPC portrait, E3 POI, and the legacy E2 POI dispatch
    /// path. The E2 mission-marker hover (which needs per-row
    /// interactivity) calls <see cref="RequestMissionTooltip"/> instead.
    /// </summary>
    /// <param name="text">Final tooltip text (already interpolated).</param>
    /// <param name="anchorScreenPosition">
    /// <b>Screen-space</b> (viewport pixel) position used as the anchor
    /// for the tooltip's top-left corner. The actual paint position adds
    /// <see cref="TooltipOffset"/>. For Node2D hotspots, callers must
    /// pass <c>hotspot.GetGlobalTransformWithCanvas().Origin</c>, not
    /// the raw <c>GlobalPosition</c> — the tooltip lives in a dedicated
    /// CanvasLayer (identity transform) and is not subject to the world
    /// Camera2D's canvas_transform.
    /// </param>
    public void RequestTooltip(string text, Vector2 anchorScreenPosition)
    {
        if (_tooltipPanel is null || _tooltipLabel is null) return;

        // Cancel any in-flight schedule + fade. We always restart from
        // a clean state — no race between an old fade-in and a new one.
        _pendingTimer = null;
        _fadeTween?.Kill();
        _fadeTween = null;

        _pendingTimer = GetTree().CreateTimer(HoverDelaySeconds);
        _pendingTimer.Timeout += () => OnHoverDelayElapsed(text, anchorScreenPosition);
    }

    /// <summary>
    /// Cancel any pending or visible (simple Label) tooltip. Called by
    /// E1WorldMap's <c>_ExitTree</c> (Risk #3 mitigation) and on
    /// hover-out from individual POIs.
    /// </summary>
    public void CancelTooltip()
    {
        _pendingTimer = null;

        if (_tooltipPanel is null) return;
        if (!_tooltipPanel.Visible && _tooltipPanel.Modulate.A == 0f) return;

        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_tooltipPanel, "modulate:a", 0f, FadeOutSeconds);
        _fadeTween.TweenCallback(Callable.From(HideAfterFadeOut));
    }

    private void OnHoverDelayElapsed(string text, Vector2 anchorScreenPosition)
    {
        // If CancelTooltip ran between schedule and timer fire,
        // _pendingTimer is null — bail before mutating the panel.
        if (_pendingTimer is null) return;
        if (_tooltipPanel is null || _tooltipLabel is null) return;

        _pendingTimer = null;

        _tooltipLabel.Text = text;
        // The tooltip lives inside a dedicated CanvasLayer (identity
        // canvas_transform), so setting Position with screen-space
        // pixels places it directly under the cursor's world-projected
        // anchor without any further transform math.
        _tooltipPanel.Position = anchorScreenPosition + TooltipOffset;
        _tooltipPanel.Visible = true;

        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_tooltipPanel, "modulate:a", 1f, FadeInSeconds);
    }

    private void HideAfterFadeOut()
    {
        if (_tooltipPanel is null) return;
        _tooltipPanel.Visible = false;
    }

    // ===================================================================
    //  E2.2 step 2 -- Mission tooltip (rows are interactive Buttons)
    // ===================================================================

    /// <summary>
    /// Schedule an interactive mission tooltip to appear after
    /// <see cref="HoverDelaySeconds"/>. The tooltip renders a header
    /// Label + one <see cref="Button"/> per row inside a shared
    /// <see cref="VBoxContainer"/>. Each row Button carries a
    /// hover-highlight (built-in <c>theme/styles/hover</c>), a
    /// pointing-hand cursor, and dispatches its
    /// <see cref="MissionTooltipRow.MissionId"/> through
    /// <paramref name="onRowClicked"/> on <see cref="Button.Pressed"/>.
    ///
    /// <para>
    /// <b>Sticky bridge anchor flag.</b> This call flips
    /// <see cref="_anchorHovered"/> to true. The matching
    /// <see cref="CancelMissionTooltip"/> on POI hover-out flips it back
    /// to false. The tooltip stays visible while either
    /// <see cref="_anchorHovered"/> OR <see cref="_tooltipHovered"/> is
    /// true.
    /// </para>
    ///
    /// <para>
    /// <b>Fast-switch behaviour (bugfix 2026-05-17, bug 1 fix).</b> If a
    /// mission tooltip is already on-screen (Visible AND alpha > 0) when
    /// a new request lands for a DIFFERENT POI, the previous content is
    /// torn down synchronously and the new content is rendered
    /// IMMEDIATELY, bypassing the 600ms hover delay. The semantic match
    /// is "the player is already in tooltip-reading mode, just swap the
    /// content" -- same pattern as a stock OS tooltip following the
    /// cursor across hot zones.
    /// </para>
    /// </summary>
    /// <param name="payload">Header text + rows. If
    /// <see cref="MissionTooltipPayload.Rows"/> is empty, the call is a
    /// no-op (matches the step 1 "pas de tooltip si pas de mission"
    /// contract). The payload is typically built via
    /// <see cref="MissionTooltipRows.Compose"/>.</param>
    /// <param name="anchorScreenPosition">Screen-space anchor, same
    /// contract as <see cref="RequestTooltip"/>.</param>
    /// <param name="onRowClicked">Callback fired with the row's
    /// MissionId on Button.Pressed. At step 2 the caller passes a
    /// no-op-with-log ; step 3 will swap in the mission-panel
    /// slide-in. May be null -- rows then render with cursor=pointing
    /// hand but the click does nothing.</param>
    public void RequestMissionTooltip(
        MissionTooltipPayload payload,
        Vector2 anchorScreenPosition,
        Action<string>? onRowClicked)
    {
        if (_missionTooltipPanel is null || _missionTooltipVbox is null) return;
        if (payload.Rows.Count == 0) return;

        // Bump the generation token. Any in-flight timer that captured
        // an older value will compare-fail in OnMissionHoverDelayElapsed
        // and silently no-op. Required because Godot 4's SceneTreeTimer
        // has no Cancel() -- a scheduled timer WILL fire eventually
        // (bug 1 + bug 3 race fix).
        _missionRequestGeneration += 1;
        var thisGeneration = _missionRequestGeneration;

        _anchorHovered = true;
        // Reset the tooltip-hovered flag to false on every new request.
        // The cursor is now over a (new) POI marker, not the tooltip --
        // a stale "true" from a previous switch would keep the fade-out
        // suppressed even when both POI and tooltip are no longer
        // hovered, leaking an orphan tooltip.
        _tooltipHovered = false;

        // Capture the click callback for this request. Stashed in a field
        // so the per-row Button.Pressed lambdas (built inside the timer
        // callback) read the right delegate even if a NEW
        // RequestMissionTooltip lands before the timer fires.
        _onRowClicked = onRowClicked;

        // Fast-switch path : tooltip already on-screen (post fade-in) ->
        // swap the content immediately. The 600ms hover delay only
        // applies when surfacing a tooltip FROM nothing ; once the
        // player is already in tooltip-reading mode, switching POI
        // should not re-introduce a 600ms blank.
        var alreadyVisible =
            _missionTooltipPanel.Visible
            && _missionTooltipPanel.Modulate.A > 0f;

        if (alreadyVisible)
        {
            _missionFadeTween?.Kill();
            _missionFadeTween = null;

            BuildMissionTooltipChildren(payload);
            _missionTooltipPanel.Position = anchorScreenPosition + TooltipOffset;
            // Snap to fully opaque -- no re-fade-in mid-swap, that reads
            // as a flicker not a content change.
            _missionTooltipPanel.Modulate = new Color(1, 1, 1, 1);
            return;
        }

        // Cold-surface path : kill any lingering fade, schedule the
        // 600ms delay. The lambda captures `thisGeneration` so a stale
        // fire (after a subsequent Request or Cancel) is silently
        // discarded by the guard in OnMissionHoverDelayElapsed.
        _missionFadeTween?.Kill();
        _missionFadeTween = null;

        var pendingTimer = GetTree().CreateTimer(HoverDelaySeconds);
        pendingTimer.Timeout += () => OnMissionHoverDelayElapsed(
            thisGeneration, payload, anchorScreenPosition);
    }

    /// <summary>
    /// Notify the controller that the anchoring POI is no longer hovered.
    /// Flips <see cref="_anchorHovered"/> to false. If
    /// <see cref="_tooltipHovered"/> is also false, the tooltip fades out
    /// ; otherwise the tooltip stays visible (sticky bridge -- the
    /// cursor is on the tooltip itself, the player is hovering a row).
    /// </summary>
    /// <param name="force">If true, force the fade-out regardless of the
    /// tooltip-hovered flag. Used by the screen's <c>_ExitTree</c> to
    /// guarantee no orphan tooltip persists after a scene change.</param>
    public void CancelMissionTooltip(bool force = false)
    {
        _anchorHovered = false;
        if (force) _tooltipHovered = false;

        // Bump the generation so any in-flight timer becomes stale.
        // Required even for force=false : if the player hovered POI A,
        // hovered out before 600ms, the scheduled timer A is still
        // queued and would fire if we did not invalidate it here. Bug 1
        // root cause -- the previous shape only nulled the field
        // reference, which the stale-timer fire ignored.
        _missionRequestGeneration += 1;

        if (_tooltipHovered) return; // sticky bridge holds the tooltip open
        FadeOutMissionTooltip();
    }

    private void OnMissionHoverDelayElapsed(
        int capturedGeneration,
        MissionTooltipPayload payload,
        Vector2 anchorScreenPosition)
    {
        // Generation guard : the closure captured the generation that
        // was current when the timer was scheduled. Any later Request or
        // Cancel bumped the counter ; the captured value no longer
        // matches and this fire is stale. Drop silently.
        if (capturedGeneration != _missionRequestGeneration) return;
        if (_missionTooltipPanel is null || _missionTooltipVbox is null) return;
        // Also bail if the anchor flag flipped back to false during the
        // delay (defends against a single race where the generation
        // counter happens to round-trip back to the captured value --
        // theoretical, but the cost is one branch).
        if (!_anchorHovered) return;

        BuildMissionTooltipChildren(payload);

        _missionTooltipPanel.Position = anchorScreenPosition + TooltipOffset;
        _missionTooltipPanel.Visible = true;

        _missionFadeTween?.Kill();
        _missionFadeTween = CreateTween();
        _missionFadeTween.TweenProperty(_missionTooltipPanel, "modulate:a", 1f, FadeInSeconds);
    }

    /// <summary>
    /// Spawn the header Label + one row Button per
    /// <see cref="MissionTooltipRow"/> under
    /// <see cref="_missionTooltipVbox"/>. Children of the previous hover
    /// are torn down synchronously (via <see cref="Node.Free"/>, NOT
    /// QueueFree) so the new render is the only content visible on the
    /// VERY NEXT frame -- the fast-switch path relies on this invariant
    /// to avoid a one-frame flash of the old payload's rows next to the
    /// new ones.
    ///
    /// <para>
    /// <b>Row visual shape (bug 3 fix 2026-05-17).</b> Each row is a
    /// <see cref="Button"/> whose Text is empty ; the visual is built
    /// from a child <see cref="VBoxContainer"/> holding two
    /// <see cref="Label"/>s (DisplayName + NarrativeHook). Reason :
    /// Godot 4's Button rendering does not reliably expose multi-line
    /// text -- the minimum-size calculation assumes a single line, the
    /// extra lines from a "\n"-separated string render below the
    /// allocated rect and are clipped. The previous shape (Button.Text
    /// = "DisplayName\n  NarrativeHook") produced invisible rows under
    /// the parchment panel : the Button claimed font-height-x1 of
    /// vertical space, the Labels rendered outside that rect, the panel
    /// drew over them. Switching to a Button-with-child-Labels layout
    /// puts the labels INSIDE the Button's child band, which the VBox
    /// parent then sizes correctly via min-size propagation. The
    /// Button's click + hover surface remains the full rect because
    /// <see cref="Control.MouseFilterEnum.Stop"/> stays on the Button
    /// and the child VBox + Labels are
    /// <see cref="Control.MouseFilterEnum.Ignore"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Min-size propagation gotcha (panel-wrap fix 2026-05-17).</b>
    /// <see cref="Button"/> is a <see cref="Control"/>, NOT a
    /// <see cref="Container"/>. Adding a <see cref="VBoxContainer"/>
    /// child does NOT make the Button recompute its min-size from the
    /// VBox's combined min-size -- the Button reports its own native
    /// min-size (single-line font height, ~icon size), the inner VBox
    /// renders at its own min-size at the Button's (0,0), and the parent
    /// <see cref="PanelContainer"/> sizes the StyleBox only to the
    /// Button's tiny reported rect. Result : the parchment panel wraps
    /// just the district header, the rows visually overflow.
    /// The fix is twofold :
    /// <list type="number">
    ///   <item>Push the inner VBox's combined min-size up into
    ///         <see cref="Control.CustomMinimumSize"/> on the Button
    ///         (so the parent <see cref="VBoxContainer"/> + grand-parent
    ///         <see cref="PanelContainer"/> see the right
    ///         min-size).</item>
    ///   <item>Anchor-fill the inner VBox to the Button's rect (so
    ///         once the VBoxContainer parent stretches the Button to
    ///         full width via <see cref="Control.SizeFlags.ExpandFill"/>,
    ///         the inner VBox + its Labels stretch with it instead of
    ///         hugging their min-width at the top-left corner).</item>
    /// </list>
    /// The propagation is re-evaluated on every
    /// <see cref="Control.MinimumSizeChanged"/> emission from the inner
    /// VBox so font swaps / locale changes / text edits stay in sync.
    /// An initial deferred call covers the first paint after AddChild
    /// (the signal isn't guaranteed to fire on construction).
    /// </para>
    /// </summary>
    private void BuildMissionTooltipChildren(MissionTooltipPayload payload)
    {
        if (_missionTooltipVbox is null) return;

        ClearMissionRows();

        // Header Label -- inherits the black + white-outline overrides
        // from the panel's Theme.
        var header = new Label
        {
            Name = "Header",
            Text = payload.DistrictDisplayName,
            // MouseFilter Ignore on the header so MouseEntered on the
            // tooltip panel still fires through (we want sticky bridge
            // even when the cursor is over the header).
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // Header reads slightly bigger than the row text -- Varn-locked
        // tonal hierarchy (district = the umbrella, missions = the
        // discrete entries).
        header.AddThemeFontSizeOverride("font_size", 20);
        _missionTooltipVbox.AddChild(header);

        foreach (var row in payload.Rows)
        {
            var button = new Button
            {
                Name = $"Row_{row.MissionId}",
                // Flat = true keeps the normal state transparent (parchment
                // panel reads through). The hover StyleBox swaps in the
                // terracotta wash. Pressed StyleBox stays empty -- the
                // press is dispatched, the visual feedback is the fade-out
                // of the tooltip (panel slides in at step 3).
                Flat = true,
                // Pointing-hand cursor on hover -- E2.2 step 2 spec.
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                // The text comes from the child Labels (see bug 3 doc
                // above). Button.Text stays empty so the engine does
                // NOT reserve a single-line text band that would push
                // the child Labels out.
                Text = string.Empty,
                // The Button MUST stop the mouse so it gets click +
                // hover events. Child VBox + Labels are set to Ignore
                // below so they don't steal the Button's events.
                MouseFilter = Control.MouseFilterEnum.Stop,
                // Stretch the button to the full VBox width -- without
                // this the Button shrinks to its content width and the
                // hover-highlight only paints a thin sliver instead of
                // the row's full span.
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = false,
            };
            // Hover highlight : a per-button StyleBoxFlat override on the
            // "hover" theme key. Locked at terracotta wash 0.35 alpha
            // (RowHoverBgColor). Keeping it per-button (not via the
            // shared theme) makes the hover paint only over the
            // button's own rect, not the whole VBox row.
            var hoverStyle = new StyleBoxFlat
            {
                BgColor = RowHoverBgColor,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                ContentMarginLeft = 6,
                ContentMarginRight = 6,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
            };
            button.AddThemeStyleboxOverride("hover", hoverStyle);
            // Normal + pressed stay flat (transparent) -- match the
            // parchment panel.
            var emptyStyle = new StyleBoxEmpty
            {
                ContentMarginLeft = 6,
                ContentMarginRight = 6,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
            };
            button.AddThemeStyleboxOverride("normal", emptyStyle);
            button.AddThemeStyleboxOverride("pressed", emptyStyle);

            // Inner VBox holding the two visible Labels. Mouse_filter
            // Ignore on the VBox + both Labels so every mouse event
            // falls through to the Button beneath. The Button's
            // Pressed/MouseEntered/MouseExited stay wired correctly
            // because the children are Ignore-transparent.
            //
            // Anchor preset = FullRect so the inner VBox stretches with
            // the Button (which gets stretched by the OUTER VBox via
            // ExpandFill). Without this, the inner VBox hugs its
            // min-width at (0,0) and the rim of the Button stays empty
            // even though the Button's own rect spans the panel width.
            var rowInnerVbox = new VBoxContainer
            {
                Name = "Inner",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rowInnerVbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            // Tight separation -- the two lines are conceptually one
            // mission, not two independent items.
            rowInnerVbox.AddThemeConstantOverride("separation", 0);

            var titleLabel = new Label
            {
                Name = "Title",
                Text = row.DisplayName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var hookLabel = new Label
            {
                Name = "Hook",
                // Indent matches the step 1 composer's "  " visual
                // rhythm so the rumor hook sits under (and inset from)
                // the title.
                Text = "  " + row.NarrativeHook,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            // The hook reads one step smaller than the title -- visual
            // hierarchy (mission name first, hook second).
            hookLabel.AddThemeFontSizeOverride("font_size", 14);

            rowInnerVbox.AddChild(titleLabel);
            rowInnerVbox.AddChild(hookLabel);
            button.AddChild(rowInnerVbox);

            // Capture row.MissionId once per closure so each button
            // dispatches its own id (Risk #1 captured-reference
            // discipline). The handlers are also stashed in
            // _rowHandlers so ClearMissionRows can disconnect with the
            // exact lambda references at next-hover teardown.
            var capturedMissionId = row.MissionId;
            var capturedButton = button;
            var capturedInner = rowInnerVbox;
            Action pressed = () => OnRowButtonPressed(capturedMissionId);
            Action mouseEntered = OnRowMouseEntered;
            Action mouseExited = OnRowMouseExited;
            // Min-size propagation : Button is a Control, not a Container,
            // so it does NOT auto-bubble the inner VBox's combined min-size
            // into its own CustomMinimumSize. Without this, the parent
            // PanelContainer sizes the parchment StyleBox to the Button's
            // tiny native min-size and the rows visually overflow. We
            // wire MinimumSizeChanged so font swaps / locale edits /
            // future text mutations stay in sync.
            Action innerMinSizeChanged = () =>
                SyncRowButtonMinSize(capturedButton, capturedInner);
            button.Pressed += pressed;
            button.MouseEntered += mouseEntered;
            button.MouseExited += mouseExited;
            rowInnerVbox.MinimumSizeChanged += innerMinSizeChanged;
            _rowHandlers.Add(new RowHandlers(
                button, rowInnerVbox, pressed, mouseEntered, mouseExited,
                innerMinSizeChanged));

            _missionTooltipVbox.AddChild(button);

            // Initial min-size push : MinimumSizeChanged is not guaranteed
            // to fire on construction (Godot only emits when the value
            // actually changes from a previous reading). Deferred so the
            // inner VBox has been laid out at least once by the engine
            // before we sample GetCombinedMinimumSize().
            Callable.From(() => SyncRowButtonMinSize(capturedButton, capturedInner)).CallDeferred();
        }

        // Diagnostic log so a future regression is visible at boot. The
        // step-1 path already logs at compose time ; the step-2 path
        // adds this at render time so a "tooltip shows but rows look
        // wrong" report can be triaged against actual child counts and
        // names.
        GD.Print(
            $"[HoverTooltipController] BuildMissionTooltipChildren : " +
            $"district='{payload.DistrictDisplayName}', " +
            $"rows_in_payload={payload.Rows.Count}, " +
            $"vbox_children_after_build={_missionTooltipVbox.GetChildCount()}, " +
            $"first_row_name='{(_rowHandlers.Count > 0 ? _rowHandlers[0].Button.Name : "<none>")}'");
    }

    /// <summary>
    /// Push the inner VBox's combined min-size into the row Button's
    /// <see cref="Control.CustomMinimumSize"/> plus the Button's stylebox
    /// content margins (6 / 6 / 4 / 4 -- see <c>emptyStyle</c> /
    /// <c>hoverStyle</c> in <see cref="BuildMissionTooltipChildren"/>).
    /// Called once at deferred time after the row is added AND on every
    /// <see cref="Control.MinimumSizeChanged"/> emission from the inner
    /// VBox. Idempotent ; safe to call when the inner VBox is empty.
    /// Guarded against freed nodes via
    /// <see cref="GodotObject.IsInstanceValid"/> because the inner VBox
    /// is Free'd synchronously in <see cref="ClearMissionRows"/> but the
    /// deferred call from <c>BuildMissionTooltipChildren</c> may still
    /// be in the queue.
    /// </summary>
    private static void SyncRowButtonMinSize(Button button, VBoxContainer innerVbox)
    {
        if (!IsInstanceValid(button) || !IsInstanceValid(innerVbox)) return;
        var innerMin = innerVbox.GetCombinedMinimumSize();
        // The horizontal margin (left + right = 12) and vertical margin
        // (top + bottom = 8) match the empty/hover StyleBox content
        // margins. Hard-coded here for clarity ; if those margins are
        // re-tuned in BuildMissionTooltipChildren, mirror the change
        // here.
        button.CustomMinimumSize = new Vector2(
            innerMin.X + 12f,
            innerMin.Y + 8f);
    }

    private void OnRowButtonPressed(string missionId)
    {
        // Dispatch to the caller's callback if one is wired. At step 2
        // the caller's callback is a no-op-with-log -- the click does
        // not yet open a panel. Step 3 will swap in the slide-in.
        _onRowClicked?.Invoke(missionId);
    }

    /// <summary>
    /// Row Button MouseEntered handler. Keeps the sticky bridge alive
    /// while the cursor is on a row (the parent Panel's
    /// <c>mouse_exited</c> already fired when the cursor crossed onto
    /// the Button -- without THIS handler, the tooltip would fade out
    /// the moment the player tried to hover a row, defeating the entire
    /// interactive-row design).
    /// </summary>
    private void OnRowMouseEntered()
    {
        _tooltipHovered = true;
        // If the fade-out had started (cursor crossed POI->Button with
        // a gap that briefly registered as "neither POI nor tooltip
        // hovered"), kill it and snap back to fully visible.
        if (_missionTooltipPanel is not null
            && _missionTooltipPanel.Visible
            && _missionTooltipPanel.Modulate.A < 1f)
        {
            _missionFadeTween?.Kill();
            _missionFadeTween = CreateTween();
            _missionFadeTween.TweenProperty(
                _missionTooltipPanel, "modulate:a", 1f, FadeInSeconds);
        }
    }

    /// <summary>
    /// Row Button MouseExited handler. Re-evaluates the sticky bridge
    /// by checking whether the cursor is still inside the Panel's rect
    /// (rect-check, NOT signal-based -- because Godot's signals can't
    /// tell us "you exited the Button but you're still over the Panel
    /// at large"). If the cursor landed outside the entire panel, drop
    /// the bridge flag ; if the cursor merely crossed onto the Panel's
    /// empty band between rows or onto another row, keep the bridge
    /// alive (the next Row's MouseEntered or the Panel's MouseEntered
    /// will flip it back to true).
    /// </summary>
    private void OnRowMouseExited()
    {
        // Defer the rect-check by one frame -- Godot dispatches
        // mouse_exited on the OLD control BEFORE mouse_entered on the
        // NEW control. Reading the cursor position immediately would
        // see the cursor still inside the Panel rect even when the
        // next event is "Panel mouse_exited" too. CallDeferred lets the
        // sibling mouse_entered (if any) flip _tooltipHovered to true
        // first ; the deferred check then trusts the bridge flag set
        // by whichever entered next.
        CallDeferred(MethodName.ReevaluateTooltipHoveredAfterRowExit);
    }

    private void ReevaluateTooltipHoveredAfterRowExit()
    {
        if (_missionTooltipPanel is null) return;
        // The mouse position in canvas (CanvasLayer-local) coordinates.
        // Our panel lives in the dedicated identity-transform tooltip
        // CanvasLayer so this maps 1:1 with the panel's Position +
        // Size.
        var mousePos = _missionTooltipPanel.GetGlobalMousePosition();
        var rect = new Rect2(_missionTooltipPanel.Position, _missionTooltipPanel.Size);
        var insidePanel = rect.HasPoint(mousePos);

        if (insidePanel)
        {
            // Cursor is still somewhere over the Panel (empty band
            // between rows, header strip, another row about to fire
            // mouse_entered). Keep the bridge alive.
            _tooltipHovered = true;
            return;
        }

        // Cursor has left the Panel rect entirely. Drop the flag and
        // close the tooltip if the anchor POI is also no longer
        // hovered.
        _tooltipHovered = false;
        if (_anchorHovered) return;
        FadeOutMissionTooltip();
    }

    /// <summary>
    /// Disconnect every per-row Button.Pressed AND
    /// MouseEntered/MouseExited handler with the EXACT captured lambda
    /// references, then free every child Control under the mission
    /// tooltip VBox SYNCHRONOUSLY (Free, not QueueFree). Synchronous
    /// removal matters for the fast-switch path : QueueFree is deferred
    /// to the end of the frame, which would leave stale Buttons in the
    /// VBox for one frame and flash both payloads side by side.
    /// Idempotent ; safe to call when the VBox is already empty.
    /// </summary>
    private void ClearMissionRows()
    {
        foreach (var rh in _rowHandlers)
        {
            if (IsInstanceValid(rh.Button))
            {
                rh.Button.Pressed -= rh.Pressed;
                rh.Button.MouseEntered -= rh.MouseEntered;
                rh.Button.MouseExited -= rh.MouseExited;
            }
            if (IsInstanceValid(rh.InnerVbox))
            {
                rh.InnerVbox.MinimumSizeChanged -= rh.InnerMinSizeChanged;
            }
        }
        _rowHandlers.Clear();

        if (_missionTooltipVbox is null) return;
        // Use Free (synchronous) not QueueFree (deferred). The
        // fast-switch path rebuilds children in the same frame -- a
        // deferred free would leak the previous payload's rows into the
        // new render for one frame. The tradeoff is that we MUST hold
        // no captured references to these Buttons past this call
        // (which the _rowHandlers.Clear() above guarantees).
        foreach (var child in _missionTooltipVbox.GetChildren())
        {
            child.Free();
        }
    }

    private void OnMissionTooltipMouseEntered()
    {
        _tooltipHovered = true;
        // Mouse landed back on the tooltip while the fade-out was
        // running -- kill the fade and snap back to fully visible. This
        // catches the race where the cursor crosses from POI marker to
        // tooltip with a tiny gap (the POI's MouseExited fires, the
        // tooltip's MouseEntered fires a few frames later).
        if (_missionTooltipPanel is not null
            && _missionTooltipPanel.Visible
            && _missionTooltipPanel.Modulate.A < 1f)
        {
            _missionFadeTween?.Kill();
            _missionFadeTween = CreateTween();
            _missionFadeTween.TweenProperty(_missionTooltipPanel, "modulate:a", 1f, FadeInSeconds);
        }
    }

    private void OnMissionTooltipMouseExited()
    {
        // Defer the bridge update by one frame for the same reason as
        // OnRowMouseExited : Godot fires mouse_exited on the Panel when
        // the cursor crosses ONTO a Stop-filter child (Button row),
        // even though the cursor is still inside the Panel rect. A
        // deferred rect-check lets the child Button's mouse_entered
        // flip _tooltipHovered to true first ; only an actual exit
        // from the Panel rect drops the flag.
        CallDeferred(MethodName.ReevaluateTooltipHoveredAfterPanelExit);
    }

    private void ReevaluateTooltipHoveredAfterPanelExit()
    {
        if (_missionTooltipPanel is null) return;
        var mousePos = _missionTooltipPanel.GetGlobalMousePosition();
        var rect = new Rect2(_missionTooltipPanel.Position, _missionTooltipPanel.Size);
        if (rect.HasPoint(mousePos))
        {
            // Cursor crossed onto a child Button -- the Panel's
            // mouse_exited fires but the cursor is still over the
            // Panel rect. Keep the bridge alive ; the child Button's
            // mouse_entered (if it was a Button) already set it to true
            // anyway, this is just defence in depth.
            _tooltipHovered = true;
            return;
        }
        _tooltipHovered = false;
        if (_anchorHovered) return;
        FadeOutMissionTooltip();
    }

    private void FadeOutMissionTooltip()
    {
        if (_missionTooltipPanel is null) return;
        if (!_missionTooltipPanel.Visible && _missionTooltipPanel.Modulate.A == 0f) return;

        _missionFadeTween?.Kill();
        _missionFadeTween = CreateTween();
        _missionFadeTween.TweenProperty(_missionTooltipPanel, "modulate:a", 0f, FadeOutSeconds);
        _missionFadeTween.TweenCallback(Callable.From(HideMissionAfterFadeOut));
    }

    private void HideMissionAfterFadeOut()
    {
        if (_missionTooltipPanel is null) return;
        _missionTooltipPanel.Visible = false;
        // Tear down rows after the fade so the player does not see the
        // panel snap to empty before disappearing.
        ClearMissionRows();
        _onRowClicked = null;
    }

    /// <summary>
    /// Build the shared Theme attached to the mission tooltip root. Every
    /// Label + Button inside inherits the black-font + white-outline
    /// typography Didier locked at E2.2 step 2 (2026-05-17). One Theme
    /// instance, one mutation surface ; per-control overrides only when
    /// the visual diverges (e.g. the row Button's hover StyleBoxFlat).
    /// </summary>
    private static Theme BuildMissionTooltipTheme()
    {
        var theme = new Theme();
        // Label : font_color black, outline white, outline_size 4 px.
        theme.SetColor("font_color", "Label", TooltipTypoFontColor);
        theme.SetColor("font_outline_color", "Label", TooltipTypoFontOutlineColor);
        theme.SetConstant("outline_size", "Label", TooltipTypoOutlineSizePx);
        // Button covers normal / hover / pressed / disabled / focus colour
        // slots. We pin the font_color the same across normal + hover so
        // the row text stays black-on-parchment AND black-on-terracotta
        // (the wash is light enough that black + white outline reads
        // cleanly on both).
        theme.SetColor("font_color", "Button", TooltipTypoFontColor);
        theme.SetColor("font_hover_color", "Button", TooltipTypoFontColor);
        theme.SetColor("font_pressed_color", "Button", TooltipTypoFontColor);
        theme.SetColor("font_focus_color", "Button", TooltipTypoFontColor);
        theme.SetColor("font_outline_color", "Button", TooltipTypoFontOutlineColor);
        theme.SetConstant("outline_size", "Button", TooltipTypoOutlineSizePx);
        return theme;
    }
}
