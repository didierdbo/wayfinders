using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the J3c-1ter desk <b>twin-corner</b> triangular
/// clip: turns the central maquette diamond — a centred iso rhombus whose
/// lower point sits on the screen's horizontal middle — into the five
/// uniforms the <c>desk_triangle_clip.gdshader</c> consumes in the desk
/// <c>TextureRect</c>'s <i>UV</i> space.
///
/// <para>
/// <b>The shape this solves.</b> Earlier J3c-1bis cut the desk as a single
/// bottom-left half-screen triangle (clip diagonal corner to corner). The
/// refined spec (Didier, 2026-05-21, from the in-game capture) is:
/// <list type="bullet">
///   <item>The <b>maquette</b> reads as a centred iso <b>diamond</b>: its
///     left/right side apexes touch the screen's left/right edges high up,
///     its <b>lower point falls on the screen's horizontal middle</b>
///     (x ≈ screenWidth/2), its top apex is cropped by the top HUD band.</item>
///   <item>The <b>desk</b> is <b>two mirrored brown triangles</b>, one in
///     the bottom-left screen corner and one in the bottom-right, in
///     vis-à-vis, separated by the diamond's lower point. Each desk triangle
///     is bounded by a screen edge (left or right), the screen bottom, and
///     a <b>hypotenuse</b> = one lower edge of the maquette diamond.</item>
/// </list>
/// The desk fills exactly the two lower corners the diamond does not cover.
/// </para>
///
/// <para>
/// <b>One SubViewport, two windows (architecture note).</b> The desk is
/// still ONE iso space rendered into ONE <c>DeskViewport</c> SubViewport —
/// the locked "two SubViewports" decision (maquette + desk) is intact. The
/// two visible desk corners are two windows onto that single world, carved
/// by the clip shader, not two viewports. The Company pawns all sit in the
/// bottom-LEFT corner (J3c-1 scope); the bottom-right corner is, for now,
/// brown floor + iso grid only.
/// </para>
///
/// <para>
/// <b>Why a dedicated helper.</b> The two hypotenuses and the split are
/// arithmetic — diamond apex points in screen pixels, a coordinate change
/// into UV, two normals that must each point toward their own corner.
/// Baked into <c>GameScreen._Ready</c> it would be invisible until an F6
/// smoke. Pinned here it is xUnit-checkable: a flipped normal, a bad UV
/// scale, or a degenerate frontier all surface red. Same logic-vs-node
/// split as <see cref="HudLayoutLogic"/>, <see cref="DeskFloorRectLogic"/>.
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
/// <c>[0,1]x[0,1]</c>, origin top-left, +Y pointing down. A screen point
/// inside the desk rect maps to
/// <c>uv = (screen - deskRectOrigin) / deskRectSize</c>.
/// </para>
/// </summary>
public static class DeskClipFrontierLogic
{
    /// <summary>
    /// The shader uniforms describing the two desk corner triangles in the
    /// desk <c>TextureRect</c>'s UV space.
    ///
    /// <para>
    /// The shader keeps a fragment at UV <c>p</c> when it is inside the
    /// bottom-LEFT corner triangle
    /// (<c>p.x &lt;= SplitU</c> AND
    /// <c>dot(p - LeftPointUv, LeftNormalUv) &gt;= 0</c>)
    /// OR inside the bottom-RIGHT corner triangle
    /// (<c>p.x &gt;= SplitU</c> AND
    /// <c>dot(p - RightPointUv, RightNormalUv) &gt;= 0</c>).
    /// </para>
    /// </summary>
    /// <param name="LeftPointUv">A point on the left triangle's hypotenuse, UV.</param>
    /// <param name="LeftNormalUv">
    /// The left hypotenuse normal, UV, pointing toward the bottom-left desk
    /// corner (the keep side). Not normalised — only the dot-product sign
    /// is used.
    /// </param>
    /// <param name="RightPointUv">A point on the right triangle's hypotenuse, UV.</param>
    /// <param name="RightNormalUv">
    /// The right hypotenuse normal, UV, pointing toward the bottom-right
    /// desk corner. Not normalised.
    /// </param>
    /// <param name="SplitU">
    /// UV x of the diamond's lower point — the vertical split that bounds
    /// each corner triangle to its own side of the screen.
    /// </param>
    public readonly record struct TwinClipFrontier(
        SysVec2 LeftPointUv,
        SysVec2 LeftNormalUv,
        SysVec2 RightPointUv,
        SysVec2 RightNormalUv,
        float SplitU);

    /// <summary>
    /// The four apexes of the centred maquette diamond, in screen pixels.
    /// Computed by <see cref="MaquetteDiamond"/> from the screen size and
    /// the HUD band; consumed by <see cref="Compute"/>. Exposed as a value
    /// so a test can pin the diamond placement independently of the clip
    /// maths.
    ///
    /// <para>
    /// +Y is down (screen convention): <see cref="TopApex"/> has the
    /// smallest Y, <see cref="LowerPoint"/> the largest.
    /// </para>
    /// </summary>
    /// <param name="TopApex">Top apex — cropped by the top HUD band.</param>
    /// <param name="LeftApex">Left side apex — touches the screen left edge.</param>
    /// <param name="RightApex">Right side apex — touches the screen right edge.</param>
    /// <param name="LowerPoint">
    /// Lower point — on the screen's horizontal middle, pointing down.
    /// </param>
    public readonly record struct DiamondApexes(
        SysVec2 TopApex,
        SysVec2 LeftApex,
        SysVec2 RightApex,
        SysVec2 LowerPoint);

    /// <summary>
    /// Build the centred maquette diamond from the screen size and the top
    /// HUD band height.
    ///
    /// <para>
    /// <b>Placement rules (from the in-game capture).</b>
    /// <list type="bullet">
    ///   <item>The left/right side apexes touch the screen's left/right
    ///     edges (x = 0 and x = screenWidth).</item>
    ///   <item>The lower point sits on the screen's horizontal middle
    ///     (x = screenWidth/2) and at <see cref="LowerPointScreenFracY"/>
    ///     of the screen height down — the diamond points down into the
    ///     centre of the screen.</item>
    ///   <item>The side apexes sit just under the top HUD band. Their Y is
    ///     derived from the iso 2:1 ratio: a diamond whose half-width is
    ///     screenWidth/2 has a half-height of screenWidth/4, so the side
    ///     apexes are screenWidth/4 above the lower point.</item>
    ///   <item>The top apex mirrors the lower point across the side-apex
    ///     line; it lands above the screen / behind the top HUD band, which
    ///     is correct — the spec says the diamond's top is cropped.</item>
    /// </list>
    /// The <paramref name="topHudBandHeight"/> is accepted so a future tweak
    /// can clamp the side apexes against the HUD; J3c-1ter keeps the pure
    /// iso-ratio placement and only documents the band.
    /// </para>
    /// </summary>
    /// <param name="screenSize">The screen size in pixels. Both positive.</param>
    /// <param name="topHudBandHeight">
    /// The top HUD band height in pixels. Non-negative.
    /// </param>
    /// <returns>The four diamond apexes in screen pixels.</returns>
    /// <exception cref="System.ArgumentException">
    /// If a screen dimension is non-positive or the band height is negative.
    /// </exception>
    public static DiamondApexes MaquetteDiamond(
        SysVec2 screenSize, float topHudBandHeight)
    {
        if (screenSize.X <= 0f || screenSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Screen size must be positive; got {screenSize}.",
                nameof(screenSize));
        }
        if (topHudBandHeight < 0f)
        {
            throw new System.ArgumentException(
                $"Top HUD band height must be non-negative; got " +
                $"{topHudBandHeight}.", nameof(topHudBandHeight));
        }

        float halfW = screenSize.X * 0.5f;
        // Iso 2:1: the diamond half-height is half the half-width.
        float halfH = halfW * 0.5f;

        float lowerY = screenSize.Y * LowerPointScreenFracY;
        float sideY = lowerY - halfH;   // side apexes, halfH above the lower point
        float topY = sideY - halfH;     // top apex, mirrors the lower point

        return new DiamondApexes(
            TopApex: new SysVec2(halfW, topY),
            LeftApex: new SysVec2(0f, sideY),
            RightApex: new SysVec2(screenSize.X, sideY),
            LowerPoint: new SysVec2(halfW, lowerY));
    }

    /// <summary>
    /// Where the maquette diamond's lower point sits vertically, as a
    /// fraction of the screen height. 0.5 = the screen's vertical middle —
    /// the spec's "the lower point arrives at the middle of the screen".
    /// </summary>
    public const float LowerPointScreenFracY = 0.5f;

    /// <summary>
    /// Build the twin-corner clip frontier from the maquette diamond and
    /// the desk's on-screen rectangle.
    ///
    /// <para>
    /// The desk's two hypotenuses are the diamond's two LOWER edges:
    /// <list type="bullet">
    ///   <item>left  hypotenuse = <c>LeftApex → LowerPoint</c>;</item>
    ///   <item>right hypotenuse = <c>LowerPoint → RightApex</c>.</item>
    /// </list>
    /// Each normal is oriented toward its own desk corner — the bottom-left
    /// corner UV (0,1) for the left triangle, the bottom-right corner
    /// UV (1,1) for the right triangle — so the shader's "≥ 0 means keep"
    /// reads correctly. The split is the lower point's UV x.
    /// </para>
    /// </summary>
    /// <param name="diamond">
    /// The maquette diamond apexes in screen pixels, from
    /// <see cref="MaquetteDiamond"/>.
    /// </param>
    /// <param name="deskRectOriginScreen">
    /// Top-left corner of the desk <c>TextureRect</c>, in screen pixels.
    /// </param>
    /// <param name="deskRectSize">
    /// Size of the desk <c>TextureRect</c>, in screen pixels. Both positive.
    /// </param>
    /// <returns>The <see cref="TwinClipFrontier"/> to push into the shader.</returns>
    /// <exception cref="System.ArgumentException">
    /// If the desk rect has a non-positive dimension, or either hypotenuse
    /// is degenerate (zero-length, or passing through its own desk corner).
    /// </exception>
    public static TwinClipFrontier Compute(
        DiamondApexes diamond,
        SysVec2 deskRectOriginScreen,
        SysVec2 deskRectSize)
    {
        if (deskRectSize.X <= 0f || deskRectSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Desk rect size must be positive; got {deskRectSize}.",
                nameof(deskRectSize));
        }

        // The left desk triangle's hypotenuse: left apex -> lower point.
        // Its keep side is the bottom-left desk corner, UV (0, 1).
        var (leftPoint, leftNormal) = BuildHalfPlane(
            diamond.LeftApex, diamond.LowerPoint,
            deskCornerUv: new SysVec2(0f, 1f),
            deskRectOriginScreen, deskRectSize,
            "left");

        // The right desk triangle's hypotenuse: lower point -> right apex.
        // Its keep side is the bottom-right desk corner, UV (1, 1).
        var (rightPoint, rightNormal) = BuildHalfPlane(
            diamond.LowerPoint, diamond.RightApex,
            deskCornerUv: new SysVec2(1f, 1f),
            deskRectOriginScreen, deskRectSize,
            "right");

        // The split is the diamond lower point's UV x — the vertical that
        // bounds each corner triangle to its own side of the screen.
        float splitU = ScreenToUv(
            diamond.LowerPoint, deskRectOriginScreen, deskRectSize).X;

        return new TwinClipFrontier(
            leftPoint, leftNormal, rightPoint, rightNormal, splitU);
    }

    /// <summary>
    /// Turn one diamond edge (two screen points) into a UV-space
    /// point/normal half-plane whose normal points toward
    /// <paramref name="deskCornerUv"/> — the keep side.
    /// </summary>
    private static (SysVec2 Point, SysVec2 Normal) BuildHalfPlane(
        SysVec2 edgeStartScreen,
        SysVec2 edgeEndScreen,
        SysVec2 deskCornerUv,
        SysVec2 deskRectOriginScreen,
        SysVec2 deskRectSize,
        string which)
    {
        SysVec2 edgeScreen = edgeEndScreen - edgeStartScreen;
        if (edgeScreen.LengthSquared() <= float.Epsilon)
        {
            throw new System.ArgumentException(
                $"Maquette diamond {which} edge is zero-length; the " +
                "frontier is undefined.", nameof(edgeStartScreen));
        }

        // Screen -> UV is a per-axis scale by 1/size. The point maps
        // directly; the direction maps by the same scale (a vector, no
        // origin term).
        SysVec2 pointUv = ScreenToUv(
            edgeStartScreen, deskRectOriginScreen, deskRectSize);
        SysVec2 edgeUv = new(
            edgeScreen.X / deskRectSize.X,
            edgeScreen.Y / deskRectSize.Y);

        // A line's normal is the edge direction rotated 90 degrees. In a
        // +Y-down UV space, rotating (dx, dy) by +90 deg gives (-dy, dx).
        SysVec2 normalUv = new(-edgeUv.Y, edgeUv.X);

        // Orient the normal so the desk corner is on the keep side, i.e.
        // dot(deskCornerUv - point, normal) > 0. The corner is the
        // unambiguous interior reference for its own triangle.
        float cornerSide = SysVec2.Dot(deskCornerUv - pointUv, normalUv);
        if (System.MathF.Abs(cornerSide) <= 1e-6f)
        {
            throw new System.ArgumentException(
                $"Maquette/desk {which} frontier passes through the desk " +
                "corner; that desk triangle is degenerate (zero area).",
                nameof(edgeStartScreen));
        }
        if (cornerSide < 0f)
        {
            normalUv = -normalUv;
        }

        return (pointUv, normalUv);
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
