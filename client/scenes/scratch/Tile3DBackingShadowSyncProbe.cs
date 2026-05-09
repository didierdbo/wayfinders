using Godot;
using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Scratch;

/// <summary>
/// Jalon 2 (Wayfinders 3D Backing Architecture, doc §6, locked
/// 2026-05-09) -- runtime probe demonstrating <b>Pattern B2</b>
/// (locked at §5 Q1) : a master <c>Camera2D</c> drives the rendering ;
/// a follower <c>Camera3D</c> in orthogonal projection mirrors its
/// transform every frame ; both cameras are anchored on the same
/// witness cell ; the cell is rendered <i>twice</i> -- once as a
/// <c>Sprite2D</c> in 2D world coords (the visible pixel), once as an
/// <c>Area3D</c> + <c>BoxShape3D</c> + red wireframe in 3D world coords
/// (the click volume). The probe pans on WASD ; the rendering and
/// the wireframe must move in lock-step, no drift, at every pan delta.
///
/// <para>
/// <b>What this probe proves.</b>
/// <list type="bullet">
///   <item><b>B2 in runtime</b> -- two cameras with two projections,
///         synchronised every <c>_Process</c> frame, can co-exist
///         without one lagging the other when input arrives every
///         frame. <i>If</i> the wireframe drifts off the sprite as
///         we pan, B2 is not viable for prod and we re-open Q1.</item>
///   <item><b>The iso projection invariant in motion</b> -- the
///         pin test (<c>IsoProjection3DTo2DTests</c>) covers static
///         coordinates ; this probe covers the same math under
///         continuous transform updates. Same identity
///         <c>sin(30°) = 1/2</c> drives both.</item>
///   <item><b>Pattern G applied again</b> (debug skin distinct from
///         prod) -- dark technical background, vivid green/red
///         tints, red wireframe, on-screen STATUS + live log. No
///         confusion possible with the production E2 World Map.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this probe does NOT prove.</b>
/// <list type="bullet">
///   <item>Production performance -- one cell, no zoom, no children
///         deeper than one node. The 50x17 production grid is left to
///         a future jalon.</item>
///   <item>Zoom synchronisation -- this probe locks zoom at 1.0 to
///         keep the math focused on pan. Zoom is its own seam (the
///         pin test asserts the <c>sqrt(2)</c> scale factor ; mapping
///         <c>Camera3D.Size</c> to <c>Camera2D.Zoom</c> is the
///         Jalon 3 concern).</item>
///   <item>Drag-pan, momentum, bounce-back, edge clamps -- all
///         Phase 8 features of <c>MapPan2DComponent</c>. The probe
///         uses straight WASD pan to isolate the camera-sync question
///         from the input-state-machine question.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>The math, in one place.</b> The 2D-to-3D camera-target inverse :
/// given a <c>Camera2D.Position</c> = <c>(camX2D, camY2D)</c> in pixel
/// world coords, the equivalent <c>Camera3D</c> look-at target on the
/// ground plane <c>y = 0</c> is :
/// <code>
///   camX3D =  camX2D + 2 * camY2D
///   camZ3D = -camX2D + 2 * camY2D
/// </code>
/// Derivation : the pin test (<c>IsoProjection3DTo2DTests</c>) fixes
/// the de-scaled forward projection
/// <c>(screenX, screenY) = ((X - Z) / 2, (X + Z) / 4)</c> in pixel
/// world units. We want the 3D camera target to project to
/// <c>(camX2D, camY2D)</c>. Solve :
/// <code>
///   camX2D = (X - Z) / 2   ->   X - Z =  2 * camX2D
///   camY2D = (X + Z) / 4   ->   X + Z =  4 * camY2D
///   add / sub :              X =  camX2D + 2 * camY2D
///                            Z = -camX2D + 2 * camY2D
/// </code>
/// Same identity as the pin test, no surprises.
/// </para>
/// </summary>
public partial class Tile3DBackingShadowSyncProbe : Node2D
{
    /// <summary>The witness cell coordinate. Static for the probe ; the
    /// production migration will iterate this over the whole grid.</summary>
    [Export] public int WitnessCol { get; set; } = 5;
    [Export] public int WitnessRow { get; set; } = 5;

    /// <summary>Cell size in pixels, matching the production E2 World
    /// convention (slice 3.5 / 3.6 lock).</summary>
    [Export] public int CellSizePx { get; set; } = 128;

    /// <summary>Tile geometry resource. Reuses the Jalon 1 thick variant
    /// by default ; the box height is derived from the resource so we
    /// can swap thick/thin in the inspector.</summary>
    [Export] public TileBackingData BackingData { get; set; } = new();

    /// <summary>Pan speed (pixels per second) when WASD is held. 600 is
    /// brisk enough to cross a 1280-wide viewport in 2 s -- fast feedback
    /// for the playtest.</summary>
    [Export] public float PanSpeed { get; set; } = 600f;

    /// <summary>The master 2D camera -- this is what the player sees.
    /// Owns pan, owns zoom (locked at 1.0 for this probe). Anchored on
    /// the witness cell at start.</summary>
    [Export] public NodePath Camera2DPath { get; set; } = "Camera2D";

    /// <summary>The follower 3D camera. Lives in the same root viewport
    /// as the <c>Camera2D</c> -- Godot 4 supports a Camera2D and a
    /// Camera3D current in the same viewport because they drive
    /// separate render layers (2D canvas / 3D world). The Camera3D is
    /// invisible to the player here because nothing 3D is rendered
    /// (no <c>MeshInstance3D</c>) -- its only purpose is to host the
    /// <c>Area3D</c> picking.</summary>
    [Export] public NodePath Camera3DPath { get; set; } = "Camera3D";

    /// <summary>The visible 2D sprite for the witness cell.</summary>
    [Export] public NodePath WitnessSprite2DPath { get; set; } = "Witness2D/WitnessSprite2D";

    /// <summary>The 3D click volume.</summary>
    [Export] public NodePath WitnessArea3DPath { get; set; } = "Witness3D/WitnessArea3D";
    [Export] public NodePath WitnessShape3DPath { get; set; } = "Witness3D/WitnessArea3D/WitnessShape3D";
    [Export] public NodePath WitnessNode3DPath { get; set; } = "Witness3D";

    /// <summary>The red wireframe overlay around the 3D click volume,
    /// drawn in 2D world coords using a <c>Polygon2D</c> with no fill
    /// (line drawn via <c>Line2D</c> children).</summary>
    [Export] public NodePath WireframeOverlayPath { get; set; } = "Witness2D/WireframeOverlay";

    /// <summary>Overlay live STATUS readout.</summary>
    [Export] public NodePath StatusLabelPath { get; set; } = "OverlayUI/OverlayRoot/StatusLabel";

    /// <summary>Overlay live mirrored log.</summary>
    [Export] public NodePath LiveLogLabelPath { get; set; } = "OverlayUI/OverlayRoot/LiveLogLabel";

    /// <summary>Overlay drift readout -- shows the per-frame measured
    /// drift between the 2D sprite center and the projected 3D click
    /// volume center, in pixels. The whole point of the probe is to see
    /// this stay at zero.</summary>
    [Export] public NodePath DriftLabelPath { get; set; } = "OverlayUI/OverlayRoot/DriftLabel";

    /// <summary>Saturated debug green (Pattern G).</summary>
    [Export] public Color HoverModulate { get; set; } = new(0.30f, 1.00f, 0.30f, 1.0f);

    /// <summary>Saturated debug red (Pattern G).</summary>
    [Export] public Color SelectedModulate { get; set; } = new(1.00f, 0.30f, 0.30f, 1.0f);

    /// <summary>Default modulate.</summary>
    [Export] public Color IdleModulate { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Wireframe edge colour -- vivid red so it pops against
    /// the parchment witness texture and the dark background.</summary>
    [Export] public Color WireframeColor { get; set; } = new(1.0f, 0.20f, 0.20f, 1.0f);

    private Camera2D? _camera2D;
    private Camera3D? _camera3D;
    private Sprite2D? _witnessSprite2D;
    private Area3D? _witnessArea3D;
    private CollisionShape3D? _witnessShape3D;
    private Node3D? _witnessNode3D;
    private Node2D? _wireframeOverlay;
    private Label? _statusLabel;
    private Label? _liveLogLabel;
    private Label? _driftLabel;

    private bool _hovering;
    private bool _selected;
    private Vector2 _camera2DStartPosition;

    private const int LiveLogMaxLines = 8;
    private readonly Queue<string> _liveLog = new();

    // Cached signal handlers so we can disconnect cleanly at _ExitTree
    // (Rune disconnection discipline -- "signal connected, never
    // disconnected, then publisher freed -> subtle leaks").
    private System.Action? _mouseEnteredHandler;
    private System.Action? _mouseExitedHandler;
    private Area3D.InputEventEventHandler? _inputEventHandler;

    public override void _Ready()
    {
        // J2 canary -- prints first so Didier can disambiguate "is the
        // shadow-sync probe actually loaded?" without spelunking the
        // Output panel.
        GD.Print("[PROBE J2 SHADOW-SYNC] scene started -- Tile3DBackingShadowSyncProbe._Ready entered");

        _camera2D = GetNode<Camera2D>(Camera2DPath);
        _camera3D = GetNode<Camera3D>(Camera3DPath);
        _witnessSprite2D = GetNode<Sprite2D>(WitnessSprite2DPath);
        _witnessArea3D = GetNode<Area3D>(WitnessArea3DPath);
        _witnessShape3D = GetNode<CollisionShape3D>(WitnessShape3DPath);
        _witnessNode3D = GetNode<Node3D>(WitnessNode3DPath);
        _wireframeOverlay = GetNodeOrNull<Node2D>(WireframeOverlayPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _liveLogLabel = GetNodeOrNull<Label>(LiveLogLabelPath);
        _driftLabel = GetNodeOrNull<Label>(DriftLabelPath);

        // Mandatory for Area3D mouse signals to fire (Pattern E,
        // Jalon 1 lesson). The 3D scene shares the root viewport with
        // the 2D rendering -- Godot 4 supports a Camera2D and a
        // Camera3D current in the same viewport, picking-wise it's the
        // Camera3D that resolves Area3D hits.
        var viewport = GetViewport();
        if (viewport is not null && !viewport.PhysicsObjectPicking)
        {
            viewport.PhysicsObjectPicking = true;
            GD.Print("[ShadowSync] enabled root Viewport.PhysicsObjectPicking");
        }

        // Make the Camera3D current explicitly. The .tscn already sets
        // current=true ; doing it from code too is belt-and-braces in
        // case a future edit drops the flag.
        _camera3D!.MakeCurrent();

        ApplyWitness2DTexture();
        ApplyWitness3DGeometry();
        ApplyWitnessPositions();
        ApplyCamera3DTransform();
        BuildWireframeOverlay();

        // Anchor the 2D camera on the witness cell so the playtest
        // starts already focused. Recorded for the drift readout.
        var witnessCenter2D = ComputeWitnessCenter2D();
        _camera2D!.Position = witnessCenter2D;
        _camera2DStartPosition = witnessCenter2D;

        _mouseEnteredHandler = OnMouseEntered;
        _mouseExitedHandler = OnMouseExited;
        _inputEventHandler = OnInputEvent;

        _witnessArea3D!.MouseEntered += _mouseEnteredHandler;
        _witnessArea3D.MouseExited += _mouseExitedHandler;
        _witnessArea3D.InputEvent += _inputEventHandler;

        ApplySpriteModulate();
        UpdateStatusLabel();

        Log("PROBE J2 SHADOW-SYNC ready -- WASD pans the Camera2D ; the red wireframe must follow the green sprite at every frame");
        Log($"witness cell ({WitnessCol}, {WitnessRow}) center2D = ({witnessCenter2D.X:F1}, {witnessCenter2D.Y:F1})");
    }

    public override void _ExitTree()
    {
        // Disconnection discipline -- same pattern as Jalon 1.
        if (_witnessArea3D is not null)
        {
            if (_mouseEnteredHandler is not null)
                _witnessArea3D.MouseEntered -= _mouseEnteredHandler;
            if (_mouseExitedHandler is not null)
                _witnessArea3D.MouseExited -= _mouseExitedHandler;
            if (_inputEventHandler is not null)
                _witnessArea3D.InputEvent -= _inputEventHandler;
        }

        _mouseEnteredHandler = null;
        _mouseExitedHandler = null;
        _inputEventHandler = null;
    }

    public override void _Process(double delta)
    {
        ApplyPanInput((float)delta);
        SyncCamera3DToCamera2D();
        UpdateDriftLabel();
    }

    // ===== Input =====

    /// <summary>
    /// Direct-input WASD pan -- intentionally simpler than the
    /// production <see cref="Wayfinders.Client.Components.MapPan2DComponent"/>.
    /// We poll <c>Input.IsKeyPressed</c> rather than driving an
    /// InputMap action because the probe is throwaway debug : we want
    /// the input path to be one line, not a whole state machine. The
    /// production migration will keep MapPan2DComponent ; this
    /// shortcut never leaves the scratch folder.
    /// </summary>
    private void ApplyPanInput(float delta)
    {
        if (_camera2D is null) return;

        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1f;

        if (dir.LengthSquared() > 0f)
        {
            dir = dir.Normalized();
            _camera2D.Position += dir * PanSpeed * delta;
        }
    }

    // ===== 2D <-> 3D camera sync =====

    /// <summary>
    /// Apply the inverse iso projection to derive the 3D camera target
    /// from the 2D camera position. Doc above derives the closed form ;
    /// this is the runtime application.
    ///
    /// <para>
    /// <b>Strategy.</b> The Camera3D is positioned on the iso vector
    /// at a fixed distance from the look-at target, then aimed at that
    /// target. The target itself moves with the Camera2D : when the
    /// 2D pan moves the visible window left, the target on the 3D
    /// ground plane moves to the matching world point, the 3D camera
    /// follows, and the 3D scene "scrolls" by the same screen pixels
    /// as the 2D scene. No drift if the math is right.
    /// </para>
    ///
    /// <para>
    /// <b>What we measure.</b> The drift label shows
    /// <c>|projected3D - sprite2D|</c> in pixels each frame. When the
    /// math is correct, this stays under one pixel for the whole
    /// playtest. If it drifts, that's our bug -- fix in this method.
    /// </para>
    /// </summary>
    private void SyncCamera3DToCamera2D()
    {
        if (_camera2D is null || _camera3D is null) return;

        var camPos2D = _camera2D.Position;

        // Inverse iso projection : pixel world (X, Y) -> 3D world (X, _, Z)
        // on the y=0 ground plane.
        //
        // Forward (derived from the pin test, after de-scaling by sqrt(2)) :
        //   screenX = (worldX - worldZ) / 2
        //   screenY = (worldX + worldZ) / 4
        // Solve for (worldX, worldZ) given (screenX, screenY) :
        //   worldX =  screenX + 2 * screenY
        //   worldZ = -screenX + 2 * screenY
        // The class docstring's earlier derivation lacked the /2 and /4
        // factors -- corrected here ; this is the formula the runtime
        // actually uses, and the drift readout in UpdateDriftLabel will
        // expose any reversion.
        var camTarget3D = new Vector3(
            x: camPos2D.X + 2f * camPos2D.Y,
            y: 0f,
            z: -camPos2D.X + 2f * camPos2D.Y);

        // Position the camera on the iso vector at distance D from the
        // target. Distance is depth-invariant for orthogonal projection
        // (no perspective foreshortening) so the value is mostly
        // cosmetic ; we pick something far enough that the camera near
        // plane never clips a tile.
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

        _camera3D.Position = camTarget3D + offset;
        _camera3D.LookAt(camTarget3D, Vector3.Up);

        // Ortho size : the Camera3D vertical extent in 3D world units
        // matches the Camera2D viewport vertical extent in pixels,
        // de-scaled by sqrt(2) (the pin test factor). With Camera2D
        // zoom locked at 1, the visible viewport vertical extent is
        // the window height in pixels.
        //
        // Picking precision : the Camera3D's frustum must cover the
        // same world region as the Camera2D so the cursor maps to the
        // same point in both. If the size is off, a hover near the
        // viewport edge will fail or pick the wrong cell. The pin
        // test fixed sqrt(2) ; we trust it and watch the drift label
        // for any leak.
        var viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920f, 1080f);
        _camera3D.Size = viewportSize.Y * Mathf.Sqrt(2f);

        _camera3D.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera3D.Near = 0.1f;
        _camera3D.Far = distance * 4f;
    }

    /// <summary>Initial Camera3D setup ; runtime sync repeats this each frame.</summary>
    private void ApplyCamera3DTransform()
    {
        SyncCamera3DToCamera2D();
    }

    // ===== Witness placement =====

    /// <summary>
    /// Compute the witness cell's 2D world center using the same math as
    /// the production fog grid (FogTileGridLogic + iso origin shift).
    /// Single source of truth for both the 2D sprite position and the
    /// drift readout.
    /// </summary>
    private Vector2 ComputeWitnessCenter2D()
    {
        var coord = new GridCoord(WitnessCol, WitnessRow);
        var dims = new GridDimensions(Columns: 64, Rows: 64);
        var center = FogTileGridLogic.ComputeCellCenter(
            coord, CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        return new Vector2(center.X + shift.X, center.Y + shift.Y);
    }

    /// <summary>Project the witness 3D node center back to 2D screen
    /// coords, applying the same math as <see cref="SyncCamera3DToCamera2D"/>
    /// in reverse. Used by <see cref="UpdateDriftLabel"/> to measure
    /// any per-frame error.</summary>
    private Vector2 ProjectWitness3DCenterTo2D()
    {
        if (_witnessNode3D is null) return Vector2.Zero;
        var pos = _witnessNode3D.Position;
        // Forward iso projection : 3D world (X, _, Z) -> 2D pixel.
        // The pin test gives the raw formula
        //   3D_screen = ((X - Z) * sqrt(2)/2, (X + Z) * sqrt(2)/4)
        // and asserts that dividing this by sqrt(2) lines up with the 2D
        // FogTileGridLogic math up to one pixel. So in pixel-world
        // coords :
        //   screenX = (X - Z) / 2
        //   screenY = (X + Z) / 4
        // matches halfW * (col - row) and halfH * (col + row) with
        // cellSize = 128, halfW = 64, halfH = 32.
        var screenX = (pos.X - pos.Z) / 2f;
        var screenY = (pos.X + pos.Z) / 4f;
        return new Vector2(screenX, screenY);
    }

    /// <summary>Place the 2D sprite and the 3D node at the witness cell
    /// position. The 2D sprite uses the FogTileGridLogic center directly ;
    /// the 3D node lives at <c>(col*cs, 0, row*cs)</c> per the iso math
    /// derivation in the pin test.</summary>
    private void ApplyWitnessPositions()
    {
        var center2D = ComputeWitnessCenter2D();
        if (_witnessSprite2D is not null)
        {
            _witnessSprite2D.Position = center2D;
        }
        if (_witnessNode3D is not null)
        {
            _witnessNode3D.Position = new Vector3(
                WitnessCol * CellSizePx,
                0f,
                WitnessRow * CellSizePx);
        }
    }

    /// <summary>Build a parchment witness texture for the 2D sprite --
    /// procedural so it cannot be confused with a production iso atlas
    /// tile (Pattern G).</summary>
    private void ApplyWitness2DTexture()
    {
        if (_witnessSprite2D is null) return;

        var w = BackingData.SpriteTopWidthPx;
        var h = BackingData.SpriteTopHeightPx;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        var fill = new Color(0.92f, 0.85f, 0.66f, 1f);
        var border = new Color(0.20f, 0.80f, 0.30f, 1f);
        const int borderPx = 6;

        image.Fill(fill);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (x < borderPx || x >= w - borderPx ||
                    y < borderPx || y >= h - borderPx)
                {
                    image.SetPixel(x, y, border);
                }
            }
        }

        // Marker dot at the centre so any drift between the sprite
        // and the wireframe is visible at a glance.
        var markerColor = new Color(0.10f, 0.10f, 0.10f, 1f);
        for (var y = h / 2 - 4; y < h / 2 + 4; y++)
        {
            for (var x = w / 2 - 4; x < w / 2 + 4; x++)
            {
                if (x >= 0 && x < w && y >= 0 && y < h)
                    image.SetPixel(x, y, markerColor);
            }
        }

        var tex = ImageTexture.CreateFromImage(image);
        _witnessSprite2D.Texture = tex;
        _witnessSprite2D.Centered = true;
    }

    /// <summary>Resize the BoxShape3D from the BackingData (Pattern C).</summary>
    private void ApplyWitness3DGeometry()
    {
        if (_witnessShape3D is null) return;

        var box = _witnessShape3D.Shape as BoxShape3D ?? new BoxShape3D();
        var w = (float)BackingData.SpriteTopWidthPx;
        var h = (float)BackingData.BackingHeightPx;
        var d = w * 0.5f;

        box.Size = new Vector3(w, h, d);
        _witnessShape3D.Shape = box;
        _witnessShape3D.Position = new Vector3(0f, -h * 0.5f, 0f);
    }

    /// <summary>Build a 2D Polygon2D wireframe (4 lines forming a
    /// diamond outline at the witness sprite's 2D center) so the user
    /// has a visual proof of the 2D-rendered click volume in 2D
    /// coordinate space. The 3D click volume is the
    /// <see cref="_witnessArea3D"/> Area3D ; this 2D wireframe is its
    /// projected shadow.
    ///
    /// <para>
    /// <b>Why a 2D wireframe and not the 3D one we used in Jalon 1.</b>
    /// Jalon 1 had a Camera3D rendering the whole scene, so an
    /// <c>ImmediateMesh</c> was the natural way to draw 3D lines.
    /// Here, the visible camera is 2D ; drawing the wireframe in 2D
    /// (so the player-facing rendering shows it directly) is simpler
    /// than projecting a 3D mesh through a SubViewport. The shape is
    /// a static diamond outline ; it does not need to be 3D-correct
    /// because the point of the probe is the <i>position match</i>,
    /// not the <i>volume match</i>.
    /// </para>
    /// </summary>
    private void BuildWireframeOverlay()
    {
        if (_wireframeOverlay is null) return;

        // Clear children if any (idempotent rebuild on hot reload).
        foreach (var child in _wireframeOverlay.GetChildren())
            child.QueueFree();

        var halfW = CellSizePx / 2f;
        var halfH = CellSizePx / 4f;
        var top = new Vector2(0, -halfH);
        var right = new Vector2(halfW, 0);
        var bottom = new Vector2(0, halfH);
        var left = new Vector2(-halfW, 0);

        var line = new Line2D
        {
            DefaultColor = WireframeColor,
            Width = 3f,
            Closed = true,
        };
        line.AddPoint(top);
        line.AddPoint(right);
        line.AddPoint(bottom);
        line.AddPoint(left);
        _wireframeOverlay.AddChild(line);

        // Position the wireframe overlay AT the witness sprite's center.
        var center2D = ComputeWitnessCenter2D();
        _wireframeOverlay.Position = center2D;
    }

    // ===== Signals =====

    private void OnMouseEntered()
    {
        _hovering = true;
        ApplySpriteModulate();
        UpdateStatusLabel();
        Log("hover ENTER (3D Area3D fired) -- sprite tinted GREEN");
    }

    private void OnMouseExited()
    {
        _hovering = false;
        ApplySpriteModulate();
        UpdateStatusLabel();
        Log("hover EXIT (3D Area3D fired) -- back to idle (or RED if selected)");
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
            Log($"click @ 3D world=({position.X:F0},{position.Y:F0},{position.Z:F0}) selected={_selected}");
        }
    }

    private void ApplySpriteModulate()
    {
        if (_witnessSprite2D is null) return;
        if (_selected)
            _witnessSprite2D.Modulate = SelectedModulate;
        else if (_hovering)
            _witnessSprite2D.Modulate = HoverModulate;
        else
            _witnessSprite2D.Modulate = IdleModulate;
    }

    // ===== Overlays =====

    private void UpdateStatusLabel()
    {
        if (_statusLabel is null) return;
        var word = _selected ? "SELECTED (red)"
                  : _hovering ? "HOVERING (green)"
                  : "idle (white)";
        var camPos = _camera2D?.Position ?? Vector2.Zero;
        _statusLabel.Text =
            $"STATUS: {word}\n" +
            $"Camera2D pos = ({camPos.X:F0}, {camPos.Y:F0})\n" +
            $"Witness cell = ({WitnessCol}, {WitnessRow})";
    }

    private void UpdateDriftLabel()
    {
        if (_driftLabel is null) return;
        if (_witnessSprite2D is null || _witnessNode3D is null)
        {
            _driftLabel.Text = "drift: n/a";
            return;
        }

        // The drift we want to see is : does the click area (the
        // wireframe's screen position) follow the sprite's screen
        // position as we pan? Both are anchored to the same Vector2 in
        // 2D world coords (the wireframe is positioned at
        // ComputeWitnessCenter2D() in BuildWireframeOverlay) and to the
        // same 3D world point (the witness Node3D at col*cs, 0, row*cs).
        //
        // We measure the drift by re-running the forward iso projection
        // on the 3D witness center and comparing with the 2D sprite
        // center. Both should equal each other up to integer rounding,
        // even after a pan, because neither moves : the cell is static,
        // only the camera moves.
        var sprite2D = _witnessSprite2D.Position;
        var projected3D = ProjectWitness3DCenterTo2D();

        // The 3D projection has its own origin (0,0,0); the 2D sprite
        // sits at a position that already includes the iso origin shift.
        // Subtract the shift to get the same frame.
        var dims = new GridDimensions(Columns: 64, Rows: 64);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        var sprite2DInIsoFrame = sprite2D - new Vector2(shift.X, shift.Y);
        // The 2D sprite center is ComputeCellCenter + shift, where the
        // y-component of ComputeCellCenter is (col+row)*halfH + halfH.
        // The 3D projection Y is just (col+row)*halfH (no +halfH offset)
        // because the 3D origin is at the diamond center, not the top
        // vertex. Strip the halfH offset to compare apples to apples.
        var halfH = CellSizePx / 4f;
        sprite2DInIsoFrame.Y -= halfH;

        var driftPx = (sprite2DInIsoFrame - projected3D).Length();
        _driftLabel.Text = $"drift: {driftPx:F2} px (expected: 0.00)";
    }

    private void Log(string message)
    {
        var line = $"[shadow-sync] {message}";
        GD.Print(line);

        _liveLog.Enqueue(line);
        while (_liveLog.Count > LiveLogMaxLines)
            _liveLog.Dequeue();

        if (_liveLogLabel is not null)
            _liveLogLabel.Text = string.Join("\n", _liveLog);
    }
}
