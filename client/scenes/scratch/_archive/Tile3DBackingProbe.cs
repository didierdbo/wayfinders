using Godot;
using System.Collections.Generic;

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
/// <c>E1WorldMap</c>, <c>FogTileLayer</c>, <c>MapPan2DComponent</c>,
/// <c>FogTileGridLogic</c>, the autoloads, the simulation grid — all
/// untouched. The probe is fully reversible : delete the
/// <c>scenes/scratch/</c> folder and the production behavior is bit-for-bit
/// identical to before Jalon 1.
/// </para>
///
/// <para>
/// <b>Debug-shaped UX (revisited 2026-05-09 after the first Didier
/// playtest).</b> The first cut of this probe used the production
/// parchment palette (skybox parchment background, subtle warm-tint
/// hover/select) and an ambient <c>card_1.png</c> asset. Didier's
/// reaction : "j'ai testé, mais je comprends pas ce qui se passe…
/// quelle teinte parchemin ? je clique où ? je vois les mêmes tuiles
/// qu'avant". Lesson : a debug probe must be aggressively distinct
/// from the production palette. This rev :
/// <list type="bullet">
///   <item>flat dark technical background (no skybox, no parchment hue)
///         so the witness tile is unmistakable ;</item>
///   <item>procedurally-painted witness texture (parchment fill with a
///         vivid green border + "TILE" label baked in) so it cannot be
///         confused with a production iso atlas tile ;</item>
///   <item>saturated hover (vivid green) and click (vivid red) tints —
///         we are debugging, not shipping ;</item>
///   <item>red wireframe box drawn over the <c>BoxShape3D</c> volume so
///         Didier sees the click volume superimposed on the painted
///         pixel ;</item>
///   <item>on-screen <c>CanvasLayer</c> overlay with title, step-by-step
///         instructions, live STATUS readout, and a live mirrored log
///         panel — no need to hunt the Godot Output panel.</item>
/// </list>
/// The patterns A / C / signal-pickup are unchanged ; only the
/// debug skin is swapped for clarity.
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
    /// Wireframe debug visualisation of the <c>BoxShape3D</c> volume —
    /// 12 red line-segments showing the click zone superimposed on the
    /// painted pixel. Lets Didier <i>see</i> the 3D collision volume.
    /// </summary>
    [Export] public NodePath WireframeBoxPath { get; set; } = "WireframeBox";

    /// <summary>Overlay live status label — reads "STATUS: idle / hovering / SELECTED".</summary>
    [Export] public NodePath StatusLabelPath { get; set; } = "OverlayUI/OverlayRoot/StatusLabel";

    /// <summary>Overlay live mirrored log — last few lines of GD.Print output.</summary>
    [Export] public NodePath LiveLogLabelPath { get; set; } = "OverlayUI/OverlayRoot/LiveLogLabel";

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
    /// Camera pitch — angle below horizontal.
    /// <para>
    /// <b>30° is numerically validated</b> as the camera Euler
    /// angle that makes a 3D ortho projection match the 2D iso math
    /// in <see cref="Wayfinders.Client.Scripts.Screens.FogTileGridLogic"/>
    /// (see <c>client/tests/Phase9_3DBacking/IsoProjection3DTo2DTests.cs</c>).
    /// The identity is <c>sin(pitch) = halfH / halfW = 1/2</c>, so
    /// <c>pitch = arcsin(0.5) = 30°</c>. The widely-quoted
    /// <c>atan(0.5) ≈ 26.57°</c> is the on-screen incidence angle of a
    /// 2:1 iso projection, <i>not</i> the camera Euler pitch -- two
    /// equivalent descriptions of the same iso, parameterised
    /// differently. The pin test guards against the natural reflex of
    /// substituting one for the other.
    /// </para>
    /// </summary>
    [Export] public float CameraPitchDegrees { get; set; } = 30f;

    /// <summary>
    /// Camera yaw. -45° aligns the X+ and Z+ axes diagonally onto
    /// screen-space, the standard iso pose.
    /// </summary>
    [Export] public float CameraYawDegrees { get; set; } = -45f;

    /// <summary>
    /// Hover tint applied to the sprite while the cursor is over the
    /// area. <b>Saturated debug green</b> — we want this to be
    /// unmistakable, not subtle. Production will swap to a
    /// palette-aligned tint at the integration jalon.
    /// </summary>
    [Export] public Color HoverModulate { get; set; } = new(0.30f, 1.00f, 0.30f, 1.0f);

    /// <summary>
    /// Selected tint applied to the sprite after a left-click.
    /// <b>Saturated debug red</b> — toggles on / off on each click.
    /// Production will swap.
    /// </summary>
    [Export] public Color SelectedModulate { get; set; } = new(1.00f, 0.30f, 0.30f, 1.0f);

    /// <summary>
    /// Default sprite modulate (no hover, no selection). Pure white so
    /// the bitmap renders untinted.
    /// </summary>
    [Export] public Color IdleModulate { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Wireframe box edge colour. Red so it pops against the parchment
    /// witness texture and the dark technical background.
    /// </summary>
    [Export] public Color WireframeColor { get; set; } = new(1.0f, 0.20f, 0.20f, 1.0f);

    private Sprite3D? _sprite;
    private Area3D? _backingArea;
    private CollisionShape3D? _backingShape;
    private Camera3D? _camera;
    private MeshInstance3D? _wireframeBox;
    private Label? _statusLabel;
    private Label? _liveLogLabel;

    private bool _hovering;
    private bool _selected;

    private const int LiveLogMaxLines = 10;
    private readonly Queue<string> _liveLog = new();

    // Cached signal handlers so we can disconnect cleanly at _ExitTree.
    private System.Action? _mouseEnteredHandler;
    private System.Action? _mouseExitedHandler;
    private Area3D.InputEventEventHandler? _inputEventHandler;

    public override void _Ready()
    {
        // J1.1 canary log -- printed before anything else so Didier sees
        // it in the Output panel AND, once the LiveLogLabel is wired up,
        // mirrored on screen. Disambiguates "is the probe actually
        // running?" without having to read the scene tree inspector.
        // If you ever see the cartographer's office background instead
        // of the dark probe, the absence of this line in the Output panel
        // tells you the probe scene was not the one launched.
        GD.Print("[PROBE J1.1] scene started, you are looking at the right scene -- Tile3DBackingProbe._Ready entered");

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
        _wireframeBox = GetNodeOrNull<MeshInstance3D>(WireframeBoxPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _liveLogLabel = GetNodeOrNull<Label>(LiveLogLabelPath);

        ApplyWitnessTexture();
        ApplyBackingGeometry();
        ApplyCameraTransform();
        ApplySpriteTransform();
        RebuildWireframeBox();

        _mouseEnteredHandler = OnMouseEntered;
        _mouseExitedHandler = OnMouseExited;
        _inputEventHandler = OnInputEvent;

        _backingArea.MouseEntered += _mouseEnteredHandler;
        _backingArea.MouseExited += _mouseExitedHandler;
        _backingArea.InputEvent += _inputEventHandler;

        ApplySpriteModulate();
        UpdateStatusLabel();

        // Mirror the canary onto the on-screen live log so it is visible
        // even if the Output panel is collapsed (the user's default).
        Log("PROBE J1.1 -- you are looking at Tile3DBackingProbe (the right scene)");

        Log(
            $"ready: sprite_h={BackingData.SpritePixelHeight}px " +
            $"(top={BackingData.SpriteTopHeightPx}px, w={BackingData.SpriteTopWidthPx}px) " +
            $"-> box.y={BackingData.BackingHeightPx}px");
        Log("hover the parchment tile -> GREEN ; click -> RED toggle ; click again -> idle");
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

    /// <summary>
    /// Build a procedural witness texture for the sprite — parchment
    /// fill with a vivid green border and a "TILE" label baked in.
    /// We do <i>not</i> reuse <c>card_1.png</c> or any production iso
    /// atlas tile, because the first playtest showed Didier could not
    /// distinguish the witness from the production map ("je vois les
    /// mêmes tuiles qu'avant"). A bespoke procedural texture removes
    /// that ambiguity and removes a runtime dependency on an asset
    /// path.
    /// </summary>
    private void ApplyWitnessTexture()
    {
        if (_sprite is null) return;

        // Match the BoxShape3D top footprint : 256-wide, 128-tall iso
        // losange. We render a rectangle (not a losange) because
        // Sprite3D bills itself by full quad ; the visible silhouette
        // does not need to be a losange for the probe to make its
        // point.
        var w = BackingData.SpriteTopWidthPx;
        var h = BackingData.SpriteTopHeightPx;
        var image = Image.CreateEmpty(w, h, useMipmaps: false, format: Image.Format.Rgba8);

        var fill = new Color(0.92f, 0.85f, 0.66f, 1f);   // parchment cream
        var border = new Color(0.20f, 0.80f, 0.30f, 1f); // vivid green border
        const int borderPx = 6;

        // Fill parchment.
        image.Fill(fill);

        // Paint border (top, bottom, left, right strips).
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var onBorder =
                    x < borderPx || x >= w - borderPx ||
                    y < borderPx || y >= h - borderPx;
                if (onBorder)
                    image.SetPixel(x, y, border);
            }
        }

        // Diagonal stripes inside the parchment so the tile orientation
        // is unambiguous (no mistaking it for a uniform parchment quad).
        var stripe = new Color(0.78f, 0.62f, 0.40f, 1f);
        for (var y = borderPx; y < h - borderPx; y++)
        {
            for (var x = borderPx; x < w - borderPx; x++)
            {
                if (((x + y) / 16) % 2 == 0)
                    image.SetPixel(x, y, stripe);
            }
        }

        // Bake a chunky "TILE" word using filled rectangles, no font
        // dependency. 5x7-ish pixel typography drawn at scale.
        DrawWord(image, "TILE", x0: w / 2 - 80, y0: h / 2 - 24, scale: 8,
                 ink: new Color(0.10f, 0.10f, 0.10f, 1f));

        var tex = ImageTexture.CreateFromImage(image);
        _sprite.Texture = tex;
        _sprite.PixelSize = 1.0f;
    }

    /// <summary>
    /// Rebuild the red wireframe overlay around the BoxShape3D so the
    /// 3D click volume is literally visible above the sprite.
    /// 12 line-segments (one per box edge), drawn via
    /// <c>ImmediateMesh</c> (the simplest Godot 4 way to draw lines
    /// from C# without spinning up a custom mesh tool).
    /// </summary>
    private void RebuildWireframeBox()
    {
        if (_wireframeBox is null) return;

        var w = (float)BackingData.SpriteTopWidthPx;
        var h = (float)BackingData.BackingHeightPx;
        var d = w * 0.5f;

        var hx = w * 0.5f;
        var hd = d * 0.5f;
        // Box centered vertically with TOP at y=0, mirrors ApplyBackingGeometry.
        var yTop = 0f;
        var yBot = -h;

        // 8 corners.
        var c000 = new Vector3(-hx, yBot, -hd);
        var c100 = new Vector3(+hx, yBot, -hd);
        var c110 = new Vector3(+hx, yBot, +hd);
        var c010 = new Vector3(-hx, yBot, +hd);
        var c001 = new Vector3(-hx, yTop, -hd);
        var c101 = new Vector3(+hx, yTop, -hd);
        var c111 = new Vector3(+hx, yTop, +hd);
        var c011 = new Vector3(-hx, yTop, +hd);

        var mesh = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = WireframeColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);
        AddEdge(mesh, c000, c100);
        AddEdge(mesh, c100, c110);
        AddEdge(mesh, c110, c010);
        AddEdge(mesh, c010, c000);

        AddEdge(mesh, c001, c101);
        AddEdge(mesh, c101, c111);
        AddEdge(mesh, c111, c011);
        AddEdge(mesh, c011, c001);

        AddEdge(mesh, c000, c001);
        AddEdge(mesh, c100, c101);
        AddEdge(mesh, c110, c111);
        AddEdge(mesh, c010, c011);
        mesh.SurfaceEnd();

        _wireframeBox.Mesh = mesh;
        _wireframeBox.Position = Vector3.Zero;
    }

    private static void AddEdge(ImmediateMesh mesh, Vector3 a, Vector3 b)
    {
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
    }

    private void OnMouseEntered()
    {
        _hovering = true;
        ApplySpriteModulate();
        UpdateStatusLabel();
        Log("hover ENTER -- sprite tinted GREEN");
    }

    private void OnMouseExited()
    {
        _hovering = false;
        ApplySpriteModulate();
        UpdateStatusLabel();
        Log("hover EXIT -- sprite back to idle (or RED if selected)");
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
            UpdateStatusLabel();
            Log(
                $"click @ world=({position.X:F0},{position.Y:F0},{position.Z:F0}) " +
                $"selected={_selected}");
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

    private void UpdateStatusLabel()
    {
        if (_statusLabel is null) return;
        var word = _selected ? "SELECTED (red)"
                  : _hovering ? "HOVERING (green)"
                  : "idle (white)";
        _statusLabel.Text =
            $"STATUS: {word}\nbox.y = {BackingData.BackingHeightPx}px " +
            $"(sprite_h={BackingData.SpritePixelHeight}, top={BackingData.SpriteTopHeightPx})";
    }

    /// <summary>
    /// Print to GD.Print (Output panel) AND mirror to the on-screen
    /// LiveLogLabel so Didier sees activity even without the Output
    /// panel open. Keeps the last <see cref="LiveLogMaxLines"/> lines.
    /// </summary>
    private void Log(string message)
    {
        var line = $"[probe] {message}";
        GD.Print(line);

        _liveLog.Enqueue(line);
        while (_liveLog.Count > LiveLogMaxLines)
            _liveLog.Dequeue();

        if (_liveLogLabel is not null)
            _liveLogLabel.Text = string.Join("\n", _liveLog);
    }

    /// <summary>
    /// Tiny baked-in "TILE" stamper. Renders the 4 ASCII letters of
    /// "TILE" as filled rectangles directly onto an <see cref="Image"/>
    /// — avoids depending on a Theme / FontFile / DynamicFont in the
    /// scratch scene. Each character is hand-coded as a 3×5 cell grid
    /// scaled up. Good enough for "I see the word TILE on the sprite".
    /// </summary>
    private static void DrawWord(Image image, string word, int x0, int y0, int scale, Color ink)
    {
        var cursor = x0;
        foreach (var ch in word)
        {
            var glyph = GlyphFor(ch);
            if (glyph is null)
            {
                cursor += 4 * scale;
                continue;
            }
            for (var gy = 0; gy < 5; gy++)
            {
                for (var gx = 0; gx < 3; gx++)
                {
                    if (glyph[gy, gx] == 0) continue;
                    FillCell(image, cursor + gx * scale, y0 + gy * scale, scale, ink);
                }
            }
            cursor += 4 * scale;
        }
    }

    private static void FillCell(Image image, int x, int y, int scale, Color ink)
    {
        var maxX = System.Math.Min(image.GetWidth(), x + scale);
        var maxY = System.Math.Min(image.GetHeight(), y + scale);
        for (var py = System.Math.Max(0, y); py < maxY; py++)
        {
            for (var px = System.Math.Max(0, x); px < maxX; px++)
            {
                image.SetPixel(px, py, ink);
            }
        }
    }

    private static int[,]? GlyphFor(char ch)
    {
        return ch switch
        {
            'T' => new[,]
            {
                { 1, 1, 1 },
                { 0, 1, 0 },
                { 0, 1, 0 },
                { 0, 1, 0 },
                { 0, 1, 0 },
            },
            'I' => new[,]
            {
                { 1, 1, 1 },
                { 0, 1, 0 },
                { 0, 1, 0 },
                { 0, 1, 0 },
                { 1, 1, 1 },
            },
            'L' => new[,]
            {
                { 1, 0, 0 },
                { 1, 0, 0 },
                { 1, 0, 0 },
                { 1, 0, 0 },
                { 1, 1, 1 },
            },
            'E' => new[,]
            {
                { 1, 1, 1 },
                { 1, 0, 0 },
                { 1, 1, 0 },
                { 1, 0, 0 },
                { 1, 1, 1 },
            },
            _ => null,
        };
    }
}
