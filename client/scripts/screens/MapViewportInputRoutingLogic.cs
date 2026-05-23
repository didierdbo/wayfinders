using System;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# screen → maquette-world coordinate routing for the eM map
/// click pipeline (Étape A — missions affichées sur eM comme POI
/// cliquables, Rune, 2026-05-23). The maquette analogue of
/// <see cref="DeskInputRoutingLogic"/> : turns a raw screen-pixel mouse
/// position into a position in the maquette's iso world space — the
/// space the maquette <see cref="IsoBoard"/>'s background pixel is laid
/// out in, and therefore the space mission POI hit-tests live in.
///
/// <para>
/// <b>What makes this different from the desk router.</b> Two things,
/// and both come straight from the J3a/J3c-1 architecture :
/// <list type="number">
///   <item><b>The maquette container is NOT full-screen.</b> The
///         <c>MapViewportContainer</c> sits at a non-zero screen offset
///         (the HUD top band pushes it down by 32 px, the
///         <c>MapRecessDepthPx</c> by another 8 px) and is shorter than
///         the screen on the bottom by the HUD bottom band. So a screen
///         pixel has to lose the container offset before it can be
///         treated as a viewport pixel — unlike the desk SubViewports,
///         which are full-screen at the origin.</item>
///   <item><b>The maquette camera is PANNABLE.</b> The desk camera is
///         immobile (J3c-1 locked invariant) ; the maquette camera moves
///         under MMB-drag (J3a). A click hit-tests against
///         <i>the camera's current position</i>, not against a frozen
///         value — the caller passes the live camera centre at click
///         time, which is what the maquette pan already mutates.</item>
/// </list>
/// The conversion itself is the same affine math
/// (<see cref="DeskInputRoutingLogic.ScreenToDeskWorld"/>) once the
/// container offset is stripped. We chose <i>not</i> to share a single
/// helper between desk and maquette : the desk path is heavily commented
/// around the "no container offset" invariant, and a generic
/// <c>ScreenToCameraWorld(screenPos, screenSize, containerOffset,
/// containerSize, cameraCentre, cameraZoom)</c> would re-introduce
/// exactly the orthogonality risk the two specialised helpers avoid
/// (trap #6). One helper per surface, each pinned independently. Same
/// concrete-first stance as instancing two <c>SubViewport</c>s instead
/// of inventing an <c>IsoSpace</c> abstraction.
/// </para>
///
/// <para>
/// <b>The conversion.</b>
/// <list type="number">
///   <item>Strip the container offset :
///         <c>containerLocalPos = screenPos - containerOffset</c>.</item>
///   <item>The <c>MapViewport</c>'s <c>stretch = true</c> mode renders at
///         the container size 1:1 — its own <c>size</c> matches the
///         container in pixels (verified at <c>GameScreen._Ready</c>,
///         <c>_mapViewport.Size = (Vector2I)_mapContainer.Size</c>). So a
///         container-local pixel <i>is</i> a viewport-local pixel, no
///         extra scale.</item>
///   <item>Apply the same camera-centre / zoom inverse as the desk :
///         <c>world = cameraCentre + (containerLocalPos -
///         containerSize/2) / cameraZoom</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Out-of-rect clicks.</b> A click outside the maquette container
/// (over the HUD bands, for instance) still produces a maquette-world
/// point — the caller decides what to do with it. We do NOT short-circuit
/// here : the HUD bands consume their own clicks at a higher priority,
/// and the four-layer shell guarantees decor TextureRects sit at
/// <c>MouseFilter = Ignore</c> so they fall through cleanly. If a click
/// lands outside the container by mistake the resulting world point
/// simply won't sit on any POI hit-rect and the click drops to the desk
/// fallthrough. Same graceful-degrade discipline as the rest of the
/// shell's input cascade.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only
/// <see cref="System.Numerics.Vector2"/>. Same seam discipline as
/// <see cref="DeskInputRoutingLogic"/>, <see cref="MapViewportPanLogic"/>
/// and the rest of the J3 logic layer ; the Godot-bound
/// <c>GameScreen._UnhandledInput</c> converts at the engine edge.
/// </para>
/// </summary>
public static class MapViewportInputRoutingLogic
{
    /// <summary>
    /// Map a screen-pixel mouse position to a position in the maquette's
    /// iso world space, given the maquette container rect and the live
    /// <c>MapCamera2D</c> frame.
    /// </summary>
    /// <param name="screenPos">Mouse position in screen pixels —
    /// <c>InputEventMouse.Position</c> at the engine edge.</param>
    /// <param name="containerOffset">Top-left of the
    /// <c>MapViewportContainer</c> in screen pixels (<c>Control.Position</c>
    /// equivalent — including any HUD band / recess offset).</param>
    /// <param name="containerSize">Size of the <c>MapViewportContainer</c>
    /// in screen pixels. Must be positive on both axes — a zero-size
    /// container is a layout bug, not a routable click. Also the size
    /// the <c>MapViewport</c> stretches to (the J3a <c>_Ready</c>
    /// invariant).</param>
    /// <param name="mapCameraCentre">The current <c>MapCamera2D.Position</c> —
    /// the maquette-world point shown at the container centre. Pannable
    /// at runtime.</param>
    /// <param name="mapCameraZoom">The current <c>MapCamera2D.Zoom</c>.
    /// Must be positive on both axes.</param>
    /// <returns>The position in maquette-world pixels.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// If either zoom or container-size component is non-positive.
    /// </exception>
    public static SysVec2 ScreenToMaquetteWorld(
        SysVec2 screenPos,
        SysVec2 containerOffset,
        SysVec2 containerSize,
        SysVec2 mapCameraCentre,
        SysVec2 mapCameraZoom)
    {
        if (mapCameraZoom.X <= 0f || mapCameraZoom.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mapCameraZoom),
                $"Map camera zoom must be positive on both axes; got " +
                $"{mapCameraZoom}.");
        }
        if (containerSize.X <= 0f || containerSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(containerSize),
                $"Container size must be positive on both axes; got " +
                $"{containerSize}.");
        }

        var containerLocalPos = screenPos - containerOffset;
        var containerCentre = containerSize * 0.5f;
        var offsetFromCentre = containerLocalPos - containerCentre;
        var worldOffset = new SysVec2(
            offsetFromCentre.X / mapCameraZoom.X,
            offsetFromCentre.Y / mapCameraZoom.Y);
        return mapCameraCentre + worldOffset;
    }

    /// <summary>
    /// True iff <paramref name="screenPos"/> falls inside the maquette
    /// container's screen rect. Used by the <c>GameScreen</c> click
    /// router to decide "is this a maquette click candidate, or did the
    /// player click on the HUD or outside the shell?" before running
    /// the world-space conversion. Inclusive on the top-left, exclusive
    /// on the bottom-right — same convention as Godot's
    /// <c>Rect2.HasPoint</c>.
    /// </summary>
    public static bool IsInsideContainer(
        SysVec2 screenPos,
        SysVec2 containerOffset,
        SysVec2 containerSize)
    {
        return screenPos.X >= containerOffset.X
            && screenPos.Y >= containerOffset.Y
            && screenPos.X < containerOffset.X + containerSize.X
            && screenPos.Y < containerOffset.Y + containerSize.Y;
    }
}
