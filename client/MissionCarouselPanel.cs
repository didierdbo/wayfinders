using System.Collections.Generic;
using Godot;

// J7: a 3-slot carousel that displays the user's currently-visible mission
// alongside the previous and next missions in the ActiveMissions list. The
// active slot is full-size, full-opacity; the prev/next slots are scaled and
// dimmed so the user reads them as secondary. Wheel-scroll (handled by Main)
// or click on a side slot rotates the index; the carousel rebinds its three
// fixed slots to the new (prev, active, next) triplet and fades the change in
// with a short tween.
//
// Why a fixed pool of three slots (not a dynamic list of cards):
//   The visible width is always at most three cards (prev / active / next).
//   Keeping the pool static means we never instantiate or free MissionCard
//   nodes at runtime — we just rebind their text + character + visibility.
//   Two wins: (a) no QueueFree / leak risk on Node lifetimes, (b) signal /
//   subscription wiring is set up once in _Ready and never churns. Concrete
//   first: when a 5-card carousel becomes a real requirement we pool five.
//   Until then, three.
//
// Why MissionCard hosts the CharacterIsoPawn as a child (instead of all six
// nodes being siblings under this carousel):
//   The card and its character form a single visual unit — same Scale, same
//   Modulate.A, same position offset. By nesting the pawn under the card we
//   get all three of those for free via parent-transform inheritance. A
//   single tween on the card animates everything. Six siblings would need
//   six synchronised tweens to keep alpha and scale in lockstep, with the
//   classic Godot footgun where a missed frame on one of them makes the slot
//   look broken for 16ms.
//
// Why this is a Node2D, not a Control:
//   Same reason as MissionCard: lives in the iso scene tree as a child of
//   PawnLayer/BoardRoot, inherits the BoardRoot transform for free. The
//   carousel positions itself at the carousel anchor cell on PawnLayer; each
//   child card then offsets itself by SlotOffset within that local space.
//
// State ownership:
//   This carousel is a PASSIVE VIEW. It does NOT own the active mission index;
//   it does NOT own the ActiveMissions list. Main owns both. The carousel
//   exposes SetActiveMission(missions, index) for full rebinds and
//   TryNavigate(delta) for index changes driven by Main's wheel handler.
//   Single-source-of-state discipline mirrors the rest of the M1 client.
//
// [GlobalClass] for the same Godot-inspector caching reason as the other
// custom Node2D scripts in this scene.
[GlobalClass]
public partial class MissionCarouselPanel : Node2D
{
	// Anchor cell on PawnLayer. The active slot lands here; prev/next slots
	// offset themselves by SlotOffset (set on the cards in the inspector).
	// Designer re-anchors via inspector without recompiling.
	[Export] public Vector2I HomeCell { get; set; } = new Vector2I(0, 1);

	// Tween duration for the rotation animation. 180ms matches the cubic-out
	// feel of MissionIsoPawn's 220ms glide (J2) — fast enough to feel snappy,
	// slow enough that the user perceives a rotation event rather than a
	// teleport. Cubic-out is the easing curve: snappy start, gentle settle.
	[Export] public float TweenDurationSec { get; set; } = 0.18f;

	// NodePaths to the three slot cards. Wired in the .tscn (the inspector
	// shows three NodePath dropdowns). NodePath rather than direct typed
	// references because GetNode<T>() at _Ready time is the standard Godot
	// pattern for scene-internal lookups; the indirection lets the .tscn own
	// the wiring without forcing a [Tool] script. Marked nullable to satisfy
	// the compiler — Godot assigns from the .tscn before _Ready, but the
	// constructor itself doesn't see a value; _Ready does the null check.
	[Export] public NodePath? CardPrevPath { get; set; }
	[Export] public NodePath? CardActivePath { get; set; }
	[Export] public NodePath? CardNextPath { get; set; }

	// NodePaths to each slot's CharacterIsoPawn child. Same rationale.
	[Export] public NodePath? PawnPrevPath { get; set; }
	[Export] public NodePath? PawnActivePath { get; set; }
	[Export] public NodePath? PawnNextPath { get; set; }

	// Resolved references — cached in _Ready, used everywhere else.
	private MissionCard _cardPrev = null!;
	private MissionCard _cardActive = null!;
	private MissionCard _cardNext = null!;
	private CharacterIsoPawn _pawnPrev = null!;
	private CharacterIsoPawn _pawnActive = null!;
	private CharacterIsoPawn _pawnNext = null!;

	private TileMapLayer _pawnLayer = null!;

	// Local cache of the (missions, activeIndex) Main most recently handed us.
	// Used by TryNavigate to clamp without round-tripping back to Main for the
	// list length. Refreshed on every SetActiveMission call.
	private List<MissionData> _missions = new List<MissionData>();
	private int _activeIndex = 0;

	// Holds the active tween so a rapid second navigation cancels the first
	// (no overlap, no compounded fade). Null when no animation is running.
	private Tween? _currentTween;

	public override void _Ready()
	{
		_pawnLayer = GetParent<TileMapLayer>();
		if (_pawnLayer == null)
		{
			GD.PushError("MissionCarouselPanel must be a child of a TileMapLayer (the PawnLayer).");
			return;
		}

		// Anchor on the iso grid. MapToLocal returns the cell's center in
		// PawnLayer-local coords. The active slot sits at this Position; the
		// side slots are offset by their own SlotOffset (read in MissionCard).
		Position = _pawnLayer.MapToLocal(HomeCell);

		// Resolve cards. Defensive null checks because a half-wired .tscn
		// would otherwise silently render an empty carousel — visible bug at
		// runtime but obscure cause; the explicit error message saves debug
		// time.
		_cardPrev   = GetNodeOrNull<MissionCard>(CardPrevPath);
		_cardActive = GetNodeOrNull<MissionCard>(CardActivePath);
		_cardNext   = GetNodeOrNull<MissionCard>(CardNextPath);
		if (_cardPrev == null || _cardActive == null || _cardNext == null)
		{
			GD.PushError("MissionCarouselPanel: one or more MissionCard NodePaths are unset or wrong type.");
			return;
		}

		// Resolve pawns. Pawns are optional in principle (a slot could have a
		// card without a character) but in M1 every active mission targets a
		// recruit so we expect all three to be wired.
		_pawnPrev   = GetNodeOrNull<CharacterIsoPawn>(PawnPrevPath);
		_pawnActive = GetNodeOrNull<CharacterIsoPawn>(PawnActivePath);
		_pawnNext   = GetNodeOrNull<CharacterIsoPawn>(PawnNextPath);
		if (_pawnPrev == null || _pawnActive == null || _pawnNext == null)
		{
			GD.PushError("MissionCarouselPanel: one or more CharacterIsoPawn NodePaths are unset or wrong type.");
			return;
		}

		// Carousel starts hidden — Main shows the whole panel on pawn-engaged
		// and hides it on pawn-released. Individual slot visibility is then
		// driven by RebindSlot once we know the (missions, index) state.
		Visible = false;
	}

	// Public entry point #1: full rebind. Main calls this every time the
	// (missions, index) tuple changes for any reason — startup, Next Tick,
	// wheel navigation, etc. The carousel computes prev/active/next from the
	// triplet and rebinds each slot. Used for non-animated state syncs
	// (initial show, tick response). Wheel-driven navigation goes through
	// TryNavigate instead so the animation can run.
	public void SetActiveMission(List<MissionData> missions, int activeIndex)
	{
		_missions    = missions ?? new List<MissionData>();
		_activeIndex = ClampIndex(activeIndex);

		RebindAllSlots();
	}

	// Public entry point #2: relative navigation. Main forwards wheel events
	// here. Returns a NavigateResult so Main can decide whether to consume
	// the input event:
	//   - Moved:      the index actually changed; consume.
	//   - AtBound:    user hit an edge; consume (defensive against a future
	//                 zoom-on-wheel feature stealing the gesture at the edges).
	//   - NotVisible: carousel is hidden; do not consume.
	public NavigateResult TryNavigate(int delta)
	{
		if (!Visible)
		{
			return NavigateResult.NotVisible;
		}

		int count = _missions.Count;
		if (count <= 1)
		{
			// Nothing to navigate to. Same treatment as a clamped bound: still
			// consume so a future zoom-on-wheel doesn't steal the gesture.
			return NavigateResult.AtBound;
		}

		int newIndex = System.Math.Clamp(_activeIndex + delta, 0, count - 1);
		if (newIndex == _activeIndex)
		{
			return NavigateResult.AtBound;
		}

		int oldIndex = _activeIndex;
		_activeIndex = newIndex;
		RebindAllSlots();
		AnimateRotation(oldIndex, newIndex);
		return NavigateResult.Moved;
	}

	// Public read accessor for Main's gating logic. Mirrors the old
	// _infoPanel.Visible check — Main consults this before forwarding wheel
	// events. Could also use Visible directly, but a named property reads
	// better at the call site and lets us evolve the gating policy (e.g.
	// "visible AND finished animating") without touching Main.
	public bool IsActiveCardVisible => Visible && _cardActive != null && _cardActive.Visible;

	// Read accessor for the navigated index. Main keeps its own
	// _activeMissionIndex in sync via this property; the carousel doesn't push
	// changes (it could via a [Signal], but a polled read keeps the data flow
	// one-directional and simpler for J7).
	public int ActiveIndex => _activeIndex;

	// Show the entire carousel (typically called from Main on pawn-engaged).
	// Individual slot visibility is governed by RebindSlot — empty slots stay
	// hidden even when the carousel itself is shown.
	public new void Show()
	{
		Visible = true;
		// Refresh slot visibility on show so an out-of-range index from a
		// previous session doesn't briefly flash before the next rebind.
		RebindAllSlots();
	}

	public new void Hide() => Visible = false;

	// --- internals -----------------------------------------------------------

	// Rebind all three slots based on _missions and _activeIndex. Idempotent:
	// safe to call multiple times in a frame (e.g. on show + on tick response).
	private void RebindAllSlots()
	{
		int count = _missions.Count;

		MissionData? prev   = (_activeIndex - 1 >= 0)    ? _missions[_activeIndex - 1] : null;
		MissionData? active = (_activeIndex >= 0 && _activeIndex < count) ? _missions[_activeIndex] : null;
		MissionData? next   = (_activeIndex + 1 <  count) ? _missions[_activeIndex + 1] : null;

		RebindSlot(_cardPrev,   _pawnPrev,   prev);
		RebindSlot(_cardActive, _pawnActive, active);
		RebindSlot(_cardNext,   _pawnNext,   next);
	}

	// Apply a single mission (or null) to a single (card, pawn) pair. Empty
	// slots are hidden outright — not alpha=0 cards — so a click-to-navigate
	// on the side slots can't accidentally hit an invisible card.
	private void RebindSlot(MissionCard card, CharacterIsoPawn pawn, MissionData? mission)
	{
		if (mission == null)
		{
			card.Hide();
			pawn.Hide();
			return;
		}

		card.SetText(mission.NarrativeHook);
		pawn.CharacterIndex = CharacterIsoPawn.NpcIdToAtlasIndex(mission.RecruitTargetNpcId);
		card.Show();
		pawn.Show();
	}

	// J7 v1 animation: pure Modulate.A fade on the three cards. Approach:
	//   - Reset Modulate.A on each visible card to 0.
	//   - Tween each visible card's Modulate.A back up to its target alpha
	//     (1.0 for the active card, SlotAlpha for the side cards) over
	//     TweenDurationSec, cubic-out easing.
	// The text + character have already been rebound to the new triplet by
	// the time the tween starts, so this reads as "the new triplet fades in".
	// Acceptable v1 for the brief: motion is perceived, no slide-and-rebind
	// double-step complexity, no risk of two cards overlapping mid-tween.
	// Promote to v2 (horizontal slide) only if Didier asks.
	private void AnimateRotation(int oldIndex, int newIndex)
	{
		// Kill any in-flight tween so the new rotation starts from a clean
		// state. Without this, a rapid wheel-spin could leave the previous
		// fade running and the alphas would compound.
		if (_currentTween != null && _currentTween.IsValid())
		{
			_currentTween.Kill();
		}

		_currentTween = CreateTween();
		_currentTween.SetTrans(Tween.TransitionType.Cubic);
		_currentTween.SetEase(Tween.EaseType.Out);
		_currentTween.SetParallel(true);

		FadeIn(_cardPrev,   _cardPrev.SlotAlpha);
		FadeIn(_cardActive, _cardActive.SlotAlpha);
		FadeIn(_cardNext,   _cardNext.SlotAlpha);
	}

	// Helper: reset alpha to 0 then tween up to target. Only acts if the card
	// is currently visible — hidden slots (e.g. at list edges) stay hidden,
	// no Modulate work.
	private void FadeIn(MissionCard card, float targetAlpha)
	{
		if (card == null || !card.Visible || _currentTween == null)
		{
			return;
		}
		// Reset the alpha synchronously so the tween starts from 0. Color is
		// (1,1,1,A); SlotScale is on Scale, not Modulate, so we don't touch
		// it here.
		card.Modulate = new Color(1f, 1f, 1f, 0f);
		_currentTween.TweenProperty(
			card,
			"modulate:a",
			targetAlpha,
			TweenDurationSec);
	}

	// Clamp an arbitrary input index against the current mission list. Used
	// by SetActiveMission to absorb a stale index from Main (e.g. the previous
	// list had 4 missions, the new list has 2 after a hypothetical prune).
	private int ClampIndex(int idx)
	{
		int count = _missions.Count;
		if (count == 0)
		{
			return 0;
		}
		return System.Math.Clamp(idx, 0, count - 1);
	}
}

// Result of a wheel-driven navigation attempt. Drives Main's
// GetViewport().SetInputAsHandled() decision. Splitting "moved" from
// "at bound" lets us refresh display only when something changed, while
// still consuming the input in both cases (defensive against future
// zoom-on-wheel handlers stealing the gesture at the edges of the list).
public enum NavigateResult
{
	Moved,
	AtBound,
	NotVisible,
}
