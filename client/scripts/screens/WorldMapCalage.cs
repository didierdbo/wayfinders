using System;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# calage (registration) logic for the J5 world map — converts a
/// world-metre coordinate into a render-pixel coordinate on Mira's eM iso
/// mesh render.
///
/// <para>
/// <b>The problem this solves.</b> <c>GET /api/world</c> gives every city a
/// <see cref="Wayfinders.Client.Services.Dtos.WorldPositionDto"/> in world
/// metres. Mira's <c>wf_e2_world_map_mesh_iso_2048x1024.png</c> is a fixed
/// raster. To pin the Halfgate marker on the map we need a single, pinned,
/// xUnit-covered transform from <c>(metres) → (pixels on that PNG)</c>. The
/// numbers come <i>only</i> from the calage sidecar
/// (<c>wf_e2_world_map_mesh_iso_2048x1024.png.calage.json</c>, Mira) — never
/// hand-tuned in scene code. This class is that transform.
/// </para>
///
/// <para>
/// <b>Why an affine matrix, not a flat px/m scale (schema v2, 2026-05-22).</b>
/// The PNG is a true isometric render — a 2:1 orthographic view of a flat
/// extruded slab, camera at <c>rot_x=60°, rot_z=45°</c>. In an iso render the
/// pixel X of a point depends on <i>both</i> its world X and its world Y, and
/// likewise for pixel Y. A separable <c>px/m</c> scale (the v1 sidecar's
/// <c>render_scale</c>) cannot express that — it is mathematically wrong for
/// an iso projection and was the bug that landed the Halfgate marker out in
/// the sea. v2 of the calage sidecar ships the <i>exact</i> projection as a
/// 2×3 affine matrix under
/// <c>iso_projection.world_to_pixel_projection.world_to_pixel.matrix</c>.
/// This struct carries that matrix and applies it.
/// </para>
///
/// <para>
/// <b>The Y-flip is already in the matrix.</b> World space (world.yaml /
/// <c>/api/world</c>) has its origin at the SW corner with Y growing north ;
/// render-pixel space has its origin top-left with Y growing down. v1
/// reconciled that with a separate explicit flip against the render height.
/// v2 bakes the flip into the affine matrix (the iso camera's <c>iso_basis_y</c>
/// carries a negative <c>dy</c> component). There is no separate flip step
/// any more — applying the matrix is the whole transform.
/// </para>
///
/// <para>
/// <b>Ground plane Z=0.</b> POIs (cities, markers) register on the flat
/// ground plane <c>world_z_m = 0</c>, not on the extruded land crest. The
/// mesh extrusion raises the visible silhouette top, but city world
/// coordinates are 2D ground coordinates — the matrix projects them at Z=0.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// The scene code (<c>HalfgateMarkerLayer</c>) converts at the engine seam.
/// </para>
/// </summary>
public readonly struct WorldMapCalage
{
    // --- The 2×3 affine, row-major: [[m00,m01,m02],[m10,m11,m12]]. -------
    //   pixelX = m00*worldX + m01*worldY + m02
    //   pixelY = m10*worldX + m11*worldY + m12
    // Applied to ABSOLUTE world coords (metres). The Y-flip and the iso
    // 2:1 squash are both baked in — this is the whole transform.

    /// <summary>Affine coefficient m00 — pixelX contribution per world X metre.</summary>
    public double M00 { get; }

    /// <summary>Affine coefficient m01 — pixelX contribution per world Y metre.</summary>
    public double M01 { get; }

    /// <summary>Affine coefficient m02 — pixelX constant offset.</summary>
    public double M02 { get; }

    /// <summary>Affine coefficient m10 — pixelY contribution per world X metre.</summary>
    public double M10 { get; }

    /// <summary>Affine coefficient m11 — pixelY contribution per world Y metre.</summary>
    public double M11 { get; }

    /// <summary>Affine coefficient m12 — pixelY constant offset.</summary>
    public double M12 { get; }

    /// <summary>
    /// Rendered PNG width in pixels. From <c>render_size_px.w</c>. Kept for
    /// in-frame bounds checks by callers — it is not part of the transform.
    /// </summary>
    public float RenderWidthPx { get; }

    /// <summary>
    /// Rendered PNG height in pixels. From <c>render_size_px.h</c>. Kept for
    /// in-frame bounds checks by callers — it is not part of the transform.
    /// </summary>
    public float RenderHeightPx { get; }

    /// <summary>
    /// Construct a calage from the sidecar's iso world→pixel affine matrix
    /// and render size.
    /// </summary>
    /// <param name="m00">Affine m00 — pixelX per world X metre.</param>
    /// <param name="m01">Affine m01 — pixelX per world Y metre.</param>
    /// <param name="m02">Affine m02 — pixelX constant offset.</param>
    /// <param name="m10">Affine m10 — pixelY per world X metre.</param>
    /// <param name="m11">Affine m11 — pixelY per world Y metre.</param>
    /// <param name="m12">Affine m12 — pixelY constant offset.</param>
    /// <param name="renderWidthPx">Rendered PNG width in pixels. Positive.</param>
    /// <param name="renderHeightPx">Rendered PNG height in pixels. Positive.</param>
    /// <exception cref="ArgumentException">If the render size is non-positive,
    /// or the affine matrix is singular (zero determinant — not invertible,
    /// which means it is not a valid projection).</exception>
    public WorldMapCalage(
        double m00,
        double m01,
        double m02,
        double m10,
        double m11,
        double m12,
        float renderWidthPx,
        float renderHeightPx)
    {
        if (renderWidthPx <= 0f || renderHeightPx <= 0f)
        {
            throw new ArgumentException(
                $"Render size must be positive; got " +
                $"{renderWidthPx}x{renderHeightPx} px.",
                nameof(renderWidthPx));
        }

        // The 2×2 linear part must be invertible — a singular matrix cannot
        // be a real projection and breaks the inverse round-trip. The iso
        // basis vectors are linearly independent by construction, so a zero
        // determinant means a corrupt sidecar.
        double det = (m00 * m11) - (m01 * m10);
        if (Math.Abs(det) < 1e-12)
        {
            throw new ArgumentException(
                $"World→pixel affine is singular (det={det}); the iso " +
                $"projection matrix is not invertible — corrupt calage sidecar.",
                nameof(m00));
        }

        M00 = m00;
        M01 = m01;
        M02 = m02;
        M10 = m10;
        M11 = m11;
        M12 = m12;
        RenderWidthPx = renderWidthPx;
        RenderHeightPx = renderHeightPx;
    }

    /// <summary>
    /// Determinant of the affine's 2×2 linear part. Non-zero by construction
    /// (the constructor rejects a singular matrix); used by the inverse.
    /// </summary>
    private double Determinant => (M00 * M11) - (M01 * M10);

    /// <summary>
    /// Convert a world-metre position to a render-pixel position on the eM
    /// mesh PNG (equivalently the maquette <c>Sprite2D</c>'s local pixel
    /// space, since the sprite is <c>Centered = false</c> at the board
    /// origin).
    ///
    /// <para>
    /// <b>The maths.</b> A single application of the iso world→pixel affine:
    /// <c>pixelX = m00·worldX + m01·worldY + m02</c> and
    /// <c>pixelY = m10·worldX + m11·worldY + m12</c>. The cross terms
    /// (<c>m01</c>, <c>m10</c>) are what makes this an iso projection rather
    /// than a flat scale ; the Y-flip is carried by the sign of <c>m11</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Out-of-frame points are not clamped.</b> A world position outside
    /// the rendered frame yields a pixel outside <c>[0, render]</c> — that is
    /// a deliberate signal (a city placed off the map is a data bug, not
    /// something to silently snap onto an edge). Callers that need a clamp
    /// apply it explicitly.
    /// </para>
    /// </summary>
    /// <param name="worldMetres">The world position in metres
    /// (<c>X</c> east, <c>Y</c> north), on the ground plane Z=0.</param>
    /// <returns>The render-pixel position (<c>X</c> right, <c>Y</c> down).</returns>
    public SysVec2 WorldMetresToRenderPixel(SysVec2 worldMetres)
    {
        double wx = worldMetres.X;
        double wy = worldMetres.Y;
        double px = (M00 * wx) + (M01 * wy) + M02;
        double py = (M10 * wx) + (M11 * wy) + M12;
        return new SysVec2((float)px, (float)py);
    }

    /// <summary>
    /// Inverse transform — a render-pixel position back to world metres on
    /// the ground plane. The inverse of the 2×3 affine: subtract the
    /// translation, then apply the inverse of the 2×2 linear part. Used by
    /// the xUnit round-trip pin and by any future eM hit-test (click a pixel
    /// on the map → world coordinate).
    /// </summary>
    /// <param name="renderPixel">A render-pixel position on the PNG.</param>
    /// <returns>The world-metre position (<c>X</c> east, <c>Y</c> north).</returns>
    public SysVec2 RenderPixelToWorldMetres(SysVec2 renderPixel)
    {
        double det = Determinant;
        double dx = renderPixel.X - M02;
        double dy = renderPixel.Y - M12;
        // [wx, wy]^T = inv(L) . [dx, dy]^T, where L is the 2×2 linear part.
        double wx = ((M11 * dx) - (M01 * dy)) / det;
        double wy = ((-M10 * dx) + (M00 * dy)) / det;
        return new SysVec2((float)wx, (float)wy);
    }

    /// <summary>
    /// The calage for the eM world mesh
    /// (<c>wf_e2_world_map_mesh_iso_2048x1024.png</c>).
    ///
    /// <para>
    /// <b>The numbers and where they come from.</b> The six affine
    /// coefficients are the committed calage sidecar
    /// <c>wf_e2_world_map_mesh_iso_2048x1024.png.calage.json</c> (Mira,
    /// schema v2, 2026-05-22) —
    /// <c>iso_projection.world_to_pixel_projection.world_to_pixel.matrix</c>,
    /// row-major. The render size is <c>render_size_px</c> (2048 × 1024). The
    /// matrix is verified by the sidecar's <c>control_point</c>: world
    /// (250000, 125000) m — the frame centre — projects to pixel
    /// (1024, 512) — the PNG centre. <c>WorldMapCalageTests</c> pins exactly
    /// that, so a sidecar regeneration that drifts from this factory goes red
    /// long before a marker lands at the wrong coastline.
    /// </para>
    ///
    /// <para>
    /// <b>Why a factory and not a sidecar read at runtime.</b> The eM frame
    /// and the iso camera are Varn-/Mira-locked. A runtime JSON parse of a
    /// static file on the boot path buys nothing and adds a failure mode. The
    /// constants live here, the sidecar is the documented source of truth,
    /// and the <c>control_point</c> assertion in <c>WorldMapCalageTests</c>
    /// guards the link. If Mira regenerates the eM mesh, this factory and the
    /// sidecar move together in one PR.
    /// </para>
    /// </summary>
    public static WorldMapCalage ForEmWorldMesh() => new(
        // iso_projection.world_to_pixel_projection.world_to_pixel.matrix
        m00: 0.00263301,
        m01: 0.00294782,
        m02: -2.73022584,
        m10: 0.0013165,
        m11: -0.00147391,
        m12: 367.11298207,
        renderWidthPx: 2048f,
        renderHeightPx: 1024f);

    /// <summary>
    /// The calage for the eT district common frame — shared by
    /// <c>wf_e3_district_lower_quays_mesh_iso_1600x800.png</c> AND
    /// <c>wf_e3_district_high_wall_mesh_iso_1600x800.png</c>.
    ///
    /// <para>
    /// <b>The puzzle-interlock invariant (Mira, <c>puzzle_interlock</c> block
    /// in both eT calage sidecars).</b> The two district renders are NOT
    /// posed at their own anchors — they are posed at the <b>same</b> common
    /// world frame with the <b>same</b> iso projection. Both sidecars carry an
    /// identical <c>world_to_pixel.matrix</c>, an identical
    /// <c>render_size_px</c> (1600 × 800) and an identical <c>control_point</c>
    /// (world (92100, 37950) m → pixel (800, 400), the PNG centre). Mira's
    /// compositor verified zero pixel overlap between the two silhouettes
    /// <i>in that common frame</i>. Therefore both district <c>Sprite2D</c>
    /// nodes pose at the <b>same top-left</b> and interlock as the puzzle.
    /// Posing them at their individual <c>district.anchor</c> values instead
    /// would shear the interlock apart. Because both eT sidecars carry the
    /// identical matrix, this single factory <i>is</i> that shared projection
    /// — one calage, used twice.
    /// </para>
    ///
    /// <para>
    /// <b>Why this is in J5 even though eT does not render yet.</b> J5 ships
    /// the eM map only ; the eM→eT swap is a later jalon. But the interlock
    /// rule is a calage decision Mira flagged now, and pinning it as a single
    /// shared factory makes the "same projection, same top-left" contract
    /// unmissable and xUnit-pinned before the eT scene code exists to get it
    /// wrong.
    /// </para>
    /// </summary>
    public static WorldMapCalage ForDistrictCommonFrame() => new(
        // Identical in BOTH eT district sidecars'
        // iso_projection.world_to_pixel_projection.world_to_pixel.matrix —
        // the shared projection that makes the puzzle interlock.
        m00: 2.57129739,
        m01: 2.57129739,
        m02: -333597.22506658,
        m10: 1.28564869,
        m11: -1.28564869,
        m12: -69217.87672955,
        renderWidthPx: 1600f,
        renderHeightPx: 800f);
}
