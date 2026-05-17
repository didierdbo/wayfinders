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
/// from <see cref="IsoSocketGeometryLogic"/>. No 3D faces, no height --
/// this is the ground plane, the drop surface a character will land on
/// at étape 6.
/// </para>
///
/// <para>
/// <b>What this is NOT.</b> NOT the same component as
/// <see cref="IsoCharacterPlaceholder"/> -- that one renders a 3-face
/// 2.5D parallelepiped (a character SILHOUETTE). This one is the GROUND
/// the character stands on, a flat rhombus, no top/front/side faces.
/// The two will compose at étape 6 (character on top of socket).
/// </para>
///
/// <para>
/// <b>Sizing.</b> Default CustomMinimumSize = 160 x 80 (the 2:1 iso
/// ratio is locked in <see cref="IsoSocketGeometryLogic.WidthOverHeight"/>).
/// Caller may override the CustomMinimumSize ; the draw scales against
/// <see cref="Control.Size"/> at every paint so the rhombus fills
/// whatever room it gets while preserving the 2:1 ratio (the limiting
/// dimension wins). Padding of 4 px keeps the outline from clipping
/// the Control edges.
/// </para>
///
/// <para>
/// <b>Colours.</b> Parchment cream fill (matches the Compagnie panel
/// background -- the socle reads as "a tile of the same parchment, one
/// step warmer"), umber outline 2 px (matches the panel border, matches
/// the IsoCharacterPlaceholder face outlines). Rim glow / drop-target
/// affordance intentionally deferred to étape 6 -- v1 ships the
/// baseline silhouette without visual flourish until the drag&amp;drop
/// surface exists to motivate it.
/// </para>
///
/// <para>
/// <b>API surface.</b> One optional setter --
/// <see cref="SetOccupiedNpc"/>. v1 stores the value without rendering
/// anything ; étape 6 will wire the visual response (e.g. swap the rim
/// glow on, host a child IsoCharacterPlaceholder). Keeping the setter
/// declared now means the wiring at étape 6 is a child render swap, not
/// a public API addition.
/// </para>
///
/// <para>
/// <b>Trap discipline (memo feedback_godot_rendering_input_traps).</b>
/// MouseFilter = Ignore -- decorative element ; the parent Compagnie
/// panel owns the hit-testing surface. Leaving it as Stop would steal
/// mouse events from the panel's eventual drop-target logic at étape 6,
/// which is the wrong layer to host that responsibility.
/// </para>
/// </summary>
public partial class IsoSocketPlaceholder : Control
{
    /// <summary>Default cmin size -- a 2:1 iso rhombus 160 x 80, plus
    /// the side padding band reserved for the outline.</summary>
    private static readonly Vector2 DefaultMinSize = new(168, 88);

    /// <summary>Padding inside the Control's rect so the outline stroke
    /// is not clipped at the rect edges.</summary>
    private const float PaddingPx = 4f;

    /// <summary>Outline stroke width, px.</summary>
    private const float OutlineWidth = 2f;

    /// <summary>Parchment cream fill -- one step warmer than the panel
    /// background so the socle reads as a distinct surface without
    /// fighting the panel chrome (locked 2026-05-17 visual DNA :
    /// parchment cream + umber).</summary>
    private static readonly Color FillColor = new(0.91f, 0.86f, 0.76f, 1f);

    /// <summary>Umber outline -- matches the IsoCharacterPlaceholder
    /// face outlines and the parchment tooltip / panel borders.</summary>
    private static readonly Color OutlineColor = new(0.18f, 0.14f, 0.11f, 1f);

    private string? _occupiedNpcId;

    public override void _Ready()
    {
        CustomMinimumSize = DefaultMinSize;
        // Decorative -- parent panel owns hit-testing (and at étape 6
        // owns the drop-target hit-test as well). Stop would steal the
        // event from the wrong layer.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Mark the socket as occupied by the given NPC id (or empty when
    /// <c>null</c>). v1 stores the value as a no-op visually -- étape 6
    /// will wire the response (host a child placeholder + swap the rim
    /// glow on). The setter exists now so the étape 6 wiring is a
    /// child-add + redraw, not a public API addition.
    /// </summary>
    public void SetOccupiedNpc(string? npcId)
    {
        if (_occupiedNpcId == npcId) return;
        _occupiedNpcId = npcId;
        QueueRedraw();
    }

    /// <summary>Currently-occupying NPC id, or <c>null</c> if empty.
    /// Exposed for the étape 6 drop-target handler to query before
    /// accepting a new character.</summary>
    public string? OccupiedNpcId => _occupiedNpcId;

    public override void _Draw()
    {
        // Available rect for the rhombus = Control's rect minus
        // PaddingPx on each side.
        var availW = Mathf.Max(0f, Size.X - 2f * PaddingPx);
        var availH = Mathf.Max(0f, Size.Y - 2f * PaddingPx);
        if (availW <= 4f || availH <= 4f) return;

        // Compute the largest 2:1 rhombus that fits the available rect.
        // Width-limited path : w = availW, h = w/2. If that height
        // overflows availH, fall back to height-limited : h = availH,
        // w = h*2.
        var w = availW;
        var h = IsoSocketGeometryLogic.HeightFor(w);
        if (h > availH)
        {
            h = availH;
            w = h * IsoSocketGeometryLogic.WidthOverHeight;
        }

        // Centre the rhombus inside the padded rect.
        var offX = PaddingPx + (availW - w) * 0.5f;
        var offY = PaddingPx + (availH - h) * 0.5f;

        // Pull the rhombus polygon from the pure-C# helper, translate
        // into the padded draw area, and convert to Godot Vector2[].
        var pts = IsoSocketGeometryLogic.Rhombus(w);
        var polygon = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            polygon[i] = new Vector2(pts[i].X + offX, pts[i].Y + offY);
        }

        DrawColoredPolygon(polygon, FillColor);

        // Outline -- walk the 4 vertices closed.
        var closed = new Vector2[polygon.Length + 1];
        polygon.CopyTo(closed, 0);
        closed[polygon.Length] = polygon[0];
        DrawPolyline(closed, OutlineColor, OutlineWidth);
    }
}
