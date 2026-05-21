using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// Root of the Game Screen Shell — the cartographer's-desk diorama in
/// which eM and eT are shown (roadmap <c>wayfinders-mvp-roadmap-eM-eT</c>).
/// As of J3b this slice stands up two layers of the diorama: the
/// <b>clipped, pannable map maquette</b> (J3a) and the <b>fixed top/bottom
/// HUD frame</b> (J3b). The interactive iso desk (J3c) is a later
/// milestone and is not present here.
///
/// <para>
/// <b>What J3a delivers.</b>
/// <list type="number">
///   <item>A <see cref="SubViewportContainer"/> hosting a
///     <see cref="SubViewport"/> — the maquette is rendered into an
///     isolated render world and is clipped to the container's rect for
///     free. No <c>clip_contents</c> on a plain <c>Control</c>: that
///     would clip the draw but give neither an isolated camera nor a
///     private coordinate space (roadmap §"Choix techniques Godot").</item>
///   <item>A <see cref="Camera2D"/> <i>inside</i> the SubViewport. The
///     pan moves this camera — never the content. The maquette keeps its
///     absolute world coordinates, so the iso projection of J3-iso stays
///     valid with no recompute.</item>
///   <item>An instance of <c>IsoBoard.tscn</c> (the J3-iso socle) as the
///     maquette content. Until Mira's district bitmap lands the board
///     runs on its placeholder rectangle — a tolerated state.</item>
///   <item>Mouse-drag panning, middle-button, clamped to the maquette
///     content extent so the player cannot scroll into the void.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What J3b adds — the fixed HUD frame.</b> A <see cref="CanvasLayer"/>
/// (<c>HudLayer</c>, high <c>layer</c> index) carries two full-width
/// <see cref="Panel"/> bands anchored to the top and bottom of the
/// screen. This is the <b>first legitimate <see cref="CanvasLayer"/></b>
/// of the shell. A <see cref="CanvasLayer"/> has its own canvas transform
/// that is <i>not</i> affected by any 2D camera — so the HUD stays welded
/// to the screen no matter how the maquette pans inside its SubViewport.
/// That is exactly the behaviour we want for a permanent overlay, and it
/// is free: no per-frame work, no maths on the bands themselves.
/// <list type="bullet">
///   <item><b>Clipped behind the HUD.</b> Two independent guarantees that
///     the maquette never bleeds over the HUD. (1) <c>HudLayer.layer</c>
///     is higher than the default-layer maquette, so the HUD draws last.
///     (2) <see cref="_Ready"/> re-sizes the
///     <see cref="SubViewportContainer"/> to the <i>residual central
///     rectangle</i> — the screen minus both bands — so the maquette is
///     physically clipped to the gap between the bands. Belt and braces.</item>
///   <item><b>Placeholder, swap-ready.</b> The bands are
///     <see cref="Panel"/>s with a flat parchment <c>StyleBoxFlat</c>.
///     Mira's real parchment liseré (J3-assets) swaps in by replacing the
///     <c>theme_override_styles/panel</c> with a <see cref="TextureRect"/>
///     child — the band node names and rects do not change, the J7 / HUD
///     content milestones key off the same <c>HudTop</c> / <c>HudBottom</c>
///     nodes. The two test <see cref="Label"/>s are placeholders for that
///     future content (district name in J7, etc.).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Two pure-C# helpers, each Godot-free and
/// xUnit-pinned, carry the only arithmetic in this shell. (1) The pan
/// decision — when a drag is live, where the clamped camera centre lands
/// — is <see cref="MapViewportPanLogic"/> (J3a). (2) The residual
/// central rectangle the maquette viewport must occupy — screen minus the
/// two HUD bands — is <see cref="HudLayoutLogic"/> (J3b). This node is the
/// engine seam only: it converts <see cref="InputEvent"/>s into helper
/// calls and applies the helper results onto Godot nodes. Same
/// logic-vs-node split as J3-iso (<c>IsoBoard</c> ↔ <c>IsoProjection</c>)
/// and P8.2 (<c>E1WorldMap</c> ↔ <c>CameraPanLogic</c>).
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette
/// <see cref="SubViewportContainer"/> is a direct child of this
/// <see cref="Control"/> root — <b>never</b> under the
/// <c>HudLayer</c> <see cref="CanvasLayer"/>. A <see cref="CanvasLayer"/>
/// has its own canvas transform that ignores the SubViewport's
/// <see cref="Camera2D"/>; putting pannable world content under it would
/// silently break the pan (trap #1 in the wrong direction). The
/// <c>HudLayer</c> is the <i>only</i> <see cref="CanvasLayer"/> in this
/// scene and it carries <i>only</i> the HUD — fixed content is exactly
/// what a <see cref="CanvasLayer"/> is for (trap #1 in the right
/// direction).
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>, not <see cref="Node._UnhandledInput"/>. A
/// <see cref="SubViewportContainer"/> has <c>MouseFilter = Stop</c> and
/// would eat the motion events before <c>_UnhandledInput</c> ever fires —
/// the exact freeze of trap #9. <see cref="_Input"/> runs ahead of the
/// GUI routing and is immune to <c>mouse_filter</c>. Motion events are
/// <b>not</b> marked handled: J3a owns the pan <i>response</i>, not the
/// event — future hover / selection systems on this surface must still
/// see the same motion. The J3b HUD <see cref="Panel"/>s default to
/// <c>MouseFilter = Stop</c> too; that is harmless here (they sit on a
/// separate <see cref="CanvasLayer"/> and the pan is read in
/// <see cref="_Input"/>) and is the correct default for when the HUD
/// gains real buttons in a later milestone.
/// </para>
///
/// <para>
/// <b>Gesture mutual-exclusion (trap #5) — deliberately deferred.</b> J3a
/// has exactly one gesture (MMB-drag pan). There is no second gesture to
/// collide with yet, so no <c>SetInputAsHandled</c> suppression is wired.
/// When J3c-2 adds left-click pawn selection and J6 adds POI clicks, the
/// trap #5 defense (the screen holding a live drag consumes the
/// mutually-exclusive gesture's events) gets added <i>then</i>, against a
/// real second gesture — not speculatively now.
/// </para>
///
/// <para>
/// <b>Preflight (trap #2).</b> <see cref="_Ready"/> prints a fixed
/// diagnostics block — viewport size, content extent, clamp bounds,
/// camera start, the resolved pan button, and (J3b) the HUD band heights
/// plus the residual map rect with a loud warning if the bands leave no
/// usable centre. A pan, clipping, or HUD-layout bug is then visible in
/// the Godot Output on the first run.
/// </para>
/// </summary>
public partial class GameScreen : Control
{
    /// <summary>
    /// Autoload-injected opening flow must not draw its CanvasLayer over
    /// this scene when launched standalone for the J3a/J3b smoke
    /// (trap #1bis). The <c>.tscn</c> root carries
    /// <c>metadata/skip_opening_bootstrap</c>; this constant documents
    /// the contract for readers of the code.
    /// </summary>
    private const string SkipBootstrapMeta = "skip_opening_bootstrap";

    /// <summary>
    /// Fixed height of the top HUD band, pixels. Must match the
    /// <c>HudTop</c> panel's <c>offset_bottom</c> in
    /// <c>GameScreen.tscn</c>. Held here as the single source of truth so
    /// <see cref="HudLayoutLogic"/> sizes the residual map rect to exactly
    /// the band the scene draws. When Mira's parchment liseré lands the
    /// asset's authored height is propagated to both places at once.
    /// </summary>
    private const float TopBandHeight = 90f;

    /// <summary>
    /// Fixed height of the bottom HUD band, pixels. Must match the
    /// <c>HudBottom</c> panel's <c>-offset_top</c> in
    /// <c>GameScreen.tscn</c>. See <see cref="TopBandHeight"/>.
    /// </summary>
    private const float BottomBandHeight = 90f;

    private SubViewportContainer _mapContainer = null!;
    private SubViewport _mapViewport = null!;
    private Camera2D _mapCamera = null!;
    private IsoBoard _maquette = null!;
    private CanvasLayer _hudLayer = null!;

    private readonly MapViewportPanLogic _pan = new();

    public override void _Ready()
    {
        _mapContainer = GetNode<SubViewportContainer>("MapViewportContainer");
        _mapViewport = GetNode<SubViewport>("MapViewportContainer/MapViewport");
        _mapCamera = GetNode<Camera2D>("MapViewportContainer/MapViewport/MapCamera2D");
        _maquette = GetNode<IsoBoard>("MapViewportContainer/MapViewport/Maquette");
        _hudLayer = GetNode<CanvasLayer>("HudLayer");

        // J3b: size the map viewport to the residual central rectangle —
        // the screen minus the two fixed HUD bands. This is the second of
        // two guarantees that the maquette never bleeds over the HUD: the
        // HudLayer's higher layer index already draws the bands last, and
        // this re-size physically clips the maquette to the gap between
        // them. The maths is HudLayoutLogic (Godot-free, xUnit-pinned);
        // this node only applies the result to the SubViewportContainer's
        // offsets. The CanvasLayer HUD bands themselves need no code —
        // their .tscn anchors keep them welded to the screen edges.
        var screen = Size;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, TopBandHeight, BottomBandHeight);
        ApplyResidualRect(residual);

        // The SubViewport's render size follows the (now residual)
        // container rect, so the maquette is clipped exactly to the
        // central panel between the HUD bands. Stretch keeps the two in
        // lockstep.
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Park the camera centred on the maquette content, then clamp so
        // an authored-out-of-bounds start (or a content smaller than the
        // viewport) is corrected before the first frame.
        var content = ContentSize();
        var viewport = ViewportSize();
        var start = new Vector2(content.X * 0.5f, content.Y * 0.5f);
        var clampedStart = MapViewportPanLogic.ClampToContent(
            ToPan(start), ToPan(content), ToPan(viewport));
        _mapCamera.Position = ToGodot(clampedStart);

        Preflight(content, viewport, clampedStart, screen);
    }

    /// <summary>
    /// Input seam (trap #9): routes the middle-button drag-pan into
    /// <see cref="MapViewportPanLogic"/> and applies the clamped result
    /// onto the SubViewport's <see cref="Camera2D"/>.
    ///
    /// <para>
    /// Events arriving here are already in the <see cref="SubViewport"/>'s
    /// coordinate space because the <see cref="SubViewportContainer"/>
    /// forwards them transformed — so the helper receives maquette-space
    /// positions directly, no manual conversion.
    /// </para>
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton button
            && button.ButtonIndex == MouseButton.Middle)
        {
            var pos = ToPan(button.Position);
            var centre = ToPan(_mapCamera.Position);
            if (button.Pressed)
            {
                _pan.OnPress(pos, centre);
            }
            else
            {
                _pan.OnRelease(centre);
            }
            // The button press/release IS owned by the pan gesture —
            // mark it handled so a future click-select on this surface
            // does not also fire from the same MMB event.
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            var outcome = _pan.OnMotion(
                ToPan(motion.Position), ToPan(ContentSize()), ToPan(ViewportSize()));
            if (outcome.CameraMoved)
            {
                _mapCamera.Position = ToGodot(outcome.Centre);
            }
            // Deliberately NOT SetInputAsHandled on motion (trap #9): the
            // pan owns its response, not the motion event itself. Hover
            // and selection systems (J3c-2, J6) must still see motion.
        }
    }

    public override void _ExitTree()
    {
        // Cut any live drag cleanly so no ambiguous state survives a
        // scene change — same reset discipline as MapPanInputLogic.
        _pan.Reset();
    }

    /// <summary>
    /// Apply a <see cref="HudRect"/> (from <see cref="HudLayoutLogic"/>)
    /// onto the <see cref="SubViewportContainer"/>'s offsets. The
    /// container uses corner anchors at the screen origin in the
    /// <c>.tscn</c>, so positioning is offset-driven. Kept as one method
    /// so the J3b residual-rect application has a single, named seam — a
    /// future window-resize handler would call exactly this.
    /// </summary>
    private void ApplyResidualRect(HudRect rect)
    {
        _mapContainer.OffsetLeft = rect.X;
        _mapContainer.OffsetTop = rect.Y;
        _mapContainer.OffsetRight = rect.X + rect.Width;
        _mapContainer.OffsetBottom = rect.Y + rect.Height;
    }

    /// <summary>
    /// The pannable maquette content extent in world pixels. Prefers the
    /// real <c>IsoBoard</c> floor bitmap size; falls back to the
    /// placeholder iso-diamond bounding box while Mira's district asset
    /// is not yet wired (J2). The clamp is computed against this extent
    /// so the camera never pans past the maquette into the void.
    /// </summary>
    private Vector2 ContentSize()
    {
        var texture = _maquette.GetBackgroundTextureSizeOrZero();
        if (texture != Vector2.Zero)
        {
            return texture;
        }

        // Placeholder: the iso-diamond of the IsoBoard's placeholder grid
        // spans (w+h) half-widths across and (w+h) half-heights tall.
        int w = _maquette.PlaceholderGridWidth;
        int h = _maquette.PlaceholderGridHeight;
        float halfW = _maquette.TileWidthPx * 0.5f;
        float halfH = _maquette.TileWidthPx * 0.25f; // 2:1 ratio
        return new Vector2((w + h) * halfW, (w + h) * halfH);
    }

    /// <summary>The SubViewport visible-rect size in world pixels.</summary>
    private Vector2 ViewportSize() => _mapContainer.Size;

    /// <summary>Trap #2 preflight: a fixed diagnostics block at <see cref="_Ready"/>.</summary>
    private void Preflight(Vector2 content, Vector2 viewport, PanVec2 cameraStart, Vector2 screen)
    {
        GD.Print($"[GameScreen] preflight: skip_bootstrap=" +
                 $"{HasMeta(SkipBootstrapMeta)}");
        GD.Print($"[GameScreen] preflight: SubViewport size={_mapViewport.Size} " +
                 $"container rect={_mapContainer.Size}");
        GD.Print($"[GameScreen] preflight: maquette content extent={content} " +
                 $"(from {( _maquette.GetBackgroundTextureSizeOrZero() != Vector2.Zero ? "bitmap" : "placeholder grid")})");
        // Mirror the clamp the runtime will enforce so a too-small content
        // or a bad start is visible in the log, not at the first drag.
        float halfVx = viewport.X * 0.5f;
        float halfVy = viewport.Y * 0.5f;
        GD.Print($"[GameScreen] preflight: camera clamp X=[{halfVx},{content.X - halfVx}] " +
                 $"Y=[{halfVy},{content.Y - halfVy}] start={cameraStart}");
        GD.Print($"[GameScreen] preflight: pan button={MapViewportPanLogic.PanButton} " +
                 $"state={_pan.State} -- _Input wired (trap #9)");

        // J3b HUD diagnostics: the CanvasLayer index, the band heights,
        // and the residual map rect. A collapsed residual (HUD bands
        // larger than the screen) is a design error — shout it.
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, TopBandHeight, BottomBandHeight);
        GD.Print($"[GameScreen] preflight: HudLayer layer={_hudLayer.Layer} " +
                 $"bands top={TopBandHeight} bottom={BottomBandHeight} " +
                 $"-- CanvasLayer overlay (trap #1, legitimate)");
        GD.Print($"[GameScreen] preflight: residual map rect=" +
                 $"[{residual.X},{residual.Y} {residual.Width}x{residual.Height}]");
        if (!HudLayoutLogic.HasUsableResidual(
                screen.X, screen.Y, TopBandHeight, BottomBandHeight))
        {
            GD.PushWarning("[GameScreen] HUD bands leave no usable central " +
                           "rectangle — the maquette would be invisible.");
        }
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 ----------------------------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);
}
