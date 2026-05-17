using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 5 placeholder Control rendering a flat iso rhombus (losange,
/// top-down view) as the central "socle" inside the Compagnie panel
/// (Didier-lock 2026-05-17). Reusable component -- not tied to the
/// Compagnie panel specifically ; any panel needing an iso drop target
/// can host it.
///
/// <para>
/// <b>What this is.</b> A single parchment-cream rhombus with an umber
/// outline, drawn procedurally by <see cref="_Draw"/> using vertices
/// from <see cref="IsoSocketGeometryLogic"/>. At étape 6 it also acts
/// as a drag-drop target (forward path : drop the recruit panel's
/// character onto here) AND as a drag source (reverse path : drag the
/// occupant back to the recruit panel), with a terracotta rim glow
/// affordance pulsing while any drag is in progress on the viewport.
/// </para>
///
/// <para>
/// <b>What this is NOT.</b> NOT the same component as
/// <see cref="IsoCharacterPlaceholder"/> -- that one renders a 3-face
/// 2.5D parallelepiped (a character SILHOUETTE). This one is the GROUND
/// the character stands on, a flat rhombus, no top/front/side faces.
/// When occupied, this socle HOSTS an <see cref="IsoCharacterPlaceholder"/>
/// child in compact mode above the rhombus.
/// </para>
///
/// <para>
/// <b>API surface.</b>
/// <list type="bullet">
///   <item><see cref="SetOccupiedNpc"/> -- mark the socket as
///         occupied by the given NPC id (or empty when null).
///         Spawns / frees the mini <see cref="IsoCharacterPlaceholder"/>
///         child + the per-NPC name label below the rhombus. Idempotent.</item>
///   <item><see cref="SetDropTargetGlow"/> -- toggle the terracotta
///         rim glow pulse (driven by a sine wave on <see cref="_Process"/>).</item>
///   <item><see cref="SetConfirmedOccupancy"/> -- freeze the socle
///         after a successful Accept round-trip : disable the reverse
///         drag (the perso is committed to the Compagnie), keep the
///         occupant visible.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Drop discipline (memo feedback_godot_rendering_input_traps trap
/// "MouseFilter on drop target").</b> When configured as a drop target
/// via <see cref="SetDropTargetRole"/>, MouseFilter flips from Ignore
/// to Stop -- Godot will NOT route _CanDropData / _DropData to a
/// Control whose mouse filter blocks events. The setter is called
/// once by the owning CompanyPanel.
/// </para>
///
/// <para>
/// <b>Rim glow (étape 6 visual feedback).</b> Activates whenever
/// <see cref="SetDropTargetGlow"/> is called with <c>true</c> (the
/// owner subscribes to <see cref="Control._Notification"/> with
/// <see cref="Control.NotificationDragBegin"/> /
/// <see cref="Control.NotificationDragEnd"/>). Colour = terracotta
/// (<c>#C8946E</c>, same NPC-palette anchor as Kira) at 50 % base
/// alpha, sine-modulated at 1.2 s period for a slow, deliberate
/// pulse (not arcade). Reset to no-glow on drop release.
/// </para>
/// </summary>
public partial class IsoSocketPlaceholder : Control
{
    /// <summary>Default cmin size -- a 2:1 iso rhombus 160 x 80, plus
    /// the side padding band reserved for the outline and the
    /// occupant label below.</summary>
    private static readonly Vector2 DefaultMinSize = new(168, 88);

    /// <summary>Padding inside the Control's rect so the outline stroke
    /// is not clipped at the rect edges.</summary>
    private const float PaddingPx = 4f;

    /// <summary>Outline stroke width, px.</summary>
    private const float OutlineWidth = 2f;

    /// <summary>Rim glow stroke width, px. Drawn outside the regular
    /// outline so the glow reads as a halo, not as a thicker border.</summary>
    private const float GlowWidth = 4f;

    /// <summary>Rim glow base colour -- terracotta (same anchor as
    /// Kira's unit tint, locked 2026-05-07 visual DNA).</summary>
    private static readonly Color GlowColor = new(200f / 255f, 148f / 255f, 110f / 255f, 0.55f);

    /// <summary>Rim glow pulse period, seconds. Slow + deliberate
    /// (not arcade) -- locked at 1.2s.</summary>
    private const float GlowPulsePeriodSeconds = 1.2f;

    /// <summary>Parchment cream fill -- one step warmer than the panel
    /// background so the socle reads as a distinct surface without
    /// fighting the panel chrome (locked 2026-05-17 visual DNA :
    /// parchment cream + umber).</summary>
    private static readonly Color FillColor = new(0.91f, 0.86f, 0.76f, 1f);

    /// <summary>Umber outline -- matches the IsoCharacterPlaceholder
    /// face outlines and the parchment tooltip / panel borders.</summary>
    private static readonly Color OutlineColor = new(0.18f, 0.14f, 0.11f, 1f);

    /// <summary>Vertical pixel offset between the rhombus and the
    /// occupant's name label (rendered below the rhombus).</summary>
    private const float NameLabelOffsetPx = 6f;

    /// <summary>Pixel band reserved BELOW the rhombus for the occupant's
    /// name label. The rhombus is drawn in the rect minus this band so
    /// the layout stays balanced.</summary>
    private const float NameLabelBandPx = 28f;

    private string? _occupiedNpcId;
    private bool _confirmedOccupancy;
    private bool _dropTargetGlowActive;
    private float _glowPhase;
    private DropTargetRole _role = DropTargetRole.Decorative;

    private IsoCharacterPlaceholder? _occupantSilhouette;
    private Label? _occupantNameLabel;

    /// <summary>
    /// Fired when the socle's occupancy changes (drop landed -> npcId
    /// non-null ; reverse drag picked the perso back -> npcId null).
    /// The owning CompanyPanel re-emits to the parent E2AreaMap which
    /// forwards to the MissionRecruitPanel's state machine.
    /// </summary>
    [Signal]
    public delegate void OccupancyChangedEventHandler(string npcId);

    /// <summary>
    /// Fired when the socle hosts a confirmed recruit and a drag is
    /// initiated from it (reverse drag). Carries the npcId so the
    /// owner can clear the local greyed-out state at the same time
    /// the drag preview spawns. Not currently used by the recruit
    /// panel flow (the drop is what triggers state changes) ; kept as
    /// a future seam for "drag from socle => preview" logging.
    /// </summary>
    [Signal]
    public delegate void ReverseDragStartedEventHandler(string npcId);

    public override void _Ready()
    {
        CustomMinimumSize = DefaultMinSize;
        // Default = decorative ; SetDropTargetRole flips to Stop when
        // the owning panel configures the socle as a drop target.
        MouseFilter = ResolveMouseFilter();
        // Need _Process for the rim glow pulse animation.
        SetProcess(true);
    }

    /// <summary>
    /// Configure the socle as either a decorative element (legacy
    /// behaviour) or as a drop target / drag source (étape 6). The
    /// CompanyPanel calls this once with <see cref="DropTargetRole.CompanyHost"/>.
    /// </summary>
    public void SetDropTargetRole(DropTargetRole role)
    {
        _role = role;
        MouseFilter = ResolveMouseFilter();
    }

    /// <summary>
    /// Toggle the rim glow pulse. Driven by the owning panel from the
    /// global drag-begin / drag-end notifications. Idempotent.
    /// </summary>
    public void SetDropTargetGlow(bool active)
    {
        if (_dropTargetGlowActive == active) return;
        _dropTargetGlowActive = active;
        if (!active) _glowPhase = 0f;
        QueueRedraw();
    }

    /// <summary>
    /// Mark the socket as occupied by the given NPC id (or empty when
    /// <c>null</c>). Spawns a child mini-IsoCharacterPlaceholder above
    /// the rhombus + a name Label below ; frees them on the empty
    /// transition. Idempotent.
    /// </summary>
    public void SetOccupiedNpc(string? npcId)
    {
        if (_occupiedNpcId == npcId) return;
        _occupiedNpcId = npcId;

        // Always reset confirmed flag on any occupancy change -- a new
        // drop is a pre-Accept state ; the owner re-calls
        // SetConfirmedOccupancy after the accept round-trip succeeds.
        if (string.IsNullOrEmpty(npcId)) _confirmedOccupancy = false;

        RebuildOccupantChildren();
        EmitSignal(SignalName.OccupancyChanged, npcId ?? string.Empty);
        QueueRedraw();
    }

    /// <summary>
    /// Freeze the socle as "confirmed occupied" after a successful
    /// Accept round-trip. The reverse drag is disabled (the perso is
    /// committed to the Compagnie). Idempotent.
    /// </summary>
    public void SetConfirmedOccupancy(bool confirmed)
    {
        if (_confirmedOccupancy == confirmed) return;
        _confirmedOccupancy = confirmed;
        // No visual change on the socle itself -- the confirmed state
        // is a behavioural lock (no reverse drag). Visual differentiation
        // could land here if the spec evolves.
    }

    /// <summary>Currently-occupying NPC id, or <c>null</c> if empty.</summary>
    public string? OccupiedNpcId => _occupiedNpcId;

    /// <summary>True iff the occupancy has been confirmed by a
    /// successful Accept round-trip.</summary>
    public bool IsOccupancyConfirmed => _confirmedOccupancy;

    public override void _Process(double delta)
    {
        if (!_dropTargetGlowActive) return;
        _glowPhase += (float)delta;
        if (_glowPhase > GlowPulsePeriodSeconds) _glowPhase -= GlowPulsePeriodSeconds;
        QueueRedraw();
    }

    /// <summary>
    /// Godot drag-source hook. Acts as the reverse-drag source when
    /// the socle hosts an unconfirmed occupant. Returns the drag
    /// payload with source = company_socle so the recruit panel's
    /// IsoCharacterPlaceholder accepts the reverse drop.
    /// </summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        _ = atPosition;
        if (_role != DropTargetRole.CompanyHost) return default;
        if (string.IsNullOrEmpty(_occupiedNpcId)) return default;
        // Confirmed occupancy = the perso is committed ; no reverse
        // drag allowed (M1 has no recall UI -- spec 6.C).
        if (_confirmedOccupancy) return default;

        var payload = new Godot.Collections.Dictionary
        {
            [IsoSocketDropLogic.PayloadKeyNpcId] = _occupiedNpcId,
            [IsoSocketDropLogic.PayloadKeyMissionId] = string.Empty,
            [IsoSocketDropLogic.PayloadKeySource] = IsoSocketDropLogic.DragSourceCompanySocle,
        };

        // Shared drag-preview construction with the recruit panel's
        // placeholder : a half-alpha compact silhouette wrapped in a
        // Control that hosts a CanvasLayer at layer = 200 so the ghost
        // renders above the recruit panel (layer 110) and the company
        // panel (layer 111). See IsoCharacterPlaceholder.BuildDragPreview
        // for the rationale.
        SetDragPreview(IsoCharacterPlaceholder.BuildDragPreview(_occupiedNpcId));

        EmitSignal(SignalName.ReverseDragStarted, _occupiedNpcId);
        return payload;
    }

    /// <summary>
    /// Godot drop-target hook -- accept the forward drop from the
    /// recruit panel's IsoCharacterPlaceholder. Forwards the payload
    /// shape to <see cref="IsoSocketDropLogic.CanAccept"/>.
    /// </summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        if (_role != DropTargetRole.CompanyHost) return false;
        // Once confirmed, the socle is locked -- no further drops.
        if (_confirmedOccupancy) return false;
        var payload = TryReadDragPayload(data);
        var decision = IsoSocketDropLogic.CanAccept(payload, RosterM1);
        return decision == IsoSocketDropLogic.DropDecision.Accept;
    }

    /// <summary>
    /// Godot drop-target hook -- consume the forward drop, set the
    /// occupant, emit <see cref="OccupancyChangedEventHandler"/>.
    /// </summary>
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        var payload = TryReadDragPayload(data);
        if (payload is null) return;
        SetOccupiedNpc(payload.NpcId);
    }

    public override void _Draw()
    {
        var availW = Mathf.Max(0f, Size.X - 2f * PaddingPx);
        // Reserve a band below the rhombus for the occupant's name
        // label, so the layout stays balanced whether occupied or not.
        var availH = Mathf.Max(0f, Size.Y - 2f * PaddingPx - NameLabelBandPx);
        if (availW <= 4f || availH <= 4f) return;

        var w = availW;
        var h = IsoSocketGeometryLogic.HeightFor(w);
        if (h > availH)
        {
            h = availH;
            w = h * IsoSocketGeometryLogic.WidthOverHeight;
        }

        var offX = PaddingPx + (availW - w) * 0.5f;
        var offY = PaddingPx + (availH - h) * 0.5f;

        var pts = IsoSocketGeometryLogic.Rhombus(w);
        var polygon = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            polygon[i] = new Vector2(pts[i].X + offX, pts[i].Y + offY);
        }

        // Draw glow halo first (under the fill) so it reads as a soft
        // surround, not as a stroke on top of the parchment.
        if (_dropTargetGlowActive)
        {
            var pulseAlpha = 0.3f + 0.7f * (0.5f - 0.5f * Mathf.Cos(
                Mathf.Tau * (_glowPhase / GlowPulsePeriodSeconds)));
            var glowTint = new Color(GlowColor.R, GlowColor.G, GlowColor.B, GlowColor.A * pulseAlpha);
            DrawPolyline(ClosePolyline(polygon), glowTint, GlowWidth);
        }

        DrawColoredPolygon(polygon, FillColor);
        DrawPolyline(ClosePolyline(polygon), OutlineColor, OutlineWidth);

        // Position the occupant silhouette + name label relative to
        // the rhombus we just drew. Done here (not in SetOccupiedNpc)
        // so a resize repositions correctly.
        PositionOccupantChildren(offY, h);
    }

    // -------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------

    private void RebuildOccupantChildren()
    {
        if (string.IsNullOrEmpty(_occupiedNpcId))
        {
            FreeOccupantChildren();
            return;
        }

        if (_occupantSilhouette is null || !IsInstanceValid(_occupantSilhouette))
        {
            _occupantSilhouette = new IsoCharacterPlaceholder
            {
                Name = "OccupantSilhouette",
            };
            _occupantSilhouette.SetCompactMode(true);
            AddChild(_occupantSilhouette);
        }
        _occupantSilhouette.SetNpc(_occupiedNpcId);

        if (_occupantNameLabel is null || !IsInstanceValid(_occupantNameLabel))
        {
            _occupantNameLabel = new Label
            {
                Name = "OccupantNameLabel",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = false,
            };
            _occupantNameLabel.AddThemeFontSizeOverride("font_size", 16);
            _occupantNameLabel.AddThemeColorOverride("font_color", new Color(0.10f, 0.08f, 0.06f, 1f));
            _occupantNameLabel.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 1f));
            _occupantNameLabel.AddThemeConstantOverride("outline_size", 3);
            AddChild(_occupantNameLabel);
        }
        _occupantNameLabel.Text = NpcCatalog.LookupDisplayName(_occupiedNpcId);
    }

    private void PositionOccupantChildren(float rhombusOffsetY, float rhombusH)
    {
        if (_occupantSilhouette is not null && IsInstanceValid(_occupantSilhouette))
        {
            // Place the silhouette so its parallelepiped BASE lands on
            // the rhombus' TOP apex (the perso visibly stands on the
            // socle, not in front of it). Reads the iso body height
            // (D + H, = 220 in compact) from the silhouette directly so
            // a future cmin / padding / label-band tweak fans out
            // without touching this site. Pinned by
            // IsoSocketCharacterPlacementLogicTests so any helper drift
            // surfaces loud at xUnit.
            var sw = _occupantSilhouette.CustomMinimumSize.X;
            var sh = _occupantSilhouette.CustomMinimumSize.Y;
            var isoBodyHeight = _occupantSilhouette.ActiveIsoBodyHeight;
            var (px, py) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
                Size.X, rhombusOffsetY, sw, isoBodyHeight, IsoCharacterPlaceholder.PaddingPx);
            _occupantSilhouette.Position = new Vector2(px, py);
            _occupantSilhouette.Size = new Vector2(sw, sh);
        }

        if (_occupantNameLabel is not null && IsInstanceValid(_occupantNameLabel))
        {
            // Place the name label band below the rhombus, full width.
            _occupantNameLabel.Position = new Vector2(
                0f,
                rhombusOffsetY + rhombusH + NameLabelOffsetPx);
            _occupantNameLabel.Size = new Vector2(Size.X, NameLabelBandPx);
        }
    }

    private void FreeOccupantChildren()
    {
        if (_occupantSilhouette is not null && IsInstanceValid(_occupantSilhouette))
        {
            _occupantSilhouette.QueueFree();
        }
        _occupantSilhouette = null;
        if (_occupantNameLabel is not null && IsInstanceValid(_occupantNameLabel))
        {
            _occupantNameLabel.QueueFree();
        }
        _occupantNameLabel = null;
    }

    private MouseFilterEnum ResolveMouseFilter()
        => _role == DropTargetRole.Decorative
            ? MouseFilterEnum.Ignore
            : MouseFilterEnum.Stop;

    private static readonly System.Collections.Generic.HashSet<string> RosterM1 =
        new() { "kira", "dorn", "vell" };

    private static DragPayload? TryReadDragPayload(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return null;
        var dict = data.AsGodotDictionary();
        string? npcId = null, missionId = null, source = null;
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyNpcId, out var nv))
            npcId = nv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyMissionId, out var mv))
            missionId = mv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeySource, out var sv))
            source = sv.AsString();
        return new DragPayload(
            NpcId: npcId ?? string.Empty,
            MissionId: missionId ?? string.Empty,
            Source: source ?? string.Empty);
    }

    private static Vector2[] ClosePolyline(Vector2[] polygon)
    {
        var closed = new Vector2[polygon.Length + 1];
        polygon.CopyTo(closed, 0);
        closed[polygon.Length] = polygon[0];
        return closed;
    }

    /// <summary>The two roles the socle can play in the étape 6
    /// drag&amp;drop seam.</summary>
    public enum DropTargetRole
    {
        /// <summary>No drop / drag, MouseFilter stays Ignore
        /// (default ; legacy behaviour, decorative only).</summary>
        Decorative = 0,

        /// <summary>Acts as the company panel's drop target (forward
        /// path) AND drag source for the reverse path. MouseFilter
        /// = Stop. _GetDragData fires on drag start when occupied ;
        /// _CanDropData / _DropData accept the recruit_panel source.</summary>
        CompanyHost = 1,
    }
}
