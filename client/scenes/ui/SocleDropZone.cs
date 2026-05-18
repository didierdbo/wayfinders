using System;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 étape 6 invisible drop-zone Control that ENGLOBE the
/// <see cref="IsoSocketPlaceholder"/> rhombus + extends ~232 px ABOVE
/// it so the GHOST-BASE hit-test fires even when the cursor sits high
/// up (= the player clicked the perso's head and the sticky-to-click
/// rule glued the cursor there).
///
/// <para>
/// <b>The bug this fixes (Didier UX bug 2 résiduel, 2026-05-17 soir).</b>
/// Godot only routes <c>_CanDropData</c> / <c>_DropData</c> to a Control
/// whose physical rect contains the mouse cursor. Pre-fix, those hooks
/// lived on the <see cref="IsoSocketPlaceholder"/> directly (rect = 280 x 240).
/// When the player clicked at the top of the silhouette, the cursor
/// sat ~232 px above the socle's top edge for the whole drag, never
/// entered the socle's rect, so <c>_CanDropData</c> was never called,
/// the drop silently refused, and the player perceived "le drop ne
/// marche pas". The ghost-base arithmetic (cursor + pickup_offset_y =
/// base position) was correct -- but it never got a chance to fire.
/// </para>
///
/// <para>
/// <b>The fix (option 1 of 4 reviewed with Didier).</b> Move the
/// drop-target hooks onto this larger Control whose rect catches every
/// cursor position the ghost-base arithmetic might still want to test.
/// The actual hit-test logic stays in <see cref="IsoDropHitTestLogic"/> ;
/// this Control only translates its local <c>atPosition</c> into the
/// socle's local frame (via
/// <see cref="SocleDropZoneGeometryLogic.MapDropZoneYToSocketY"/>) and
/// forwards. On accept, it delegates back to the socle's
/// <see cref="IsoSocketPlaceholder.SetOccupiedNpc"/> so the socle keeps
/// owning the visual occupied state.
/// </para>
///
/// <para>
/// <b>Étape 7 slot gate (Varn §A.10.D5, 2026-05-18).</b> The multi-
/// recruit company panel hosts N drop zones, one per roster slot.
/// Each zone must additionally reject drops whose <c>npc_id</c> does
/// not match the slot's roster NpcId (= wrong-slot drop) AND drops
/// onto an already-recruited slot (= occupied-slot drop). The owner
/// (CompanyPanel) injects a <see cref="SlotGateDelegate"/> via
/// <see cref="Configure"/> that wraps the
/// <c>CompanyPanelRosterLogic.CanDropOnSocle</c> decision. When the
/// gate returns false, <see cref="_CanDropData"/> rejects before the
/// ghost-base hit-test even runs.
/// </para>
///
/// <para>
/// <b>Reverse-drag discipline (approach (a) from the brief).</b> The
/// socle keeps its <c>_GetDragData</c> hook. To route reverse drag
/// correctly, the socle's MouseFilter follows occupancy via
/// <see cref="SocleMouseFilterLogic.Resolve"/> :
/// <list type="bullet">
///   <item>Empty -> Ignore -> click passes to this drop zone -> forward drop works.</item>
///   <item>Occupied -> Stop -> click stops at the socle -> reverse drag works.</item>
/// </list>
/// The drop zone is always MouseFilter.Stop ; when the socle is empty
/// it is the topmost intercepting Control over the rhombus area, when
/// the socle is occupied the socle wins because it draws ON TOP of the
/// drop zone in the tree (drop zone is the parent / earlier-in-tree).
/// </para>
///
/// <para>
/// <b>Debug visualization.</b> Set <see cref="DebugVisible"/> = true to
/// render the drop zone as a semi-transparent terracotta tint -- useful
/// when smoke-testing the hit-test bounds. Off by default ; the runtime
/// CompanyPanel does NOT expose this externally for now (the constant
/// is here so a future <c>--enable-debug-drop-zone</c> CLI flag can flip
/// it without touching the layout).
/// </para>
/// </summary>
public partial class SocleDropZone : Control
{
    /// <summary>
    /// Slot-gate delegate injected by the owning CompanyPanel. Returns
    /// true iff the drop is allowed on THIS specific slot (= matching
    /// NpcId + empty). The drop zone calls it with the payload's
    /// <c>npc_id</c> after the global
    /// <see cref="IsoSocketDropLogic.CanAccept"/> roster gate passes.
    /// Optional ; null gate = legacy étape 6 behaviour (any roster id
    /// accepted on any drop zone, used by the M1 single-socle path
    /// until step 7).
    /// </summary>
    public delegate bool SlotGateDelegate(string? payloadNpcId);

    /// <summary>Tag injected by the CompanyPanel at construction so the
    /// drop zone can reach its sibling socle to forward
    /// <see cref="IsoSocketPlaceholder.SetOccupiedNpc"/> on accept.</summary>
    private IsoSocketPlaceholder? _socle;

    /// <summary>Y offset of the socle inside the drop zone's rect (= the
    /// above-socle band height). The socle sits at the bottom of the
    /// drop zone ; the above-band sits on top. Used to map atPosition
    /// from drop-zone-local to socket-local before forwarding to the
    /// existing hit-test helper.</summary>
    private float _socleOffsetYInZone;

    /// <summary>Roster of known NPC ids the drop zone gates on. Owner
    /// injects ; currently the M1 trio.</summary>
    private System.Collections.Generic.IReadOnlySet<string> _knownNpcIds =
        new System.Collections.Generic.HashSet<string>();

    /// <summary>Per-slot drop gate (étape 7). Null = legacy behaviour
    /// (any roster id passes ; matches single-socle étape 6 mode).</summary>
    private SlotGateDelegate? _slotGate;

    /// <summary>Debug paint flag. False by default ; the constant is in
    /// place so a future debug-CLI flag flips it without recompile.</summary>
    public bool DebugVisible { get; set; }

    private static readonly Color DebugTint = new(200f / 255f, 148f / 255f, 110f / 255f, 0.18f);

    public override void _Ready()
    {
        // Drop zone is always intercepting. The socle's MouseFilter
        // follows occupancy (Stop when occupied so the click stops at
        // the socle for reverse drag ; Ignore when empty so the click
        // passes through to this drop zone for forward drop).
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>
    /// Owner-injected configuration. Called once by the CompanyPanel
    /// after both nodes are added to the tree but before the panel
    /// becomes visible.
    /// </summary>
    /// <param name="socle">Sibling socle this zone routes drops into.</param>
    /// <param name="socleOffsetYInZone">Y offset of the socle within
    /// the drop zone's rect (= the above-socle band height).</param>
    /// <param name="knownNpcIds">Roster set fed to
    /// <see cref="IsoSocketDropLogic.CanAccept"/>.</param>
    /// <param name="slotGate">Optional per-slot gate (étape 7). Null
    /// keeps the legacy any-slot behaviour for backward compatibility
    /// with the étape 6 single-socle wiring.</param>
    public void Configure(
        IsoSocketPlaceholder socle,
        float socleOffsetYInZone,
        System.Collections.Generic.IReadOnlySet<string> knownNpcIds,
        SlotGateDelegate? slotGate = null)
    {
        _socle = socle;
        _socleOffsetYInZone = socleOffsetYInZone;
        _knownNpcIds = knownNpcIds;
        _slotGate = slotGate;
    }

    /// <summary>
    /// Godot drop-target hook -- now lives on this drop zone instead of
    /// on <see cref="IsoSocketPlaceholder"/>. Maps the drop-zone-local
    /// atPosition into socket-local Y, then forwards to the existing
    /// <see cref="IsoDropHitTestLogic.ComputeEffectiveBaseY"/> +
    /// <see cref="IsoDropHitTestLogic.IsBaseWithinSocketHeight"/> pair.
    /// The accept gate is unchanged -- only the routing changed.
    /// </summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (_socle is null || !IsInstanceValid(_socle)) return false;
        // Once confirmed, the socle is locked -- no further drops. Same
        // gate as the pre-fix IsoSocketPlaceholder._CanDropData.
        if (_socle.IsOccupancyConfirmed) return false;
        var payload = TryReadDragPayload(data);
        var decision = IsoSocketDropLogic.CanAccept(payload, _knownNpcIds);
        if (decision != IsoSocketDropLogic.DropDecision.Accept) return false;

        // Étape 7 D3+D5 -- per-slot gate (wrong-slot reject + occupied
        // reject). The owner-injected gate wraps CompanyPanelRosterLogic.
        // CanDropOnSocle so the test surface pins both reject paths.
        if (_slotGate is not null && !_slotGate(payload!.NpcId))
        {
            return false;
        }

        // Map drop-zone-local Y -> socket-local Y, then run the existing
        // ghost-base hit-test against the socle's vertical bounds. The
        // helper handles the tolerance + edge-inclusivity ; we just feed
        // it the translated coords.
        var socketLocalY = SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(
            atPosition.Y, _socleOffsetYInZone);
        var effectiveBaseY = IsoDropHitTestLogic.ComputeEffectiveBaseY(
            socketLocalY, payload!.PickupOffsetY);
        return IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY,
            _socle.Size.Y,
            IsoDropHitTestLogic.DefaultBaseToleranceYpx);
    }

    /// <summary>
    /// Godot drop-target hook -- on accept, forward to the socle's
    /// <see cref="IsoSocketPlaceholder.SetOccupiedNpc"/> so the socle
    /// keeps owning the visual occupied state (silhouette + name label)
    /// AND fires its OccupancyChanged signal (which the CompanyPanel
    /// re-emits to the E2AreaMap). The drop zone is a pure routing
    /// layer -- no state of its own.
    /// </summary>
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        if (_socle is null || !IsInstanceValid(_socle)) return;
        var payload = TryReadDragPayload(data);
        if (payload is null) return;
        _socle.SetOccupiedNpc(payload.NpcId);
    }

    public override void _Draw()
    {
        if (!DebugVisible) return;
        DrawRect(new Rect2(Vector2.Zero, Size), DebugTint, filled: true);
    }

    private static DragPayload? TryReadDragPayload(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return null;
        var dict = data.AsGodotDictionary();
        string? npcId = null, missionId = null, source = null;
        float pickupOffsetY = 0f;
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyNpcId, out var nv))
            npcId = nv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyMissionId, out var mv))
            missionId = mv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeySource, out var sv))
            source = sv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyPickupOffsetY, out var pv))
            pickupOffsetY = (float)pv.AsDouble();
        return new DragPayload(
            NpcId: npcId ?? string.Empty,
            MissionId: missionId ?? string.Empty,
            Source: source ?? string.Empty)
        {
            PickupOffsetY = pickupOffsetY,
        };
    }
}
