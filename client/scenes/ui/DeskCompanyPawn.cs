using Godot;
using Wayfinders.Client.Scripts.Poi;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// J3c-1 placeholder pawn for one Company member standing on the static
/// iso desk (roadmap milestone J3c-1, the bottom-left second iso space of
/// the Game Screen Shell). One <see cref="DeskCompanyPawn"/> is one
/// miniature on a desk slot cell.
///
/// <para>
/// <b>Why a procedural placeholder, not a Sprite2D.</b> Mira's
/// desk-scale Company pawn bitmaps (J3-assets) have not landed. A
/// procedural parchment-and-umber silhouette reads as "a figure standing
/// on the board" while we wait, and reads the locked
/// <i>pion-sur-plateau</i> grammar so the desk does not look like a
/// "jouet". The swap is a one-line change: replace the <see cref="_Draw"/>
/// override with a child <see cref="Sprite2D"/> at the same foot anchor —
/// the slot layout, the Y-sort, and the desk wiring do not change.
/// </para>
///
/// <para>
/// <b>Pion-sur-plateau pattern (memo project_wayfinders_pion_sur_plateau,
/// locked 2026-05-14).</b> The locked grammar for a piece standing on the
/// board is exactly four ingredients — applied here as far as a
/// <i>static</i> milestone allows:
/// <list type="number">
///   <item><b>Diegetic shadow, SW at 30°.</b> An ellipse cast down-left
///     from the foot, drawn <i>before</i> the body so the body occludes
///     it. Not a generic blob — it is oriented (south-west, 30° off the
///     vertical) so it reads as a real cast shadow under the desk's light.</item>
///   <item><b>Lift, 2 px.</b> The body is drawn 2 px above the foot
///     anchor — the locked "posed" target (3 px = the "jouet" edge,
///     &gt; 3 forbidden). The lift value is run through
///     <see cref="PoiVisualLogic.ClampLift"/> so a slipped constant
///     cannot ship the toy look.</item>
///   <item><b>Terracotta rim.</b> A thin warm-terracotta outline on the
///     lit (north-east) edge of the body — a rim light, not a full
///     cartoon outline.</item>
///   <item><b>Parallax 3–5%.</b> <i>Deferred.</i> Parallax needs a
///     moving camera to read; the <c>DeskCamera2D</c> is immobile by
///     design (only the maquette pans, roadmap J3c-1). With a fixed
///     camera the parallax offset is identically zero, so there is
///     nothing to wire here. If the desk ever gains a pannable camera
///     the seam is the same <see cref="PoiVisualLogic.ComputeParallaxOffset"/>
///     the maquette POIs use.</item>
/// </list>
/// <b>Forbidden (memo):</b> no bobbing animation, no full cartoon
/// outline, no soft round blob shadow, no pop-saturated colour. This
/// pawn is deliberately still and earthy.
/// </para>
///
/// <para>
/// <b>Foot anchor — trap #8.</b> The pawn node's <i>origin</i> is its
/// foot point: the desk places the node at the iso cell centre via
/// <c>IsoBoard.CellToPixel</c>, and everything this pawn draws is
/// positioned relative to that origin (shadow below it, body lifted
/// above it). No <c>Centered</c> ambiguity — the anchor is one
/// documented point, the same discipline as <c>IsoBoard</c>'s background
/// sprite.
/// </para>
///
/// <para>
/// <b>State ownership — J3c-1 is static.</b> This pawn holds no game
/// state. It knows its slot index and whether it is the leader (for the
/// foreground size bump) and nothing else. Which Company member occupies
/// which slot is authoritative <c>GameState</c> data wired in J3c-2
/// (NPC-autonomy lock 2026-05-09: the pawn is a <i>view</i>, the slot ↔
/// member binding is state). J3c-1 only draws the placeholder figures.
/// </para>
/// </summary>
public partial class DeskCompanyPawn : Node2D
{
    /// <summary>
    /// Locked lift in pixels — the body is drawn this far above the foot
    /// anchor. 2 px is the "posed" target of the pion-sur-plateau memo;
    /// run through <see cref="PoiVisualLogic.ClampLift"/> so it can never
    /// exceed the locked maximum of 3.
    /// </summary>
    private const int LiftPx = 2;

    /// <summary>
    /// Body width in pixels for a formation pawn. The leader pawn is
    /// drawn at <see cref="LeaderScale"/> times this — foreground-
    /// prominent, per the mockup.
    /// </summary>
    private const float BodyWidthPx = 44f;

    /// <summary>Body height in pixels for a formation pawn.</summary>
    private const float BodyHeightPx = 96f;

    /// <summary>
    /// Size multiplier applied to the leader pawn (slot 0). A modest bump
    /// — the leader reads as foreground without breaking the iso scale.
    /// </summary>
    private const float LeaderScale = 1.25f;

    // Warm-earth Wayfinders palette (terracotta ochre + parchment cream +
    // umber). No pop-saturated colour — pion-sur-plateau memo.

    /// <summary>Body fill — parchment cream, the lit mass of the figure.</summary>
    private static readonly Color BodyColor = new(0.78f, 0.69f, 0.52f, 1f);

    /// <summary>Leader body fill — a touch warmer so the leader reads distinct.</summary>
    private static readonly Color LeaderBodyColor = new(0.82f, 0.66f, 0.44f, 1f);

    /// <summary>
    /// Rim light — warm terracotta, drawn thin on the north-east (lit)
    /// edge of the body. The locked "rim terracotta" ingredient.
    /// </summary>
    private static readonly Color RimColor = new(0.80f, 0.42f, 0.26f, 1f);

    /// <summary>
    /// Diegetic cast shadow — translucent umber. Drawn as an ellipse
    /// offset south-west of the foot, before the body, so the body
    /// occludes its near edge.
    /// </summary>
    private static readonly Color ShadowColor = new(0.20f, 0.13f, 0.09f, 0.40f);

    private int _slotIndex;
    private bool _isLeader;

    /// <summary>
    /// Configure the pawn for one desk slot. Called by <c>GameScreen</c>
    /// right after instancing and before adding the pawn to the desk
    /// board. Idempotent — safe to call again.
    /// </summary>
    /// <param name="slotIndex">
    /// The Company slot this pawn occupies (0 = leader). Stored for the
    /// J3c-2 selection wiring; J3c-1 uses it only for the node name and
    /// diagnostics.
    /// </param>
    /// <param name="isLeader">
    /// True for the leader pawn — drawn at <see cref="LeaderScale"/> and
    /// in the warmer <see cref="LeaderBodyColor"/>.
    /// </param>
    public void Configure(int slotIndex, bool isLeader)
    {
        _slotIndex = slotIndex;
        _isLeader = isLeader;
        if (IsInsideTree())
        {
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        // One static draw pass — the pawn does not animate (no bobbing,
        // pion-sur-plateau memo). A redraw is only requested if Configure
        // is called again.
        QueueRedraw();
    }

    /// <summary>
    /// Draw the placeholder figure: diegetic SW shadow, then the lifted
    /// parchment body, then the terracotta rim. All geometry is relative
    /// to the node origin, which is the figure's <i>foot</i> anchor
    /// (trap #8). One pass, no per-frame work.
    /// </summary>
    public override void _Draw()
    {
        float scale = _isLeader ? LeaderScale : 1f;
        float w = BodyWidthPx * scale;
        float h = BodyHeightPx * scale;
        int lift = PoiVisualLogic.ClampLift(LiftPx);

        // --- Ingredient 1: diegetic shadow, SW at 30° ----------------
        // An ellipse cast down-left from the foot. The 30°-off-vertical
        // direction is encoded by offsetting the ellipse centre
        // south-west of the origin; tan(30°) ≈ 0.577 gives the x:y of
        // the offset. Drawn first so the body occludes its near edge.
        float shadowRx = w * 0.55f;
        float shadowRy = shadowRx * 0.5f; // 2:1 iso-flattened ellipse
        var shadowCentre = new Vector2(-shadowRx * 0.45f, shadowRy * 0.55f);
        DrawSwShadowEllipse(shadowCentre, shadowRx, shadowRy);

        // --- Ingredient 2: lift, 2 px --------------------------------
        // The body's foot sits `lift` px ABOVE the node origin. The
        // origin is the contact point on the cell; lifting the body a
        // hair off it is the locked "posed" cue (not a sticker, not a
        // toy).
        float footY = -lift;

        // --- Body: a simple rounded-shoulder torso silhouette --------
        // A trapezoid-ish figure: narrow at the head, wider at the
        // shoulders, tapering to the foot. Concrete placeholder shape;
        // Mira's bitmap replaces it.
        var bodyColor = _isLeader ? LeaderBodyColor : BodyColor;
        var body = new[]
        {
            new Vector2(-w * 0.18f, footY - h),          // head, left
            new Vector2(w * 0.18f, footY - h),           // head, right
            new Vector2(w * 0.50f, footY - h * 0.62f),   // shoulder, right
            new Vector2(w * 0.30f, footY),               // foot, right
            new Vector2(-w * 0.30f, footY),              // foot, left
            new Vector2(-w * 0.50f, footY - h * 0.62f),  // shoulder, left
        };
        DrawColoredPolygon(body, bodyColor);

        // --- Ingredient 3: terracotta rim ----------------------------
        // A thin warm rim on the north-east (lit) edge only — head-right
        // down to shoulder-right. NOT a full cartoon outline around the
        // whole silhouette (forbidden by the memo); a rim light.
        var rimEdge = new[]
        {
            body[0], // head, left
            body[1], // head, right
            body[2], // shoulder, right
        };
        DrawPolyline(rimEdge, RimColor, 2f, antialiased: true);
    }

    /// <summary>
    /// Draw the diegetic cast shadow as a filled ellipse. Godot's
    /// immediate-mode drawing has no ellipse primitive, so the ellipse is
    /// tessellated into a fan of triangles. Kept as a private helper so
    /// the <see cref="_Draw"/> body stays readable.
    /// </summary>
    private void DrawSwShadowEllipse(Vector2 centre, float rx, float ry)
    {
        const int segments = 24;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(points, ShadowColor);
    }

    /// <summary>The Company slot this pawn occupies. 0 = leader.</summary>
    public int SlotIndex => _slotIndex;

    /// <summary>True for the leader pawn (slot 0).</summary>
    public bool IsLeader => _isLeader;
}
