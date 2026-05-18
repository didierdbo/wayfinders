using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 étape 7 multi-recruit Compagnie panel (Varn-lock §A.10,
/// 2026-05-18). Slide-in from the LEFT edge of the viewport when the
/// player clicks a mission row in the E2 marker tooltip ; displays a
/// vertical column of <see cref="CompanyPanelRosterLogic.CompanyCapM1"/>
/// fixed iso socles (slot 0 = kira, slot 1 = dorn, slot 2 = vell). Each
/// socle is bound 1:1 to its roster NpcId ; the socle's occupied/empty
/// state is read from <see cref="NpcRegistry"/>, not from any
/// panel-local mutable state. Sticky strict (no auto-dismiss on
/// hover-out), closes via ESC or the header X.
///
/// <para>
/// <b>Étape 7 architecture shift (D4 lock).</b> The panel is now a
/// <b>stateless renderer</b>. State of truth lives on the
/// <see cref="NpcRegistry"/> autoload : the panel reads
/// <see cref="NpcRegistry.AllStates"/> at <c>_Ready</c> + subscribes to
/// <see cref="NpcRegistry.NpcStateChanged"/> to re-render whenever any
/// NPC's <see cref="NpcRuntimeState.IsRecruited"/> flips. Close +
/// reopen preserves occupancy automatically -- the panel scenetree is
/// rebuilt from the same registry snapshot.
/// </para>
///
/// <para>
/// <b>Étape 7 D5 highlight.</b> When a mission panel is open and its
/// <c>RecruitTargetNpcId</c> matches an empty roster slot, that slot's
/// socle is highlighted (terracotta rim glow, same colour as the
/// existing drag-begin glow per visual DNA lock 2026-05-07). The owner
/// (E2AreaMap) calls <see cref="SetActiveMissionTarget"/> when the
/// recruit panel opens / closes ; the panel translates the npcId into
/// a slot index via <see cref="CompanyPanelRosterLogic.SlotIndexOf"/>
/// and toggles the glow on the matching socle.
/// </para>
///
/// <para>
/// <b>D5 drop gate.</b> Each <see cref="SocleDropZone"/> is configured
/// with its slot index + a callback into
/// <see cref="CompanyPanelRosterLogic.CanDropOnSocle"/> so the drop
/// hook rejects a drop whose <c>npc_id</c> does not match the slot's
/// roster NpcId (= wrong-slot drop, D5 row 2) OR whose slot is already
/// occupied (D3 row 2). Combined with the existing
/// <see cref="IsoSocketDropLogic.CanAccept"/> roster gate, a drop
/// lands iff (slot.NpcId == payload.NpcId) AND (slot empty).
/// </para>
///
/// <para>
/// <b>D3 reverse-drag.</b> Reverse drag (drag from socle back to
/// recruit panel) is preserved -- the
/// <see cref="IsoSocketPlaceholder.SetConfirmedOccupancy"/> on
/// confirmed-occupied socles still disables <c>_GetDragData</c>.
/// Reorder / dismiss / patrol all stay M2 scope.
/// </para>
///
/// <para>
/// <b>No CTS at v1.</b> The Compagnie panel issues no async ApiClient
/// calls of its own -- recruitment writes happen at the
/// <see cref="MissionRecruitPanel"/> after a successful Accept
/// round-trip ; the company panel only renders the state.
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

    /// <summary>Socle visual rect, px. Locked at 280 x 240 -- the
    /// rhombus' visible width + a tall enough band for the occupant
    /// silhouette + name label.</summary>
    private const float SocleWidthPx = 280f;
    private const float SocleHeightPx = 240f;

    /// <summary>The M1 roster fed to each drop zone for the npc-id
    /// gate. Same set as IsoSocketPlaceholder.RosterM1 -- pinned by
    /// xUnit so any drift between this constant and the roster helper
    /// surfaces red.</summary>
    private static readonly HashSet<string> RosterM1KnownIds =
        new() { "kira", "dorn", "vell" };

    private static readonly Color ParchmentBg = new(0.91f, 0.85f, 0.72f, 0.97f);
    private static readonly Color ParchmentBorder = new(0.18f, 0.14f, 0.11f, 1f);

    private PanelContainer? _panelContainer;
    private Label? _headerLabel;
    private Button? _closeButton;
    private Tween? _slideTween;

    /// <summary>One iso socle per roster entry. Index aligns with
    /// <see cref="CompanyPanelRosterLogic.Slots"/>.</summary>
    private readonly List<IsoSocketPlaceholder> _socles = new();
    private readonly List<SocleDropZone> _dropZones = new();

    private NpcRegistry? _npcRegistry;
    private bool _isOpen;

    /// <summary>Currently-targeted NpcId from the open mission panel,
    /// null when no mission panel is open. Drives the slot-highlight.
    /// Set via <see cref="SetActiveMissionTarget"/>.</summary>
    private string? _activeMissionTargetNpcId;

    /// <summary>
    /// Fired when the panel finishes its close animation.
    /// </summary>
    [Signal]
    public delegate void PanelClosedEventHandler();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.LeftWide);
        OffsetLeft = ClosedOffsetX;
        OffsetRight = ClosedOffsetX + PanelWidthPx;
        OffsetTop = 0f;
        OffsetBottom = 0f;

        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        // Resolve the NpcRegistry autoload BEFORE building children :
        // each socle's initial occupancy is read from the registry, so
        // the lookup has to be ready before BuildChildren materialises
        // the sockets. Failure to find the autoload (= broken project
        // setup) surfaces as an explicit warning + all socles render
        // empty, rather than a silent NRE deep in the socle build.
        _npcRegistry = GetTree()?.Root?.GetNodeOrNull<NpcRegistry>("NpcRegistry");
        if (_npcRegistry is null)
        {
            GD.PushWarning("[CompanyPanel] NpcRegistry autoload not found -- panel will render empty socles");
        }
        else
        {
            // Subscribe to the registry's state-changed signal so the
            // panel re-renders whenever any NPC's IsRecruited flips.
            // Disconnection discipline : unsubscribed in _ExitTree
            // (signal-leak trap Risk #1).
            _npcRegistry.NpcStateChanged += OnNpcStateChanged;
        }

        BuildChildren();
        // Seed the initial rendering from the registry snapshot.
        RefreshSocleRendering();
        RefreshHeaderText();
    }

    public override void _ExitTree()
    {
        if (_npcRegistry is not null && IsInstanceValid(_npcRegistry))
        {
            _npcRegistry.NpcStateChanged -= OnNpcStateChanged;
        }
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
        // Drag-begin / drag-end : route the rim glow to the targeted
        // socle (the one matching the active mission's RecruitTargetNpcId)
        // rather than every socle. Wrong-slot socles stay quiet during
        // the drag.
        switch ((long)what)
        {
            case NotificationDragBegin:
                SetTargetedSocleGlow(true);
                break;
            case NotificationDragEnd:
                SetTargetedSocleGlow(false);
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
    /// Étape 7 D5 -- track which NpcId the currently-open mission
    /// panel is targeting, so the matching empty socle can be
    /// highlighted while the panel is open. Called by the owner
    /// (E2AreaMap) when the recruit panel opens / closes / swaps
    /// content. <paramref name="npcId"/> = null clears the highlight
    /// (no mission panel open).
    /// </summary>
    public void SetActiveMissionTarget(string? npcId)
    {
        var normalized = string.IsNullOrEmpty(npcId) ? null : npcId;
        if (_activeMissionTargetNpcId == normalized) return;
        _activeMissionTargetNpcId = normalized;
        // The highlight visibility now follows
        // _activeMissionTargetNpcId + the socle's empty state ; the
        // glow itself fires on drag-begin (the existing rim-glow
        // pulse). For now we DO NOT pulse the highlight without a
        // drag in flight (avoids ambient noise) ; the static "this
        // is the slot you want" cue is implicit via the empty socle
        // already standing out from the occupied ones.
        // If playtest later wants an ambient highlight, flip the
        // SetDropTargetGlow call below on whenever the target is set.
    }

    /// <summary>Read-only roster snapshot of socle placeholders. Used
    /// by integration smoke + xUnit tests to assert the panel scene
    /// tree contains exactly <see cref="CompanyPanelRosterLogic.CompanyCapM1"/>
    /// sockets at construction time.</summary>
    public IReadOnlyList<IsoSocketPlaceholder> Socles => _socles;

    /// <summary>Read-only drop zone snapshot (same indexing as
    /// <see cref="Socles"/>).</summary>
    public IReadOnlyList<SocleDropZone> DropZones => _dropZones;

    /// <summary>Compatibility shim : returns the first socle so the
    /// existing E2AreaMap wiring (subscribed at the panel level via
    /// the old <c>Socle</c> getter) continues to compile during the
    /// step-7 transition. Tests should use <see cref="Socles"/>.</summary>
    public IsoSocketPlaceholder? Socle => _socles.Count > 0 ? _socles[0] : null;

    // -------------------------------------------------------------------
    // D4 stateless renderer -- registry-driven occupancy + header.
    // -------------------------------------------------------------------

    private void OnNpcStateChanged(string npcId)
    {
        // Re-render lazily : a single npc's state changed, but the
        // header recount + every socle re-evaluation is cheap (3
        // entries). Doing all of them keeps the code dead simple and
        // avoids divergence on "did we miss a slot?".
        RefreshSocleRendering();
        RefreshHeaderText();
    }

    private void RefreshSocleRendering()
    {
        for (int i = 0; i < _socles.Count; i++)
        {
            var npcId = CompanyPanelRosterLogic.NpcIdAtSlot(i);
            if (string.IsNullOrEmpty(npcId)) continue;
            var state = _npcRegistry?.GetState(npcId);
            var isRecruited = state?.IsRecruited == true;
            ApplySocleOccupancy(_socles[i], npcId, isRecruited);
        }
    }

    private static void ApplySocleOccupancy(
        IsoSocketPlaceholder socle, string npcId, bool isRecruited)
    {
        if (isRecruited)
        {
            socle.SetOccupiedNpc(npcId);
            socle.SetConfirmedOccupancy(true);
        }
        else
        {
            socle.SetOccupiedNpc(null);
            socle.SetConfirmedOccupancy(false);
        }
    }

    private void RefreshHeaderText()
    {
        if (_headerLabel is null) return;
        var recruitedCount = CountRecruited();
        _headerLabel.Text = CompanyPanelRosterLogic.ComposeHeaderText(recruitedCount);
    }

    private int CountRecruited()
    {
        if (_npcRegistry is null) return 0;
        int count = 0;
        foreach (var npcId in CompanyPanelRosterLogic.Slots)
        {
            if (_npcRegistry.GetState(npcId)?.IsRecruited == true) count++;
        }
        return count;
    }

    /// <summary>
    /// Étape 7 D5 -- toggle the rim glow only on the socle whose
    /// roster NpcId matches the active mission's RecruitTargetNpcId
    /// (and that socle is currently empty). Wrong-slot socles stay
    /// quiet during the drag.
    /// </summary>
    private void SetTargetedSocleGlow(bool active)
    {
        for (int i = 0; i < _socles.Count; i++)
        {
            var npcId = CompanyPanelRosterLogic.NpcIdAtSlot(i);
            if (string.IsNullOrEmpty(npcId)) continue;
            var isRecruited = _npcRegistry?.GetState(npcId)?.IsRecruited == true;
            var shouldGlow = active && CompanyPanelRosterLogic.ShouldHighlightSocle(
                i, _activeMissionTargetNpcId, isRecruited);
            _socles[i].SetDropTargetGlow(shouldGlow);
        }
    }

    /// <summary>
    /// Read whether a socle index can accept the active mission's
    /// drop (= matching NpcId AND empty). Used by
    /// <see cref="SocleDropZone._CanDropData"/> via the
    /// <see cref="SocleDropZone.SlotGateDelegate"/> hook.
    /// </summary>
    internal bool CanDropOnSlot(int socleIndex, string? payloadNpcId)
    {
        var slotNpcId = CompanyPanelRosterLogic.NpcIdAtSlot(socleIndex);
        if (string.IsNullOrEmpty(slotNpcId)) return false;
        // Wrong-slot drop : payload NpcId does not match the slot's
        // roster NpcId (D5 row 2 reject).
        if (payloadNpcId != slotNpcId) return false;
        // Occupied-slot drop : NpcRegistry says IsRecruited=true
        // (D3 row 2 reject).
        var isRecruited = _npcRegistry?.GetState(slotNpcId)?.IsRecruited == true;
        if (isRecruited) return false;
        return true;
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

        // Compute the drop-zone geometry once -- every socle shares
        // the same above-band height + extra margin.
        var aboveBandPx = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            IsoSocketCharacterPlacementLogic.OccupantPaddingPx,
            IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight,
            SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
        var (zoneW, zoneH) = SocleDropZoneGeometryLogic.ComputeDropZoneSize(
            SocleWidthPx, SocleHeightPx,
            aboveBandPx,
            SocleDropZoneGeometryLogic.DefaultExtraMarginXpx);

        // Build N socles (one per roster entry, in the locked order).
        // Each goes into its own CenterContainer child of the rootVbox
        // so the vertical stack reads as a clean "header / socle 0 /
        // socle 1 / socle 2" layout (Varn §A.10.D1).
        for (int i = 0; i < CompanyPanelRosterLogic.CompanyCapM1; i++)
        {
            var slotNpcId = CompanyPanelRosterLogic.Slots[i];

            var socleWrapper = new CenterContainer
            {
                Name = $"SocleWrapper{i}",
                MouseFilter = MouseFilterEnum.Pass,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            rootVbox.AddChild(socleWrapper);

            var dropZone = new SocleDropZone
            {
                Name = $"SocleDropZone{i}",
            };
            dropZone.CustomMinimumSize = new Vector2(zoneW, zoneH);
            socleWrapper.AddChild(dropZone);

            var socle = new IsoSocketPlaceholder
            {
                Name = $"IsoSocket{i}",
            };
            socle.CustomMinimumSize = new Vector2(SocleWidthPx, SocleHeightPx);
            socle.Size = new Vector2(SocleWidthPx, SocleHeightPx);
            socle.Position = new Vector2(SocleDropZoneGeometryLogic.DefaultExtraMarginXpx, aboveBandPx);
            socle.SetDropTargetRole(IsoSocketPlaceholder.DropTargetRole.CompanyHost);
            dropZone.AddChild(socle);

            // Configure the drop zone with the slot index + the slot
            // gate callback so _CanDropData rejects wrong-slot drops
            // and occupied-slot drops (D3 row 2 + D5 row 2).
            // Captured-i pattern : C# foreach loop variable was
            // historically captured-by-reference, but C# 5+ captures
            // per-iteration so the closure binds the right index.
            int capturedIndex = i;
            dropZone.Configure(
                socle,
                socleOffsetYInZone: aboveBandPx,
                knownNpcIds: RosterM1KnownIds,
                slotGate: payloadNpcId => CanDropOnSlot(capturedIndex, payloadNpcId));

            _socles.Add(socle);
            _dropZones.Add(dropZone);
        }
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
}
