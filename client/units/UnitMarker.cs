using Godot;

namespace Wayfinders.Client.Units;

/// <summary>
/// Placeholder visual for a <see cref="PartyUnit"/> on the tactical scene
/// (Phase 5 L5). Draws a filled colored circle with a darker outline,
/// centered on the node's local origin.
///
/// <para>
/// <b>Why a custom <see cref="_Draw"/>, not a <see cref="Sprite2D"/> with a
/// generated <see cref="ImageTexture"/>.</b> The marker is a debug surface
/// that vanishes the moment Mira's portrait sprites land. A custom
/// <see cref="_Draw"/> override is the lowest-overhead, lowest-asset-pipeline
/// way to ship a visible "there is a unit here" indicator: no
/// <see cref="ImageTexture"/> allocation, no atlas plumbing, no
/// <c>.png</c>/<c>.tres</c> on disk. When Mira's sprites land, this whole
/// class disappears and the unit visual becomes a <see cref="Sprite2D"/>
/// on a <c>UnitVisual.tscn</c> — the call site in
/// <see cref="Wayfinders.Client.Scenes.TacticalScene"/> changes from
/// <c>new UnitMarker { ... }</c> to <c>visualScene.Instantiate&lt;Node2D&gt;()</c>,
/// nothing else moves.
/// </para>
///
/// <para>
/// <b>Y-sort behavior.</b> The parent <c>Units</c> <see cref="Node2D"/> in
/// the scene has <c>y_sort_enabled = true</c>. With this marker as a
/// direct child positioned at <see cref="TileMapLayer.MapToLocal"/> of its
/// cell, Y-sort renders the unit at row <c>y</c> behind any unit at row
/// <c>y + 1</c> on screen — the iso "south-is-front" convention. The
/// circle is drawn around <see cref="Vector2.Zero"/>, so the node's
/// position fully determines its sort key.
/// </para>
///
/// <para>
/// <b>Selection state (L5 optional).</b> <see cref="SetSelected"/> toggles
/// a thicker, brighter outline. The selection visual is intentionally
/// loud — at debug-grade, it should be impossible to miss which unit is
/// the path origin. Real art will land a softer marker.
/// </para>
///
/// <para>
/// <b>AOT-clean.</b> No reflection, no dynamic, no source-gen. The
/// <see cref="_Draw"/> override is plain virtual dispatch on a Godot type
/// the export pipeline already knows about.
/// </para>
/// </summary>
public partial class UnitMarker : Node2D
{
    /// <summary>Radius of the filled-circle body, in pixels.</summary>
    private const float BodyRadius = 11f;

    /// <summary>Width of the unselected outline, in pixels.</summary>
    private const float OutlineWidth = 2f;

    /// <summary>
    /// Width of the selection outline, in pixels. Thicker than
    /// <see cref="OutlineWidth"/> so the selected unit is unambiguous at a
    /// glance even when the camera is zoomed out.
    /// </summary>
    private const float SelectedOutlineWidth = 3.5f;

    /// <summary>
    /// Outset of the selection ring beyond the body. Drawn as a separate
    /// stroke so the body color is preserved underneath.
    /// </summary>
    private const float SelectedRingOutset = 4.5f;

    private Color _bodyColor = new(0.85f, 0.85f, 0.85f);
    private Color _outlineColor = new(0.10f, 0.10f, 0.10f);
    private bool _selected;

    /// <summary>
    /// Set the body fill color. Outline is auto-derived as a darker shade.
    /// Triggers a redraw via <see cref="CanvasItem.QueueRedraw"/>; safe to
    /// call before or after <c>_Ready</c>.
    /// </summary>
    public void SetBodyColor(Color body)
    {
        _bodyColor = body;
        _outlineColor = new Color(body.R * 0.30f, body.G * 0.30f, body.B * 0.30f);
        QueueRedraw();
    }

    /// <summary>
    /// Toggle the selection outline. Cyan ring around the body when
    /// selected, no extra ring when not. Idempotent — calling
    /// <c>SetSelected(true)</c> twice is fine.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_selected == selected)
        {
            return;
        }
        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Filled body, then outline. Drawing in this order keeps the
        // outline crisp on top of the fill.
        DrawCircle(Vector2.Zero, BodyRadius, _bodyColor);
        DrawArc(
            center: Vector2.Zero,
            radius: BodyRadius,
            startAngle: 0f,
            endAngle: Mathf.Tau,
            pointCount: 32,
            color: _outlineColor,
            width: OutlineWidth,
            antialiased: true);

        if (_selected)
        {
            // Cyan selection ring, drawn outside the body so the body
            // color stays readable underneath.
            DrawArc(
                center: Vector2.Zero,
                radius: BodyRadius + SelectedRingOutset,
                startAngle: 0f,
                endAngle: Mathf.Tau,
                pointCount: 40,
                color: new Color(0.30f, 0.85f, 0.95f, 0.95f),
                width: SelectedOutlineWidth,
                antialiased: true);
        }
    }
}
