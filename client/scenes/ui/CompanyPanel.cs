using Godot;

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
/// <b>Slide direction.</b> The panel is anchored to the LEFT edge with
/// negative-going offsets when closed (entirely off-screen to the
/// left). At open it sits at OffsetLeft = 0, OffsetRight = PanelWidthPx.
/// Mirror image of the recruit panel's right-edge offsets.
/// </para>
///
/// <para>
/// <b>Close paths.</b>
/// <list type="bullet">
///   <item>Bouton X dans le header -- click triggers <see cref="Close"/>.</item>
///   <item>ESC clavier -- handled via <see cref="Control._UnhandledInput"/>.
///         The recruit panel ALSO handles ui_cancel ; whichever
///         control's ancestor sits earlier in the input chain wins.
///         Because the CanvasLayer index of the Compagnie panel
///         (= 111) sits above the recruit panel (= 110), Godot's
///         input chain delivers ESC to the Compagnie panel FIRST. We
///         decided (Didier UX decision, 2026-05-17) that ESC closes
///         the FOCUSED panel only -- not a synchronous "close both" --
///         so the panel calls <c>SetInputAsHandled()</c> after closing
///         itself, preventing the recruit panel from also reacting to
///         the same ESC press.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>CanvasLayer index 111.</b> One step above the recruit panel's
/// 110 so that at étape 6, when a character placeholder is dragged
/// from the recruit panel TO the Compagnie socle, the Compagnie panel
/// (the drop target) stays visually on top of the source panel during
/// the drag. Decided 2026-05-17 ; if at étape 6 the drag preview
/// rendering reveals this is wrong, we revisit.
/// </para>
///
/// <para>
/// <b>What this scene IS NOT.</b> NOT the future drag&amp;drop logic --
/// étape 6. The socle child is a static placeholder (its
/// <c>SetOccupiedNpc</c> setter exists but stays unused at étape 5).
/// NOT a modal -- like the recruit panel, the underlying area-map
/// stays interactive (pan / zoom / hover) while the Compagnie sits
/// open.
/// </para>
///
/// <para>
/// <b>MouseFilter discipline (memo feedback_godot_rendering_input_traps).</b>
/// Root Control = Stop so clicks over the panel's rect do not fall
/// through to the area-map POI markers behind it. Decorative children
/// (Label, IsoSocketPlaceholder) = Ignore so events fall through to
/// the panel container. Header HBox + close button keep their default
/// (Pass / Stop) so the close button is clickable.
/// </para>
///
/// <para>
/// <b>No CTS at v1.</b> The Compagnie panel is purely visual at étape 5 --
/// no async ApiClient calls. The CTS pattern §C will land at étape 6
/// when drag&amp;drop wires party-composition POSTs.
/// </para>
/// </summary>
public partial class CompanyPanel : Control
{
    /// <summary>Panel width, px. Locked at 360 -- wide enough for the
    /// centre socle (~280 socle + 32 padding x 2) without crowding 2/3
    /// of a 1920px viewport reserved for the area-map underneath.</summary>
    private const float PanelWidthPx = 360f;

    /// <summary>X-offset (from the left edge) when open. Locked at 0 --
    /// the panel hugs the left edge.</summary>
    private const float OpenOffsetX = 0f;

    /// <summary>X-offset (from the left edge) when closed. Negative
    /// PanelWidthPx + a small safety margin so the panel is entirely
    /// off-screen and decorative children do not paint.</summary>
    private const float ClosedOffsetX = -(PanelWidthPx + 8f);

    /// <summary>Open tween duration, seconds. 300ms ease-out cubic --
    /// mirror of the recruit panel for visual symmetry.</summary>
    private const float OpenTweenSeconds = 0.30f;

    /// <summary>Close tween duration, seconds. 200ms ease-in cubic --
    /// mirror of the recruit panel.</summary>
    private const float CloseTweenSeconds = 0.20f;

    /// <summary>Parchment background -- cohérent with the recruit
    /// panel + the mission tooltip (locked 2026-05-17 visual DNA).</summary>
    private static readonly Color ParchmentBg = new(0.91f, 0.85f, 0.72f, 0.97f);

    /// <summary>Umber border -- matches the recruit panel border.</summary>
    private static readonly Color ParchmentBorder = new(0.18f, 0.14f, 0.11f, 1f);

    private PanelContainer? _panelContainer;
    private Label? _headerLabel;
    private IsoSocketPlaceholder? _socle;
    private Button? _closeButton;
    private Tween? _slideTween;

    private bool _isOpen;

    /// <summary>
    /// Fired when the panel finishes its close animation. The
    /// E2AreaMap owner may listen if it later needs to free the
    /// instance ; at M1 the panel is pooled across mission-row clicks
    /// so the signal is informational only.
    /// </summary>
    [Signal]
    public delegate void PanelClosedEventHandler();

    public override void _Ready()
    {
        // Anchor the root to the LEFT edge, full height. The mirror of
        // the recruit panel's right-edge anchoring.
        SetAnchorsPreset(LayoutPreset.LeftWide);
        OffsetLeft = ClosedOffsetX;
        OffsetRight = ClosedOffsetX + PanelWidthPx;
        OffsetTop = 0f;
        OffsetBottom = 0f;

        // Root captures the mouse over its own rect (Stop) -- prevents
        // panel-bounds clicks from leaking to area-map POI hotspots
        // behind it (trap discipline).
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
        // ESC -> close THIS panel only. SetInputAsHandled prevents the
        // sibling recruit panel from also reacting to the same press.
        // The recruit panel handles its own ESC independently when its
        // _UnhandledInput fires -- but if BOTH panels were open and the
        // Compagnie panel's CanvasLayer (111) sits above the recruit
        // panel's (110), Godot delivers ESC to the Compagnie first.
        // The handled-flag stops the cascade. Inverted order (close
        // both on a single ESC) was considered but rejected : a focused
        // close per-panel mirrors common desktop UX and keeps the X
        // button + ESC sémantically equivalent.
        if (!_isOpen) return;
        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Slide the panel in. Idempotent : calling on an already-open
    /// panel is a no-op.
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
    /// Close the panel. Idempotent : closing an already-closed panel
    /// is a no-op.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        StartSlideTween(toOpen: false);
        GD.Print("[CompanyPanel] close");
    }

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

        // -- Header row : Title label left, Close button (X) on right.
        var headerRow = new HBoxContainer
        {
            Name = "HeaderRow",
            MouseFilter = MouseFilterEnum.Pass,
        };
        rootVbox.AddChild(headerRow);

        _headerLabel = new Label
        {
            Name = "HeaderLabel",
            Text = "Compagnie",
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

        // -- Central socle area : CenterContainer fills the remaining
        // vertical space and centres the IsoSocketPlaceholder horizontally
        // and vertically. The socle's CustomMinimumSize controls the
        // visible footprint ; the centring is geometric, not anchor-based,
        // so resizing the panel re-centres for free.
        var socleWrapper = new CenterContainer
        {
            Name = "SocleWrapper",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        rootVbox.AddChild(socleWrapper);

        _socle = new IsoSocketPlaceholder
        {
            Name = "IsoSocket",
        };
        // Override the default cmin (168 x 88) with a larger landing
        // surface that reads as the centerpiece of the panel. Width 240
        // -> height 120 (2:1 locked).
        _socle.CustomMinimumSize = new Vector2(240, 120);
        socleWrapper.AddChild(_socle);
    }

    // -------------------------------------------------------------------
    // Slide tween
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

    // -------------------------------------------------------------------
    // Button handlers
    // -------------------------------------------------------------------

    private void OnClosePressed()
    {
        Close();
    }
}
