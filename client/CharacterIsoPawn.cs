using Godot;

// A character sprite drawn from a shared characters atlas via Sprite2D's
// region feature. J3b shows the recruitment character (Halfgate woman) appearing
// next to the Halfgate mission pawn while the player hovers it; the same class
// will back every character we put on the iso board later (each unit / NPC = one
// CharacterIsoPawn node, different CharacterIndex into the same atlas).
//
// J7 lifetime change (Option B from the carousel plan): this node no longer
// anchors itself on a PawnLayer cell. Its Position is inherited from its parent
// transform. In the J7 carousel scene tree, the parent is a MissionCard which
// is itself positioned by MissionCarouselPanel; the pawn just rides along.
// Before J7 the parent was PawnLayer directly and the pawn set its own Position
// via PawnLayer.MapToLocal(HomeCell) — that path is gone. HomeCell is kept on
// the class as a hint / future-use field but is no longer read by _Ready.
//
// Rationale for the change: the carousel needs to tween a single Node2D per
// slot (Modulate/Scale/Position on the MissionCard) and have the pawn follow
// gratis via parent transform inheritance. If the pawn kept its own anchor it
// would fight the parent transform — three slots would need six synchronised
// tweens instead of three. Concrete-first: when a future scene needs an
// independently-anchored pawn, we extract a shared base. Today, one use case.
//
// Why an atlas + RegionRect instead of one PNG per character:
//   - One texture upload to the GPU instead of N; one .import file to maintain
//     instead of N; one alpha-mask convention to enforce instead of N. The
//     marginal cost of "this sprite shows frame K of the atlas" is two
//     [Export]s and a Rect2 — cheap, and the editor can preview the region.
//   - Characters all share the same 256x512 cell size (Mira's pipeline locks
//     this), so a uniform grid works.
//
// [GlobalClass] is mandatory here for the same reason as on MissionIsoPawn:
// without it, the inspector caches the engine-type ([Sprite2D]) property list
// and may not show our [Export]s after a rebuild until the scene is fully
// reloaded.
[GlobalClass]
public partial class CharacterIsoPawn : Sprite2D
{
	// Mira's atlas convention: each frame is a 256x512 column, laid out left to
	// right starting at x=0, y=0. Update FrameWidth/FrameHeight if the painter
	// ever changes cell size — keep the indices stable so .tscn references and
	// (later) backend identifiers don't break.
	private const int FrameWidth  = 256;
	private const int FrameHeight = 512;

	// The atlas texture. Assigned in the scene file (Main.tscn) so the asset
	// path lives with the scene, not in code. Defensive null check in _Ready
	// keeps a misconfigured scene from silently rendering a blank Sprite2D.
	[Export] public Texture2D Atlas { get; set; }

	// Backing field for CharacterIndex.
	private int _characterIndex = 0;

	// Which 256x512 column of the atlas this character draws from. 0 = woman_1,
	// 1 = nain at J4b (see NpcIdToAtlasIndex). Two write paths:
	//   1. Inspector / scene file: the setter runs BEFORE _Ready. At that point
	//      Texture is still null (we assign it in _Ready), so the conditional
	//      below short-circuits and we just store the value. _Ready then applies
	//      RegionRect from the stored value as before — no double-init.
	//   2. Runtime mutation from MissionCarouselPanel (the carousel sets
	//      CharacterIndex when the active mission's recruit target changes for
	//      this slot). Texture is now set, so the conditional fires and
	//      RegionRect is updated immediately — the next frame draws the new
	//      column without a Show/Hide cycle.
	[Export]
	public int CharacterIndex
	{
		get => _characterIndex;
		set
		{
			_characterIndex = value;
			if (Texture != null)
			{
				RegionRect = new Rect2(
					_characterIndex * FrameWidth,
					0,
					FrameWidth,
					FrameHeight);
			}
		}
	}

	// J7: kept as a hint for designers but no longer read by _Ready. The pawn's
	// Position is now inherited from its parent (a MissionCard in the carousel).
	// Marked here so a future "anchored pawn" path can re-introduce the read
	// without churning the [Export] surface. Deprecated by intent, not by API.
	[Export] public Vector2I HomeCell { get; set; } = new Vector2I(0, 3);

	// J4b: backend NPC identifier -> atlas column index.
	//
	// Lives on CharacterIsoPawn (not Main) because the atlas layout is this
	// class's responsibility: FrameWidth/FrameHeight, the "column K = character
	// X" convention, and the Mira pipeline assumption all live here. The
	// carousel just asks "what column for this npc id?" and writes the result
	// into CharacterIndex.
	//
	// Fallback policy (Varn-locked M1):
	//   - "kira"  -> 0 (woman_1) — atlas-present
	//   - "dorn"  -> 1 (nain)    — atlas-present
	//   - "vell"  -> 2 — TEMPORARY: vell asset not yet in atlas; owner will
	//                    bump to a new column when Mira delivers. Silent (no
	//                    warning) because the fallback is expected for M1.
	//   - null    -> 0           — mission isn't a recruit type (or backend
	//                              pre-dates the field). Silent.
	//   - other   -> 0 + warning — unknown id from backend.
	public static int NpcIdToAtlasIndex(string? npcId)
	{
		switch (npcId)
		{
			case "kira": return 0;
			case "dorn": return 1;
			case "vell": return 2;
			case null:   return 0;
			default:
				GD.PushWarning(
					$"CharacterIsoPawn.NpcIdToAtlasIndex: unknown recruit_target_npc_id '{npcId}', falling back to woman_1 (index 0).");
				return 0;
		}
	}

	public override void _Ready()
	{
		// J7: no more PawnLayer / HomeCell anchoring. Position is inherited
		// from the parent (a MissionCard in the carousel scene tree). The
		// over-tall 256x512 frame still extends well above the parent's
		// origin; Y-sort on PawnLayer uses the parent's Y, so layering with
		// other board elements stays correct as long as the carousel itself
		// is anchored sensibly.

		// Configure the region. We don't trust whatever the scene file may or
		// may not have set on RegionEnabled/RegionRect — _Ready is the single
		// source of truth so the same code path runs whether the sprite was
		// instantiated from a packed scene or constructed in code.
		if (Atlas != null)
		{
			Texture       = Atlas;
			RegionEnabled = true;
			RegionRect    = new Rect2(
				_characterIndex * FrameWidth,
				0,
				FrameWidth,
				FrameHeight);
		}
		else
		{
			GD.PushError(
				$"CharacterIsoPawn '{Name}': Atlas is null. Assign the atlas texture in the inspector.");
		}

		// Hidden until MissionCarouselPanel.RebindSlot decides this slot has
		// a mission with a recruit target to display. Visibility is now
		// driven by the carousel, not by a direct signal from Main.
		Visible = false;
	}

	// Visibility surface unchanged. The carousel calls Show()/Hide() per slot
	// based on whether a mission is bound and whether that mission has a
	// recruit target.
	public new void Show() => Visible = true;

	public new void Hide() => Visible = false;
}
