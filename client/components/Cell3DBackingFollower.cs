using System;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Components;

/// <summary>
/// Wayfinders 3D Backing Architecture (doc 2026-05-09, locked) -- a
/// single cell's 3D-authoritative backing. Each instance owns one
/// <see cref="Node3D"/> witness (the spatial truth), one
/// <see cref="Area3D"/> pickup volume with a <see cref="ConvexPolygonShape3D"/>
/// rhombus prism, and exposes a derived 2D screen position via
/// <see cref="WitnessCell2DPosition"/>.
///
/// <para>
/// <b>J4 refactor (2026-05-09 evening, locked).</b> This component used
/// to also own the <see cref="Camera3D"/> follower, which made it
/// impossible to instantiate per-cell at scale -- 600 cells would have
/// meant 600 Camera3D in the scene tree. The Camera3D is now hosted by
/// <see cref="Camera3DShadowFollower"/> at the <see cref="FogTileLayer"/>
/// level (one per layer), and the <see cref="Cell3DBackingFollower"/> is
/// reduced to its essential per-cell role : witness Node3D + Area3D +
/// shape + forward iso projection. Pattern M (new J4) -- "extract
/// singletons before generalising".
/// </para>
///
/// <para>
/// <b>What this component pins.</b>
/// <list type="bullet">
///   <item>The cell's <i>spatial</i> position lives on
///         <see cref="WitnessNode3D"/> at world coords
///         <c>(col*cs, 0, row*cs)</c>. The 2D consumer reads
///         <see cref="WitnessCell2DPosition"/> after each
///         <see cref="ApplySync"/> ; no other source of truth.</item>
///   <item>The pickup volume is a 3D <see cref="Area3D"/> with a
///         <see cref="ConvexPolygonShape3D"/> rhombus prism (Pattern J
///         invariant 7) anchored at the same 3D world point. Hover and
///         click events fire through Godot physics picking ; the 2D
///         <c>WorldPositionToCell</c> path on
///         <see cref="FogTileLayer"/> stays as a fallback for the F debug
///         command and the drill resolver.</item>
///   <item>Forward iso projection is the J2-pinned math (
///         <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.IsoProjection3DTo2DTests"/>
///         and <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.J3CellAuthorityTests"/>) :
///         <c>screen2D = (X - Z) / 2 (X), (X + Z) / 4 (Y) + shift + halfH + fogYOffset</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>J4 dirty-bit optimisation (2026-05-09).</b> J4 introduces hundreds
/// of followers running per-frame. The forward projection itself is
/// cheap, but the 2D <see cref="Vector2"/> assignment to <see cref="CellNode2D"/>
/// crosses the C++/C# marshalling boundary -- not free. Most cells are
/// static (J4 scope, J6+ mutates them only during animations), so we
/// guard the per-frame work with <see cref="IsPositionDirty"/> and
/// short-circuit when the witness Node3D position has not moved since
/// the last sync. After an initial spawn the dirty bit is cleared and
/// every subsequent frame is a single bool read per cell. <b>Pattern N
/// (new J4) -- "Dirty-bit gate on per-frame marshalling crossings"</b>.
/// </para>
///
/// <para>
/// <b>Spawn protocol (entirely code-driven, no .tscn).</b>
/// <list type="number">
///   <item>The owner (<see cref="FogTileLayer"/>) calls <see cref="Spawn"/>
///         with the host Node, the witness coord, the cell size, the
///         iso shift, the fog Y offset, and optional hover callbacks.</item>
///   <item>Spawn instantiates <see cref="WitnessNode3D"/> +
///         <see cref="WitnessArea3D"/> + <see cref="WitnessShape3D"/>,
///         positions Witness at <c>(col*cs, 0, row*cs)</c>, builds the
///         rhombus prism, connects the Area3D signals to the supplied
///         callbacks (handlers captured in fields for disconnection at
///         <see cref="Despawn"/>).</item>
///   <item>An initial <see cref="ApplySync"/> seeds
///         <see cref="WitnessCell2DPosition"/> from the static Node3D
///         position. The dirty bit is cleared after this seed -- next
///         frame is a no-op unless something mutates the Node3D position.</item>
///   <item><see cref="Despawn"/> at layer <c>_ExitTree</c> disconnects
///         signals (Rune disconnection discipline) and frees the
///         spawned Nodes (Rune GC-handles-it reflex -- Node is not
///         GC-managed).</item>
/// </list>
/// </para>
/// </summary>
public sealed class Cell3DBackingFollower : IDisposable
{
    /// <summary>The host node that owns the spawned 3D nodes.</summary>
    public Node Host { get; }

    /// <summary>The 3D-authoritative witness cell node.</summary>
    public Node3D? WitnessNode3D { get; private set; }

    /// <summary>The witness Area3D (pickup volume).</summary>
    public Area3D? WitnessArea3D { get; private set; }

    /// <summary>The witness CollisionShape3D wrapping the rhombus prism.</summary>
    public CollisionShape3D? WitnessShape3D { get; private set; }

    /// <summary>The 2D Cell node bound to this follower (set by the
    /// owner via <see cref="BindCellNode2D"/>). When non-null, the
    /// follower writes <see cref="WitnessCell2DPosition"/> directly to
    /// this node's <c>Position</c> on each <see cref="ApplySync"/>.
    /// When null, the owner is responsible for reading
    /// <see cref="WitnessCell2DPosition"/> and applying it (J3 path).</summary>
    public Node2D? CellNode2D { get; private set; }

    /// <summary>The cell coord this follower backs.</summary>
    public GridCoord WitnessCell { get; }

    /// <summary>The cell size in pixels.</summary>
    public int CellSizePx { get; }

    /// <summary>The iso origin shift baked into 2D positions.</summary>
    public PanVec2 IsoOriginShift { get; }

    /// <summary>The static Y offset for the floating-tile effect.</summary>
    public float FogYOffset { get; }

    /// <summary>The witness's 2D screen position derived from
    /// <see cref="WitnessNode3D"/>'s position via forward iso projection.
    /// Updated by <see cref="ApplySync"/> when the dirty bit is set ;
    /// stable between syncs. Reading direction : 3D node is source, 2D
    /// position is derived -- never the other way (Option A locked).</summary>
    public Vector2 WitnessCell2DPosition { get; private set; }

    /// <summary>True when <see cref="WitnessNode3D"/> has been mutated
    /// since the last sync. Set by <see cref="MarkDirty"/> ; cleared at
    /// the end of <see cref="ApplySync"/>. J4 optimisation : static
    /// followers stay clean and skip per-frame work.</summary>
    public bool IsPositionDirty { get; private set; }

    private Action? _mouseEnteredHandler;
    private Action? _mouseExitedHandler;
    private Area3D.InputEventEventHandler? _inputEventHandler;
    private Vector3 _lastSyncedNode3DPosition;

    private bool _disposed;

    public Cell3DBackingFollower(
        Node host,
        GridCoord witnessCell,
        int cellSizePx,
        PanVec2 isoOriginShift,
        float fogYOffset)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        WitnessCell = witnessCell;
        CellSizePx = cellSizePx;
        IsoOriginShift = isoOriginShift;
        FogYOffset = fogYOffset;
    }

    /// <summary>
    /// Spawn the 3D backing nodes : Witness Node3D + Area3D +
    /// ConvexPolygonShape3D rhombus prism. Connects the Area3D signals
    /// to the supplied callbacks. Runs an initial <see cref="ApplySync"/>
    /// to seed <see cref="WitnessCell2DPosition"/>.
    /// </summary>
    public void Spawn(
        Action? onMouseEntered = null,
        Action? onMouseExited = null,
        Area3D.InputEventEventHandler? onInputEvent = null,
        int defaultBackingHeightPx = 6)
    {
        // -- Witness Node3D at the iso world coord (col*cs, 0, row*cs).
        WitnessNode3D = new Node3D
        {
            Name = $"J4Witness3D_Cell_{WitnessCell.Col}_{WitnessCell.Row}",
            Position = new Vector3(
                WitnessCell.Col * CellSizePx,
                0f,
                WitnessCell.Row * CellSizePx),
        };
        Host.AddChild(WitnessNode3D);

        // -- Area3D pickup volume.
        WitnessArea3D = new Area3D
        {
            Name = "J4Area3D",
            InputRayPickable = true,
            CollisionLayer = 1u,
            CollisionMask = 1u,
        };
        WitnessNode3D.AddChild(WitnessArea3D);

        WitnessShape3D = new CollisionShape3D
        {
            Name = "J4Shape3D",
            Shape = BuildRhombusPrismShape(CellSizePx, defaultBackingHeightPx),
        };
        WitnessArea3D.AddChild(WitnessShape3D);

        // Capture handlers in fields for clean Despawn disconnect (Rune
        // disconnection discipline -- the publisher is freed at
        // _ExitTree, so we MUST detach before that or risk leaks).
        _mouseEnteredHandler = onMouseEntered;
        _mouseExitedHandler = onMouseExited;
        _inputEventHandler = onInputEvent;

        if (_mouseEnteredHandler is not null)
            WitnessArea3D.MouseEntered += _mouseEnteredHandler;
        if (_mouseExitedHandler is not null)
            WitnessArea3D.MouseExited += _mouseExitedHandler;
        if (_inputEventHandler is not null)
            WitnessArea3D.InputEvent += _inputEventHandler;

        // Seed the 2D-derived position. Mark dirty so the first sync
        // computes a value, then clears the bit -- subsequent frames
        // are no-ops unless something mutates the Node3D position.
        IsPositionDirty = true;
        ApplySync();
    }

    /// <summary>
    /// Bind a 2D <see cref="Node2D"/> as the consumer of this follower's
    /// derived position. After binding, <see cref="ApplySync"/> writes
    /// directly to the bound node's <c>Position</c>. Pass <c>null</c>
    /// to unbind (consumer reads <see cref="WitnessCell2DPosition"/>
    /// manually -- the J3 single-cell path).
    /// </summary>
    public void BindCellNode2D(Node2D? cell)
    {
        CellNode2D = cell;
    }

    /// <summary>
    /// Mark this follower as dirty. The next <see cref="ApplySync"/>
    /// will recompute and re-write <see cref="WitnessCell2DPosition"/>.
    /// Called by anything that mutates <see cref="WitnessNode3D.Position"/>.
    /// </summary>
    public void MarkDirty() => IsPositionDirty = true;

    /// <summary>
    /// Per-frame sync. Compute the witness's 2D screen position from
    /// the 3D Node3D position via forward iso projection ; if a 2D Cell
    /// node is bound, write the position to it. Skipped when
    /// <see cref="IsPositionDirty"/> is false (J4 optimisation -- static
    /// cells contribute zero work per frame after the initial seed).
    ///
    /// <para>
    /// <b>Why a dirty-bit instead of always recomputing.</b> The math
    /// is cheap (5 multiplications) but the C++/C# marshalling on
    /// <c>Node2D.Position = ...</c> is one boundary call per frame per
    /// cell. At 600 cells × 60 fps that is 36 000 marshalled writes per
    /// second -- worth measuring before paying. The dirty bit collapses
    /// this to zero for static cells.
    /// </para>
    /// </summary>
    public void ApplySync()
    {
        if (WitnessNode3D is null) return;
        var p = WitnessNode3D.Position;

        // Belt-and-braces : even if the dirty bit was not flipped, the
        // Node3D position itself is the canonical input. If a future
        // mutation forgets to call MarkDirty(), the position-equality
        // check catches it. Cheap (3 float compares).
        if (!IsPositionDirty && p == _lastSyncedNode3DPosition) return;

        var screenX = (p.X - p.Z) / 2f;
        var screenY = (p.X + p.Z) / 4f;
        var halfH = CellSizePx / 4f;
        WitnessCell2DPosition = new Vector2(
            screenX + IsoOriginShift.X,
            screenY + IsoOriginShift.Y + halfH + FogYOffset);

        if (CellNode2D is not null && GodotObject.IsInstanceValid(CellNode2D))
        {
            CellNode2D.Position = WitnessCell2DPosition;
        }

        _lastSyncedNode3DPosition = p;
        IsPositionDirty = false;
    }

    /// <summary>
    /// Disconnect signals + free the spawned 3D nodes.
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

        if (WitnessNode3D is not null && GodotObject.IsInstanceValid(WitnessNode3D))
            WitnessNode3D.QueueFree();

        WitnessNode3D = null;
        WitnessArea3D = null;
        WitnessShape3D = null;
        CellNode2D = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Despawn();
    }

    /// <summary>
    /// Build the rhombus prism collision shape (Pattern J invariant 7,
    /// locked 2026-05-09 night). 4 top-face vertices at the rhombus
    /// poles <c>(±halfW, 0, ±halfW)</c> with <c>halfW = cellSizePx / 2</c>,
    /// extruded by <c>-defaultBackingHeightPx</c> on Y to give the prism
    /// volume. The convex hull computed by Godot's physics is order-
    /// independent, but the canary self-check expects this exact pole
    /// order to verify the shape didn't regress to a box.
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
}
