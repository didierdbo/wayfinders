using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 5 Compagnie panel (Didier-lock 2026-05-17) -- mirror
/// gauche of the <see cref="MissionRecruitPanel"/>. Slide-in from the
/// LEFT edge of the viewport when the player clicks a mission row in
/// the E2 marker tooltip, displays the "Compagnie" header + a central
/// flat iso socle (<see cref="IsoSocketPlaceholder"/>) waiting for the
/// étape 6 drag&amp;drop landing. Sticky strict (no auto-dismiss on
/// hover-out), closes via ESC or the header X.
///
/// <para>
/// <b>Why a separate scene + script instead of subclassing the recruit
/// panel.</b> The two panels share a slide-in tempo and a sticky
/// discipline, but their content shape is genuinely different (the
/// recruit panel renders one mission ; the Compagnie panel renders the
/// roster + drop targets, which will grow at étape 6 and beyond).
/// "Premature interface" reflex caught -- build the concrete sibling
/// first, extract the shared base only when a third panel actually
/// appears with the same shape.
/// </para>
///
/// <para>
/// <b>Slide direction.</b> Anchored to the LEFT edge with negative
/// offsets when closed. Mirror of the recruit panel's right-edge offsets.
/// </para>
///
/// <para>
/// <b>Étape 6 additions.</b>
/// <list type="bullet">
///   <item>The central socle is configured as
///         <see cref="IsoSocketPlaceholder.DropTargetRole.CompanyHost"/>
///         so it accepts the forward drop from the recruit panel +
///         hosts the reverse drag back.</item>
///   <item>The panel forwards the socle's
///         <see cref="IsoSocketPlaceholder.OccupancyChangedEventHandler"/>
///         to its own <see cref="OccupancyChangedEventHandler"/>
///         (re-emission lets the E2AreaMap subscribe once at the
///         panel level rather than two layers deep).</item>
///   <item>The panel listens to the viewport's drag-begin / drag-end
///         notifications (via <see cref="Control._Notification"/>)
///         and toggles the socle's rim glow accordingly -- the glow
///         is a UI affordance, not a state-machine input.</item>
///   <item><see cref="ApplyConfirmedOccupancy"/> -- called by the
///         owner (E2AreaMap) after a successful
///         <c>POST /api/missions/&lt;id&gt;/accept</c> round-trip.
///         Updates the header text ("Compagnie -- 1 membre"), freezes
///         the socle (no reverse drag), and keeps the occupant visible.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this scene IS NOT.</b> NOT a modal -- the underlying
/// area-map stays interactive (pan / zoom / hover) while the
/// Compagnie sits open.
/// </para>
///
/// <para>
/// <b>No CTS at v1.</b> The Compagnie panel issues no async ApiClient
/// calls of its own -- the Accept POST lives on the recruit panel,
/// the company panel only renders the confirmed state.
/// </para>
/// </summary>
public partial class CompanyPanel : Control
{
    /// <summary>Panel width, px. Locked at 360.</summary>
    private const float PanelWidthPx = 360f;

    /// <summary>X-offset (from the left edge) when open.</summary>
    private const float OpenOffsetX = 0f;

    /// <summary>X-offset when closed -- entirely off-screen left.</summary>
    private const float ClosedOffsetX = -(PanelWidthPx + 8f);

    private const float OpenTweenSeconds = 0.30f;
    private const float CloseTweenSeconds = 0.20f;

    private static readonly Color ParchmentBg = new(0.91f, 0.85f, 0.72f, 0.97f);
    private static readonly Color ParchmentBorder = new(0.18f, 0.14f, 0.11f, 1f);

    private PanelContainer? _panelContainer;
    private Label? _headerLabel;
    private IsoSocketPlaceholder? _socle;
    private Button? _closeButton;
    private Tween? _slideTween;

    private bool _isOpen;

    /// <summary>
    /// Fired when the panel finishes its close animation.
    /// </summary>
    [Signal]
    public delegate void PanelClosedEventHandler();

    /// <summary>
    /// Étape 6 re-emission of the socle's
    /// <see cref="IsoSocketPlaceholder.OccupancyChangedEventHandler"/>.
    /// The E2AreaMap subscribes to this so it can forward the
    /// occupancy change to the recruit panel's state machine.
    /// Payload is the npcId (empty string when the socle reverts
    /// to empty).
    /// </summary>
    [Signal]
    public delegate void OccupancyChangedEventHandler(string npcId);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.LeftWide);
        OffsetLeft = ClosedOffsetX;
        OffsetRight = ClosedOffsetX + PanelWidthPx;
        OffsetTop = 0f;
        OffsetBottom = 0f;

        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildChildren();
    }

    public override void _ExitTree()
    {
        _slideTween?.Kill();
        _slideTween = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isOpen) return;
        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // Étape 6 rim glow : Godot fires NotificationDragBegin /
        // NotificationDragEnd on every Control that is in the tree
        // when a drag starts / ends. We capture them at the panel
        // level so the socle does not have to poll, and so closing
        // the panel mid-drag (e.g. via X click during a drag, edge
        // case) still clears the glow.
        switch ((long)what)
        {
            case NotificationDragBegin:
                _socle?.SetDropTargetGlow(true);
                break;
            case NotificationDragEnd:
                _socle?.SetDropTargetGlow(false);
                break;
        }
    }

    /// <summary>
    /// Slide the panel in. Idempotent.
    /// </summary>
    public void SlideIn()
    {
        if (_isOpen) return;
        _isOpen = true;
        Visible = true;
        StartSlideTween(toOpen: true);
        GD.Print("[CompanyPanel] open");
    }

    /// <summary>
    /// Close the panel. Idempotent.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        StartSlideTween(toOpen: false);
        GD.Print("[CompanyPanel] close");
    }

    /// <summary>
    /// Reset the panel's occupancy to empty (no recruit on socle,
    /// "Compagnie" header). Called by the owner when the recruit
    /// panel closes without a successful accept (decline / X / ESC),
    /// to wipe any in-progress drop state.
    /// </summary>
    public void ResetOccupancy()
    {
        if (_socle is not null)
        {
            _socle.SetConfirmedOccupancy(false);
            _socle.SetOccupiedNpc(null);
        }
        if (_headerLabel is not null)
            _headerLabel.Text = CompanyPanelOccupiedLogic.EmptyHeaderText;
    }

    /// <summary>
    /// Apply the confirmed-occupied state after a successful Accept
    /// round-trip. Called by the owner (E2AreaMap) on the
    /// <see cref="MissionRecruitPanel.RecruitConfirmedEventHandler"/>
    /// signal. Updates the header + freezes the socle (no reverse
    /// drag once confirmed).
    /// </summary>
    public void ApplyConfirmedOccupancy(string npcId)
    {
        if (string.IsNullOrEmpty(npcId))
        {
            GD.PushWarning("[CompanyPanel] ApplyConfirmedOccupancy called with empty npcId -- no-op");
            return;
        }
        var payload = CompanyPanelOccupiedLogic.Compose(npcId);
        if (_headerLabel is not null) _headerLabel.Text = payload.HeaderText;
        if (_socle is not null)
        {
            _socle.SetOccupiedNpc(npcId);
            _socle.SetConfirmedOccupancy(true);
        }
        GD.Print($"[CompanyPanel] occupancy confirmed npc='{npcId}' header='{payload.HeaderText}'");
    }

    /// <summary>Expose the socle so the owning E2AreaMap can wire the
    /// recruit panel's reverse-drop target to it (the recruit panel
    /// needs to forward CharacterReturnedFromSocle back to the
    /// company-panel-owned socle on the reverse path).</summary>
    public IsoSocketPlaceholder? Socle => _socle;

    // -------------------------------------------------------------------
    // Internal -- scene tree build
    // -------------------------------------------------------------------

    private void BuildChildren()
    {
        var styleBox = new StyleBoxFlat
        {
            BgColor = ParchmentBg,
            BorderColor = ParchmentBorder,
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            ContentMarginLeft = 16,
            ContentMarginTop = 16,
            ContentMarginRight = 16,
            ContentMarginBottom = 16,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 4,
        };

        _panelContainer = new PanelContainer
        {
            Name = "PanelContainer",
            MouseFilter = MouseFilterEnum.Stop,
        };
        _panelContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        _panelContainer.AddThemeStyleboxOverride("panel", styleBox);
        AddChild(_panelContainer);

        var rootVbox = new VBoxContainer
        {
            Name = "RootVBox",
            MouseFilter = MouseFilterEnum.Pass,
        };
        rootVbox.AddThemeConstantOverride("separation", 12);
        _panelContainer.AddChild(rootVbox);

        var headerRow = new HBoxContainer
        {
            Name = "HeaderRow",
            MouseFilter = MouseFilterEnum.Pass,
        };
        rootVbox.AddChild(headerRow);

        _headerLabel = new Label
        {
            Name = "HeaderLabel",
            Text = CompanyPanelOccupiedLogic.EmptyHeaderText,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _headerLabel.AddThemeFontSizeOverride("font_size", 22);
        _headerLabel.AddThemeColorOverride("font_color", new Color(0.10f, 0.08f, 0.06f, 1f));
        headerRow.AddChild(_headerLabel);

        _closeButton = new Button
        {
            Name = "CloseButton",
            Text = "X",
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Fermer (ESC)",
            CustomMinimumSize = new Vector2(32, 32),
        };
        _closeButton.Pressed += OnClosePressed;
        headerRow.AddChild(_closeButton);

        var socleWrapper = new CenterContainer
        {
            Name = "SocleWrapper",
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        rootVbox.AddChild(socleWrapper);

        _socle = new IsoSocketPlaceholder
        {
            Name = "IsoSocket",
        };
        // Slightly larger landing surface than the cmin default + a
        // tall enough vertical band to host the occupant silhouette
        // + the name label below.
        _socle.CustomMinimumSize = new Vector2(280, 240);
        // Étape 6 : flip the socle from decorative to drop target +
        // reverse drag source. MouseFilter is reconciled inside.
        _socle.SetDropTargetRole(IsoSocketPlaceholder.DropTargetRole.CompanyHost);
        // Re-emit the socle's occupancy changes at the panel level so
        // the owner (E2AreaMap) subscribes once at one layer up.
        _socle.OccupancyChanged += OnSocleOccupancyChanged;
        socleWrapper.AddChild(_socle);
    }

    // -------------------------------------------------------------------
    // Slide tween + signal forwarders
    // -------------------------------------------------------------------

    private void StartSlideTween(bool toOpen)
    {
        _slideTween?.Kill();
        _slideTween = CreateTween();
        _slideTween.SetTrans(Tween.TransitionType.Cubic);
        _slideTween.SetEase(toOpen ? Tween.EaseType.Out : Tween.EaseType.In);

        var targetOffsetX = toOpen ? OpenOffsetX : ClosedOffsetX;
        var duration = toOpen ? OpenTweenSeconds : CloseTweenSeconds;

        _slideTween.TweenProperty(this, "offset_left", targetOffsetX, duration);
        _slideTween.Parallel().TweenProperty(this, "offset_right", targetOffsetX + PanelWidthPx, duration);

        if (!toOpen)
        {
            _slideTween.TweenCallback(Callable.From(OnCloseAnimationFinished));
        }
    }

    private void OnCloseAnimationFinished()
    {
        Visible = false;
        EmitSignal(SignalName.PanelClosed);
    }

    private void OnClosePressed()
    {
        Close();
    }

    private void OnSocleOccupancyChanged(string npcId)
    {
        // Re-emit at the panel level. Empty string carries the
        // "socle reverted to vide" signal -- the owner translates
        // that into the recruit panel's CharacterReturnedFromSocle
        // state-machine event.
        EmitSignal(SignalName.OccupancyChanged, npcId);
    }
}
