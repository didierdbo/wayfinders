using System.Numerics;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Services;
using SysVec = System.Numerics.Vector2;
using GdVec = Godot.Vector2;

namespace Wayfinders.Client.Scripts.Poi;

/// <summary>
/// PR3 — Sprite2D MVP scene for a POI placed on the iso world tree.
/// PR4 — registers itself with the <see cref="PoiInputRouter"/> autoload
/// on <c>_Ready</c> and unregisters on <c>_ExitTree</c>.
/// PR5 — the four "pion sur plateau" ingredients (locked memo
/// <c>project_wayfinders_pion_sur_plateau</c>) : runtime shadow, lift 2 px,
/// rim light terracotta, parallax pan 3-5 %.
///
/// <para>
/// <b>Scope locked PR5.</b> The four ingredients are EXACTLY :
/// <list type="number">
///   <item>diegetic shadow SW 30° (Sprite2D child with <c>poi_shadow.gdshader</c>),</item>
///   <item>lift 2 px above the tile (subtle "posed" feel),</item>
///   <item>rim light terracotta on the sun-facing side (Sprite2D <c>Material</c>
///         = <c>poi_rim.gdshader</c>),</item>
///   <item>parallax pan 4 % (visual offset proportional to camera distance
///         from the spawn-time reference).</item>
///  </list>
/// Anti-checklist locked (memo) : NO bobbing, NO outline, NO lift &gt; 3 px,
/// NO glow / particles / idle animation, NO pop color, NO blob footprint.
/// The clamps in <see cref="PoiVisualLogic"/> defend the [0, 3] / [0, 5 %]
/// windows even if an inspector typo or a probe override slips in.
/// </para>
///
/// <para>
/// <b>Shadow as child with <c>ShowBehindParent</c>, not sibling.</b> The
/// pre-brief sketched the sibling-with-PoiGroup pattern ; the canonical
/// Godot 4 alternative is a child <c>Sprite2D</c> with
/// <c>ShowBehindParent = true</c>, which forces the child to render BEFORE
/// the parent in the same canvas. This keeps :
///  <list type="bullet">
///    <item>The PR3 scene root locked at <see cref="Sprite2D"/> (zero
///          breaking change to <see cref="PoiInputRouter"/>'s assumption
///          that a <c>Poi</c> is a <c>Sprite2D</c> exposing
///          <c>Texture</c> / <c>Offset</c> / <c>GlobalPosition</c>).</item>
///    <item>The shadow lifetime tied to its POI (free the POI, free the
///          shadow — no separate registry).</item>
///    <item>Y-sort intact : the world root sorts <see cref="Poi"/>
///          instances ; the shadow rides along inside its parent's draw
///          slot.</item>
///  </list>
/// </para>
///
/// <para>
/// <b>Fog edge cohabitation (locked memo, MVP fallback).</b> The locked
/// rule is <c>shadow.Modulate.A = poi.Modulate.A</c> so a POI fading inside
/// a fog edge takes its shadow with it. Since there is no <c>FogService</c>
/// autoload yet (only the per-tile <c>FogTileLayer</c>, no central opacity
/// query), we propagate the alpha in <c>_Process</c> from the POI's own
/// modulate. When a FogService surfaces (post-PR5 likely), we read the
/// resolved alpha at the POI's tile and assign it to <c>Modulate</c> ; the
/// shadow stays synced by the same <c>_Process</c> line.
/// </para>
///
/// <para>
/// <b>Trap §8 — preflight logs delivered values.</b> The four debug
/// toggles are observable via the spawn log line, and the probe HUD
/// exposes them live so Didier can A/B at F6.
/// </para>
///
/// <para>
/// <b>Trap #10 — disconnection.</b> Every <c>Register</c> in
/// <c>_Ready</c> is paired with an <c>Unregister</c> in <c>_ExitTree</c>.
/// No tween / timer is allocated (anti-checklist : no idle animation), so
/// no extra cleanup surface.
/// </para>
/// </summary>
public partial class Poi : Sprite2D
{
    /// <summary>
    /// The data driving this POI's render. Required — must be assigned before
    /// the node enters the scene tree (either via the inspector on a saved
    /// <c>.tres</c> or programmatically by <see cref="PoiSpawner"/> before
    /// <c>AddChild</c>).
    /// </summary>
    [Export] public PoiData? PoiData { get; set; }

    /// <summary>
    /// PR5 — toggle the runtime shadow. <c>true</c> = attach the shadow
    /// child with the <c>poi_shadow.gdshader</c> material. <c>false</c> =
    /// no shadow (sticker feel — A/B at probe via F1). Locked default
    /// <c>true</c> for production.
    /// </summary>
    [Export] public bool ShadowEnabled { get; set; } = true;

    /// <summary>
    /// PR5 — lift in screen pixels (Y up). Locked window [0, 3]
    /// (<see cref="PoiVisualLogic.MinLiftPx"/> / <see cref="PoiVisualLogic.MaxLiftPx"/>) :
    /// 0 = sticker, 2 = posed (target), 3 = jouet edge. Anything &gt; 3 is
    /// clamped at <c>_Ready</c> so a slipped inspector value cannot ship.
    /// Cycle 0 → 2 → 3 → 0 at probe via F2.
    /// </summary>
    [Export] public int LiftPx { get; set; } = 2;

    /// <summary>
    /// PR5 — toggle the rim light shader on the POI's own Material. F3 at
    /// probe. <c>true</c> by default for production. When toggling off, we
    /// detach <see cref="Sprite2D.Material"/> so the comparison is
    /// "no material at all" vs "rim material applied".
    /// </summary>
    [Export] public bool RimEnabled { get; set; } = true;

    /// <summary>
    /// PR5 — parallax strength. Locked window [0, 0.05]
    /// (<see cref="PoiVisualLogic.MinParallaxStrength"/> /
    /// <see cref="PoiVisualLogic.MaxParallaxStrength"/>). 0 = off, 0.04 = locked
    /// target (4 %), 0.05 = upper edge. Cycle 0 → 0.03 → 0.05 → 0 at probe
    /// via F4.
    /// </summary>
    [Export] public float ParallaxStrength { get; set; } = 0.04f;

    /// <summary>
    /// PR5 — optional Camera2D node path for the parallax follow. When set,
    /// <c>_Process</c> reads <c>camera.GlobalPosition</c> each frame and
    /// drifts the POI by <see cref="PoiVisualLogic.ComputeParallaxOffset"/>.
    /// When null / unset, the parallax is disabled — same effect as
    /// <c>ParallaxStrength = 0</c>. The probe wires this in
    /// <see cref="PoiSpawner.SpawnAt"/> (locked 2026-05-15 — keeps the
    /// public API minimal). Field, not auto-property, so the runtime
    /// camera-resolved reference does not roundtrip through Godot
    /// serialisation.
    /// </summary>
    private Camera2D? _camera;

    private Sprite2D? _shadow;
    private GdVec _basePosition; // POI position BEFORE lift + parallax
    private GdVec _referencePosition; // tile spawn position, parallax reference
    private ShaderMaterial? _rimMaterial;

    private static readonly string ShadowShaderPath = "res://shaders/poi_shadow.gdshader";
    private static readonly string RimShaderPath = "res://shaders/poi_rim.gdshader";

    public override void _Ready()
    {
        if (PoiData == null)
        {
            GD.PrintErr($"[POI SPAWN] {Name} — PoiData is null at _Ready. " +
                        "Caller must set PoiData before AddChild.");
            return;
        }

        if (PoiData.Texture == null)
        {
            GD.PrintErr($"[POI SPAWN] {Name} — PoiData.Texture is null at _Ready. " +
                        $"(DisplayName='{PoiData.DisplayName}', TexturePath='{PoiData.TexturePath}')");
            return;
        }

        // Bind the sprite to the data.
        Texture = PoiData.Texture;
        Centered = false;
        Offset = -(GdVec)PoiData.AnchorPixel; // pivot at the anchor pixel (foot)

        // PR5 — clamp lift to the locked [0,3] window. Defends "jouet"
        // anti-pattern even if a probe override or inspector typo slipped.
        LiftPx = PoiVisualLogic.ClampLift(LiftPx);
        ParallaxStrength = PoiVisualLogic.ClampParallaxStrength(ParallaxStrength);

        // Capture the base position (set by the spawner from the tile coord)
        // BEFORE applying lift. Parallax reference = the unlifted position.
        _basePosition = Position;
        _referencePosition = Position;

        // PR5 — apply lift. Y- in Godot 2D = upward on screen.
        Position += new GdVec(0, -LiftPx);

        // PR5 — attach the rim material to the POI's own Material slot.
        if (RimEnabled)
        {
            AttachRimMaterial();
        }

        // PR5 — attach the shadow child (Sprite2D with shadow shader,
        // ShowBehindParent=true so it draws first inside the parent slot).
        if (ShadowEnabled)
        {
            AttachShadow();
        }

        // Preflight (trap §2 + §8) — log delivered values, not "spawned OK".
        var parent = GetParent();
        var parentType = parent?.GetType().Name ?? "<no parent>";
        var isCanvasLayer = parent is CanvasLayer;
        var anchorAlignedOk = Offset == -(GdVec)PoiData.AnchorPixel;

        GD.Print(
            $"[POI SPAWN] {PoiData.DisplayName} " +
            $"basePos=({_basePosition.X:F1},{_basePosition.Y:F1}) " +
            $"liftedPos=({Position.X:F1},{Position.Y:F1}) " +
            $"anchorPixel={PoiData.AnchorPixel} " +
            $"offset={Offset} " +
            $"centered={Centered} " +
            $"texSize={Texture.GetSize()} " +
            $"visible={Visible} " +
            $"parent={parentType}" +
            (isCanvasLayer ? " [TRAP §1 — parent is CanvasLayer!]" : "") +
            (anchorAlignedOk ? " anchorAlignedOK" : " ANCHOR-MISALIGNED"));

        GD.Print(
            $"[POI {PoiData.DisplayName}] " +
            $"shadow={(ShadowEnabled ? "ON" : "OFF")} " +
            $"lift={LiftPx} " +
            $"rim={(RimEnabled ? "ON" : "OFF")} " +
            $"parallax={ParallaxStrength:F3} " +
            $"camera={(_camera != null ? "wired" : "none")} " +
            "(trap §8 — values delivered to screen)");

        // PR4 — register with the input router. GetNodeOrNull so a probe
        // that somehow runs without the autoload degrades gracefully.
        var router = GetNodeOrNull<PoiInputRouter>("/root/PoiInputRouter");
        if (router != null)
        {
            router.Register(this);
            GD.Print($"[POI {PoiData.DisplayName}] registered with router");
        }
        else
        {
            GD.PrintErr(
                $"[POI {PoiData.DisplayName}] PoiInputRouter autoload missing at " +
                "/root/PoiInputRouter — input will NOT fire for this POI");
        }
    }

    public override void _Process(double delta)
    {
        // PR5 — parallax follow. Skip the math entirely when disabled to
        // keep the per-frame cost at "one bool check" for the OFF case.
        if (_camera != null && ParallaxStrength > 0.0f)
        {
            var camPos = _camera.GlobalPosition;
            var refPos = _referencePosition;
            var offset = PoiVisualLogic.ComputeParallaxOffset(
                new SysVec(camPos.X, camPos.Y),
                new SysVec(refPos.X, refPos.Y),
                ParallaxStrength);
            // Apply parallax ON TOP of the lifted base position. Y- lift
            // stays applied : the POI floats slightly + drifts with the pan.
            Position = _basePosition + new GdVec(offset.X, offset.Y - LiftPx);
        }

        // PR5 — fog edge cohabitation (locked rule shadow.alpha = poi.alpha).
        // Cheap to do every frame ; if a FogService later overrides the
        // POI's Modulate.A, this line keeps the shadow synced for free.
        if (_shadow != null)
        {
            var poiAlpha = Modulate.A;
            var shadowMod = _shadow.Modulate;
            if (System.Math.Abs(shadowMod.A - poiAlpha) > 0.001f)
            {
                _shadow.Modulate = new Color(shadowMod.R, shadowMod.G, shadowMod.B, poiAlpha);
            }
        }
    }

    public override void _ExitTree()
    {
        // Methodology trap #10 — must mirror the Register call.
        var router = GetNodeOrNull<PoiInputRouter>("/root/PoiInputRouter");
        if (router != null)
        {
            router.Unregister(this);
            var name = PoiData?.DisplayName ?? Name.ToString();
            GD.Print($"[POI {name}] unregistered");
        }
        // Shadow child is freed automatically by Godot when the POI is freed.
    }

    /// <summary>
    /// Wire the parallax follow target. Called by <see cref="PoiSpawner"/>
    /// at spawn time so the probe / slice does not have to set the export
    /// path manually. Pass <c>null</c> to detach the parallax (same effect
    /// as <see cref="ParallaxStrength"/> = 0).
    /// </summary>
    public void SetParallaxCamera(Camera2D? camera)
    {
        _camera = camera;
        if (camera == null && _basePosition != GdVec.Zero)
        {
            // Snap back to lifted base if parallax is being removed live.
            Position = _basePosition + new GdVec(0, -LiftPx);
        }
    }

    // ====================================================================
    // PR5 — debug toggles, callable by the probe HUD for A/B at F6.
    // These mutate the running scene ; preflight invariants are NOT
    // re-checked (the clamps were applied at _Ready). The toggles do
    // their own clamping for the same reason : a slipped F-key cycle
    // cannot ship a "jouet" silhouette.
    // ====================================================================

    /// <summary>F1 — flip shadow ON/OFF. Re-attaches or frees the child.</summary>
    public void DebugToggleShadow()
    {
        ShadowEnabled = !ShadowEnabled;
        if (ShadowEnabled && _shadow == null)
        {
            AttachShadow();
        }
        else if (!ShadowEnabled && _shadow != null)
        {
            _shadow.QueueFree();
            _shadow = null;
        }
    }

    /// <summary>F2 — cycle lift 0 → 2 → 3 → 0. Re-applies on Position.</summary>
    public void DebugCycleLift()
    {
        LiftPx = LiftPx switch
        {
            0 => 2,
            2 => 3,
            _ => 0,
        };
        LiftPx = PoiVisualLogic.ClampLift(LiftPx);
        Position = _basePosition + new GdVec(0, -LiftPx);
    }

    /// <summary>F3 — flip rim ON/OFF. Sets / clears <see cref="Material"/>.</summary>
    public void DebugToggleRim()
    {
        RimEnabled = !RimEnabled;
        if (RimEnabled && Material == null)
        {
            AttachRimMaterial();
        }
        else if (!RimEnabled)
        {
            Material = null;
            _rimMaterial = null;
        }
    }

    /// <summary>F4 — cycle parallax 0 → 0.03 → 0.05 → 0.</summary>
    public void DebugCycleParallax()
    {
        ParallaxStrength = ParallaxStrength switch
        {
            0.0f => 0.03f,
            0.03f => 0.05f,
            _ => 0.0f,
        };
        ParallaxStrength = PoiVisualLogic.ClampParallaxStrength(ParallaxStrength);
        if (ParallaxStrength <= 0.0f)
        {
            // Snap back to lifted base when parallax is disabled mid-pan.
            Position = _basePosition + new GdVec(0, -LiftPx);
        }
    }

    // ====================================================================
    // PR5 — private helpers (Godot-bound construction of children).
    // ====================================================================

    private void AttachShadow()
    {
        if (Texture == null) return;

        var shadow = new Sprite2D
        {
            Name = "Shadow",
            Texture = Texture,            // share the asset texture (alpha source)
            Centered = false,
            Offset = Offset,              // same anchor pivot as parent
            Modulate = new Color(1, 1, 1, 1), // shader does the darkening
            ShowBehindParent = true,      // draw BEFORE parent in same canvas
        };

        // Iso shadow projection — "lay the silhouette down on the ground"
        // toward the SW (locked memo project_wayfinders_pion_sur_plateau,
        // ombre diégétique SO 30°).
        //
        // Pivot. With Centered=false + Offset=-AnchorPixel, the anchor pixel
        // (the foot of the silhouette) sits at the node's local origin (0,0).
        // ANY Transform2D applied to the node pivots around that origin —
        // which is exactly what we want : the shadow's "contact point" with
        // the ground must coincide with the parent POI's foot, then the
        // silhouette must rotate-and-flatten OUTWARD from that point.
        //
        // Bug fix (Rune, 2026-05-15 PR5 post-mortem). The first cut used
        //   X basis = (1.0, 0.0)
        //   Y basis = (-0.5, +0.4)
        // which maps local (lx, ly) → screen (lx - 0.5*ly, 0.4*ly). For a
        // 1076×575 Halfgate with foot at local y=0 and top at local y=-575,
        // the top of the silhouette landed at screen (+287.5, -230) — i.e.
        // STILL ABOVE the foot, just sheared sideways. Result : a darkened
        // near-duplicate of the gate overlapped the original, reading as a
        // "ghost dédoublé décalé vers la droite" at F6 — not a ground
        // shadow at all. The Y-component of the Y basis must be NEGATIVE
        // to flip the silhouette's top BELOW the foot on screen (laying it
        // flat on the ground plane), and the X skew sign must be POSITIVE
        // for the SW direction in Godot screen coords (Y+ = south, X- = west).
        //
        // Correct math (SW 30° iso shadow) :
        //   X basis = (1.0, 0.0)      — full silhouette width preserved
        //   Y basis = (+0.5, -0.4)    — top lays toward SW, flattened to 40 %
        // Local (lx, -h) → screen (lx + h*0.5, h*0.4). For Halfgate top
        // (h=575) : (+287.5, +230) — south-east on the ground.
        //
        // Wait. SW = negative X, positive Y in screen coords. With Y basis
        // (+0.5, -0.4) the top of texture (local y < 0) maps to NEGATIVE
        // skew*ly product → local (0, -575) → (+0.5*-575, -0.4*-575)
        // = (-287.5, +230). That IS SW : -X (west) and +Y (south). Match
        // for the locked SO 30° direction.
        //
        // Transform2D in Godot stores column-major basis vectors :
        //   [ basis_x.x   basis_y.x   origin.x ]
        //   [ basis_x.y   basis_y.y   origin.y ]
        // so passing (basis_x, basis_y, origin) feeds the two columns.
        var shadowXform = new Transform2D(
            new GdVec(1.0f, 0.0f),       // X basis : full width preserved
            new GdVec(0.5f, -0.4f),      // Y basis : lay silhouette down toward SW
            new GdVec(0, 0));             // local origin (parent-relative)
        shadow.Transform = shadowXform;

        // Material with the shadow shader. Set the texel uniform from the
        // actual texture size — trap §8 (computed values), the shadow
        // softness scales correctly for any asset shape.
        var shader = GD.Load<Shader>(ShadowShaderPath);
        if (shader == null)
        {
            GD.PrintErr($"[POI {PoiData?.DisplayName}] failed to load shadow shader at {ShadowShaderPath}");
        }
        else
        {
            var mat = new ShaderMaterial { Shader = shader };
            var texSize = Texture.GetSize();
            mat.SetShaderParameter("texel",
                new GdVec(1.0f / texSize.X, 1.0f / texSize.Y));
            mat.SetShaderParameter("shadow_alpha", 0.35f);
            shadow.Material = mat;
        }

        AddChild(shadow);
        _shadow = shadow;
    }

    private void AttachRimMaterial()
    {
        if (Texture == null) return;

        var shader = GD.Load<Shader>(RimShaderPath);
        if (shader == null)
        {
            GD.PrintErr($"[POI {PoiData?.DisplayName}] failed to load rim shader at {RimShaderPath}");
            return;
        }

        var mat = new ShaderMaterial { Shader = shader };
        var texSize = Texture.GetSize();
        mat.SetShaderParameter("texel",
            new GdVec(1.0f / texSize.X, 1.0f / texSize.Y));
        // Locked SW 30° sun direction. Memo project_wayfinders_pion_sur_plateau.
        mat.SetShaderParameter("light_dir", new GdVec(-0.866f, 0.5f));
        // Locked rim color #c87844 (terracotta saturated sun-side). Decision
        // micro Rune, between locked #7d4e2c (terracotta dark edge frame) and
        // ochre clair. To be validated by Didier at F6 — bump down saturation
        // if too warm.
        mat.SetShaderParameter("rim_color", new Color(0.78f, 0.47f, 0.27f, 1.0f));
        mat.SetShaderParameter("rim_strength", 0.6f);
        Material = mat;
        _rimMaterial = mat;
    }
}
