using System;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Components;

/// <summary>
/// Wayfinders 3D Backing Architecture (J4 refactor, 2026-05-09 evening,
/// locked) -- the single Camera3D follower that mirrors the prod
/// <see cref="Camera2D"/> transform onto a hidden 3D camera so Godot
/// physics picking has a current camera to cast rays from.
///
/// <para>
/// <b>Why this is its own component.</b> The J3 implementation embedded
/// the Camera3D inside <see cref="Cell3DBackingFollower"/>, which was
/// fine for a single witness cell but would require 600 Camera3D
/// instances at J4 scale. The Camera3D is conceptually a singleton per
/// <see cref="FogTileLayer"/> (one viewport, one current camera), so we
/// extract it. <b>Pattern M (new J4) -- "Extract singletons before
/// generalising"</b>.
/// </para>
///
/// <para>
/// <b>What this component pins (J2 + J3 contracts, untouched).</b>
/// <list type="bullet">
///   <item><b>Frame conversion</b> : the Camera2D position is in the
///         <see cref="FogTileGridLogic"/> frame (shifted + halfH baked-
///         in). Strip both before inverting the projection -- the 3D
///         witnesses live in the unshifted iso frame. Caught by the
///         J2-night regression test.</item>
///   <item><b>Inverse iso projection</b> : pixel <c>(X, Y)</c> -> 3D
///         <c>(X, _, Z)</c> via <c>(camX2D + 2 camY2D, -camX2D + 2 camY2D)</c>.</item>
///   <item><b>Camera3D pose</b> : pitch 30°, yaw -45°, distance 1500
///         (large enough to keep the witnesses inside the 1500-unit
///         frustum at any zoom).</item>
///   <item><b>Zoom sync formula</b> : <c>Camera3D.Size = (viewportH /
///         Camera2D.Zoom.Y) * sqrt(2)</c> -- pinned by
///         <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.J3CellAuthorityTests.Camera3D_Size_matches_viewportH_over_Camera2D_zoom_times_sqrt2"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class Camera3DShadowFollower : IDisposable
{
    private const float CameraDistance = 1500f;
    private const float PitchDegrees = 30f;
    private const float YawDegrees = -45f;

    /// <summary>The host node that owns the spawned Camera3D.</summary>
    public Node Host { get; }

    /// <summary>The follower Camera3D (current=true, ortho).</summary>
    public Camera3D? Camera3D { get; private set; }

    /// <summary>The prod Camera2D this follower mirrors. Captured by
    /// reference at <see cref="Spawn"/> ; can be null in test harnesses
    /// (the camera then stays at identity).</summary>
    public Camera2D? Camera2D { get; }

    /// <summary>Iso origin shift applied to the prod 2D grid -- the
    /// frame conversion subtracts this before inverting the projection.</summary>
    public PanVec2 IsoOriginShift { get; }

    /// <summary>Cell size in pixels (drives the halfH frame correction).</summary>
    public int CellSizePx { get; }

    private bool _disposed;

    public Camera3DShadowFollower(
        Node host,
        Camera2D? camera2D,
        PanVec2 isoOriginShift,
        int cellSizePx)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Camera2D = camera2D;
        IsoOriginShift = isoOriginShift;
        CellSizePx = cellSizePx;
    }

    /// <summary>
    /// Spawn the Camera3D, make it current, and apply the initial sync
    /// so it points at the Camera2D-anchored target on the very first
    /// physics tick (otherwise the canary self-check fires).
    /// </summary>
    public void Spawn()
    {
        // Force-enable Viewport.PhysicsObjectPicking (Pattern E, locked
        // J1). Idempotent.
        var viewport = Host.GetViewport();
        if (viewport is not null && !viewport.PhysicsObjectPicking)
        {
            viewport.PhysicsObjectPicking = true;
            GD.Print("[Camera3DShadowFollower] enabled Viewport.PhysicsObjectPicking");
        }

        Camera3D = new Camera3D
        {
            Name = "J4Camera3DShadowFollower",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = 0.1f,
            Far = 100000f,
            Size = 1080f, // overwritten by ApplySync each frame
        };
        Host.AddChild(Camera3D);
        Camera3D.MakeCurrent();

        ApplySync();

        // Pattern J -- every new component owning a fresh responsibility
        // ships with its own birth canary. Pattern M (J4) extracted the
        // Camera3D singleton out of Cell3DBackingFollower ; this log is
        // what proves the extracted singleton is alive in prod. The line
        // is unconditional (the picking-enable line above only fires on
        // cold start) so the canary survives editor reloads, scene-change
        // re-spawns, and project-level picking presets that flip the
        // viewport flag before us. Search the Output panel for
        // "Camera3DShadowFollower" to confirm Pattern M is active.
        var camPos = Camera3D.Position;
        var cam2DName = Camera2D is null ? "<none>" : Camera2D.Name.ToString();
        GD.Print(
            $"[Camera3DShadowFollower] spawned, current=true, ortho size={Camera3D.Size:F1}, " +
            $"pos=({camPos.X:F1},{camPos.Y:F1},{camPos.Z:F1}), camera2D={cam2DName}, " +
            $"isoShift=({IsoOriginShift.X:F1},{IsoOriginShift.Y:F1}), cellSizePx={CellSizePx}");
    }

    /// <summary>
    /// Per-frame sync. Mirror the prod Camera2D transform onto the
    /// Camera3D, applying the J2-locked frame conversion and the
    /// J3 zoom formula.
    /// </summary>
    public void ApplySync()
    {
        if (Camera3D is null) return;
        if (Camera2D is null) return;

        var halfH = CellSizePx / 4f;
        var camPos2DInIsoFrame = Camera2D.Position
            - new Vector2(IsoOriginShift.X, IsoOriginShift.Y + halfH);

        var camTarget3D = new Vector3(
            x: camPos2DInIsoFrame.X + 2f * camPos2DInIsoFrame.Y,
            y: 0f,
            z: -camPos2DInIsoFrame.X + 2f * camPos2DInIsoFrame.Y);

        var pitch = Mathf.DegToRad(PitchDegrees);
        var yaw = Mathf.DegToRad(YawDegrees);
        var horiz = Mathf.Cos(pitch) * CameraDistance;
        var vert = Mathf.Sin(pitch) * CameraDistance;
        var offset = new Vector3(
            horiz * Mathf.Sin(-yaw),
            vert,
            horiz * Mathf.Cos(-yaw));

        Camera3D.Position = camTarget3D + offset;
        Camera3D.LookAt(camTarget3D, Vector3.Up);

        var viewportSize = Host.GetViewport()?.GetVisibleRect().Size
            ?? new Vector2(1920f, 1080f);
        var zoomY = Mathf.Max(Camera2D.Zoom.Y, 1e-4f);
        Camera3D.Size = (viewportSize.Y / zoomY) * Mathf.Sqrt(2f);
    }

    /// <summary>Free the spawned Camera3D.</summary>
    public void Despawn()
    {
        if (Camera3D is not null && GodotObject.IsInstanceValid(Camera3D))
        {
            Camera3D.QueueFree();
        }
        Camera3D = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Despawn();
    }
}
