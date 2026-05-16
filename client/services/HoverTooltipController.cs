using Godot;

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
/// <b>Risk #3 mitigation (tooltip orphelin if E2 freed mid-hover).</b>
/// E1WorldMap's <c>_ExitTree</c> calls
/// <see cref="CancelTooltip"/>. Without that, the autoload (which
/// outlives E2) would still own a scheduled timer that fires after E2
/// is gone, briefly showing a tooltip on the next screen. Cancellation
/// is the discipline.
/// </para>
///
/// <para>
/// <b>Why a Node not a RefCounted.</b> The autoload contract requires a
/// <see cref="Node"/>. The tooltip child Control is parented to this
/// node (and lives behind the modal CanvasLayer because the autoload
/// itself sits at <c>/root/HoverTooltipController</c>). For higher
/// z-order stacking later we will add a <see cref="CanvasLayer"/> wrapper
/// — not needed for J3's E2 tooltip-over-POI use case.
/// </para>
/// </summary>
public partial class HoverTooltipController : Node
{
    private const string TooltipScenePath = "res://scenes/ui/HoverTooltip.tscn";

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

    private PanelContainer? _tooltipPanel;
    private Label? _tooltipLabel;
    private SceneTreeTimer? _pendingTimer;
    private Tween? _fadeTween;

    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>(TooltipScenePath);
        if (scene is null)
        {
            GD.PushError($"[HoverTooltipController] failed to load {TooltipScenePath} — tooltips disabled");
            return;
        }

        _tooltipPanel = scene.Instantiate<PanelContainer>();
        _tooltipLabel = _tooltipPanel.GetNode<Label>("Label");
        _tooltipPanel.Modulate = new Color(1, 1, 1, 0);
        _tooltipPanel.Visible = false;
        AddChild(_tooltipPanel);

        GD.Print("[HoverTooltipController] ready");
    }

    /// <summary>
    /// Schedule a tooltip to appear after <see cref="HoverDelaySeconds"/>.
    /// If another request is pending, it is cancelled — only the most
    /// recent hover wins.
    /// </summary>
    /// <param name="text">Final tooltip text (already interpolated).</param>
    /// <param name="anchorGlobalPosition">
    /// Global screen position used as the anchor for the tooltip's
    /// top-left corner. The actual paint position adds
    /// <see cref="TooltipOffset"/>.
    /// </param>
    public void RequestTooltip(string text, Vector2 anchorGlobalPosition)
    {
        if (_tooltipPanel is null || _tooltipLabel is null) return;

        // Cancel any in-flight schedule + fade. We always restart from
        // a clean state — no race between an old fade-in and a new one.
        _pendingTimer = null;
        _fadeTween?.Kill();
        _fadeTween = null;

        _pendingTimer = GetTree().CreateTimer(HoverDelaySeconds);
        _pendingTimer.Timeout += () => OnHoverDelayElapsed(text, anchorGlobalPosition);
    }

    /// <summary>
    /// Cancel any pending or visible tooltip. Called by E1WorldMap's
    /// <c>_ExitTree</c> (Risk #3 mitigation) and on hover-out from
    /// individual POIs.
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

    private void OnHoverDelayElapsed(string text, Vector2 anchorGlobalPosition)
    {
        // If CancelTooltip ran between schedule and timer fire,
        // _pendingTimer is null — bail before mutating the panel.
        if (_pendingTimer is null) return;
        if (_tooltipPanel is null || _tooltipLabel is null) return;

        _pendingTimer = null;

        _tooltipLabel.Text = text;
        _tooltipPanel.GlobalPosition = anchorGlobalPosition + TooltipOffset;
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
}
