using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 3+4+6 mission recruit panel (Varn-lock 2026-05-17 §A.8.D5
/// + Didier-lock 2026-05-17 étape 6). Slides in from the right when
/// the player clicks a mission row in the E2 marker tooltip, displays
/// the mission header / district / hook / iso character placeholder,
/// and exposes Accept + Decline buttons wired to
/// <c>POST /api/missions/&lt;id&gt;/{accept,decline}</c>.
///
/// <para>
/// <b>Étape 6 additions.</b>
/// <list type="bullet">
///   <item>The hero <see cref="IsoCharacterPlaceholder"/> is configured
///         as <see cref="IsoCharacterPlaceholder.SourceRole.RecruitPanelHost"/>
///         -- drag source forward AND reverse-drop target.</item>
///   <item>The panel owns a <see cref="MissionRecruitPanelState"/>
///         state machine driven by drop / return events forwarded from
///         the Compagnie socle (via the owning E2AreaMap) and from
///         the placeholder's own reverse-drop signal. State transitions
///         drive the placeholder's greyed-out flag + the Accept
///         button's highlight theming.</item>
///   <item>On a successful Accept round-trip the panel emits
///         <see cref="RecruitConfirmedEventHandler"/> with the
///         (missionId, npcId) so the owner can call
///         <see cref="CompanyPanel.ApplyConfirmedOccupancy"/>.</item>
///   <item>On Decline (with or without a prior drop) the panel
///         restores the socle (via the owning E2AreaMap) by emitting
///         <see cref="RecruitDeclinedEventHandler"/> with the
///         missionId.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Panel-scoped. The state resets to Initial
/// on every <see cref="ShowForMission"/> and on every
/// <see cref="Close"/> -- the panel's lifecycle defines the state's
/// lifecycle. No global store, no autoload. Justified by Rune coaching
/// brief §3 ("Stateless-everywhere reflex caught -- client UI is
/// stateful").
/// </para>
///
/// <para>
/// <b>CTS discipline (memo feedback_godot_rendering_input_traps).</b>
/// The panel owns its own CTS tied to its own
/// <see cref="Node._ExitTree"/>. In-flight Accept / Decline POSTs are
/// cancelled cleanly when the panel is freed.
/// </para>
///
/// <para>
/// <b>MouseFilter discipline.</b> Root = Stop ; decorative children =
/// Ignore ; buttons = Stop (default). The hero placeholder switches
/// to Stop via SetSourceContext(RecruitPanelHost) so Godot routes
/// _GetDragData / _CanDropData to it.
/// </para>
/// </summary>
public partial class MissionRecruitPanel : Control
{
    private const float PanelWidthPx = 480f;
    private const float OpenOffsetX = 0f;
    private const float ClosedOffsetX = PanelWidthPx + 8f;
    private const float OpenTweenSeconds = 0.30f;
    private const float CloseTweenSeconds = 0.20f;

    private static readonly Color ParchmentBg = new(0.91f, 0.85f, 0.72f, 0.97f);
    private static readonly Color ParchmentBorder = new(0.18f, 0.14f, 0.11f, 1f);

    /// <summary>Accept button modulate when highlighted (state =
    /// DroppedReadyToValidate). Warm parchment tint so the affordance
    /// reads as "ready to go" without overpowering the panel chrome.</summary>
    private static readonly Color AcceptHighlightTint = new(1.15f, 1.05f, 0.85f, 1f);

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
    private string? _currentNpcId;
    private bool _isOpen;
    private MissionRecruitPanelState _state = MissionRecruitPanelState.Initial;

    private ApiClient? _apiClient;
    private MissionStore? _missionStore;

    /// <summary>
    /// Fired when the panel finishes its close animation.
    /// </summary>
    [Signal]
    public delegate void PanelClosedEventHandler();

    /// <summary>
    /// Étape 6 -- fired after a successful Accept round-trip. The
    /// owner (E2AreaMap) subscribes to forward the npcId to
    /// <see cref="CompanyPanel.ApplyConfirmedOccupancy"/>.
    /// </summary>
    [Signal]
    public delegate void RecruitConfirmedEventHandler(string missionId, string npcId);

    /// <summary>
    /// Étape 6 -- fired on Decline (whether or not a prior drop
    /// landed on the socle). The owner forwards to
    /// <see cref="CompanyPanel.ResetOccupancy"/> so the socle reverts
    /// to vide if the perso had been parked there mid-flow.
    /// </summary>
    [Signal]
    public delegate void RecruitDeclinedEventHandler(string missionId);

    /// <summary>
    /// Étape 6 -- fired when the player reverse-drags the perso back
    /// to this panel from the company socle. The owner forwards to
    /// the company panel's socle to clear it.
    /// </summary>
    [Signal]
    public delegate void CharacterReturnedToRecruitPanelEventHandler(string missionId, string npcId);

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();

        SetAnchorsPreset(LayoutPreset.RightWide);
        OffsetLeft = -PanelWidthPx + ClosedOffsetX;
        OffsetRight = ClosedOffsetX;
        OffsetTop = 0f;
        OffsetBottom = 0f;

        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildChildren();

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
        if (!_isOpen) return;
        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Open (or swap content of) the panel for the given mission.
    /// </summary>
    public void ShowForMission(EmergentMissionDto mission)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));

        var payload = MissionRecruitPanelLogic.Compose(mission);
        ApplyPayload(payload);
        _currentMissionId = mission.Id;
        _currentNpcId = payload.NpcId;

        // Étape 6 : configure the placeholder as a drag-drop host for
        // this mission. Mission id travels in every drag payload.
        _isoPlaceholder?.SetSourceContext(
            IsoCharacterPlaceholder.SourceRole.RecruitPanelHost,
            mission.Id);

        // Reset state for the new mission (state machine pin
        // PanelOpened -> Initial).
        ApplyState(MissionRecruitPanelStateLogic.Next(
            _state, MissionRecruitPanelEvent.PanelOpened));

        if (_isOpen)
        {
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
    /// Close the panel without firing a backend action.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _currentMissionId = null;
        _currentNpcId = null;
        // Reset the state machine + visuals so the next ShowForMission
        // starts clean (the panel pool reuses the same instance).
        ApplyState(MissionRecruitPanelStateLogic.Next(
            _state, MissionRecruitPanelEvent.PanelClosed));
        StartSlideTween(toOpen: false);
        GD.Print("[MissionRecruitPanel] close");
    }

    /// <summary>
    /// Étape 6 -- called by the owner (E2AreaMap) when the company
    /// panel's socle reports an occupancy change. <paramref name="dropped"/>
    /// = true (npcId landed on socle) or false (socle reverted to
    /// empty). Forwards into the state machine.
    /// </summary>
    public void SetCharacterDropped(bool dropped)
    {
        var ev = dropped
            ? MissionRecruitPanelEvent.CharacterDroppedOnSocle
            : MissionRecruitPanelEvent.CharacterReturnedFromSocle;
        ApplyState(MissionRecruitPanelStateLogic.Next(_state, ev));
    }

    /// <summary>Currently-displayed mission id (null when panel is
    /// closed). Read by the owner when it needs to correlate a socle
    /// occupancy change with the current panel.</summary>
    public string? CurrentMissionId => _currentMissionId;

    /// <summary>Currently-displayed NPC id (null when panel is closed).</summary>
    public string? CurrentNpcId => _currentNpcId;

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

        // Iso placeholder wrapper -- use Pass on the wrapper so the
        // placeholder receives mouse events for its drag-source role.
        var isoWrapper = new CenterContainer
        {
            Name = "IsoWrapper",
            MouseFilter = MouseFilterEnum.Pass,
        };
        isoWrapper.CustomMinimumSize = new Vector2(0, 280);
        rootVbox.AddChild(isoWrapper);

        _isoPlaceholder = new IsoCharacterPlaceholder
        {
            Name = "IsoCharacterPlaceholder",
        };
        _isoPlaceholder.CustomMinimumSize = new Vector2(200, 280);
        isoWrapper.AddChild(_isoPlaceholder);
        // Subscribe to the reverse-drop signal so the panel forwards
        // CharacterReturnedFromSocle into its own state machine + emits
        // the owner-facing CharacterReturnedToRecruitPanel signal.
        _isoPlaceholder.CharacterReturnedFromSocle += OnReverseDropLanded;

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
    // Payload binding + state -> visual projection
    // -------------------------------------------------------------------

    private void ApplyPayload(MissionRecruitPanelPayload payload)
    {
        if (_headerLabel is not null) _headerLabel.Text = payload.HeaderText;
        if (_districtLabel is not null) _districtLabel.Text = payload.DistrictLabel;
        if (_hookLabel is not null) _hookLabel.Text = payload.NarrativeHook;
        _isoPlaceholder?.SetNpc(payload.NpcId);
    }

    private void ApplyState(MissionRecruitPanelState next)
    {
        if (_state == next) return;
        _state = next;
        // Grey-out the placeholder iff the perso is on the socle.
        _isoPlaceholder?.SetGreyedOut(
            MissionRecruitPanelStateLogic.ShouldGreyOutCharacterPlaceholder(next));
        // Highlight the Accept button via Modulate (themable later).
        if (_acceptButton is not null)
        {
            _acceptButton.Modulate =
                MissionRecruitPanelStateLogic.ShouldHighlightAcceptButton(next)
                    ? AcceptHighlightTint
                    : Colors.White;
        }
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
            _slideTween.TweenCallback(Callable.From(OnCloseAnimationFinished));
        }
    }

    private void OnCloseAnimationFinished()
    {
        Visible = false;
        EmitSignal(SignalName.PanelClosed);
    }

    // -------------------------------------------------------------------
    // Button + signal handlers
    // -------------------------------------------------------------------

    private void OnClosePressed()
    {
        Close();
    }

    private void OnAcceptPressed()
    {
        var missionId = _currentMissionId;
        var npcId = _currentNpcId ?? string.Empty;
        if (string.IsNullOrEmpty(missionId))
        {
            GD.PushWarning("[MissionRecruitPanel] Accept pressed with no current mission -- no-op");
            return;
        }
        // Drop from local cache immediately so the tooltip refreshes.
        _missionStore?.RemoveById(missionId);
        _ = FireAcceptAsync(missionId, npcId);
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
        // Tell the owner to wipe the socle in case the perso had been
        // parked there mid-flow (spec 6.D).
        EmitSignal(SignalName.RecruitDeclined, missionId);
        Close();
    }

    private void OnReverseDropLanded(string npcId)
    {
        // The placeholder accepted a reverse drop from the socle.
        // Drive the state machine + tell the owner so it can wipe the
        // company socle.
        ApplyState(MissionRecruitPanelStateLogic.Next(
            _state, MissionRecruitPanelEvent.CharacterReturnedFromSocle));
        var missionId = _currentMissionId ?? string.Empty;
        EmitSignal(SignalName.CharacterReturnedToRecruitPanel, missionId, npcId);
    }

    private async Task FireAcceptAsync(string missionId, string npcId)
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
                    // Marshall back to the main thread before touching
                    // any scene state (CallDeferred = the trap memo's
                    // L4 main-thread-marshalling pin).
                    CallDeferred(MethodName.OnAcceptSucceededDeferred, missionId, npcId);
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
                    // Failure path : tell the owner to restore the
                    // socle visual state -- the player's intent did
                    // not land on the server.
                    CallDeferred(MethodName.OnAcceptFailedDeferred, missionId);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Panel freed mid-flight -- expected on E2 unload.
        }
    }

    /// <summary>
    /// Main-thread continuation : emits RecruitConfirmed so the owner
    /// can call CompanyPanel.ApplyConfirmedOccupancy. The panel itself
    /// has already closed by this point (Close ran synchronously
    /// inside OnAcceptPressed) ; the signal fires on the freed-but-
    /// still-instance panel, so the owner subscribes before Show.
    /// </summary>
    private void OnAcceptSucceededDeferred(string missionId, string npcId)
    {
        EmitSignal(SignalName.RecruitConfirmed, missionId, npcId);
    }

    private void OnAcceptFailedDeferred(string missionId)
    {
        // Owner restores the socle on Decline-shaped signal (the
        // server did not accept, treat as cancelled).
        EmitSignal(SignalName.RecruitDeclined, missionId);
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
