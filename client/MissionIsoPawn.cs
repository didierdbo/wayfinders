using Godot;

// Iso-projected mission scene rendered as a board element anchored to a cell of
// PawnLayer. Hidden at rest; shown when the player selects a mission cell on
// the world map; hidden again when the selection clears.
//
// Generic by intent: one instance per POI / mission scene on the iso board.
// The texture is currently hardcoded to the Halfgate art (e1.halfgate.768x512)
// because Halfgate is the only painted scene at J2. J3b/c will introduce a
// texture-swap path (and/or [Export] Texture2D) so the same MissionIsoPawn
// class can back any POI.
//
// Rationale for living on PawnLayer (and not a CanvasLayer overlay):
//   The board is an iso scene tree. Every visible "thing" on the board — ground,
//   future pawns, mission detail art — is a child of the same iso grid so that
//   movement reduces to "snap to a cell". A future J3+ transition like "the
//   panel slides across to a new resting spot" becomes a single MapToLocal()
//   call instead of bespoke viewport-coords math.
//
// The 768x512 art is much larger than one iso cell (256x128). That's expected:
//   a sprite anchored to a cell is allowed to extend well beyond its anchor.
//   Y-sort (enabled on PawnLayer) will use the sprite's anchor Y to decide its
//   draw order relative to other board elements, so overflow doesn't break
//   layering as long as the anchor cell is chosen sensibly.
//
// J3a additions:
//   - [GlobalClass] so Godot registers MissionIsoPawn as a first-class custom
//     type in the editor. Without it, a node typed as Sprite2D with this script
//     attached can have its [Export] properties hidden in the inspector after a
//     rebuild — the inspector caches the engine-type property list and doesn't
//     always pick up newly-added script Exports until the scene is fully
//     reloaded. [GlobalClass] makes the script the node's authoritative type
//     and the inspector refresh becomes reliable.
//   - RestCell and EngagedCell are [Export]ed so the designer can re-anchor
//     from the Godot inspector without recompiling C#.
//
// J3b additions:
//   - [Signal] Engaged / Released, emitted at the *start* of the glide (when
//     the engaged state flips), not at the end. The intent is that downstream
//     listeners (CharacterIsoPawn, MissionInfoPanel) animate in simultaneously
//     with the pawn's motion — emitting on tween completion would feel laggy.
//     The signals are pure state-change notifications: they carry no payload,
//     because everything a listener needs (which POI, which character) is
//     already known by Main (the dispatcher).
//   - Hide() emits Released if the pawn was engaged at the moment of hide.
//     This covers the edge case where the user clicks a different world cell
//     while the pawn is engaged: Main.OnMissionCellSelected routes to a
//     different POI -> calls Hide() on this pawn -> engaged state is forcibly
//     cleared without ever passing through the _Input release path that would
//     normally emit Released. Without this guard, satellites (CharacterIsoPawn,
//     panel) would remain visible after the pawn vanished.
//
// J4b refactor — click-driven engagement (was: hover-driven):
//   The hover model caused a ping-pong: the HG pawn art (768x512) is much
//   larger than its anchor cell, so its pixel-perfect hit-test zone visually
//   overlaps the character pawn that appears on Engaged. Hovering the
//   character re-entered the pawn's hit zone, re-engaged, character moved,
//   no longer under the cursor, released, re-hovered, ad nauseam.
//
//   New model:
//     - Engagement is a *click toggle* on the pawn's painted pixels (still
//       sampled pixel-perfect at the RestCell projection, same fixed hit-test
//       as before — the hit zone never moves).
//     - First click on the painted silhouette  -> _isEngaged=true, glide to
//       EngagedCell, emit Engaged, consume event.
//     - Click on the painted silhouette while engaged -> _isEngaged=false,
//       glide back to RestCell, emit Released, consume event (toggle).
//     - Click *outside* the painted silhouette while engaged -> _isEngaged=
//       false, glide back to RestCell, emit Released, but do NOT consume the
//       event. This lets MissionGridOverlay process its own selection logic
//       (click on another POI = select that one; click on empty world =
//       deselect). The result is the natural "click away to dismiss" UX
//       without stepping on the overlay's responsibilities.
//
//   Input-ordering note:
//     _Input (not _UnhandledInput) so we run before WorldLayer's pan/drag
//     starter (which is on _UnhandledInput). _UnhandledInput would see the
//     click first and start a pan when the user clicks the pawn to engage it.
//
//     We share _Input with MissionGridOverlay. The contract that keeps us
//     from stepping on each other is consumption-on-hit:
//       - We only consume clicks that hit the pawn's painted pixels.
//       - All other clicks (miss, while invisible, while not engaged) pass
//         through untouched. MissionGridOverlay then runs its own hit-test
//         against the world grid and acts on its own terms.
//     This works regardless of which node's _Input Godot calls first: either
//     we consume because the click is ours, or we leave the event alone for
//     the overlay to claim.
[GlobalClass]
public partial class MissionIsoPawn : Sprite2D
{
	// Fired the instant the engaged state transitions false -> true (user
	// clicked the painted silhouette while Visible). Companions (character,
	// panel) should Show() themselves on this signal so they appear in
	// lockstep with the pawn's glide toward EngagedCell.
	[Signal] public delegate void EngagedEventHandler();

	// Fired the instant the engaged state transitions true -> false. Three
	// trigger paths:
	//   1. Click on the painted silhouette while engaged (toggle off).
	//   2. Click anywhere else on the screen while engaged (click-away dismiss).
	//   3. Hide() called by Main while the pawn was engaged (selection routed
	//      to a different POI — see Hide() comment).
	// Companions should Hide() themselves on this signal.
	[Signal] public delegate void ReleasedEventHandler();

	// Painted by Mira; final art, no pipeline churn expected at J2.
	private const string TexturePath = "res://assets/world/e1/e1_halfgate_panel.png";

	// Default resting cell on the PawnLayer, chosen visually to sit just west of
	// the eastern point of the iso board (the "right triangle"). The board's
	// occupied cells span x in [-5, 3], y in [-1, 7]; (0, 3) is mid-height, well
	// inside the board, two columns west of the easternmost column. Designer can
	// re-anchor by editing the value in the inspector — no recompile.
	//
	// [Export] note: Godot's C# binding reads the inspector value *before*
	// _Ready() runs, so reading RestCell in _Ready() returns the designer-set
	// value (not the C# default initializer). We deliberately avoid static
	// readonly here for that reason.
	[Export] public Vector2I RestCell { get; set; } = new Vector2I(0, 3);

	// Where the pawn glides on engage. Default is 2 cells east of RestCell —
	// in iso coords (x+2, y+0) shifts the sprite roughly one tile-width to the
	// screen-right and slightly down, freeing the screen-left side where the
	// recruitment character (J3b) and the mission panel (J3c) land.
	// Tune in the inspector if the companion layout shifts.
	[Export] public Vector2I EngagedCell { get; set; } = new Vector2I(2, 3);

	// Tween parameters. 220ms is short enough to feel responsive (no lag between
	// click and motion) but long enough to read as deliberate motion, not a
	// teleport. EaseOut + Cubic gives the "snap forward, settle softly" feel
	// that fits a pawn-on-board metaphor better than linear or sine.
	private const double TweenDurationSec = 0.22;
	private const Tween.TransitionType TweenTransition = Tween.TransitionType.Cubic;
	private const Tween.EaseType TweenEase = Tween.EaseType.Out;

	// Alpha threshold for the hit-test. 0.1 (= 25/255) discards anti-aliased
	// fringe pixels that visually read as "outside the sprite" but technically
	// carry a few percent of alpha. Tune up if clicks feel too generous on
	// soft edges, down if they feel too strict on detail work.
	private const float AlphaHitThreshold = 0.1f;

	private TileMapLayer _pawnLayer = null!;

	// Cached CPU-side copy of the sprite's texture. Texture2D.GetImage() is a
	// GPU->CPU readback — calling it per click would be wasteful. We cache once
	// in _Ready and re-use for every alpha lookup. The cache is fine to hold
	// for the node's lifetime: the texture is a const asset, it never swaps
	// at runtime.
	private Image _alphaImage = null!;

	// Pre-computed viewport-space center of the sprite *as if it were at RestCell*.
	// The click hit-test is anchored to this fixed point, never to the sprite's
	// moving Position. See class comment ("fixed hit-test zone") for the why.
	private Vector2 _restGlobalCenter;

	private bool _isEngaged;
	private Tween _activeTween;

	public override void _Ready()
	{
		// Defensive: if the scene file forgot to assign the texture, load it.
		// Cheap idempotent guard; lets the scene file stay authoritative when
		// it does the right thing.
		if (Texture == null)
		{
			Texture = GD.Load<Texture2D>(TexturePath);
		}

		// Cache the CPU-side image for alpha hit-testing. GetImage() pulls
		// pixel data back from the GPU — expensive enough that we want to do
		// it exactly once per node lifetime, never inside _Input.
		_alphaImage = Texture.GetImage();
		if (_alphaImage == null)
		{
			GD.PushError("MissionIsoPawn: Texture.GetImage() returned null; alpha hit-test will fail open.");
		}

		// 768x512 with Sprite2D's default Centered=true. The anchor (Position) is
		// the *center* of the sprite, snapped to the MapToLocal of RestCell.

		_pawnLayer = GetParent<TileMapLayer>();
		if (_pawnLayer == null)
		{
			GD.PushError("MissionIsoPawn must be a child of a TileMapLayer (the PawnLayer).");
			return;
		}

		MoveToCell(RestCell);
		CacheRestGlobalCenter();

		// Hidden until the player selects the mission cell.
		Visible = false;
	}

	// Snap to a new iso cell, instantly (no Tween). Used for initial placement
	// and for the Hide() reset path. Engage/release transitions use TweenToCell.
	public void MoveToCell(Vector2I cell)
	{
		if (_pawnLayer == null)
		{
			return;
		}
		// MapToLocal returns the *center* of the cell in the TileMapLayer's local
		// coords, which is exactly what we want for a centered Sprite2D anchor.
		Position = _pawnLayer.MapToLocal(cell);
	}

	// Visibility — the public surface used by Main from selection signals.
	// Show() does not touch position: the pawn keeps whatever cell it was
	// placed on (RestCell after a fresh Hide+Show cycle, see Hide() below).
	// Hide() snaps back to RestCell so the next Show() always opens at rest,
	// even if the user dismissed the panel from an engaged state.
	public void Show() => Visible = true;

	public void Hide()
	{
		// Capture the engaged state *before* we mutate it, so the Released
		// emission below reflects the situation as seen by the user (the pawn
		// was visibly in the engaged position) rather than the post-reset
		// internal state. This is the J3b edge-case fix: clicking a different
		// world cell while the pawn was engaged bypasses the _Input release
		// path entirely; without emitting here, the companion character would
		// stay visible after the pawn vanished.
		bool wasEngaged = _isEngaged;

		Visible = false;

		// Kill any in-flight tween so it doesn't keep mutating Position after
		// we've hidden the node (cosmetic, but it also prevents the pawn from
		// re-appearing mid-glide on the next Show()).
		if (_activeTween != null && _activeTween.IsValid())
		{
			_activeTween.Kill();
			_activeTween = null;
		}

		// Reset engaged state and snap back to rest so the next Show() starts
		// from a known good position. No animation — the user can't see it
		// happen anyway (Visible=false).
		_isEngaged = false;
		MoveToCell(RestCell);

		if (wasEngaged)
		{
			EmitSignal(SignalName.Released);
		}
	}

	// Click-driven engagement. See class header for the full state machine.
	//
	// _Input (not _UnhandledInput): we must run before WorldLayer's pan/drag
	// starter (which sits on _UnhandledInput). Otherwise a click on the pawn
	// to engage it would also trigger a map pan.
	//
	// MissionGridOverlay also runs on _Input. We coexist by consuming *only*
	// the events that hit the pawn's painted pixels. Any other click passes
	// through to the overlay (and onward to WorldLayer if the overlay also
	// declines to consume it).
	public override void _Input(InputEvent @event)
	{
		if (!Visible)
		{
			return;
		}
		if (@event is not InputEventMouseButton mb)
		{
			return;
		}
		if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
		{
			return;
		}

		bool hitPawn = IsMouseOnPaintedPixel(mb.Position);

		if (hitPawn)
		{
			// Toggle. First click engages, second click releases. Either way we
			// consume the event: the click was unambiguously on the pawn, so
			// neither the overlay (selection) nor WorldLayer (pan) should also
			// react to it.
			_isEngaged = !_isEngaged;
			TweenToCell(_isEngaged ? EngagedCell : RestCell);
			EmitSignal(_isEngaged ? SignalName.Engaged : SignalName.Released);
			GetViewport().SetInputAsHandled();
			return;
		}

		// Miss on the pawn. If we're engaged, this is the click-away-to-dismiss
		// path: collapse back to RestCell and notify companions, but do NOT
		// consume the event. The overlay will then process the same click —
		// it might land on another POI cell (-> select it; Main will then
		// route the new POI's pawn into Show()) or on empty world (-> overlay
		// emits MissionCellDeselected, which already does the right thing).
		if (_isEngaged)
		{
			_isEngaged = false;
			TweenToCell(RestCell);
			EmitSignal(SignalName.Released);
			// No SetInputAsHandled — by design.
		}
	}

	// Cache the viewport-space position of the sprite's center *as if it were at
	// RestCell*. This is the anchor for the fixed hit-test zone. We compute it
	// from the parent's GlobalTransform composed with the rest cell's local
	// position, instead of from this.GlobalTransform, precisely because
	// this.Position moves during tweens and we want a stable reference.
	private void CacheRestGlobalCenter()
	{
		if (_pawnLayer == null)
		{
			return;
		}
		Vector2 restLocal = _pawnLayer.MapToLocal(RestCell);
		_restGlobalCenter = _pawnLayer.GlobalTransform * restLocal;
	}

	// Convert the viewport-space mouse position into a pixel coordinate inside
	// the texture *as projected from the RestCell position*, then sample the
	// alpha channel.
	//
	// Anchoring to RestCell (a fixed cell) rather than the sprite's current
	// Position means the hit-zone is stable: the user sees the pawn glide to
	// EngagedCell as a visual response, but the clickable region stays put.
	// This was load-bearing in the hover era (prevented ping-pong); it's still
	// the right model for click because it means "the user clicks where the
	// painted art *originally* was", which is a more forgiving target than a
	// moving silhouette.
	//
	// The math:
	//   - mouse.Position is viewport-space.
	//   - _restGlobalCenter is the viewport-space center of the sprite as if it
	//     were at RestCell (cached in _Ready; PawnLayer's transform doesn't
	//     change at runtime in this scene, so re-caching isn't needed).
	//   - Subtracting gives an offset in viewport pixels from the rest-center.
	//     Sprite2D is unscaled and unrotated here (the parent chain is too, in
	//     this scene), so viewport pixels equal texture pixels 1:1.
	//   - Add (width/2, height/2) to shift origin from center to top-left.
	//   - Bounds-reject before sampling so we don't call GetPixel out of range.
	//
	// If the parent chain ever picks up a non-identity scale or rotation, this
	// math needs an AffineInverse step against the *rest* transform (compose
	// PawnLayer.GlobalTransform with a translation of RestCell's local pos).
	// Today, identity in the relevant chain, so the straight-line math wins.
	private bool IsMouseOnPaintedPixel(Vector2 viewportPos)
	{
		if (_alphaImage == null)
		{
			// Fail-open: if the cache is missing we treat the whole bbox as
			// clickable rather than killing the feature outright. The earlier
			// PushError in _Ready will have flagged the misconfiguration.
			return true;
		}

		Vector2 fromRestCenter = viewportPos - _restGlobalCenter;
		int px = (int)(fromRestCenter.X + _alphaImage.GetWidth()  * 0.5f);
		int py = (int)(fromRestCenter.Y + _alphaImage.GetHeight() * 0.5f);

		if (px < 0 || py < 0 || px >= _alphaImage.GetWidth() || py >= _alphaImage.GetHeight())
		{
			return false;
		}

		return _alphaImage.GetPixel(px, py).A > AlphaHitThreshold;
	}

	// Animate Position from wherever it currently is to the center of `cell`.
	// Killing the previous tween (if any) is the key correctness step — without
	// it, two overlapping tweens fight for control of Position and the sprite
	// jitters during fast click sequences.
	private void TweenToCell(Vector2I cell)
	{
		if (_pawnLayer == null)
		{
			return;
		}

		if (_activeTween != null && _activeTween.IsValid())
		{
			_activeTween.Kill();
		}

		Vector2 target = _pawnLayer.MapToLocal(cell);
		_activeTween = CreateTween();
		_activeTween
			.TweenProperty(this, "position", target, TweenDurationSec)
			.SetTrans(TweenTransition)
			.SetEase(TweenEase);
	}
}
