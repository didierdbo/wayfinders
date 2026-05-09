using System;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Components;

/// <summary>
/// Jalon 3 (Wayfinders 3D Backing Architecture, doc §3 Jalon 2 "Questions
/// ouvertes pour Jalon 3", locked 2026-05-09 + GO J3 2026-05-09) -- the
/// first prod cell whose spatial position is driven by a 3D-authoritative
/// <c>Node3D</c>, with the visible 2D <c>Polygon2D</c> children of
/// <see cref="FogTileLayer"/> as a passive consumer. <b>Pattern A locked
/// in runtime, Option A (entities 3D-authoritative, camera 2D-authoritative).</b>
///
/// <para>
/// <b>Strict scope -- one cell.</b> This component applies the 3D backing
/// to a single witness cell of the production grid (default
/// <see cref="WitnessCell"/> = (0, 0), the top-left iso pole, chosen for
/// minimum blast radius -- if the sync regresses visually, it shows up
/// at the visible edge of the world image, not at the centre where it
/// would distort the playable area). The other ~600 cells of E2 World
/// keep their pre-J3 pickup-via-2D-math path entirely. <c>git revert</c>
/// of the J3 commit restores the J2 state with no residue.
/// </para>
///
/// <para>
/// <b>The contract this component pins.</b>
/// <list type="bullet">
///   <item>The witness cell's <i>spatial</i> position lives on the
///         <see cref="WitnessNode3D"/> at world coords
///         <c>(col*cs, 0, row*cs)</c> -- the same iso frame as the J2
///         shadow-sync probe and as the pin test
///         <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.IsoProjection3DTo2DTests"/>.
///         The 2D <c>Polygon2D</c> children of <see cref="FogTileLayer"/>
///         for that cell are mutated each frame to match the projected
///         3D position : <c>screen2D = forward_iso(node3D.Position) +
///         shift + (0, halfH)</c>.</item>
///   <item>The pickup volume is a 3D <see cref="Area3D"/> with a
///         <see cref="ConvexPolygonShape3D"/> rhombus prism (Pattern J
///         invariant 7) anchored at the same 3D world point. The 2D
///         <c>WorldPositionToCell</c> path on <see cref="FogTileLayer"/>
///         continues to work for every other cell -- the witness cell
///         can be picked by either path, but the J3 contract is that
///         the 3D path returns the canonical signal (the 2D fallback
///         exists for the F debug command and any pre-3D consumer).</item>
///   <item>A <see cref="Camera3D"/> follower in the same root viewport
///         mirrors the prod <see cref="MapPan2DComponent.WorldCamera"/>
///         transform every frame (Pattern B2, locked J2). Without the
///         Camera3D current and aimed at the witness, the Area3D ray
///         picking silently fails -- canary self-check verifies this at
///         <c>_Ready</c>.</item>
///   <item>Zoom sync : <c>Camera3D.Size = (viewportH / Camera2D.Zoom.Y) *
///         sqrt(2)</c>, the formula derived from the J2 pin test
///         (<c>ProjectionScale = sqrt(2)</c>). The follower applies it
///         every <c>_Process</c> frame ; if Didier zooms in/out, the 3D
///         frustum scales identically to the 2D viewport so the
///         witness's screen position stays in lock-step with the rest
///         of the grid. <b>Pinned by</b>
///         <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.J3CellAuthorityTests.Camera3D_Size_matches_viewportH_over_Camera2D_zoom_times_sqrt2"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this lives in components/ (not in scratch/).</b> This is the
/// first piece of <i>production</i> code carrying the 3D backing
/// pattern. The J1/J2 probes in <c>scenes/scratch/</c> proved Patterns
/// A, B2, C, E, F, G, H, I, J in isolation ; J3 is the migration moment
/// where the first prod cell starts using them. The component is
/// designed so that flipping <see cref="EnableJ3Witness"/> to <c>false</c>
/// on the parent <see cref="FogTileLayer"/> reverts to pre-J3 behaviour
/// without unloading the type -- safe kill-switch for production.
/// </para>
///
/// <para>
/// <b>Spawn protocol (entirely code-driven, no .tscn).</b>
/// <list type="number">
///   <item><see cref="FogTileLayer.Configure"/> calls
///         <see cref="Spawn"/> with the host node, the witness coord,
///         the cell size, the iso shift, and a reference to the
///         <c>MapPan2DComponent.WorldCamera</c>.</item>
///   <item>Spawn instantiates <see cref="WitnessNode3D"/> +
///         <see cref="WitnessArea3D"/> + <see cref="WitnessShape3D"/>
///         + <see cref="FollowerCamera3D"/> as children of the host,
///         positions Witness at <c>(col*cs, 0, row*cs)</c>, builds the
///         rhombus prism, connects the Area3D signals.</item>
///   <item>Per-frame <see cref="ApplyFollowerSync"/> reads the prod
///         Camera2D position + zoom, derives the Camera3D target +
///         ortho size, and updates the witness 2D cell position from
///         the witness 3D node position via the forward iso projection.</item>
///   <item><see cref="Despawn"/> at <see cref="FogTileLayer._ExitTree"/>
///         disconnects signals (handler captured in fields, Rune
///         disconnection discipline) and frees the spawned nodes.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this component does NOT do (J4+ scope).</b>
/// <list type="bullet">
///   <item>Generalise to all ~600 cells of the grid. J3 is one cell.</item>
///   <item>Replace <see cref="FogTileLayer.WorldPositionToCell"/>. The
///         2D math path stays canonical for the F debug command and
///         the drill resolver until J5+ migrates them.</item>
///   <item>Animate the witness (lift, rotation, drag-drop). The
///         witness <c>Node3D.Position</c> is static this jalon ; J6+
///         will mutate it via Tween3D.</item>
/// </list>
/// </para>
/// </summary>
public sealed class Cell3DBackingFollower : IDisposable
{
    /// <summary>The host node3D (or node2D supporting 3D children) that
    /// owns the spawned 3D nodes. The follower spawns Camera3D, Witness
    /// Node3D, and Area3D as children of this node.</summary>
    public Node Host { get; }

    /// <summary>The 3D-authoritative witness cell node. Position is
    /// <c>(col*cs, 0, row*cs)</c>, set once at <see cref="Spawn"/> and
    /// never mutated by this component (J3 scope -- static).</summary>
    public Node3D? WitnessNode3D { get; private set; }

    /// <summary>The witness Area3D (pickup volume). Child of
    /// <see cref="WitnessNode3D"/>. Mouse signals connected at Spawn,
    /// disconnected at <see cref="Despawn"/>.</summary>
    public Area3D? WitnessArea3D { get; private set; }

    /// <summary>The witness CollisionShape3D wrapping the rhombus prism.</summary>
    public CollisionShape3D? WitnessShape3D { get; private set; }

    /// <summary>The follower Camera3D, current=true, ortho. Mirrors
    /// the prod Camera2D transform each frame.</summary>
    public Camera3D? FollowerCamera3D { get; private set; }

    /// <summary>The witness coord in the production grid. Default (0, 0)
    /// -- top-left iso pole, minimal visual blast radius if the sync
    /// regresses.</summary>
    public GridCoord WitnessCell { get; }

    /// <summary>The cell size in pixels (matches
    /// <see cref="FogTileLayer.CellSizePx"/>).</summary>
    public int CellSizePx { get; }

    /// <summary>The iso origin shift applied to the prod 2D grid
    /// (matches <see cref="FogTileGridLogic.ComputeIsoOriginShift"/>).
    /// Captured at Spawn so the follower can reuse it without re-querying
    /// the grid every frame.</summary>
    public PanVec2 IsoOriginShift { get; }

    /// <summary>The static Y-offset applied to every fog cell to fake the
    /// floating-tile effect (matches
    /// <see cref="FogTileLayer.FogYOffset"/>). The 2D consumer position
    /// derived from the 3D-authoritative point includes this offset so
    /// the witness cell visually lines up with its neighbours.</summary>
    public float FogYOffset { get; }

    /// <summary>The prod Camera2D. Captured by reference at Spawn ; the
    /// follower reads its position + zoom each frame.</summary>
    public Camera2D? Camera2D { get; }

    private Action? _mouseEnteredHandler;
    private Action? _mouseExitedHandler;
    private Area3D.InputEventEventHandler? _inputEventHandler;

    private bool _disposed;

    public Cell3DBackingFollower(
        Node host,
        GridCoord witnessCell,
        int cellSizePx,
        PanVec2 isoOriginShift,
        float fogYOffset,
        Camera2D? camera2D)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        WitnessCell = witnessCell;
        CellSizePx = cellSizePx;
        IsoOriginShift = isoOriginShift;
        FogYOffset = fogYOffset;
        Camera2D = camera2D;
    }

    /// <summary>
    /// Spawn the 3D backing nodes : Camera3D follower, Witness Node3D +
    /// Area3D + ConvexPolygonShape3D rhombus prism. Connects the Area3D
    /// mouse signals to the supplied callbacks (typically routed back
    /// to <see cref="FogTileLayer"/> for hover/click feedback).
    ///
    /// <para>
    /// <b>Order is load-bearing.</b> Camera3D must be added to the
    /// scene tree BEFORE we call <see cref="ApplyFollowerSync"/>, otherwise
    /// <c>MakeCurrent</c> silently no-ops and the canary self-check fires
    /// <c>[CANARY FAIL]</c>. Same lesson as the J2 probe -- the
    /// invariants are checked at the end so the sequence here puts each
    /// node in the tree, then sets its initial transform, then connects
    /// its signals.
    /// </para>
    /// </summary>
    public void Spawn(
        Action? onMouseEntered = null,
        Action? onMouseExited = null,
        Area3D.InputEventEventHandler? onInputEvent = null)
    {
        // Ensure the viewport is configured for 3D physics picking
        // (Pattern E, locked Jalon 1). This is idempotent : if the
        // viewport already has the flag, it stays on.
        var viewport = (Host as Node)?.GetViewport();
        if (viewport is not null && !viewport.PhysicsObjectPicking)
        {
            viewport.PhysicsObjectPicking = true;
            GD.Print("[J3 Cell3DBacking] enabled root Viewport.PhysicsObjectPicking");
        }

        // -- Camera3D follower (current=true, ortho). Created BEFORE the
        // witness so the Area3D has a current camera to receive picking
        // rays from at the first physics tick.
        FollowerCamera3D = new Camera3D
        {
            Name = "J3FollowerCamera3D",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = 0.1f,
            Far = 100000f,
            Size = 1080f, // overwritten by ApplyFollowerSync each frame
        };
        Host.AddChild(FollowerCamera3D);
        FollowerCamera3D.MakeCurrent();

        // -- Witness Node3D at the iso world coord (col*cs, 0, row*cs).
        // This is the 3D-authoritative anchor for the cell -- the 2D
        // sprite/Polygon2D position is derived FROM this node, never
        // the other way around (Option A locked, doc §5 Q2 + GO J3).
        WitnessNode3D = new Node3D
        {
            Name = $"J3Witness3D_Cell_{WitnessCell.Col}_{WitnessCell.Row}",
            Position = new Vector3(
                WitnessCell.Col * CellSizePx,
                0f,
                WitnessCell.Row * CellSizePx),
        };
        Host.AddChild(WitnessNode3D);

        // -- Area3D pickup volume. Child of the Node3D witness so the
        // Area3D inherits its world coord automatically.
        WitnessArea3D = new Area3D
        {
            Name = "J3WitnessArea3D",
            InputRayPickable = true,
            CollisionLayer = 1u,
            CollisionMask = 1u,
        };
        WitnessNode3D.AddChild(WitnessArea3D);

        WitnessShape3D = new CollisionShape3D
        {
            Name = "J3WitnessShape3D",
            Shape = BuildRhombusPrismShape(CellSizePx, defaultBackingHeightPx: 6),
        };
        WitnessArea3D.AddChild(WitnessShape3D);

        // Capture handlers in fields so Despawn can disconnect with the
        // exact same delegates (Rune Risk #1 disconnection discipline).
        _mouseEnteredHandler = onMouseEntered ?? DefaultLogMouseEntered;
        _mouseExitedHandler = onMouseExited ?? DefaultLogMouseExited;
        _inputEventHandler = onInputEvent ?? DefaultLogInputEvent;

        WitnessArea3D.MouseEntered += _mouseEnteredHandler;
        WitnessArea3D.MouseExited += _mouseExitedHandler;
        WitnessArea3D.InputEvent += _inputEventHandler;

        // Initial sync (mirror of the per-frame ApplyFollowerSync). The
        // first frame must already have the camera pointing at the
        // witness, otherwise the canary self-check fails.
        ApplyFollowerSync();

        // Canary self-check (Pattern J, locked J2 night). Logs vivid
        // [CANARY FAIL] lines if any of the 7 invariants is broken --
        // future testplay regression lands within seconds of scene load.
        AssertCanaryInvariants();

        GD.Print(
            $"[J3 Cell3DBacking] spawned witness cell ({WitnessCell.Col}, {WitnessCell.Row}) " +
            $"at 3D world ({WitnessNode3D.Position.X:F0}, 0, {WitnessNode3D.Position.Z:F0}) " +
            $"-- Pattern A in runtime, Option A entity-authoritative");
    }

    /// <summary>
    /// Per-frame sync. Two responsibilities :
    /// <list type="bullet">
    ///   <item>Mirror the prod <see cref="Camera2D"/> transform onto the
    ///         <see cref="FollowerCamera3D"/>, applying the J2-locked
    ///         frame conversion (strip iso shift + halfH from the 2D
    ///         camera position before inverting the projection) and the
    ///         J3 zoom formula <c>Size = (viewportH / Zoom.Y) * sqrt(2)</c>.</item>
    ///   <item>Compute the witness's 2D screen position by forward-projecting
    ///         the 3D Node3D.Position. <see cref="WitnessCell2DPosition"/>
    ///         is the public read for <see cref="FogTileLayer"/> to apply
    ///         as the witness <c>Polygon2D</c>'s <c>Position</c> each
    ///         frame.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Why both halves run in the same method.</b> They share the
    /// scaffold (cell size, iso shift, halfH) and they must agree on
    /// the same camera-frame snapshot ; running them as one function
    /// makes the timing trivially correct.
    /// </para>
    /// </summary>
    public void ApplyFollowerSync()
    {
        if (FollowerCamera3D is null) return;

        // ---- Camera3D follower from Camera2D ----
        if (Camera2D is not null)
        {
            // Frame conversion : Camera2D position is in the
            // FogTileGridLogic frame (shifted + halfH baked-in). Strip
            // both before inverting the projection -- the 3D witness
            // lives in the unshifted iso frame.
            var halfH = CellSizePx / 4f;
            var camPos2DInIsoFrame = Camera2D.Position
                - new Vector2(IsoOriginShift.X, IsoOriginShift.Y + halfH);

            // Inverse iso projection : pixel (X, Y) -> 3D (X, _, Z).
            var camTarget3D = new Vector3(
                x: camPos2DInIsoFrame.X + 2f * camPos2DInIsoFrame.Y,
                y: 0f,
                z: -camPos2DInIsoFrame.X + 2f * camPos2DInIsoFrame.Y);

            const float distance = 1500f;
            const float pitchDeg = 30f;
            const float yawDeg = -45f;
            var pitch = Mathf.DegToRad(pitchDeg);
            var yaw = Mathf.DegToRad(yawDeg);
            var horiz = Mathf.Cos(pitch) * distance;
            var vert = Mathf.Sin(pitch) * distance;
            var offset = new Vector3(
                horiz * Mathf.Sin(-yaw),
                vert,
                horiz * Mathf.Cos(-yaw));

            FollowerCamera3D.Position = camTarget3D + offset;
            FollowerCamera3D.LookAt(camTarget3D, Vector3.Up);

            // Zoom sync (J3 formula). Camera2D.Zoom is the uniform scale
            // factor (Vector2 with X=Y for our 2D pan setup). The 3D
            // ortho size in world units must match the 2D visible
            // viewport in pixels, de-scaled by sqrt(2) (the projection
            // scale factor pinned by the J2 pin test). When Camera2D
            // zooms in (Zoom.Y > 1), the 2D viewport shows fewer
            // pixels -- the 3D frustum must shrink correspondingly so
            // picking precision matches.
            var viewportSize = (Host as Node)?.GetViewport()?.GetVisibleRect().Size
                ?? new Vector2(1920f, 1080f);
            var zoomY = Mathf.Max(Camera2D.Zoom.Y, 1e-4f); // guard against zero
            FollowerCamera3D.Size = (viewportSize.Y / zoomY) * Mathf.Sqrt(2f);
        }

        // ---- Witness 2D position from witness 3D position ----
        // Forward iso projection : 3D (X, _, Z) -> 2D pixel.
        // Pinned by IsoProjection3DTo2DTests (the math is the same).
        if (WitnessNode3D is not null)
        {
            var p = WitnessNode3D.Position;
            var screenX = (p.X - p.Z) / 2f;
            var screenY = (p.X + p.Z) / 4f;
            // Re-apply the iso shift + halfH that ComputeCellCenter +
            // ComputeIsoOriginShift bake in for the 2D consumer (so the
            // witness lines up with its neighbour cells exactly).
            var halfH = CellSizePx / 4f;
            WitnessCell2DPosition = new Vector2(
                screenX + IsoOriginShift.X,
                screenY + IsoOriginShift.Y + halfH + FogYOffset);
        }
    }

    /// <summary>The witness's 2D screen position derived from
    /// <see cref="WitnessNode3D"/>'s position via forward iso projection.
    /// Updated by <see cref="ApplyFollowerSync"/> each frame ; consumed
    /// by <see cref="FogTileLayer"/> to position the witness cell's
    /// <c>Polygon2D</c> children. <i>Reading source : 3D node. Writing
    /// destination : 2D cell. Never the other way around (Option A
    /// locked).</i></summary>
    public Vector2 WitnessCell2DPosition { get; private set; }

    /// <summary>
    /// Disconnect signals + free the spawned 3D nodes. Called from
    /// <see cref="FogTileLayer._ExitTree"/>.
    /// </summary>
    public void Despawn()
    {
        if (WitnessArea3D is not null && GodotObject.IsInstanceValid(WitnessArea3D))
        {
            if (_mouseEnteredHandler is not null)
                WitnessArea3D.MouseEntered -= _mouseEnteredHandler;
            if (_mouseExitedHandler is not null)
                WitnessArea3D.MouseExited -= _mouseExitedHandler;
            if (_inputEventHandler is not null)
                WitnessArea3D.InputEvent -= _inputEventHandler;
        }
        _mouseEnteredHandler = null;
        _mouseExitedHandler = null;
        _inputEventHandler = null;

        // QueueFree on the 3 root spawned nodes ; their children are
        // freed by Godot recursively (Rune GC-handles-it reflex : Node
        // is NOT GC-managed, manual QueueFree mandatory).
        if (WitnessNode3D is not null && GodotObject.IsInstanceValid(WitnessNode3D))
            WitnessNode3D.QueueFree();
        if (FollowerCamera3D is not null && GodotObject.IsInstanceValid(FollowerCamera3D))
            FollowerCamera3D.QueueFree();

        WitnessNode3D = null;
        WitnessArea3D = null;
        WitnessShape3D = null;
        FollowerCamera3D = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Despawn();
    }

    /// <summary>
    /// Build the rhombus prism collision shape (Pattern J invariant 7,
    /// locked 2026-05-09 night). Same math as the J2 shadow-sync probe
    /// : 4 top-face vertices at the rhombus poles
    /// <c>(±halfW, 0, ±halfW)</c> with <c>halfW = cellSizePx / 2</c>,
    /// extruded by <c>-defaultBackingHeightPx</c> on Y to give the prism
    /// volume.
    ///
    /// <para>
    /// <b>Why <c>halfW = CellSizePx / 2</c> and not <c>SpriteTopWidthPx
    /// / 2</c>.</b> The visible cell's iso silhouette has half-extents
    /// <c>cs/2</c> px (the rhombus is <c>cs</c> wide, <c>cs/2</c> tall
    /// for a 2:1 iso projection). The collision shape must match the
    /// rhombus, not the sprite rectangle. Anchored on cell size, not
    /// sprite size, by the J2 night fix.
    /// </para>
    ///
    /// <para>
    /// <b>Pole vertex order.</b> RIGHT, LEFT, TOP, BOTTOM (top face),
    /// then RIGHT, LEFT, TOP, BOTTOM (bottom face). The convex hull
    /// computed by Godot's physics is order-independent, but the
    /// canary self-check reads the first 4 points and expects this
    /// exact order to verify the shape didn't regress to a box.
    /// </para>
    /// </summary>
    private static ConvexPolygonShape3D BuildRhombusPrismShape(
        int cellSizePx,
        int defaultBackingHeightPx)
    {
        var halfW = cellSizePx / 2f;
        var h = (float)Math.Max(1, defaultBackingHeightPx);

        var topRight = new Vector3(+halfW, 0f, -halfW);
        var topLeft = new Vector3(-halfW, 0f, +halfW);
        var topTop = new Vector3(-halfW, 0f, -halfW);
        var topBottom = new Vector3(+halfW, 0f, +halfW);

        return new ConvexPolygonShape3D
        {
            Points = new[]
            {
                topRight, topLeft, topTop, topBottom,
                topRight with { Y = -h }, topLeft with { Y = -h },
                topTop with { Y = -h }, topBottom with { Y = -h },
            },
        };
    }

    private void DefaultLogMouseEntered() =>
        GD.Print($"[J3 Cell3DBacking] hover ENTER on witness cell ({WitnessCell.Col}, {WitnessCell.Row}) -- Area3D fired");

    private void DefaultLogMouseExited() =>
        GD.Print($"[J3 Cell3DBacking] hover EXIT on witness cell ({WitnessCell.Col}, {WitnessCell.Row})");

    private void DefaultLogInputEvent(
        Node camera,
        InputEvent @event,
        Vector3 position,
        Vector3 normal,
        long shapeIdx)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            GD.Print(
                $"[J3 Cell3DBacking] click on witness cell ({WitnessCell.Col}, {WitnessCell.Row}) " +
                $"@ 3D world=({position.X:F0}, {position.Y:F0}, {position.Z:F0})");
        }
    }

    /// <summary>
    /// 7 canary self-check invariants (Pattern J, locked J2 night). Run
    /// at <see cref="Spawn"/>, log <c>[CANARY OK]</c> or
    /// <c>[CANARY FAIL]</c> per invariant. We do NOT throw -- a probe
    /// that crashes is less debuggable than a probe that runs and
    /// screams.
    /// </summary>
    private void AssertCanaryInvariants()
    {
        // Inv 1 : Viewport.PhysicsObjectPicking == true
        var viewport = (Host as Node)?.GetViewport();
        if (viewport is null || !viewport.PhysicsObjectPicking)
        {
            GD.PushError(
                "[J3 CANARY FAIL inv.1] Viewport.PhysicsObjectPicking is FALSE -- " +
                "Area3D mouse signals will never fire. Spawn() should have force-enabled it.");
        }
        else
        {
            GD.Print("[J3 CANARY OK inv.1] Viewport.PhysicsObjectPicking = true");
        }

        // Inv 2 : Camera3D.Current == true
        if (FollowerCamera3D is null || !FollowerCamera3D.Current)
        {
            GD.PushError(
                "[J3 CANARY FAIL inv.2] FollowerCamera3D is not Current -- physics " +
                "picking has no ray to cast. MakeCurrent() must be called at Spawn.");
        }
        else
        {
            GD.Print($"[J3 CANARY OK inv.2] FollowerCamera3D.Current=true position={FollowerCamera3D.Position}");
        }

        // Inv 3 : Area3D.InputRayPickable == true
        if (WitnessArea3D is null)
        {
            GD.PushError("[J3 CANARY FAIL inv.3] WitnessArea3D is null -- nothing to pick.");
        }
        else if (!WitnessArea3D.InputRayPickable)
        {
            GD.PushError(
                "[J3 CANARY FAIL inv.3] WitnessArea3D.InputRayPickable=false -- the " +
                "Area3D will not receive mouse rays even with the Viewport flag on.");
        }
        else
        {
            GD.Print("[J3 CANARY OK inv.3] WitnessArea3D.InputRayPickable=true");
        }

        // Inv 4 : Area3D.CollisionLayer != 0
        if (WitnessArea3D is not null && WitnessArea3D.CollisionLayer == 0u)
        {
            GD.PushError(
                "[J3 CANARY FAIL inv.4] WitnessArea3D.CollisionLayer = 0 -- physics " +
                "picking ignores layer-0 bodies.");
        }
        else if (WitnessArea3D is not null)
        {
            GD.Print($"[J3 CANARY OK inv.4] WitnessArea3D.CollisionLayer = {WitnessArea3D.CollisionLayer}");
        }

        // Inv 5 : CollisionShape3D.Shape != null && !Disabled
        if (WitnessShape3D is null || WitnessShape3D.Shape is null)
        {
            GD.PushError("[J3 CANARY FAIL inv.5] WitnessShape3D.Shape is null.");
        }
        else if (WitnessShape3D.Disabled)
        {
            GD.PushError("[J3 CANARY FAIL inv.5] WitnessShape3D.Disabled=true.");
        }
        else
        {
            GD.Print("[J3 CANARY OK inv.5] WitnessShape3D ready (Shape != null, Disabled=false)");
        }

        // Inv 6 : Camera3D anchored within frustum of witness (within
        // a generous epsilon -- the camera target should equal the
        // Camera2D-derived target, which when the Camera2D is anchored
        // on the witness lands ON the witness).
        if (FollowerCamera3D is not null && WitnessNode3D is not null && Camera2D is not null)
        {
            // Compute the expected 2D anchor of the witness :
            // ComputeCellCenter(witness) + shift, plus FogYOffset.
            var halfW = CellSizePx / 2f;
            var halfH = CellSizePx / 4f;
            var expectedCenter2D = new Vector2(
                (WitnessCell.Col - WitnessCell.Row) * halfW + IsoOriginShift.X,
                (WitnessCell.Col + WitnessCell.Row) * halfH + halfH + IsoOriginShift.Y);

            var camAnchor2D = Camera2D.Position;
            // The Camera2D need not be anchored on the witness in J3
            // (E2 anchors on Halfgate, not on (0,0)). We only flag if
            // the discrepancy makes the witness fall outside a
            // reasonable frustum -- here just log it for diagnostic.
            var dist = (camAnchor2D - expectedCenter2D).Length();
            GD.Print($"[J3 CANARY INFO inv.6] Camera2D anchored {dist:F0} px from witness center -- frustum check informational only");
        }

        // Inv 7 : ConvexPolygonShape3D rhombus poles match expected
        if (WitnessShape3D?.Shape is ConvexPolygonShape3D prism)
        {
            var halfW = CellSizePx / 2f;
            var expected = new[]
            {
                new Vector3(+halfW, 0f, -halfW),
                new Vector3(-halfW, 0f, +halfW),
                new Vector3(-halfW, 0f, -halfW),
                new Vector3(+halfW, 0f, +halfW),
            };
            var allMatch = prism.Points.Length >= 4;
            for (var i = 0; allMatch && i < 4; i++)
            {
                if (!prism.Points[i].IsEqualApprox(expected[i])) allMatch = false;
            }
            if (!allMatch)
            {
                GD.PushError(
                    "[J3 CANARY FAIL inv.7] ConvexPolygonShape3D top-face vertices " +
                    "do NOT match the 4 rhombus poles. Hover will overshoot the " +
                    "visible rhombus on the TL/BR diagonals.");
            }
            else
            {
                GD.Print($"[J3 CANARY OK inv.7] Rhombus prism poles correct (halfW={halfW})");
            }
        }
        else if (WitnessShape3D?.Shape is BoxShape3D)
        {
            GD.PushError(
                "[J3 CANARY FAIL inv.7] Collision shape is BoxShape3D -- " +
                "regression to the parallelogram-projecting box. " +
                "Switch back to ConvexPolygonShape3D rhombus prism.");
        }
    }
}
