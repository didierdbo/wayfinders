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
///         the mission tooltip panel. Flipped by the panel's
///         <c>MouseEntered</c> / <c>MouseExited</c> signals wired here.</item>
/// </list>
/// The tooltip stays visible while EITHER flag is true. When both go
/// false, the fade-out runs. This is the "POI ↔ tooltip sticky bridge"
/// the spec calls out.
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
    private SceneTreeTimer? _missionPendingTimer;
    private Tween? _missionFadeTween;
    private Theme? _missionTooltipTheme;

    /// <summary>
    /// Sticky bridge -- true while the anchoring POI is hovered. Flipped
    /// by <see cref="RequestMissionTooltip"/> (true) and
    /// <see cref="CancelMissionTooltip"/> (false).
    /// </summary>
    private bool _anchorHovered;

    /// <summary>
    /// Sticky bridge -- true while the cursor is on the mission tooltip
    /// panel itself. Flipped by the panel's MouseEntered (true) and
    /// MouseExited (false) signals.
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
    /// portraits).
    /// </summary>
    private readonly List<(Button button, Action pressed)> _rowPressedHandlers = new();

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
            // diverge here deliberately.
            _missionTooltipPanel.MouseFilter = Control.MouseFilterEnum.Stop;
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

        _anchorHovered = true;

        // Cancel any in-flight schedule + fade -- last hover wins, same
        // policy as the simple path.
        _missionPendingTimer = null;
        _missionFadeTween?.Kill();
        _missionFadeTween = null;

        // Capture the click callback for this request. Stashed in a field
        // so the per-row Button.Pressed lambdas (built inside the timer
        // callback) read the right delegate even if a NEW
        // RequestMissionTooltip lands before the timer fires.
        _onRowClicked = onRowClicked;

        _missionPendingTimer = GetTree().CreateTimer(HoverDelaySeconds);
        _missionPendingTimer.Timeout += () => OnMissionHoverDelayElapsed(payload, anchorScreenPosition);
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

        // Drop any pending timer that has not yet fired. If the player
        // hovered the POI, hovered out before 600ms, the row spawn
        // never happens -- correct.
        _missionPendingTimer = null;

        if (_tooltipHovered) return; // sticky bridge holds the tooltip open
        FadeOutMissionTooltip();
    }

    private void OnMissionHoverDelayElapsed(
        MissionTooltipPayload payload,
        Vector2 anchorScreenPosition)
    {
        // If CancelMissionTooltip flipped _anchorHovered to false between
        // schedule and timer fire, _missionPendingTimer is null -- bail
        // before mutating the panel. Symmetric with OnHoverDelayElapsed.
        if (_missionPendingTimer is null) return;
        if (_missionTooltipPanel is null || _missionTooltipVbox is null) return;
        // Also bail if the anchor flag flipped back to false during the
        // delay (a cleaner cancel than relying on the pending-timer-null
        // alone -- defends against a second RequestMissionTooltip racing
        // a CancelMissionTooltip on the same frame).
        if (!_anchorHovered) return;

        _missionPendingTimer = null;

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
    /// <see cref="_missionTooltipVbox"/>. Idempotent : any previous
    /// children (from a prior hover) are torn down first, including the
    /// per-row Button.Pressed handler disconnects.
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
                // Two-line layout : DisplayName + indented NarrativeHook.
                // Button.Text supports \n natively ; we feed it pre-baked
                // (the indent matches the step 1 composer's "  " indent
                // so the visual rhythm carries over).
                Text = row.DisplayName + "\n  " + row.NarrativeHook,
                // Left-align the multi-line text -- Buttons centre by
                // default which reads as floating.
                Alignment = HorizontalAlignment.Left,
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

            // Capture row.MissionId once per closure so each button
            // dispatches its own id (Risk #1 captured-reference
            // discipline). The handler is also stashed in
            // _rowPressedHandlers so ClearMissionRows can disconnect
            // with the exact lambda reference at next-hover teardown.
            var capturedMissionId = row.MissionId;
            Action pressed = () => OnRowButtonPressed(capturedMissionId);
            button.Pressed += pressed;
            _rowPressedHandlers.Add((button, pressed));

            _missionTooltipVbox.AddChild(button);
        }
    }

    private void OnRowButtonPressed(string missionId)
    {
        // Dispatch to the caller's callback if one is wired. At step 2
        // the caller's callback is a no-op-with-log -- the click does
        // not yet open a panel. Step 3 will swap in the slide-in.
        _onRowClicked?.Invoke(missionId);
    }

    /// <summary>
    /// Disconnect every per-row Button.Pressed handler with the EXACT
    /// captured lambda reference, then free every child Control under
    /// the mission tooltip VBox. Idempotent ; safe to call when the
    /// VBox is already empty.
    /// </summary>
    private void ClearMissionRows()
    {
        foreach (var (button, pressed) in _rowPressedHandlers)
        {
            if (IsInstanceValid(button))
            {
                button.Pressed -= pressed;
            }
        }
        _rowPressedHandlers.Clear();

        if (_missionTooltipVbox is null) return;
        foreach (var child in _missionTooltipVbox.GetChildren())
        {
            child.QueueFree();
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
        _tooltipHovered = false;
        // If the anchor POI is also no longer hovered, fade out now.
        // Otherwise the player has moved from the tooltip back onto the
        // POI marker -- keep the tooltip visible.
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
