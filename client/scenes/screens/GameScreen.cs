using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// Root of the Game Screen Shell — the cartographer's-desk diorama in
/// which eM and eT are shown (roadmap <c>wayfinders-mvp-roadmap-eM-eT</c>).
/// As of J3c-1 this slice stands up three layers of the diorama: the
/// <b>clipped, pannable map maquette</b> (J3a), the <b>fixed top/bottom
/// HUD frame</b> (J3b), and the <b>static iso desk</b> (J3c-1). The desk
/// interactivity (pawn selection / placement, J3c-2) is a later milestone
/// and is not present here.
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
///   <item><b>Slim 32 px bands (2026-05-21, Didier).</b> The two bands
///     were re-authored from 90 px to <see cref="HudBandHeight"/> = 32 px,
///     so the residual map rect grows to <c>[0,32 1920x1016]</c>. The
///     single source of truth for that height is <see cref="HudBandHeight"/>
///     — both the top and the bottom band read it, and the
///     <c>GameScreen.tscn</c> panel offsets are authored to the same value.</item>
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
/// <b>What J3c-1 adds — the static iso desk.</b> A <i>second</i>
/// <see cref="SubViewportContainer"/> (<c>DeskViewportContainer</c>),
/// parked bottom-left, hosting its own <see cref="SubViewport"/>, its own
/// <see cref="Camera2D"/> (<c>DeskCamera2D</c>) and its own
/// <c>IsoBoard.tscn</c> instance (<c>DeskBoard</c>). It is a genuine
/// <b>second iso space</b> at a larger iso scale than the maquette — the
/// Company pawns read big, foreground.
/// <list type="bullet">
///   <item><b>Two SubViewports, not one</b> (roadmap §"Choix techniques
///     Godot"). Two iso scales + two camera behaviours (the maquette
///     pans, the desk is fixed) + two clippings = two render worlds. A
///     single SubViewport would force hand-scaling nodes and juggling two
///     iso origins. Two concrete nodes, no <c>IsoSpace</c> abstraction —
///     the anti-over-architecture line of the roadmap.</item>
///   <item><b>The desk camera is immobile.</b> <c>DeskCamera2D</c> is
///     parked once in <see cref="ConfigureDesk"/> on the centroid of the
///     Company slot block and never moved — only the maquette pans
///     (roadmap J3c-1).</item>
///   <item><b>Larger iso scale.</b> The desk board's <c>TileWidthPx</c>
///     is authored larger than the maquette's, so the desk cells — and
///     the pawns on them — read big in the foreground. The illusion of
///     "two scales" comes from two boards with different tile sizes, not
///     from hand-scaling any node.</item>
///   <item><b>Static.</b> J3c-1 lays out the ~7 Company slots
///     (<see cref="DeskSlotLayoutLogic"/>, Godot-free, xUnit-pinned) and
///     places one placeholder <see cref="DeskCompanyPawn"/> on each.
///     Nothing is selectable or draggable yet — that is J3c-2.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Three pure-C# helpers, each Godot-free and
/// xUnit-pinned, carry the only arithmetic in this shell. (1) The pan
/// decision — when a drag is live, where the clamped camera centre lands
/// — is <see cref="MapViewportPanLogic"/> (J3a). (2) The residual
/// central rectangle the maquette viewport must occupy — screen minus the
/// two HUD bands — is <see cref="HudLayoutLogic"/> (J3b). (3) The Company
/// slot layout — which iso cell each of the ~7 pawns sits on — is
/// <see cref="DeskSlotLayoutLogic"/> (J3c-1). This node is the engine
/// seam only: it converts <see cref="InputEvent"/>s into helper calls and
/// applies the helper results onto Godot nodes. Same logic-vs-node split
/// as J3-iso (<c>IsoBoard</c> ↔ <c>IsoProjection</c>) and P8.2
/// (<c>E1WorldMap</c> ↔ <c>CameraPanLogic</c>).
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> Both
/// <see cref="SubViewportContainer"/>s (maquette and desk) are direct
/// children of this <see cref="Control"/> root — <b>never</b> under the
/// <c>HudLayer</c> <see cref="CanvasLayer"/>. A <see cref="CanvasLayer"/>
/// has its own canvas transform that ignores a SubViewport's
/// <see cref="Camera2D"/>; putting pannable world content under it would
/// silently break the pan (trap #1 in the wrong direction). The
/// <c>HudLayer</c> is the <i>only</i> <see cref="CanvasLayer"/> in this
/// scene and it carries <i>only</i> the HUD — fixed content is exactly
/// what a <see cref="CanvasLayer"/> is for (trap #1 in the right
/// direction). The desk sits <i>behind</i> the HUD by tree order plus the
/// HUD's higher layer index — no maths needed.
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
/// gains real buttons in a later milestone. The desk
/// <see cref="SubViewportContainer"/> has its <c>MouseFilter</c> set to
/// <c>Ignore</c> in <see cref="_Ready"/> for J3c-1: the static desk takes
/// no input, and an <c>Ignore</c> filter means an MMB press over the desk
/// corner still reaches the maquette pan. J3c-2 will flip this to
/// <c>Stop</c> when the desk gains pawn selection.
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
/// camera start, the resolved pan button, the HUD band heights plus the
/// residual map rect, and (J3c-1) the desk viewport rect plus the
/// resolved Company slot cells. A pan, clipping, HUD-layout, or desk-slot
/// bug is then visible in the Godot Output on the first run.
/// </para>
/// </summary>
public partial class GameScreen : Control
{
    /// <summary>
    /// Autoload-injected opening flow must not draw its CanvasLayer over
    /// this scene when launched standalone for the J3a/J3b/J3c smoke
    /// (trap #1bis). The <c>.tscn</c> root carries
    /// <c>metadata/skip_opening_bootstrap</c>; this constant documents
    /// the contract for readers of the code.
    /// </summary>
    private const string SkipBootstrapMeta = "skip_opening_bootstrap";

    /// <summary>
    /// Fixed height of each HUD band, pixels — the same value for the top
    /// and the bottom band. <b>The single named source of truth</b> for
    /// the band height: it must match the <c>HudTop</c> panel's
    /// <c>offset_bottom</c> and the <c>HudBottom</c> panel's
    /// <c>-offset_top</c> in <c>GameScreen.tscn</c>. Held here so
    /// <see cref="HudLayoutLogic"/> sizes the residual map rect to exactly
    /// the band the scene draws, and so the magic number is not duplicated
    /// across the top/bottom pair. When Mira's parchment liseré lands the
    /// asset's authored height is propagated from this one constant.
    ///
    /// <para>
    /// Adjusted 2026-05-21 (Didier) from 90 px to 32 px — slim bands. The
    /// residual map rect grows accordingly to
    /// <c>[0,32 1920x1016]</c> on a 1920x1080 screen.
    /// </para>
    /// </summary>
    private const float HudBandHeight = 32f;

    private SubViewportContainer _mapContainer = null!;
    private SubViewport _mapViewport = null!;
    private Camera2D _mapCamera = null!;
    private IsoBoard _maquette = null!;
    private CanvasLayer _hudLayer = null!;

    private SubViewportContainer _deskContainer = null!;
    private SubViewport _deskViewport = null!;
    private Camera2D _deskCamera = null!;
    private IsoBoard _deskBoard = null!;

    private readonly MapViewportPanLogic _pan = new();

    public override void _Ready()
    {
        _mapContainer = GetNode<SubViewportContainer>("MapViewportContainer");
        _mapViewport = GetNode<SubViewport>("MapViewportContainer/MapViewport");
        _mapCamera = GetNode<Camera2D>("MapViewportContainer/MapViewport/MapCamera2D");
        _maquette = GetNode<IsoBoard>("MapViewportContainer/MapViewport/Maquette");
        _hudLayer = GetNode<CanvasLayer>("HudLayer");

        _deskContainer = GetNode<SubViewportContainer>("DeskViewportContainer");
        _deskViewport = GetNode<SubViewport>("DeskViewportContainer/DeskViewport");
        _deskCamera = GetNode<Camera2D>("DeskViewportContainer/DeskViewport/DeskCamera2D");
        _deskBoard = GetNode<IsoBoard>("DeskViewportContainer/DeskViewport/DeskBoard");

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
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
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

        ConfigureDesk();

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
    /// J3c-1: stand up the static iso desk. Sizes the
    /// <c>DeskViewport</c>'s render target to its container rect (so the
    /// desk is clipped to the bottom-left panel), parks the desk camera
    /// centred on the slot block (the desk does NOT pan — the camera is
    /// set once here and never moved), and places one placeholder pawn on
    /// each Company slot via <see cref="DeskSlotLayoutLogic"/>.
    ///
    /// <para>
    /// The slot cells are pure-C# maths (Godot-free, xUnit-pinned); this
    /// method only converts each <c>TileCoordinate</c> to a Godot pixel
    /// through the desk board's own <see cref="IsoProjection"/> and
    /// instances a <see cref="DeskCompanyPawn"/> there. No pawn carries
    /// game state — J3c-1 is static; selection / placement / the
    /// authoritative <c>GameState</c> occupancy are J3c-2.
    /// </para>
    /// </summary>
    private void ConfigureDesk()
    {
        // The desk viewport render size follows its container rect, the
        // same stretch lockstep as the maquette — the desk content is
        // clipped exactly to the bottom-left panel.
        _deskViewport.Size = (Vector2I)_deskContainer.Size;

        // The static desk takes no input in J3c-1. An Ignore filter means
        // an MMB press landing over the desk corner still reaches the
        // maquette pan underneath. J3c-2 flips this to Stop for selection.
        _deskContainer.MouseFilter = MouseFilterEnum.Ignore;

        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // Park the desk camera on the centroid of the slot cells so the
        // whole Company block is framed. Computed once — the desk camera
        // is immobile by design (only the maquette pans).
        var centroidCell = DeskSlotLayoutLogic.SlotBlockCentroidCell(slots);
        _deskCamera.Position = _deskBoard.CellToPixel(
            new Vector2I(centroidCell.Col, centroidCell.Row));

        // One placeholder pawn per slot. The leader slot (index 0) renders
        // slightly larger — foreground-prominent — per the mockup; the
        // rest are the formation behind it. Y-sort on the IsoBoard's
        // Occupants node keeps a pawn lower on screen drawing over one
        // higher up.
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var pawn = new DeskCompanyPawn
            {
                Name = $"DeskPawn{i}",
                Position = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row)),
            };
            pawn.Configure(
                slotIndex: i,
                isLeader: DeskSlotLayoutLogic.IsLeaderSlot(i));
            _deskBoard.AddOccupant(pawn);
        }
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
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        GD.Print($"[GameScreen] preflight: HudLayer layer={_hudLayer.Layer} " +
                 $"bands top={HudBandHeight} bottom={HudBandHeight} " +
                 $"-- CanvasLayer overlay (trap #1, legitimate)");
        GD.Print($"[GameScreen] preflight: residual map rect=" +
                 $"[{residual.X},{residual.Y} {residual.Width}x{residual.Height}]");
        if (!HudLayoutLogic.HasUsableResidual(
                screen.X, screen.Y, HudBandHeight, HudBandHeight))
        {
            GD.PushWarning("[GameScreen] HUD bands leave no usable central " +
                           "rectangle — the maquette would be invisible.");
        }

        // J3c-1 desk diagnostics: the second SubViewport rect, the desk
        // iso scale vs the maquette scale, and the resolved Company slot
        // cells. A wrong desk viewport size or a slot count drift
        // surfaces here on first run.
        var slots = DeskSlotLayoutLogic.CompanySlotCells();
        GD.Print($"[GameScreen] preflight: DeskViewport size={_deskViewport.Size} " +
                 $"container rect={_deskContainer.Size} " +
                 $"mouseFilter={_deskContainer.MouseFilter} (J3c-1 static)");
        GD.Print($"[GameScreen] preflight: desk iso tile={_deskBoard.TileWidthPx}px " +
                 $"(maquette tile={_maquette.TileWidthPx}px) " +
                 $"-- two iso scales, distinct SubViewports");
        GD.Print($"[GameScreen] preflight: Company slots count={slots.Count} " +
                 $"leader=slot{DeskSlotLayoutLogic.LeaderSlotIndex} " +
                 $"cells=[{string.Join(", ", slots)}]");
        if (slots.Count != DeskSlotLayoutLogic.CompanySlotCount)
        {
            GD.PushWarning("[GameScreen] Company slot count drifted from " +
                           $"{DeskSlotLayoutLogic.CompanySlotCount}.");
        }
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 ----------------------------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);
}
