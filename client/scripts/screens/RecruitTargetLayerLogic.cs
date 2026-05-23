using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# layout maths for the recruit-target perso on eT — where the
/// silhouette stands on the district mesh, and how the click hit-test
/// is gated (Rune, 2026-05-23, roadmap J8 Étape C). The Godot-bound
/// node <c>RecruitTargetLayer</c> reads these helpers.
///
/// <para>
/// <b>The placement decision (M1 MVP).</b> The world referential
/// (<see cref="Wayfinders.Client.Services.Dtos.WorldReferentialResponse"/>)
/// carries district anchors and footprints but does NOT carry NPC
/// positions ; M1 ships no "Kira is at (x,y) inside the gateway district"
/// payload. We do NOT invent one — that would be data fiction. Instead
/// the silhouette is parked at a fixed offset from the district mesh
/// centre, computed by <see cref="ComputePersoCentrePx"/> below. The
/// offset is calibrated to the eT 1600×800 common-frame render so the
/// perso lands clearly on the marchable zone (not on the water, not on
/// a wall). M2 will populate a real NPC-position field when NPCs gain
/// a place on the district grid ; this helper becomes a one-line
/// projector of that field on the same calage.
/// </para>
///
/// <para>
/// <b>The hit-test decision.</b> <see cref="IsClickOnTarget"/> answers
/// "did this board-local click land within the perso's pick radius ?".
/// Mirrors the discipline of
/// <see cref="MissionPoiLayerLogic.PickClosestPin"/> : a generous
/// constant pick radius keeps the click forgiving without inviting the
/// "I tried to pan and accidentally recruited" misfire (the pick radius
/// is bounded by the silhouette's visible footprint).
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> —
/// so cherry-picks into the xUnit host. Same seam as
/// <see cref="WorldMapCalage"/>, <see cref="MissionPoiLayerLogic"/>,
/// <see cref="MapViewportInputRoutingLogic"/>.
/// </para>
/// </summary>
public static class RecruitTargetLayerLogic
{
    /// <summary>
    /// Pick radius in board-local pixels — the per-perso hit-test window
    /// the <c>GameScreen</c> click router feeds through
    /// <see cref="IsClickOnTarget"/>. Generous on purpose : the
    /// silhouette body is ~160 px wide and ~280 px tall at default
    /// <c>IsoCharacterPlaceholder</c> geometry, so a 90 px radius covers
    /// the visible bounding box plus a comfort margin. Larger than the
    /// mission-POI radius (22 px) on purpose — the perso is the only
    /// clickable thing on eT in Étape C, no neighbouring pins compete.
    /// </summary>
    public const float DefaultPickRadiusPx = 90f;

    /// <summary>
    /// Y-offset from the district mesh geometric centre to the perso's
    /// foot anchor, in board-local pixels. Positive = down. The perso
    /// silhouette draws ABOVE its foot anchor (the <c>Node2D</c> origin
    /// is the foot point — trap #8), so parking the foot 90 px below the
    /// mesh centre puts the body straddling the centre line, clearly
    /// readable against the lower_quays + high_wall meshes' upper-bay
    /// composition. Calibrated for the 1600×800 eT common frame ;
    /// re-tune if Mira reshapes the district mesh framing.
    /// </summary>
    public const float CentreToFootOffsetYPx = 90f;

    /// <summary>
    /// Compute the perso's foot-anchor centre in board-local pixels for
    /// a district mesh of the given size. The board's
    /// <see cref="Wayfinders.Client.Scenes.Iso.IsoBoard"/> renders its
    /// <c>Sprite2D</c> with <c>Centered = false</c> at the board origin,
    /// so the mesh's top-left is at <c>(0,0)</c> board-local and its
    /// geometric centre is at <c>meshSize / 2</c>. The perso lands at
    /// the centre + <see cref="CentreToFootOffsetYPx"/> downward.
    ///
    /// <para>
    /// <b>Zero-size guard.</b> When the district mesh has not yet loaded
    /// (the bascule failed, or the texture swap is in flight), the
    /// caller observes <c>meshSize == Vector2.Zero</c> ; this function
    /// returns <c>Vector2.Zero</c> verbatim so the caller's "is the layer
    /// ready" guard sees a consistent sentinel and does not spawn the
    /// perso on a blank board.
    /// </para>
    /// </summary>
    /// <param name="meshSizePx">The district mesh size in board-local
    /// pixels — typically <c>IsoBoard.GetBackgroundTextureSizeOrZero()</c>.</param>
    /// <returns>The perso's foot-anchor centre in board-local pixels.</returns>
    public static SysVec2 ComputePersoCentrePx(SysVec2 meshSizePx)
    {
        if (meshSizePx.X <= 0f || meshSizePx.Y <= 0f)
        {
            return SysVec2.Zero;
        }
        return new SysVec2(
            meshSizePx.X * 0.5f,
            (meshSizePx.Y * 0.5f) + CentreToFootOffsetYPx);
    }

    /// <summary>
    /// Hit-test : true when <paramref name="clickBoardLocalPx"/> is within
    /// <paramref name="pickRadiusPx"/> of <paramref name="persoFootPx"/>.
    /// Uses squared distance to skip the sqrt — the radius is the
    /// authoritative number, not the distance itself.
    /// </summary>
    /// <param name="clickBoardLocalPx">Click position in the maquette
    /// board's local pixel space.</param>
    /// <param name="persoFootPx">The perso's foot anchor in board-local
    /// pixels (from <see cref="ComputePersoCentrePx"/>).</param>
    /// <param name="pickRadiusPx">Pick window radius. Defaults to
    /// <see cref="DefaultPickRadiusPx"/> at the call site.</param>
    /// <returns>True iff the click is inside the pick disc.</returns>
    public static bool IsClickOnTarget(
        SysVec2 clickBoardLocalPx, SysVec2 persoFootPx, float pickRadiusPx)
    {
        if (pickRadiusPx <= 0f) return false;
        float dx = clickBoardLocalPx.X - persoFootPx.X;
        float dy = clickBoardLocalPx.Y - persoFootPx.Y;
        return (dx * dx) + (dy * dy) <= (pickRadiusPx * pickRadiusPx);
    }
}
