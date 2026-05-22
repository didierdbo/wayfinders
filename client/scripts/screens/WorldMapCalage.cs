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
/// <b>The two conventions that must be reconciled.</b>
/// <list type="number">
///   <item><b>World space</b> (world.yaml / <c>/api/world</c>) — origin at
///         the <i>SW corner</i>, X grows east, <b>Y grows north</b>.</item>
///   <item><b>Render-pixel space</b> (the PNG, and therefore the Godot
///         <c>Sprite2D</c> local space with <c>Centered = false</c>) —
///         origin at the <i>top-left</i>, X grows right, <b>Y grows
///         down</b>.</item>
/// </list>
/// The X axes agree ; the Y axes are flipped. A world point high in the
/// north (large world Y) must land near the <i>top</i> of the PNG (small
/// pixel Y). <see cref="WorldMetresToRenderPixel"/> does that flip — this is
/// the one spot the flip lives, so it cannot drift between the marker
/// placement and any future eM hit-test.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// The scene code (<c>GameScreen</c>) converts at the engine seam. Same
/// logic-vs-node split as <see cref="MaquetteContentLogic"/> and
/// <see cref="DeskFloorRectLogic"/>.
/// </para>
///
/// <para>
/// <b>Anisotropic scale is supported on purpose.</b> The eM mesh sidecar
/// happens to carry equal X/Y scale (0.004096 px/m both axes), but the
/// district (eT) sidecars do <i>not</i> (4.0 px/m X vs 1.6 px/m Y — the iso
/// 2:1 squash). The transform keeps the two scales separate so the same
/// class serves the eT calage at the next jalon without a rewrite — see
/// <see cref="ForDistrictCommonFrame"/>.
/// </para>
/// </summary>
public readonly struct WorldMapCalage
{
    /// <summary>
    /// World metres covered, on the X (east-west) axis, by the rendered
    /// frame. From <c>world_frame_m.size_x_m</c>.
    /// </summary>
    public float FrameSizeXMetres { get; }

    /// <summary>
    /// World metres covered, on the Y (north-south) axis, by the rendered
    /// frame. From <c>world_frame_m.size_y_m</c>.
    /// </summary>
    public float FrameSizeYMetres { get; }

    /// <summary>
    /// World X (metres) of the SW corner of the rendered frame. From
    /// <c>world_frame_m.frame_sw_anchor_m.x</c>. eM is 0 ; the eT districts
    /// are non-zero (the district frame sits at 91 900 m east).
    /// </summary>
    public float FrameSwAnchorXMetres { get; }

    /// <summary>
    /// World Y (metres) of the SW corner of the rendered frame. From
    /// <c>world_frame_m.frame_sw_anchor_m.y</c>.
    /// </summary>
    public float FrameSwAnchorYMetres { get; }

    /// <summary>
    /// Rendered PNG width in pixels. From <c>render_size_px.w</c>.
    /// </summary>
    public float RenderWidthPx { get; }

    /// <summary>
    /// Rendered PNG height in pixels. From <c>render_size_px.h</c>.
    /// </summary>
    public float RenderHeightPx { get; }

    /// <summary>
    /// Construct a calage from the sidecar's world frame and render size.
    /// </summary>
    /// <param name="frameSizeXMetres">World metres on X covered by the frame.
    /// Positive.</param>
    /// <param name="frameSizeYMetres">World metres on Y covered by the frame.
    /// Positive.</param>
    /// <param name="frameSwAnchorXMetres">World X of the frame's SW corner.</param>
    /// <param name="frameSwAnchorYMetres">World Y of the frame's SW corner.</param>
    /// <param name="renderWidthPx">Rendered PNG width in pixels. Positive.</param>
    /// <param name="renderHeightPx">Rendered PNG height in pixels. Positive.</param>
    /// <exception cref="ArgumentException">If any size is non-positive.</exception>
    public WorldMapCalage(
        float frameSizeXMetres,
        float frameSizeYMetres,
        float frameSwAnchorXMetres,
        float frameSwAnchorYMetres,
        float renderWidthPx,
        float renderHeightPx)
    {
        if (frameSizeXMetres <= 0f || frameSizeYMetres <= 0f)
        {
            throw new ArgumentException(
                $"World frame size must be positive; got " +
                $"{frameSizeXMetres}x{frameSizeYMetres} m.",
                nameof(frameSizeXMetres));
        }
        if (renderWidthPx <= 0f || renderHeightPx <= 0f)
        {
            throw new ArgumentException(
                $"Render size must be positive; got " +
                $"{renderWidthPx}x{renderHeightPx} px.",
                nameof(renderWidthPx));
        }

        FrameSizeXMetres = frameSizeXMetres;
        FrameSizeYMetres = frameSizeYMetres;
        FrameSwAnchorXMetres = frameSwAnchorXMetres;
        FrameSwAnchorYMetres = frameSwAnchorYMetres;
        RenderWidthPx = renderWidthPx;
        RenderHeightPx = renderHeightPx;
    }

    /// <summary>
    /// Render pixels per world metre on the X axis — derived, not stored,
    /// so it can never drift from the frame/render numbers. Matches the
    /// sidecar's <c>render_scale.render_px_per_world_m_x</c>.
    /// </summary>
    public float RenderPxPerMetreX => RenderWidthPx / FrameSizeXMetres;

    /// <summary>
    /// Render pixels per world metre on the Y axis. Matches the sidecar's
    /// <c>render_scale.render_px_per_world_m_y</c>.
    /// </summary>
    public float RenderPxPerMetreY => RenderHeightPx / FrameSizeYMetres;

    /// <summary>
    /// Convert a world-metre position to a render-pixel position on the eM
    /// mesh PNG (equivalently the maquette <c>Sprite2D</c>'s local pixel
    /// space, since the sprite is <c>Centered = false</c> at the board
    /// origin).
    ///
    /// <para>
    /// <b>The maths.</b> X is a straight scale-and-offset:
    /// <c>px = (worldX - swAnchorX) * pxPerMetreX</c>. Y is scale-and-offset
    /// <i>then flipped</i> against the render height, because world Y grows
    /// north (up) while pixel Y grows down:
    /// <c>py = renderHeight - (worldY - swAnchorY) * pxPerMetreY</c>.
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
    /// (<c>X</c> east, <c>Y</c> north).</param>
    /// <returns>The render-pixel position (<c>X</c> right, <c>Y</c> down).</returns>
    public SysVec2 WorldMetresToRenderPixel(SysVec2 worldMetres)
    {
        float px = (worldMetres.X - FrameSwAnchorXMetres) * RenderPxPerMetreX;
        float pyFromSw = (worldMetres.Y - FrameSwAnchorYMetres) * RenderPxPerMetreY;
        // Y-flip: world-north (large Y) -> pixel-top (small Y).
        float py = RenderHeightPx - pyFromSw;
        return new SysVec2(px, py);
    }

    /// <summary>
    /// The calage for the eM world mesh
    /// (<c>wf_e2_world_map_mesh_iso_2048x1024.png</c>).
    ///
    /// <para>
    /// <b>The numbers and where they come from.</b> Every value below is the
    /// committed calage sidecar
    /// <c>wf_e2_world_map_mesh_iso_2048x1024.png.calage.json</c> (Mira,
    /// 2026-05-22) — <c>world_frame_m</c> and <c>render_size_px</c>. The
    /// world frame is the full 500 × 250 km world with its SW corner at the
    /// world origin (0, 0) ; the render is 2048 × 1024 px. The derived
    /// <see cref="RenderPxPerMetreX"/> is 2048 / 500000 = 0.004096 px/m,
    /// matching the sidecar's <c>render_scale.render_px_per_world_m_x</c>
    /// exactly — that equality is the xUnit pin that catches a sidecar
    /// regeneration drifting from this factory.
    /// </para>
    ///
    /// <para>
    /// <b>Why a factory and not a sidecar read at runtime.</b> The eM frame
    /// is Varn-locked (world is 500 × 250 km, the render size is fixed by
    /// Mira's pipeline). A runtime JSON parse of a 4-line static file on the
    /// boot path buys nothing and adds a failure mode. The constants live
    /// here, the sidecar is the documented source of truth, and the
    /// equality assertions in <c>WorldMapCalageTests</c> guard the link. If
    /// Mira ever regenerates the eM mesh at a different size, this factory
    /// and the sidecar move together in one PR.
    /// </para>
    /// </summary>
    public static WorldMapCalage ForEmWorldMesh() => new(
        frameSizeXMetres: 500_000f,
        frameSizeYMetres: 250_000f,
        frameSwAnchorXMetres: 0f,
        frameSwAnchorYMetres: 0f,
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
    /// world frame. Both sidecars carry an identical <c>world_frame_m</c>
    /// (400 × 500 m at SW corner 91 900, 37 700) and an identical
    /// <c>render_size_px</c> (1600 × 800). Mira's compositor verified zero
    /// pixel overlap between the two silhouettes <i>in that common frame</i>.
    /// Therefore : both district <c>Sprite2D</c> nodes must be placed at the
    /// <b>same top-left</b>, and they interlock as the puzzle. Posing them at
    /// their individual <c>district.anchor</c> values instead would shear the
    /// interlock apart.
    /// </para>
    ///
    /// <para>
    /// <b>Why this is in J5 even though eT does not render yet.</b> J5 ships
    /// the eM map only ; the eM→eT swap is a later jalon. But the interlock
    /// rule is a calage decision Mira flagged now, and pinning it as a
    /// single shared factory (one calage, used twice) makes the "same
    /// top-left" contract unmissable and xUnit-pinned before the eT scene
    /// code exists to get it wrong. <see cref="ForEmWorldMesh"/> and this
    /// method are the two calage anchors the map layers register against.
    /// </para>
    /// </summary>
    public static WorldMapCalage ForDistrictCommonFrame() => new(
        frameSizeXMetres: 400f,
        frameSizeYMetres: 500f,
        frameSwAnchorXMetres: 91_900f,
        frameSwAnchorYMetres: 37_700f,
        renderWidthPx: 1600f,
        renderHeightPx: 800f);
}
