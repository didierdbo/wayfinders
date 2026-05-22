using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the J3c-1bis desk <i>floor</i> fill rectangle —
/// the warm table surface the desk board paints below its iso grid.
///
/// <para>
/// <b>The bug this solves (F6, 2026-05-21).</b> The desk floor was drawn
/// as the iso diamond that bounds the 8x8 placeholder grid
/// (<c>IsoBoard.GridBoundingDiamond</c>). That diamond is a compact
/// rhombus: its left and top apexes sit well inside the screen, so it
/// left an unpainted sliver between the floor and the screen edge. The
/// floor must instead fill the <b>whole desk viewport</b> so the desk
/// zone is solidly covered out to the screen edges.
/// </para>
///
/// <para>
/// <b>Note (4-layer rework, 2026-05-22).</b> This helper predates the
/// four-layer model. It used to feed a triangular clip shader that
/// carved the desk floor into two corner triangles; that shader is
/// removed — the shipped Blender front decor (layer D) carries the
/// shape now. The helper still earns its keep: layer C's desk floor
/// is a live <c>IsoBoard</c> and still needs a viewport-covering fill
/// rect under the immobile desk camera. Only the downstream consumer
/// changed.
/// </para>
///
/// <para>
/// <b>What this helper returns.</b> The desk floor, in the desk board's
/// <i>local</i> pixel space, as an axis-aligned rectangle that covers the
/// entire desk viewport rect under the immobile <c>DeskCamera2D</c>. A
/// <see cref="Camera2D"/>'s <c>Position</c> is the world point shown at
/// the viewport centre; the visible world rect is therefore
/// <c>cameraCentre ± viewportSize/2</c>. The board fills exactly that,
/// so the desk zone is solidly covered with no unpainted sliver.
/// </para>
///
/// <para>
/// <b>Why a margin.</b> The rect is grown by a small slack on every side
/// so a sub-pixel rounding of the camera park, or a one-frame viewport
/// resize, can never expose an unpainted sliver between the floor and the
/// screen edge. Slack on a flat fill is free; an unpainted sliver is an
/// F6 bug.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>IsoBoard</c> converts at the engine seam. Same logic-vs-node split
/// as <see cref="DeskSlotLayoutLogic"/>.
/// </para>
/// </summary>
public static class DeskFloorRectLogic
{
    /// <summary>
    /// Safety slack added on every side of the floor rect, in pixels. Big
    /// enough to swallow a rounded camera park or a one-frame resize jitter;
    /// small relative to the viewport so it costs nothing on a flat fill.
    /// </summary>
    public const float EdgeSlackPx = 64f;

    /// <summary>
    /// An axis-aligned rectangle in the desk board's local pixel space —
    /// the corners the board's flat floor fill is drawn between. Returned
    /// as a value type so the helper stays a pure function.
    /// </summary>
    /// <param name="TopLeft">Top-left corner, local pixels.</param>
    /// <param name="Size">Width/height, local pixels. Both positive.</param>
    public readonly record struct FloorRect(SysVec2 TopLeft, SysVec2 Size);

    /// <summary>
    /// Compute the desk floor fill rectangle, in the desk board's local
    /// pixel space, covering the whole desk viewport under the given
    /// immobile camera.
    /// </summary>
    /// <param name="cameraCentreLocal">
    /// The desk camera's <c>Position</c> — the world point shown at the
    /// viewport centre — in the desk board's local pixel space.
    /// </param>
    /// <param name="viewportSize">
    /// The desk viewport size in pixels. Both components must be positive.
    /// </param>
    /// <returns>The floor rectangle to fill with the table-surface colour.</returns>
    /// <exception cref="System.ArgumentException">
    /// If either viewport dimension is non-positive — a caller bug, not a
    /// silent default.
    /// </exception>
    public static FloorRect Compute(SysVec2 cameraCentreLocal, SysVec2 viewportSize)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Desk viewport size must be positive; got {viewportSize}.",
                nameof(viewportSize));
        }

        SysVec2 half = viewportSize * 0.5f;
        SysVec2 slack = new(EdgeSlackPx, EdgeSlackPx);

        SysVec2 topLeft = cameraCentreLocal - half - slack;
        SysVec2 size = viewportSize + slack * 2f;
        return new FloorRect(topLeft, size);
    }

    /// <summary>
    /// The four corners of a <see cref="FloorRect"/>, clockwise from the
    /// top-left, ready for a polygon fill. Exposed so the engine seam
    /// converts one array of corners rather than re-deriving them.
    /// </summary>
    public static SysVec2[] Corners(FloorRect rect)
        => new[]
        {
            rect.TopLeft,
            rect.TopLeft + new SysVec2(rect.Size.X, 0f),
            rect.TopLeft + rect.Size,
            rect.TopLeft + new SysVec2(0f, rect.Size.Y),
        };
}
