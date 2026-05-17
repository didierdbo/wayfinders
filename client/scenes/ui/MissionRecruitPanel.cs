using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 3+4 mission recruit panel (Varn-lock 2026-05-17 §A.8.D5).
/// Slides in from the right when the player clicks a mission row in the
/// E2 marker tooltip, displays the mission header / district / hook /
/// iso character placeholder, and exposes Accept + Decline buttons
/// wired to the §A.8.D5 endpoints
/// <c>POST /api/missions/&lt;id&gt;/{accept,decline}</c>.
///
/// <para>
/// <b>What this scene IS.</b> A sticky, screen-anchored Control hosted
/// inside a dedicated <see cref="CanvasLayer"/> (layer = 110) so it
/// always renders ABOVE the mission tooltip (layer = 100) and every
/// screen's chrome (layer = 2 or 3). The panel stays visible until
/// closed explicitly -- no hover auto-dismiss, no fade-out on losing
/// focus.
/// </para>
///
/// <para>
/// <b>What this scene is NOT.</b> NOT the future Compagnie panel (step
/// 5 -- separate scene, separate slide direction, separate state shape).
/// NOT a modal -- it does not freeze the underlying area-map ; the
/// player can still pan / zoom / hover other markers while it sits open.
/// </para>
///
/// <para>
/// <b>Slide animation.</b> Tween on the Control's
/// <see cref="Control.OffsetLeft"/> + <see cref="Control.OffsetRight"/>
/// (since the panel is anchored to the right edge with positive
/// offsets, sliding "in" means shrinking those offsets toward
/// <see cref="OpenOffsetX"/>). 300ms ease-out cubic on the way in,
/// 200ms ease-in cubic on the way out -- the open feels deliberate, the
/// close feels snappy.
/// </para>
///
/// <para>
/// <b>Close paths.</b>
/// <list type="bullet">
///   <item>Bouton X dans le header -- click triggers <see cref="Close"/>
///         without an API call (cancel intent).</item>
///   <item>ESC clavier -- the panel handles <c>ui_cancel</c> via
///         <see cref="Control._UnhandledInput"/>. Listening at the
///         unhandled level keeps focus-stealing trivial -- if a child
///         Button consumes ESC first it wins.</item>
/// </list>
/// Click-outside-to-close is intentionally NOT implemented in step 3 :
/// the close affordances are visible enough (the X + ESC), and adding
/// an invisible MouseFilter=Stop overlay would prevent the underlying
/// area-map from receiving pan/zoom input -- the spec rules that
/// out ("sticky, not modal").
/// </para>
///
/// <para>
/// <b>Single-panel-at-a-time discipline.</b> The owner (E2AreaMap)
/// holds a single instance and calls <see cref="ShowForMission"/> per
/// click. If the panel is already open for mission X and the player
/// clicks mission Y, <see cref="ShowForMission"/> swaps the content
/// in-place without re-sliding (mirrors the HoverTooltipController
/// fast-switch path -- the player is already in panel-reading mode).
/// </para>
///
/// <para>
/// <b>Action routing (§A.8.D5).</b> Accept fires
/// <see cref="ApiClient.AcceptMissionAsync"/> ; Decline fires
/// <see cref="ApiClient.DeclineMissionAsync"/>. Both are
/// fire-and-forget from the panel's POV : on a 200 response, the
/// MissionStore.RemoveById call drops the mission from the cache (next
/// poll reconciles), the panel closes, the tooltip's row will
/// disappear at the next PendingMissionsChanged signal. 404/409 races
/// (mission already accepted by another path) are logged at warning
/// level and the panel still closes -- the player's intent was clear.
/// </para>
///
/// <para>
/// <b>CTS discipline (memory feedback_godot_rendering_input_traps trap
/// #12).</b> The panel owns its own
/// <see cref="CancellationTokenSource"/> tied to its own
/// <see cref="Node._ExitTree"/>. In-flight Accept / Decline POSTs are
/// cancelled cleanly when the panel is freed (e.g. on E2 unload).
/// Pattern §C "screen-scoped CTS" -- the panel IS the screen lifetime
/// here.
/// </para>
///
/// <para>
/// <b>MouseFilter discipline (memory feedback_godot_rendering_input_traps).</b>
/// The root Control is <see cref="MouseFilterEnum.Stop"/> so the panel
/// captures hover / click events over its own rect (preventing the
/// underlying area-map from receiving stray clicks that would land on
/// a POI marker behind it). Decorative children (Labels, the iso
/// placeholder) are <see cref="MouseFilterEnum.Ignore"/> so clicks fall
/// through to the buttons. Buttons keep their default
/// <see cref="MouseFilterEnum.Stop"/>.
/// </para>
/// </summary>
public partial class MissionRecruitPanel : Control
{
    /// <summary>Width of the panel in pixels. Locked at 480 -- wide
    /// enough for the iso placeholder (200) + a column of text + 32 px
    /// padding on each side, narrow enough to leave 2/3 of a 1920px
    /// viewport free for the underlying area-map.</summary>
    private const float PanelWidthPx = 480f;

    /// <summary>X-offset (from the right edge) the panel sits at when
    /// open. Locked at 0 -- the panel hugs the right edge of the
    /// viewport.</summary>
    private const float OpenOffsetX = 0f;

    /// <summary>X-offset (from the right edge) the panel sits at when
    /// closed. Equal to <see cref="PanelWidthPx"/> so the panel is
    /// entirely off-screen and decorative children do not paint.</summary>
    private const float ClosedOffsetX = PanelWidthPx + 8f;

    /// <summary>Open tween duration, seconds. 300ms ease-out cubic
    /// reads as "deliberate landing", matching the rest of the UI
    /// animation tempo.</summary>
    private const float OpenTweenSeconds = 0.30f;

    /// <summary>Close tween duration, seconds. 200ms ease-in cubic is
    /// slightly snappier so the close feels intentional rather than
    /// lingering.</summary>
    private const float CloseTweenSeconds = 0.20f;

    /// <summary>Parchment background colour -- cohérent with the
    /// MissionTooltip's StyleBoxFlat (locked 2026-05-17).</summary>
    private static readonly Color ParchmentBg = new(0.91f, 0.85f, 0.72f, 0.97f);

    /// <summary>Umber border colour -- matches the parchment tooltip
    /// border.</summary>
    private static readonly Color ParchmentBorder = new(0.18f, 0.14f, 0.11f, 1f);

    private PanelContainer? _panelContainer;
    private Label? _headerLabel;
    private Label? _districtLabel;
    private Label? _hookLabel;
    private IsoCharacterPlaceholder? _isoPlaceholder;
    private Button? _acceptButton;
    private Button? _declineButton;
    private Button? _closeButton;
    private Tween? _slideTween;

    private CancellationTokenSource _shutdownCts = null!;
    private string? _currentMissionId;
    private bool _isOpen;

    private ApiClient? _apiClient;
    private MissionStore? _missionStore;

    /// <summary>
    /// Fired when the panel finishes its close animation. Owners (the
    /// E2AreaMap) may listen to drop their reference to the panel
    /// instance and free it ; M1 keeps the instance pooled and just
    /// hides it.
    /// </summary>
    [Signal]
    public delegate void PanelClosedEventHandler();

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();

        // Anchor the root Control to the right edge, full height. The
        // offset_left / offset_right delta is what the slide tween
        // animates : at "closed" the panel sits offsetLeft = +488 (off
        // screen to the right) ; at "open" it sits offsetLeft = 0.
        SetAnchorsPreset(LayoutPreset.RightWide);
        OffsetLeft = -PanelWidthPx + ClosedOffsetX;
        OffsetRight = ClosedOffsetX;
        OffsetTop = 0f;
        OffsetBottom = 0f;

        // Root captures the mouse over its own rect so clicks do NOT
        // fall through to the area-map's POI hotspots behind the
        // panel. Trap discipline (memory feedback_godot_rendering_input_traps).
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildChildren();

        // Resolve sibling autoloads. If either is missing the panel
        // still renders -- Accept / Decline become silent no-ops (with
        // a warning log). Lets the F6 launch path render the panel for
        // visual smoke even without the autoload graph fully wired.
        _apiClient = GetTree()?.Root?.GetNodeOrNull<ApiClient>("ApiClient");
        _missionStore = GetTree()?.Root?.GetNodeOrNull<MissionStore>("MissionStore");
        if (_apiClient is null)
            GD.PushWarning("[MissionRecruitPanel] ApiClient autoload not found -- Accept/Decline disabled");
        if (_missionStore is null)
            GD.PushWarning("[MissionRecruitPanel] MissionStore autoload not found -- local cache drop disabled");
    }

    public override void _ExitTree()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _slideTween?.Kill();
        _slideTween = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ESC -> close. Listening at the unhandled level so a child
        // Button that consumes ui_cancel first (e.g. focused Accept)
        // wins ; the panel only acts on ESC when no descendant did.
        if (!_isOpen) return;
        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Open (or swap content of) the panel for the given mission. If
    /// the panel is already open for a different mission, the content
    /// is swapped in-place without a re-slide -- mirrors the
    /// HoverTooltipController fast-switch path. Idempotent for the
    /// same mission id.
    /// </summary>
    public void ShowForMission(EmergentMissionDto mission)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));

        var payload = MissionRecruitPanelLogic.Compose(mission);
        ApplyPayload(payload);
        _currentMissionId = mission.Id;

        if (_isOpen)
        {
            // Fast-switch : already open, content just swapped above.
            // No re-slide, no flicker.
            GD.PrintRaw(
                $"[MissionRecruitPanel] swap content -> mission='{mission.Id}' " +
                $"npc='{payload.NpcId}' district='{payload.DistrictLabel}'\n");
            return;
        }

        _isOpen = true;
        Visible = true;
        StartSlideTween(toOpen: true);
        GD.Print(
            $"[MissionRecruitPanel] open -> mission='{mission.Id}' " +
            $"npc='{payload.NpcId}' district='{payload.DistrictLabel}'");
    }

    /// <summary>
    /// Close the panel without firing a backend action. Used by the
    /// X button, ESC, and as the convergence point of the
    /// Accept/Decline button handlers after the HTTP POST completes.
    /// Idempotent : closing a closed panel is a no-op.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _currentMissionId = null;
        StartSlideTween(toOpen: false);
        GD.Print("[MissionRecruitPanel] close");
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
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 0,
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

        // -- Header row : Close button (X) on the right + Title label
        var headerRow = new HBoxContainer
        {
            Name = "HeaderRow",
            MouseFilter = MouseFilterEnum.Pass,
        };
        rootVbox.AddChild(headerRow);

        _headerLabel = new Label
        {
            Name = "HeaderLabel",
            Text = "Recrutement",
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

        // -- District subtitle
        _districtLabel = new Label
        {
            Name = "DistrictLabel",
            Text = string.Empty,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _districtLabel.AddThemeFontSizeOverride("font_size", 16);
        _districtLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.25f, 0.15f, 1f));
        rootVbox.AddChild(_districtLabel);

        // -- Iso placeholder (centered, fixed area)
        var isoWrapper = new CenterContainer
        {
            Name = "IsoWrapper",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        isoWrapper.CustomMinimumSize = new Vector2(0, 280);
        rootVbox.AddChild(isoWrapper);

        _isoPlaceholder = new IsoCharacterPlaceholder
        {
            Name = "IsoCharacterPlaceholder",
        };
        _isoPlaceholder.CustomMinimumSize = new Vector2(200, 280);
        isoWrapper.AddChild(_isoPlaceholder);

        // -- Hook body (autowrap, takes remaining vertical room)
        _hookLabel = new Label
        {
            Name = "HookLabel",
            Text = string.Empty,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _hookLabel.AddThemeFontSizeOverride("font_size", 15);
        _hookLabel.AddThemeColorOverride("font_color", new Color(0.10f, 0.08f, 0.06f, 1f));
        rootVbox.AddChild(_hookLabel);

        // -- Action buttons : Accept + Decline side by side at the bottom
        var buttonRow = new HBoxContainer
        {
            Name = "ButtonRow",
            MouseFilter = MouseFilterEnum.Pass,
        };
        buttonRow.AddThemeConstantOverride("separation", 12);
        rootVbox.AddChild(buttonRow);

        _acceptButton = new Button
        {
            Name = "AcceptButton",
            Text = "Accepter",
            MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40),
        };
        _acceptButton.Pressed += OnAcceptPressed;
        buttonRow.AddChild(_acceptButton);

        _declineButton = new Button
        {
            Name = "DeclineButton",
            Text = "Décliner",
            MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40),
        };
        _declineButton.Pressed += OnDeclinePressed;
        buttonRow.AddChild(_declineButton);
    }

    // -------------------------------------------------------------------
    // Payload binding
    // -------------------------------------------------------------------

    private void ApplyPayload(MissionRecruitPanelPayload payload)
    {
        if (_headerLabel is not null) _headerLabel.Text = payload.HeaderText;
        if (_districtLabel is not null) _districtLabel.Text = payload.DistrictLabel;
        if (_hookLabel is not null) _hookLabel.Text = payload.NarrativeHook;
        _isoPlaceholder?.SetNpc(payload.NpcId);
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

        _slideTween.TweenProperty(this, "offset_left", -PanelWidthPx + targetOffsetX, duration);
        _slideTween.Parallel().TweenProperty(this, "offset_right", targetOffsetX, duration);

        if (!toOpen)
        {
            // After the close tween, hide the panel + emit the close
            // signal so owners may free / pool the instance.
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

    private void OnAcceptPressed()
    {
        var missionId = _currentMissionId;
        if (string.IsNullOrEmpty(missionId))
        {
            GD.PushWarning("[MissionRecruitPanel] Accept pressed with no current mission -- no-op");
            return;
        }
        // Drop the mission from the local cache immediately so the
        // tooltip rows refresh on the next signal cycle. The
        // authoritative server state is reconciled on the next poll.
        _missionStore?.RemoveById(missionId);
        _ = FireAcceptAsync(missionId);
        Close();
    }

    private void OnDeclinePressed()
    {
        var missionId = _currentMissionId;
        if (string.IsNullOrEmpty(missionId))
        {
            GD.PushWarning("[MissionRecruitPanel] Decline pressed with no current mission -- no-op");
            return;
        }
        _missionStore?.RemoveById(missionId);
        _ = FireDeclineAsync(missionId);
        Close();
    }

    private async Task FireAcceptAsync(string missionId)
    {
        if (_apiClient is null) return;
        try
        {
            var result = await _apiClient
                .AcceptMissionAsync(missionId, _shutdownCts.Token)
                .ConfigureAwait(false);
            if (_shutdownCts.IsCancellationRequested) return;
            switch (result)
            {
                case Result<MissionAcceptResponseDto, ApiError>.Success ok:
                    GD.PrintRaw($"[MissionRecruitPanel] accept ack id={missionId} status={ok.Value.Status}\n");
                    break;
                case Result<MissionAcceptResponseDto, ApiError>.Failure fail:
                    if (fail.Error is ApiError.ServerError serverErr && serverErr.StatusCode == 404)
                    {
                        GD.PrintRaw($"[MissionRecruitPanel] accept 404 id={missionId} (race ? ignoring)\n");
                    }
                    else
                    {
                        GD.PushWarning($"[MissionRecruitPanel] accept failed id={missionId} : {fail.Error.GetType().Name}");
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Panel freed mid-flight -- expected on E2 unload.
        }
    }

    private async Task FireDeclineAsync(string missionId)
    {
        if (_apiClient is null) return;
        try
        {
            var result = await _apiClient
                .DeclineMissionAsync(missionId, _shutdownCts.Token)
                .ConfigureAwait(false);
            if (_shutdownCts.IsCancellationRequested) return;
            switch (result)
            {
                case Result<MissionDeclineResponseDto, ApiError>.Success ok:
                    GD.PrintRaw($"[MissionRecruitPanel] decline ack id={missionId} status={ok.Value.Status}\n");
                    break;
                case Result<MissionDeclineResponseDto, ApiError>.Failure fail:
                    if (fail.Error is ApiError.ServerError serverErr
                        && (serverErr.StatusCode == 404 || serverErr.StatusCode == 409))
                    {
                        GD.PrintRaw($"[MissionRecruitPanel] decline {serverErr.StatusCode} id={missionId} (race ? ignoring)\n");
                    }
                    else
                    {
                        GD.PushWarning($"[MissionRecruitPanel] decline failed id={missionId} : {fail.Error.GetType().Name}");
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Panel freed mid-flight -- expected on E2 unload.
        }
    }
}
