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
/// <b>Scope locked PR5 + 2026-05-15 fallback decision.</b> The four
/// ingredients in the locked memo are :
/// <list type="number">
///   <item>diegetic shadow SW 30° — <b>PR5 ships as Modulate-only black
///         α 0.35</b> on a child Sprite2D with Y-skew transform (no shader).
///         The locked essential effect (silhouette + α 0.35) is preserved ;
///         the 3-tap blur softness is deferred (see PR5.X note below).</item>
///   <item>lift 2 px above the tile (subtle "posed" feel) — ships in PR5.</item>
///   <item>rim light terracotta on the sun-facing side — <b>deferred to
///         PR5.X</b> (see below). The <c>poi_rim.gdshader</c> stays in the
///         tree for the future PR but is not wired by this commit.</item>
///   <item>parallax pan 4 % (visual offset proportional to camera distance
///         from the spawn-time reference) — ships in PR5.</item>
///  </list>
/// </para>
///
/// <para>
/// <b>PR5.X — shader-based polish, deferred future PR.</b> The first PR5
/// cut wired two custom <c>canvas_item</c> shaders (<c>poi_shadow.gdshader</c>
/// for a 3-tap blur'd shadow ; <c>poi_rim.gdshader</c> for a sun-side
/// terracotta highlight). Two visible-at-runtime bugs surfaced :
///  <list type="bullet">
///    <item>the rim shader made the Halfgate sprite read as flou/délavé
///          even after the d549277 fix that stripped a spurious
///          <c>* COLOR</c> multiplication ;</item>
///    <item>the shadow rendered with the sprite's texture colors visible
///          (terracotta, blue water) instead of solid black α 0.35, even
///          though headless diagnostic logs confirm the shader compiles,
///          the ShaderMaterial is assigned, the texture is set, and the
///          ShowBehindParent flag is true.</item>
///  </list>
/// The disconnect between "all the C# wiring is correct" and "the rendered
/// pixels do not match the shader's COLOR write" remains unexplained
/// without an interactive frame capture (RenderDoc or the Godot Forward+
/// shader debugger). Rather than ship a regression, PR5 falls back to the
/// engine-builtin <see cref="CanvasItem.Modulate"/> path for the shadow —
/// black with α 0.35 — which renders correctly without any custom shader.
/// The rim is stripped from this commit ; <c>poi_rim.gdshader</c> stays on
/// disk for the deferred PR. When that PR opens, the diagnostic shaders
/// in commit history (forced red / forced green output) are the starting
/// point — they confirm whether the fragment stage runs at all under the
/// project's d3d12 Forward+ pipeline.
/// </para>
///
/// <para>
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
/// modulate. The 2026-05-15 Modulate-only path multiplies the locked
/// shadow_alpha (0.35) by the POI's current alpha each frame so the fog
/// fade reads correctly.
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
    /// child with Modulate-only black α 0.35. <c>false</c> = no shadow
    /// (sticker feel — A/B at probe via F1). Locked default <c>true</c>
    /// for production.
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
    /// PR5 — toggle the rim light. <b>Deferred to PR5.X</b> — when set to
    /// <c>true</c> in this commit, the rim is a no-op (no Material attached).
    /// The probe HUD label still flips to "ON" so Didier can see the
    /// toggle path is wired ; visually nothing changes until the shader
    /// pipeline is debugged separately.
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
    /// PR5 — locked shadow density. 0.35 = pion-sur-plateau locked value
    /// (subconscient, pas "tâche noire"). Constant for PR5 ; promoted to
    /// uniform / Export when PR5.X reintroduces the blur shader.
    /// </summary>
    private const float ShadowAlpha = 0.35f;

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

        // PR5.X DEFERRED — rim shader stays uninstalled in this commit. See
        // class docs (PR5.X note). The toggle path stays so Didier can flip
        // the bool at probe ; visually nothing changes until the shader
        // pipeline is debugged.
        // if (RimEnabled) { AttachRimMaterial(); }   // PR5.X

        // PR5 — attach the shadow child (Sprite2D with Modulate-only black,
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
            $"shadow={(ShadowEnabled ? "ON(Modulate)" : "OFF")} " +
            $"lift={LiftPx} " +
            $"rim={(RimEnabled ? "ON(deferred PR5.X)" : "OFF")} " +
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
        // Modulate-only path : the effective alpha is ShadowAlpha (0.35)
        // multiplied by the POI's current alpha each frame.
        if (_shadow != null)
        {
            var poiAlpha = Modulate.A;
            var targetA = ShadowAlpha * poiAlpha;
            var shadowMod = _shadow.Modulate;
            if (System.Math.Abs(shadowMod.A - targetA) > 0.001f)
            {
                _shadow.Modulate = new Color(0, 0, 0, targetA);
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

    /// <summary>F3 — flip rim ON/OFF. <b>PR5.X deferred</b> : the toggle
    /// flips the export bool so the HUD label stays consistent, but no
    /// Material is attached / detached in this commit.</summary>
    public void DebugToggleRim()
    {
        RimEnabled = !RimEnabled;
        // PR5.X deferred — no Material attach/detach in this commit.
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

        // PR5 fallback (2026-05-15) — Modulate-only black α 0.35. The
        // Sprite2D draws the texture multiplied by Modulate ; with
        // Modulate=(0,0,0,0.35), the output is black at 35 % opacity wherever
        // the texture has non-zero alpha — i.e. a solid silhouette of the
        // sprite's alpha shape. This is exactly the locked "ombre diégétique
        // noir α 0.35" effect, minus the 3-tap blur softness (deferred PR5.X).
        var shadow = new Sprite2D
        {
            Name = "Shadow",
            Texture = Texture,                                  // share alpha shape
            Centered = false,
            Offset = Offset,                                    // same anchor pivot
            Modulate = new Color(0, 0, 0, ShadowAlpha),         // PR5 — solid black α 0.35
            ShowBehindParent = true,                            // draw BEFORE parent
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
        // SW 30° iso shadow basis (locked in commit a5dbd42, kept verbatim) :
        //   X basis = (1.0, 0.0)      — full silhouette width preserved
        //   Y basis = (+0.5, -0.4)    — top lays toward SW, flattened to 40 %
        // For Halfgate top (local y=-575) the top maps to screen (-287.5,
        // +230) — west (-X) and south (+Y), matching the locked SO 30°.
        //
        // Transform2D in Godot stores column-major basis vectors :
        //   [ basis_x.x   basis_y.x   origin.x ]
        //   [ basis_x.y   basis_y.y   origin.y ]
        var shadowXform = new Transform2D(
            new GdVec(1.0f, 0.0f),       // X basis : full width preserved
            new GdVec(0.5f, -0.4f),      // Y basis : lay silhouette down toward SW
            new GdVec(0, 0));             // local origin (parent-relative)
        shadow.Transform = shadowXform;

        // PR5.X DEFERRED — the shader-based 3-tap blur path is parked.
        // See class docs (PR5.X note). The .gdshader files stay on disk
        // for that future PR.

        AddChild(shadow);
        _shadow = shadow;
    }
}
