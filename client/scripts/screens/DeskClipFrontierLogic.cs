using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the J3c-1 desk <b>twin-corner</b> triangular clip:
/// turns the central maquette diamond — a centred <b>strict iso 2:1</b>
/// rhombus that owns the bulk of the screen — into the five uniforms the
/// <c>desk_triangle_clip.gdshader</c> consumes in the desk
/// <c>TextureRect</c>'s <i>UV</i> space.
///
/// <para>
/// <b>The shape this solves — J3c-1quinquies (Didier, 2026-05-21,
/// regression fix on commit 5f9aeb5).</b> J3c-1bis cut the desk as one
/// bottom-left half-screen triangle. J3c-1ter cut two mirrored corner
/// triangles. J3c-1quater grew the maquette by driving the diamond's
/// vertical extent from two free screen-height fractions — and that
/// <b>broke the iso angle</b>: with the side apexes at 0.30 and the lower
/// point at 0.94 of a 1080-tall screen, a lower edge ran
/// <c>(0,324) -&gt; (960,1015.2)</c>, slope <c>691.2/960 ≈ 0.72</c>. A real
/// iso 2:1 diamond edge has slope <b>exactly 0.5</b> (one vertical for two
/// horizontal). The diamond outline was therefore no longer parallel to the
/// iso grid drawn inside it — Didier: <i>"les diagonales ne sont pas
/// bonnes"</i>.
/// </para>
///
/// <para>
/// <b>The fix: a real iso diamond that overflows sideways.</b> The diamond
/// is once again a strict iso 2:1 rhombus — its four edges have slope
/// exactly <see cref="IsoEdgeSlope"/> (0.5). To keep the maquette large
/// <b>without stretching it vertically</b> (which is what broke the angle),
/// the diamond is allowed to <b>overflow the screen horizontally</b>: its
/// half-width can exceed half the screen width, so the left/right side
/// apexes fall <i>off-screen</i> (x &lt; 0 and x &gt; screenWidth). The
/// visible portion of the diamond is then large, the edge slope stays a
/// true iso 0.5, and the desk is still just two thin triangles in the
/// bottom-left and bottom-right screen corners.
/// </para>
///
/// <para>
/// <b>The lower point is derived from the bottom HUD band (J3c-1octies,
/// Didier, 2026-05-21).</b> The diamond is sized by one knob
/// (<see cref="MaquetteHalfWidthScreenFracX"/>) and its lower point is now
/// <b>placed</b> flush against the bottom HUD band's top edge —
/// <c>screenHeight - bottomHudBandHeight</c> — not at a magic screen-height
/// fraction. Before this fix the lower point sat ~11 px above the HUD band,
/// leaving a thin strip of desk floor visible between the diamond and the
/// HUD; deriving the placement from the HUD band closes that strip and the
/// lower point follows automatically if the band height changes. Everything
/// else is still derived: the half-height is <c>halfWidth * IsoEdgeSlope</c>
/// (locking the slope), the side apexes and top apex follow. Didier
/// re-tunes the maquette-vs-desk balance with the half-width knob alone.
/// </para>
///
/// <para>
/// <b>The twin clip is mirror-symmetric BY CONSTRUCTION (J3c-1nonies,
/// Didier, 2026-05-21 — "il faut que ce soit exact").</b> Didier saw the
/// bottom of the screen read as <i>not</i> a perfect left/right mirror:
/// <i>"on perd à gauche, on gagne plus à droite"</i>. The diamond apexes
/// were already exact mirrors — <see cref="MaquetteDiamond"/> builds them
/// as <c>centreX ∓ halfW</c> at a shared <c>sideY</c>, equidistant from the
/// centre for ANY <c>halfW</c>. The twin-clip frontier, however, used to be
/// computed by running the half-plane builder twice, once per side, and the
/// two sides came out symmetric only by an arithmetic coincidence. This
/// round promotes that coincidence to a guarantee: <see cref="Compute"/>
/// builds the LEFT half-plane, then derives the RIGHT one as the EXACT
/// MIRROR of the left about the vertical split column. See
/// <see cref="Compute"/> for the reflection maths and the defensive check
/// that the diamond inputs really are mirror inputs.
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
/// scale, a degenerate frontier — or a broken iso slope, or a broken
/// mirror symmetry — all surface red.
/// Same logic-vs-node split as <see cref="HudLayoutLogic"/>,
/// <see cref="DeskFloorRectLogic"/>.
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
    ///
    /// <para>
    /// <b>Mirror invariant.</b> Since J3c-1nonies the right side is the
    /// exact reflection of the left about <see cref="SplitU"/>:
    /// <c>RightPointUv == (2·SplitU − LeftPointUv.X, LeftPointUv.Y)</c> and
    /// <c>RightNormalUv == (−LeftNormalUv.X, LeftNormalUv.Y)</c>.
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
    /// the HUD bands; consumed by <see cref="Compute"/>. Exposed as a value
    /// so a test can pin the diamond placement independently of the clip
    /// maths.
    ///
    /// <para>
    /// +Y is down (screen convention): <see cref="TopApex"/> has the
    /// smallest Y, <see cref="LowerPoint"/> the largest.
    /// </para>
    ///
    /// <para>
    /// The diamond is a strict iso 2:1 rhombus that overflows the screen
    /// horizontally: <see cref="LeftApex"/> has a NEGATIVE X and
    /// <see cref="RightApex"/> an X past the screen width whenever
    /// <see cref="MaquetteHalfWidthScreenFracX"/> exceeds 0.5. Only the
    /// portion of the diamond inside the screen is visible.
    /// </para>
    /// </summary>
    /// <param name="TopApex">Top apex — cropped above the screen / behind the HUD.</param>
    /// <param name="LeftApex">Left side apex — off-screen left (x &lt; 0) when the diamond overflows.</param>
    /// <param name="RightApex">Right side apex — off-screen right (x &gt; screenWidth) when the diamond overflows.</param>
    /// <param name="LowerPoint">
    /// Lower point — on the screen's horizontal middle, sitting flush on the
    /// bottom HUD band's top edge so no desk strip shows below the diamond.
    /// </param>
    public readonly record struct DiamondApexes(
        SysVec2 TopApex,
        SysVec2 LeftApex,
        SysVec2 RightApex,
        SysVec2 LowerPoint);

    // ----------------------------------------------------------------------
    //  THE LOCKED ISO INVARIANT — do not turn this into a free parameter.
    // ----------------------------------------------------------------------

    /// <summary>
    /// <b>The iso 2:1 edge slope — LOCKED at 0.5.</b>
    ///
    /// <para>
    /// Every edge of the maquette diamond rises one pixel vertically for
    /// every two pixels horizontally: <c>|Δy / Δx| = 0.5</c>. This is the
    /// defining property of an isometric 2:1 projection and it MUST match
    /// the slope of the diagonals of the iso grid drawn inside the diamond.
    /// </para>
    ///
    /// <para>
    /// This is the invariant commit 5f9aeb5 broke by deriving the diamond
    /// height from a free screen-height fraction. It is <b>not</b> a tuning
    /// knob — the maquette is re-sized through
    /// <see cref="MaquetteHalfWidthScreenFracX"/> (and the diamond is then
    /// allowed to overflow the screen sideways), never by changing this
    /// slope. <c>DeskClipFrontierLogicTests</c> pins it on all four edges.
    /// </para>
    /// </summary>
    public const float IsoEdgeSlope = 0.5f;

    // ----------------------------------------------------------------------
    //  THE PROPORTION KNOB — tune this to re-balance maquette vs desk.
    // ----------------------------------------------------------------------

    /// <summary>
    /// <b>Proportion knob — the maquette diamond's half-width, as a
    /// fraction of the screen WIDTH.</b>
    ///
    /// <para>
    /// This is the single knob that sizes the diamond. A value of 0.5 makes
    /// the side apexes land exactly on the screen's left/right edges; any
    /// value <b>above 0.5 makes the diamond overflow the screen
    /// horizontally</b> — the side apexes fall off-screen (left apex x &lt; 0,
    /// right apex x &gt; screenWidth). The half-height is then DERIVED as
    /// <c>halfWidth * IsoEdgeSlope</c>, so the diamond stays a strict iso
    /// 2:1 rhombus no matter how wide it gets. The bigger this value, the
    /// larger the visible diamond and the thinner the two desk corners.
    /// </para>
    ///
    /// <para>
    /// <b>Default 0.95 (J3c-1quinquies).</b> The diamond's half-width is
    /// 95% of the screen width — i.e. the full diamond is 1.9x the screen
    /// wide, so each side apex sits ~864 px (45% of 1920) OFF-screen. On a
    /// 1920x1080 screen the half-height is then
    /// <c>0.95 * 1920 * 0.5 = 912 px</c>, so the visible diamond is tall
    /// enough to own the bulk of the screen while every edge keeps the true
    /// iso 0.5 slope.
    /// </para>
    ///
    /// <para>
    /// <b>To re-tune:</b> raise toward (and past) 1.0 to grow the maquette
    /// and shrink the desk corners; lower toward 0.5 to bring the side
    /// apexes back on-screen and give the desk corners more room. Keep it
    /// strictly positive. Values at or below 0.5 stop the diamond from
    /// overflowing — still valid, just a smaller maquette.
    /// </para>
    /// </summary>
    public const float MaquetteHalfWidthScreenFracX = 0.95f;

    /// <summary>
    /// <b>The maquette diamond's lower point is DERIVED from the bottom HUD
    /// band — it is no longer a free fraction (J3c-1octies, Didier,
    /// 2026-05-21, "no brown desk band must show below the diamond").</b>
    ///
    /// <para>
    /// <b>What changed and why.</b> Earlier rounds placed the lower point at
    /// a magic screen-height fraction (0.94 → 0.96). At 0.96 of a 1080-tall
    /// screen the lower point sat at y ≈ 1037 while the bottom HUD band
    /// covers <c>[1048, 1080]</c> — so a ~11 px strip of desk floor stayed
    /// visible <i>between</i> the diamond's lower point and the HUD. That
    /// strip is the top of the previous row's desk wedge and must not show.
    /// </para>
    ///
    /// <para>
    /// <b>The fix: derive, do not tune.</b> <see cref="MaquetteDiamond"/>
    /// now places the lower point at <c>screenHeight - bottomHudBandHeight</c>
    /// — exactly on the bottom HUD band's TOP edge. The diamond's lower point
    /// and the HUD band then meet with no gap, so no desk strip can show
    /// below the diamond, and the lower point follows automatically if the
    /// HUD band height ever changes — no magic FracY to keep re-tuning.
    /// </para>
    ///
    /// <para>
    /// This constant is kept only as a documented <b>fallback default</b>,
    /// used when a caller passes a zero bottom band. It is the fraction
    /// equivalent of "flush with a 32 px band on a 1080 screen"
    /// (<c>(1080 - 32) / 1080 ≈ 0.9704</c>) so the historical 1920x1080
    /// behaviour is reproduced when no band is supplied. The live desk never
    /// relies on it — <c>GameScreen</c> always passes the real
    /// <see cref="HudBandHeight"/>.
    /// </para>
    /// </summary>
    public const float MaquetteLowerPointScreenFracY = (1080f - 32f) / 1080f;

    /// <summary>
    /// Build the centred maquette diamond from the screen size and the two
    /// HUD band heights.
    ///
    /// <para>
    /// <b>Placement rules (J3c-1octies, 2026-05-21 — "no brown desk band
    /// below the diamond"; J3c-1quinquies — "the diamond outline must be a
    /// real iso 2:1, parallel to the grid diagonals").</b>
    /// <list type="bullet">
    ///   <item>The half-width is <see cref="MaquetteHalfWidthScreenFracX"/>
    ///     of the screen width. The diamond is centred on the screen's
    ///     horizontal middle, so the side apexes land at
    ///     <c>screenWidth/2 ± halfWidth</c> — OFF-screen when the half-width
    ///     fraction exceeds 0.5.</item>
    ///   <item>The half-height is DERIVED: <c>halfWidth * IsoEdgeSlope</c>.
    ///     This locks every edge to the iso 2:1 slope of 0.5 — the diamond
    ///     is never stretched vertically.</item>
    ///   <item>The lower point sits on the horizontal middle, at
    ///     <c>screenHeight - bottomHudBandHeight</c> — flush on the bottom
    ///     HUD band's top edge, so no strip of desk floor shows between the
    ///     diamond's lower point and the HUD. If
    ///     <paramref name="bottomHudBandHeight"/> is zero the lower point
    ///     falls back to <see cref="MaquetteLowerPointScreenFracY"/> of the
    ///     screen height.</item>
    ///   <item>The side apexes sit half-height above the lower point; the
    ///     top apex sits a full height above it. With a wide overflowing
    ///     diamond the top apex lands well above the screen — the diamond's
    ///     top is cropped, as the spec wants.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Mirror symmetry is structural (J3c-1nonies).</b> The two side
    /// apexes are built as <c>centreX − halfW</c> and <c>centreX + halfW</c>
    /// at the SAME <c>sideY</c>; the top apex and the lower point both sit
    /// on <c>centreX</c>. So the four apexes are an exact left/right mirror
    /// about <c>screenWidth/2</c> for ANY <c>halfW</c> — the symmetry is a
    /// property of how the points are written, not an arithmetic
    /// coincidence. <c>DeskClipFrontierLogicTests</c> pins it.
    /// </para>
    ///
    /// <para>
    /// <b>Why the diamond overflows sideways instead of stretching down.</b>
    /// Commit 5f9aeb5 grew the maquette by driving the diamond's vertical
    /// extent from a free screen-height fraction — which made the edge slope
    /// drift to 0.72 and stop being parallel to the iso grid. The 2:1 ratio
    /// is the defining property of an iso shape; it cannot be a free
    /// parameter. To keep the maquette large, J3c-1quinquies instead grows
    /// the diamond's WIDTH past the screen width and derives the height from
    /// the slope-locked ratio. Pushing the lower point down to the HUD band
    /// (J3c-1octies) only lets the diamond overflow sideways a little more —
    /// the slope is untouched. The visible portion is large, the slope stays
    /// a true iso 0.5, the desk stays two thin corners.
    /// </para>
    /// </summary>
    /// <param name="screenSize">The screen size in pixels. Both positive.</param>
    /// <param name="topHudBandHeight">
    /// The top HUD band height in pixels. Non-negative. Accepted so a future
    /// tweak can clamp the side apexes against the top HUD band; the current
    /// placement does not consume it.
    /// </param>
    /// <param name="bottomHudBandHeight">
    /// The bottom HUD band height in pixels. Non-negative. The lower point
    /// is placed at <c>screenHeight - bottomHudBandHeight</c> — flush on the
    /// band's top edge. A zero value falls back to the
    /// <see cref="MaquetteLowerPointScreenFracY"/> fraction.
    /// </param>
    /// <returns>The four diamond apexes in screen pixels.</returns>
    /// <exception cref="System.ArgumentException">
    /// If a screen dimension is non-positive or a band height is negative.
    /// </exception>
    public static DiamondApexes MaquetteDiamond(
        SysVec2 screenSize, float topHudBandHeight, float bottomHudBandHeight)
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
        if (bottomHudBandHeight < 0f)
        {
            throw new System.ArgumentException(
                $"Bottom HUD band height must be non-negative; got " +
                $"{bottomHudBandHeight}.", nameof(bottomHudBandHeight));
        }

        // The diamond is centred on the screen's horizontal middle.
        float centreX = screenSize.X * 0.5f;

        // The knob sizes the diamond: half-width as a fraction of the screen
        // WIDTH. A fraction > 0.5 makes the diamond wider than the screen,
        // so the side apexes land off-screen — the deliberate sideways
        // overflow that keeps the maquette large without vertical stretch.
        float halfW = screenSize.X * MaquetteHalfWidthScreenFracX;

        // The half-height is DERIVED, never free: halfWidth * the locked
        // iso 2:1 slope. This is what guarantees every diamond edge has
        // slope exactly 0.5 and stays parallel to the iso grid diagonals.
        float halfH = halfW * IsoEdgeSlope;

        // J3c-1octies: the lower point is placed flush on the bottom HUD
        // band's TOP edge — screenHeight - bottomHudBandHeight — so no strip
        // of desk floor shows between the diamond and the HUD. With a zero
        // bottom band the lower point falls back to the documented fraction.
        float lowerY = bottomHudBandHeight > 0f
            ? screenSize.Y - bottomHudBandHeight
            : screenSize.Y * MaquetteLowerPointScreenFracY;

        // The side apexes are half-height above the lower point; the top
        // apex a full height above it. With a wide overflowing diamond the
        // top apex lands above the screen — the spec's cropped diamond top.
        float sideY = lowerY - halfH;
        float topY = lowerY - 2f * halfH;

        // The two side apexes are written as centreX ∓ halfW at the SAME
        // sideY; the top apex and the lower point both sit on centreX. The
        // four apexes are therefore an EXACT left/right mirror about
        // screenWidth/2 — for any halfW — by construction (J3c-1nonies).
        return new DiamondApexes(
            TopApex: new SysVec2(centreX, topY),
            LeftApex: new SysVec2(centreX - halfW, sideY),
            RightApex: new SysVec2(centreX + halfW, sideY),
            LowerPoint: new SysVec2(centreX, lowerY));
    }

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
    ///
    /// <para>
    /// Because the diamond overflows the screen sideways, the left apex is
    /// OFF the desk rect; its UV x is negative. That is fine — the
    /// half-plane is defined by any point on the edge and a normal, and the
    /// shader only ever evaluates it for on-rect fragments. The edge slope
    /// (and therefore the clip line) is identical whether the apex is on- or
    /// off-screen.
    /// </para>
    ///
    /// <para>
    /// <b>The right half-plane is the EXACT MIRROR of the left
    /// (J3c-1nonies, Didier, 2026-05-21 — "il faut que ce soit exact").</b>
    /// Didier saw the bottom of the screen read as not a perfect mirror:
    /// <i>"on perd à gauche, on gagne plus à droite"</i>. Only the LEFT
    /// half-plane is computed here, from the diamond's left lower edge. The
    /// RIGHT half-plane is then NOT computed independently — it is derived
    /// as the reflection of the left one about the vertical split column
    /// <c>splitU</c>:
    /// <list type="bullet">
    ///   <item>a UV point <c>(x, y)</c> reflects to <c>(2·splitU − x, y)</c>;</item>
    ///   <item>a UV normal <c>(nx, ny)</c> reflects to <c>(−nx, ny)</c>.</item>
    /// </list>
    /// Reflecting rather than recomputing makes the left/right symmetry an
    /// invariant the code cannot drift away from: the right frontier <i>is</i>
    /// the mirrored left frontier, not a value that merely happens to match.
    /// A defensive check first asserts the diamond's own apexes are genuine
    /// mirror inputs (lower point on the split, side apexes equidistant at a
    /// shared Y), so a caller passing a skewed diamond fails loud rather than
    /// producing a silently mirrored-but-wrong clip.
    /// </para>
    /// </summary>
    /// <param name="diamond">
    /// The maquette diamond apexes in screen pixels, from
    /// <see cref="MaquetteDiamond"/>. Must be left/right mirror-symmetric
    /// about its lower point's X.
    /// </param>
    /// <param name="deskRectOriginScreen">
    /// Top-left corner of the desk <c>TextureRect</c>, in screen pixels.
    /// </param>
    /// <param name="deskRectSize">
    /// Size of the desk <c>TextureRect</c>, in screen pixels. Both positive.
    /// </param>
    /// <returns>The <see cref="TwinClipFrontier"/> to push into the shader.</returns>
    /// <exception cref="System.ArgumentException">
    /// If the desk rect has a non-positive dimension, the left hypotenuse is
    /// degenerate (zero-length, or passing through its desk corner), or the
    /// diamond is not left/right mirror-symmetric about its lower point.
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

        // J3c-1nonies — the mirror invariant the shader symmetry rests on.
        // The diamond must already be a left/right mirror about its lower
        // point's X: the lower point is the mirror axis, the two side apexes
        // must be equidistant from it and share a Y. A skewed diamond would
        // otherwise be silently mirrored into a wrong-but-symmetric clip.
        float axisScreenX = diamond.LowerPoint.X;
        float leftGap = axisScreenX - diamond.LeftApex.X;
        float rightGap = diamond.RightApex.X - axisScreenX;
        if (System.MathF.Abs(leftGap - rightGap) > 1e-3f
            || System.MathF.Abs(diamond.LeftApex.Y - diamond.RightApex.Y) > 1e-3f
            || System.MathF.Abs(diamond.TopApex.X - axisScreenX) > 1e-3f)
        {
            throw new System.ArgumentException(
                "Maquette diamond is not left/right mirror-symmetric about " +
                $"its lower point X ({axisScreenX}): leftGap={leftGap}, " +
                $"rightGap={rightGap}, leftApexY={diamond.LeftApex.Y}, " +
                $"rightApexY={diamond.RightApex.Y}, topApexX={diamond.TopApex.X}.",
                nameof(diamond));
        }

        // The split is the diamond lower point's UV x — the vertical that
        // bounds each corner triangle to its own side of the screen, and the
        // axis the right half-plane is mirrored about.
        float splitU = ScreenToUv(
            diamond.LowerPoint, deskRectOriginScreen, deskRectSize).X;

        // The LEFT desk triangle's hypotenuse: left apex -> lower point.
        // Its keep side is the bottom-left desk corner, UV (0, 1). This is
        // the ONLY half-plane computed from geometry; the right side is its
        // exact mirror.
        var (leftPoint, leftNormal) = BuildHalfPlane(
            diamond.LeftApex, diamond.LowerPoint,
            deskCornerUv: new SysVec2(0f, 1f),
            deskRectOriginScreen, deskRectSize);

        // The RIGHT half-plane = the LEFT one reflected about the split
        // column. A UV point (x,y) reflects to (2·split − x, y); a UV normal
        // (nx,ny) reflects to (−nx, ny). The keep side flips with it: the
        // mirror of "points toward UV (0,1)" is "points toward UV (1,1)".
        var rightPoint = ReflectPointAboutSplit(leftPoint, splitU);
        var rightNormal = ReflectNormalAboutSplit(leftNormal);

        return new TwinClipFrontier(
            leftPoint, leftNormal, rightPoint, rightNormal, splitU);
    }

    /// <summary>
    /// Reflect a UV-space point across the vertical line <c>x = splitU</c>:
    /// <c>(x, y) ↦ (2·splitU − x, y)</c>. The mirror operation the right
    /// twin-clip half-plane is built with (J3c-1nonies).
    /// </summary>
    public static SysVec2 ReflectPointAboutSplit(SysVec2 pointUv, float splitU)
        => new(2f * splitU - pointUv.X, pointUv.Y);

    /// <summary>
    /// Reflect a UV-space direction/normal across a vertical line:
    /// <c>(nx, ny) ↦ (−nx, ny)</c>. The Y component is unchanged because the
    /// mirror axis is vertical (J3c-1nonies).
    /// </summary>
    public static SysVec2 ReflectNormalAboutSplit(SysVec2 normalUv)
        => new(-normalUv.X, normalUv.Y);

    /// <summary>
    /// The absolute slope <c>|Δy / Δx|</c> of one diamond edge in screen
    /// pixels. Exposed so <c>DeskClipFrontierLogicTests</c> can pin the iso
    /// invariant on all four edges — the regression commit 5f9aeb5
    /// introduced (and the one this whole helper is built to lock).
    /// </summary>
    /// <param name="edgeStartScreen">One edge endpoint, screen pixels.</param>
    /// <param name="edgeEndScreen">The other edge endpoint, screen pixels.</param>
    /// <returns>
    /// <c>|Δy / Δx|</c>. For a strict iso 2:1 diamond this is exactly
    /// <see cref="IsoEdgeSlope"/> (0.5) for every edge.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If the edge is vertical (Δx == 0) — an iso diamond has no vertical
    /// edge, so this is a degenerate input.
    /// </exception>
    public static float EdgeSlope(SysVec2 edgeStartScreen, SysVec2 edgeEndScreen)
    {
        float dx = edgeEndScreen.X - edgeStartScreen.X;
        float dy = edgeEndScreen.Y - edgeStartScreen.Y;
        if (System.MathF.Abs(dx) <= float.Epsilon)
        {
            throw new System.ArgumentException(
                "Diamond edge is vertical (Δx == 0); an iso diamond has no " +
                "vertical edge.", nameof(edgeEndScreen));
        }
        return System.MathF.Abs(dy / dx);
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
        SysVec2 deskRectSize)
    {
        SysVec2 edgeScreen = edgeEndScreen - edgeStartScreen;
        if (edgeScreen.LengthSquared() <= float.Epsilon)
        {
            throw new System.ArgumentException(
                "Maquette diamond left edge is zero-length; the frontier " +
                "is undefined.", nameof(edgeStartScreen));
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
                "Maquette/desk left frontier passes through the desk " +
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
