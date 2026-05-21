using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the J3c-1bis desk triangular clip: turns the
/// maquette/desk frontier — a straight diagonal in <i>screen</i> pixels —
/// into the <c>clip_line_point</c> / <c>clip_line_normal</c> uniforms the
/// <c>desk_triangle_clip.gdshader</c> consumes in the desk
/// <c>TextureRect</c>'s <i>UV</i> space.
///
/// <para>
/// <b>The problem this solves.</b> The cartographer's-desk is the second
/// iso space of the Game Screen Shell, parked bottom-left. Its content is
/// rendered into its own <c>SubViewport</c> (the locked "two SubViewports,
/// two iso scales" decision is untouched). But a SubViewport texture is
/// rectangular, so drawn flat the desk reads as a brown rectangle. The
/// mockup wants a <b>triangle</b>: the desk fills the bottom-left wedge
/// the central maquette diamond does not cover, and the maquette/desk
/// frontier is the lower-left edge of that iso diamond. The clip shader
/// drops every fragment on the maquette side of that line; this helper
/// computes the line for it.
/// </para>
///
/// <para>
/// <b>Why a dedicated helper.</b> The frontier is arithmetic — two screen
/// points of the diamond edge, the desk's screen rect, a coordinate
/// change into UV, a normal that must point toward the keep side. Baked
/// into <c>GameScreen._Ready</c> it would be invisible until an F6 smoke
/// test (and an F6 smoke test is exactly what just caught the rectangle
/// bug). Pinned here it is xUnit-checkable: a flipped normal, a bad UV
/// scale, or a degenerate (zero-length) frontier all surface red. Same
/// logic-vs-node split as <see cref="HudLayoutLogic"/>,
/// <see cref="MapViewportPanLogic"/>, <see cref="DeskSlotLayoutLogic"/>.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>GameScreen</c> converts at the engine seam.
/// </para>
///
/// <para>
/// <b>UV convention.</b> The desk <c>TextureRect</c>'s UV space is
/// <c>[0,1]x[0,1]</c>, origin top-left, +Y pointing down — the standard
/// Godot canvas-item UV. A screen point inside the desk rect maps to
/// <c>uv = (screen - deskRectOrigin) / deskRectSize</c>.
/// </para>
/// </summary>
public static class DeskClipFrontierLogic
{
    /// <summary>
    /// The shader uniforms describing the maquette/desk frontier line in
    /// the desk <c>TextureRect</c>'s UV space. A fragment at UV <c>p</c> is
    /// <i>kept</i> (part of the desk triangle) when
    /// <c>dot(p - <see cref="PointUv"/>, <see cref="NormalUv"/>) &gt;= 0</c>.
    /// </summary>
    /// <param name="PointUv">A point on the frontier line, in UV space.</param>
    /// <param name="NormalUv">
    /// The frontier normal in UV space, pointing toward the desk (keep)
    /// side. Not normalised — only the sign of the dot product is used by
    /// the shader, so magnitude is irrelevant.
    /// </param>
    public readonly record struct ClipFrontier(SysVec2 PointUv, SysVec2 NormalUv);

    /// <summary>
    /// Build the clip frontier from the maquette diamond edge and the
    /// desk's on-screen rectangle.
    /// </summary>
    /// <param name="edgeStartScreen">
    /// One endpoint of the maquette diamond's lower-left edge, in screen
    /// pixels. Typically the diamond's left apex.
    /// </param>
    /// <param name="edgeEndScreen">
    /// The other endpoint of that same edge, in screen pixels. Typically
    /// the diamond's bottom apex. Must differ from
    /// <paramref name="edgeStartScreen"/> — a zero-length edge is a bug.
    /// </param>
    /// <param name="deskRectOriginScreen">
    /// Top-left corner of the desk <c>TextureRect</c>, in screen pixels.
    /// </param>
    /// <param name="deskRectSize">
    /// Size of the desk <c>TextureRect</c>, in screen pixels. Both
    /// components must be positive.
    /// </param>
    /// <returns>
    /// The <see cref="ClipFrontier"/> to push into the clip shader.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If the edge is zero-length, or the desk rect has a non-positive
    /// dimension — either is a caller bug, not a silent default.
    /// </exception>
    public static ClipFrontier Compute(
        SysVec2 edgeStartScreen,
        SysVec2 edgeEndScreen,
        SysVec2 deskRectOriginScreen,
        SysVec2 deskRectSize)
    {
        if (deskRectSize.X <= 0f || deskRectSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Desk rect size must be positive; got {deskRectSize}.",
                nameof(deskRectSize));
        }

        SysVec2 edgeScreen = edgeEndScreen - edgeStartScreen;
        if (edgeScreen.LengthSquared() <= float.Epsilon)
        {
            throw new System.ArgumentException(
                "Maquette diamond edge is zero-length; the frontier is " +
                "undefined.", nameof(edgeEndScreen));
        }

        // Screen -> UV is an axis-independent scale by 1/size. The frontier
        // POINT maps directly. The frontier DIRECTION maps by the same
        // per-axis scale (a vector, so no origin term).
        SysVec2 pointUv = ScreenToUv(
            edgeStartScreen, deskRectOriginScreen, deskRectSize);
        SysVec2 edgeUv = new(
            edgeScreen.X / deskRectSize.X,
            edgeScreen.Y / deskRectSize.Y);

        // A line's normal is the edge direction rotated 90 degrees. In a
        // +Y-down UV space, rotating (dx, dy) by +90 deg gives (-dy, dx).
        // That normal points to one side; we then orient it toward the
        // desk corner so the shader's ">= 0 means keep" reads correctly.
        SysVec2 normalUv = new(-edgeUv.Y, edgeUv.X);

        // The desk is the BOTTOM-LEFT triangle. Orient the normal so the
        // bottom-left corner of the desk rect — UV (0, 1) — is on the keep
        // side, i.e. dot((0,1) - point, normal) > 0. The corner is the
        // unambiguous interior reference: the frontier (the lower-left edge
        // of the maquette diamond) cuts the rect into a maquette wedge and
        // a desk wedge, and the bottom-left corner is always inside the
        // desk wedge for any non-degenerate frontier.
        SysVec2 deskCornerUv = new(0f, 1f);
        float cornerSide = SysVec2.Dot(deskCornerUv - pointUv, normalUv);
        if (System.MathF.Abs(cornerSide) <= 1e-6f)
        {
            // The frontier passes exactly through the desk's bottom-left
            // corner: the desk triangle would have zero area. A degenerate
            // scene setup — fail loud rather than ship an empty desk.
            throw new System.ArgumentException(
                "Maquette/desk frontier passes through the desk corner; " +
                "the desk triangle is degenerate (zero area).",
                nameof(edgeStartScreen));
        }
        if (cornerSide < 0f)
        {
            normalUv = -normalUv;
        }

        return new ClipFrontier(pointUv, normalUv);
    }

    /// <summary>
    /// Map a screen-pixel point into the desk <c>TextureRect</c>'s UV
    /// space (<c>[0,1]x[0,1]</c>, origin top-left). Exposed so a test can
    /// assert the UV mapping independently of the frontier maths.
    /// </summary>
    public static SysVec2 ScreenToUv(
        SysVec2 screenPoint, SysVec2 deskRectOriginScreen, SysVec2 deskRectSize)
        => new(
            (screenPoint.X - deskRectOriginScreen.X) / deskRectSize.X,
            (screenPoint.Y - deskRectOriginScreen.Y) / deskRectSize.Y);
}
