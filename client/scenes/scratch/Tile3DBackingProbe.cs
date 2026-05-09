using Godot;

namespace Wayfinders.Client.Scenes.Scratch;

/// <summary>
/// Jalon 1 du pivot 3D backing — scratch scene root that wires a single
/// witness tile (one <c>Sprite3D</c> + one <c>Area3D</c> + one
/// <c>CollisionShape3D</c> with a <c>BoxShape3D</c>) and proves three
/// patterns in isolation:
/// <list type="bullet">
///   <item><b>Pattern A</b> — "le 3D porte, le 2D peint" : the
///         <c>Sprite3D</c> child is the visible pixel ; the <c>Area3D</c>
///         sibling carries the click zone. Two distinct nodes, two
///         distinct responsibilities. The player perceives the sprite ;
///         the engine arbitrates the click.</item>
///   <item><b>Pattern C</b> — "hauteur dérivée de l'asset" : the
///         <c>BoxShape3D.size.y</c> is computed from
///         <see cref="BackingData"/>'s <c>BackingHeightPx</c>. Swap the
///         <c>.tres</c> reference in the inspector ; reload the scene ;
///         the click volume rebuilds. No C# rebuild.</item>
///   <item><b>Signal-driven pickup</b> — hover and click are routed via
///         <c>Area3D.MouseEntered</c>, <c>Area3D.MouseExited</c>, and
///         <c>Area3D.InputEvent</c>. No inverse projection math, no
///         ray-cast plumbing — Godot's collision system already does
///         that work, and we let it.</item>
/// </list>
///
/// <para>
/// <b>Strict isolation.</b> This scene touches no production node :
/// <c>E2WorldMap</c>, <c>FogTileLayer</c>, <c>MapPan2DComponent</c>,
/// <c>FogTileGridLogic</c>, the autoloads, the simulation grid — all
/// untouched. The probe is fully reversible : delete the
/// <c>scenes/scratch/</c> folder and the production behavior is bit-for-bit
/// identical to before Jalon 1.
/// </para>
///
/// <para>
/// <b>Why <c>Sprite3D</c> and not <c>Sprite2D</c>.</b> Production keeps
/// 2D-painted sprites — the visual DNA lock 2026-05-07 stands. But for
/// the Jalon 1 probe we want the sprite and the collision in <i>the
/// same coordinate space</i> so the demo is honest : the click volume
/// hugs the sprite's silhouette, no 2D/3D projection-sync trick needed.
/// <c>Sprite3D</c> is Godot's built-in answer to "show a 2D texture in
/// the 3D world without writing a shader" — it is a single textured
/// quad that lives in 3D coords. The Jalon 2 question
/// <i>"do we keep the visible sprite as a 2D <c>Sprite2D</c> overlay
/// synchronized with a 3D <c>Area3D</c>, or do we move both into a
/// single 3D scene with <c>Sprite3D</c> for the visible pixel ?"</i> is
/// explicitly deferred to the camera/architecture jalon. The probe
/// shows that the <c>Sprite3D</c> path works ; it does not yet rule on
/// production rendering.
/// </para>
///
/// <para>
/// <b>Camera.</b> The probe owns a dedicated <c>Camera3D</c> in
/// orthogonal projection at the iso angle (30° pitch, -45° yaw — the
/// standard 2:1 iso pose). The camera transform is set at
/// <c>_Ready</c> in C# rather than authored in the .tscn so the
/// rotation matrix stays human-readable (Euler angles in code) and
/// there is one place to fiddle if Didier wants to test other angles
/// before Jalon 2's camera lock. This is a probe-camera, not a
/// production-camera proposal.
/// </para>
///
/// <para>
/// <b>Sprite orientation.</b> A bare <c>Sprite3D</c> stands upright in
/// the X-Y plane (default Godot orientation, like a billboard).
/// Wayfinders renders top-down iso, so we pitch the sprite -90° around
/// X at <c>_Ready</c> to lay it flat on the ground (X-Z plane facing
/// +Y). Same trick we'd use for any "card on a table" 3D layout.
/// </para>
///
/// <para>
/// <b>Gotcha — Viewport.PhysicsObjectPicking.</b> Godot's <c>Area3D</c>
/// mouse signals only fire when the active viewport has
/// <c>physics_object_picking = true</c>. The flag is OFF by default —
/// quietly. We force it on at <c>_Ready</c> here so the probe works
/// out of the box ; production will need the same toggle on whichever
/// viewport ends up hosting the 3D backing layer (Jalon 2 question).
/// </para>
/// </summary>
public partial class Tile3DBackingProbe : Node3D
{
    /// <summary>
    /// Tile geometry data driving the <c>BoxShape3D</c> dimensions.
    /// Authored as <c>.tres</c> in <c>scenes/scratch/</c>. Two witness
    /// resources are committed alongside the probe scene :
    /// <c>tile_backing_thin.tres</c> (h=2) and
    /// <c>tile_backing_thick.tres</c> (h=6) — matching Didier's spec
    /// 2026-05-09 §1.
    /// </summary>
    [Export] public TileBackingData BackingData { get; set; } = new();

    /// <summary>
    /// The visible sprite — a textured 2D quad living in 3D coords
    /// (Sprite3D). Wired by the .tscn scene tree, fetched by node
    /// path at <c>_Ready</c>.
    /// </summary>
    [Export] public NodePath SpritePath { get; set; } = "TileSprite3D";

    /// <summary>
    /// The click zone. Owns <c>MouseEntered</c> / <c>MouseExited</c> /
    /// <c>InputEvent</c> signals. Wired by the .tscn ; we connect at
    /// <c>_Ready</c> and disconnect at <c>_ExitTree</c>.
    /// </summary>
    [Export] public NodePath BackingAreaPath { get; set; } = "TileBackingArea3D";

    /// <summary>
    /// The shape carrier. We resize its <c>BoxShape3D</c> at
    /// <c>_Ready</c> from <see cref="BackingData"/>.
    /// </summary>
    [Export] public NodePath BackingShapePath { get; set; } = "TileBackingArea3D/BackingShape3D";

    /// <summary>
    /// The probe-owned camera. Set up to ortho-iso at <c>_Ready</c>.
    /// </summary>
    [Export] public NodePath CameraPath { get; set; } = "Camera3D";

    /// <summary>
    /// Camera ortho size — the vertical extent (in world units, which
    /// here equal pixels) the camera covers. 600 keeps a 256-px tile
    /// comfortably visible with margin.
    /// </summary>
    [Export] public float CameraOrthoSize { get; set; } = 600f;

    /// <summary>
    /// Camera distance from origin along the iso vector. Doesn't
    /// affect ortho rendering scale (ortho is depth-invariant) but
    /// keeps the camera outside the box and outside any future
    /// gizmo clutter.
    /// </summary>
    [Export] public float CameraDistance { get; set; } = 1500f;

    /// <summary>
    /// Camera pitch — angle below horizontal. 30° is the classic 2:1
    /// iso projection pitch (mathematically pure 2:1 is
    /// atan(0.5) ≈ 26.57° ; 30° is the convention most game engines
    /// ship with and is what we'll match for visual consistency).
    /// </summary>
    [Export] public float CameraPitchDegrees { get; set; } = 30f;

    /// <summary>
    /// Camera yaw. -45° aligns the X+ and Z+ axes diagonally onto
    /// screen-space, the standard iso pose.
    /// </summary>
    [Export] public float CameraYawDegrees { get; set; } = -45f;

    /// <summary>
    /// Hover tint applied to the sprite while the cursor is over the
    /// area. Multiplied into <c>Sprite3D.Modulate</c>. Warm parchment
    /// glow — keeps the visual DNA palette.
    /// </summary>
    [Export] public Color HoverModulate { get; set; } = new(1.20f, 1.10f, 0.90f, 1.0f);

    /// <summary>
    /// Selected tint applied to the sprite after a left-click. Stronger
    /// terracotta accent ; persists until another click.
    /// </summary>
    [Export] public Color SelectedModulate { get; set; } = new(1.40f, 0.85f, 0.60f, 1.0f);

    /// <summary>
    /// Default sprite modulate (no hover, no selection). Pure white so
    /// the bitmap renders untinted.
    /// </summary>
    [Export] public Color IdleModulate { get; set; } = new(1f, 1f, 1f, 1f);

    private Sprite3D? _sprite;
    private Area3D? _backingArea;
    private CollisionShape3D? _backingShape;
    private Camera3D? _camera;

    private bool _hovering;
    private bool _selected;

    // Cached signal handlers so we can disconnect cleanly at _ExitTree.
    private System.Action? _mouseEnteredHandler;
    private System.Action? _mouseExitedHandler;
    private Area3D.InputEventEventHandler? _inputEventHandler;

    public override void _Ready()
    {
        // Mandatory for Area3D mouse signals to fire. Default false on
        // the root viewport — silent failure mode if forgotten. See
        // class-level "Gotcha" docstring.
        var viewport = GetViewport();
        if (viewport is not null && !viewport.PhysicsObjectPicking)
        {
            viewport.PhysicsObjectPicking = true;
            GD.Print("[Tile3DBackingProbe] enabled Viewport.PhysicsObjectPicking");
        }

        _sprite = GetNode<Sprite3D>(SpritePath);
        _backingArea = GetNode<Area3D>(BackingAreaPath);
        _backingShape = GetNode<CollisionShape3D>(BackingShapePath);
        _camera = GetNode<Camera3D>(CameraPath);

        ApplyBackingGeometry();
        ApplyCameraTransform();
        ApplySpriteTransform();

        _mouseEnteredHandler = OnMouseEntered;
        _mouseExitedHandler = OnMouseExited;
        _inputEventHandler = OnInputEvent;

        _backingArea.MouseEntered += _mouseEnteredHandler;
        _backingArea.MouseExited += _mouseExitedHandler;
        _backingArea.InputEvent += _inputEventHandler;

        ApplySpriteModulate();

        GD.Print(
            $"[Tile3DBackingProbe] ready: sprite_h={BackingData.SpritePixelHeight}px " +
            $"(top={BackingData.SpriteTopHeightPx}px, w={BackingData.SpriteTopWidthPx}px) " +
            $"→ box.y={BackingData.BackingHeightPx}px " +
            $"(swap BackingData .tres in inspector to test thin/thick)");
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Rune §"signals connected, never
        // disconnected, then publisher freed → subtle leaks").
        // Area3D outlives this node only when the scene reload races
        // the tree exit ; defensive disconnection costs nothing.
        if (_backingArea is not null)
        {
            if (_mouseEnteredHandler is not null)
                _backingArea.MouseEntered -= _mouseEnteredHandler;
            if (_mouseExitedHandler is not null)
                _backingArea.MouseExited -= _mouseExitedHandler;
            if (_inputEventHandler is not null)
                _backingArea.InputEvent -= _inputEventHandler;
        }

        _mouseEnteredHandler = null;
        _mouseExitedHandler = null;
        _inputEventHandler = null;
    }

    /// <summary>
    /// Resize the <c>BoxShape3D</c> from the current
    /// <see cref="BackingData"/>. Pattern C — hauteur dérivée de
    /// l'asset, pas codée en dur. Idempotent ; safe to call again
    /// after a hot-swap of the resource.
    /// </summary>
    private void ApplyBackingGeometry()
    {
        if (_backingShape is null)
        {
            GD.PushWarning("[Tile3DBackingProbe] ApplyBackingGeometry: shape not resolved");
            return;
        }

        var box = _backingShape.Shape as BoxShape3D ?? new BoxShape3D();

        // X / Z = top footprint. Y = derived backing height. Half-extents
        // are NOT applied here — Godot's BoxShape3D.size is the full
        // edge-to-edge extent.
        var w = (float)BackingData.SpriteTopWidthPx;
        var h = (float)BackingData.BackingHeightPx;
        // Iso losange : the visible top is twice as wide as it is tall
        // in screen pixels. In 3D world space the playable surface is a
        // square (width = depth). We pick depth = width / 2 so the
        // overhead view of the box aligns with the iso losange's screen
        // footprint after the 30° camera pitch — an intentional cheat
        // for the probe ; Jalon 2 will revisit when we lock the iso
        // projection invariant in 3D.
        var d = w * 0.5f;

        box.Size = new Vector3(w, h, d);
        _backingShape.Shape = box;

        // Center the box vertically so its TOP sits at y=0 (the
        // sprite's playable surface). The skirt hangs below into
        // negative Y. Click zone hugs the visual top.
        _backingShape.Position = new Vector3(0f, -h * 0.5f, 0f);
    }

    /// <summary>
    /// Position the camera at <see cref="CameraDistance"/> along the
    /// iso vector and aim it at the origin. Set ortho projection +
    /// size. Done in code so the iso angles stay readable as Euler
    /// floats — authoring a 30°/-45° rotation matrix in .tscn float
    /// soup is harder to verify by eye.
    /// </summary>
    private void ApplyCameraTransform()
    {
        if (_camera is null)
        {
            GD.PushWarning("[Tile3DBackingProbe] ApplyCameraTransform: camera not resolved");
            return;
        }

        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = CameraOrthoSize;
        _camera.Near = 0.1f;
        _camera.Far = CameraDistance * 4f;

        // Compute iso position : start from origin, walk distance D
        // along a vector pitched up by `pitch` and yawed by `yaw`.
        var pitch = Mathf.DegToRad(CameraPitchDegrees);
        var yaw = Mathf.DegToRad(CameraYawDegrees);
        var horiz = Mathf.Cos(pitch) * CameraDistance;
        var vert = Mathf.Sin(pitch) * CameraDistance;
        var pos = new Vector3(
            horiz * Mathf.Sin(-yaw),
            vert,
            horiz * Mathf.Cos(-yaw));
        _camera.Position = pos;
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    /// <summary>
    /// Lay the Sprite3D flat on the X-Z plane (facing +Y), so the
    /// camera looking down the iso vector sees the texture at the
    /// expected orientation. Without this rotation, the sprite stands
    /// upright (default Godot orientation) and the camera sees its
    /// edge.
    /// </summary>
    private void ApplySpriteTransform()
    {
        if (_sprite is null) return;
        _sprite.Rotation = new Vector3(Mathf.DegToRad(-90f), 0f, 0f);
        _sprite.Position = Vector3.Zero;
    }

    private void OnMouseEntered()
    {
        _hovering = true;
        ApplySpriteModulate();
        GD.Print("[Tile3DBackingProbe] hover enter");
    }

    private void OnMouseExited()
    {
        _hovering = false;
        ApplySpriteModulate();
        GD.Print("[Tile3DBackingProbe] hover exit");
    }

    private void OnInputEvent(
        Node camera,
        InputEvent @event,
        Vector3 position,
        Vector3 normal,
        long shapeIdx)
    {
        if (@event is InputEventMouseButton mb
            && mb.Pressed
            && mb.ButtonIndex == MouseButton.Left)
        {
            _selected = !_selected;
            ApplySpriteModulate();
            GD.Print(
                $"[Tile3DBackingProbe] click at world={position} " +
                $"(selected={_selected}, hit normal={normal})");
        }
    }

    private void ApplySpriteModulate()
    {
        if (_sprite is null) return;

        // Selected wins over hover ; both tints stack via Sprite3D's
        // built-in Modulate channel (no shader required).
        if (_selected)
            _sprite.Modulate = SelectedModulate;
        else if (_hovering)
            _sprite.Modulate = HoverModulate;
        else
            _sprite.Modulate = IdleModulate;
    }
}
