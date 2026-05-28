using System.Collections.Generic;
using Godot;

// Highlights the grid cell of every active mission's target POI as a textured
// 166x166 tile from the revealed-tiles atlas, drawn directly on the world map,
// and routes mouse clicks on those cells to a selection state machine. Lives
// as a child of WorldLayer so it pans with the map for free.
//
// The full sprite is 2560x2560 (2048 logical map + 256px wooden frame on each side),
// centered at WorldLayer-local (960,540). The logical map origin in WorldLayer space is
// sprite_top_left + (256,256) = (960-1280+256, 540-1280+256) = (-64,-484),
// which equals (MapCenterX - MapHalfSize, MapCenterY - MapHalfSize) with MapHalfSize=1024.
// We slice the 2048x2048 logical area as a 16x16 grid of 128px cells, 0-indexed (col,row).
//
// Atlas layout:
//   revealed_tiles_atlas.png is 2656x2656 = 16x16 frames of 166x166 RGBA.
//   For cell (col, row) the source rect in atlas-space is
//     Rect2(col * 166, row * 166, 166, 166).
//   Each 166x166 tile includes the 128x128 map content centered plus a 19px
//   drop-shadow margin on all sides (19 = (166-128)/2), so when we draw it on
//   the logical grid we offset by -19 to keep the content perfectly aligned
//   with the underlying 128x128 cell.
//
// _Draw is the right hook for rendering (not a child Sprite2D per mission) because:
//   1. mission count is small and bounded (typically 1-3),
//   2. _Draw runs only when QueueRedraw is called, so it's idle outside of state
//      transitions,
//   3. it keeps the highlight purely visual — no nodes to free, no signals to
//      disconnect.
//
// _Input is the right hook for hit-testing (not Area2D + InputEvent) because:
//   1. cells are axis-aligned rectangles in WorldLayer-local space — a single
//      coordinate transform + rect contains check is cheaper than building a
//      collision shape per mission,
//   2. WorldLayer drags the map on _UnhandledInput (mouse down + motion), so we
//      get first crack at the event in _Input and consume it via
//      SetInputAsHandled() when the click lands on a mission cell. That naturally
//      suppresses a spurious drag-start on the same click.
public partial class MissionGridOverlay : Node2D
{
	// Emitted when the player clicks a mission cell. Selection is one-shot per
	// click: clicking the same cell again re-emits (the consumer decides whether
	// that's a toggle or a re-trigger).
	//
	// J2 subscribes to this from Main to show the iso Halfgate panel.
	[Signal]
	public delegate void MissionCellSelectedEventHandler(string targetPoi);

	// Emitted when a previously-set selection is cleared by clicking outside any
	// mission cell. Separate from MissionCellSelected because (a) Godot signals
	// read more cleanly as positive events ("X happened") than as overloaded
	// channels carrying both "selected POI X" and "selection cleared" through a
	// magic empty-string payload, and (b) it mirrors the standard Godot pattern
	// pair pressed/released, mouse_entered/mouse_exited, focus_entered/focus_exited.
	// No payload — the consumer already knows the previous selection if it tracked
	// MissionCellSelected.
	[Signal]
	public delegate void MissionCellDeselectedEventHandler();

	// Map geometry — kept as constants because they're tied to the WorldGround
	// sprite layout, not configurable at runtime.
	private const float MapCenterX = 960f;
	private const float MapCenterY = 540f;
	private const float MapHalfSize = 1024f; // (2560/2) - 256px frame = logical map half
	private const float CellSize = 128f;     // 2048 logical / 16 cells

	// Atlas geometry. TileSize is the per-frame dimension in the atlas; the
	// 19px margin is the drop-shadow extension beyond the 128x128 content,
	// which becomes the draw-position offset so the content lands centered on
	// the logical cell.
	private const int   TileSize   = 166;
	private const float ShadowMargin = (TileSize - CellSize) * 0.5f; // 19

	// One CanvasItemMaterial-free modulation per state — Godot's DrawTextureRectRegion
	// honors self_modulate on the CanvasItem implicitly, but doing it per-draw
	// via the modulate argument is cleaner: we don't smear state across frames.
	//
	// Revealed: identity (alpha 1.0, no tint) — the tile asset is already final art.
	// Hover:    +10% RGB lift, nothing exotic. Reads as "this is reactive" without
	//           wrecking the painterly look; aesthetic-safe.
	// Selected: extra lift on top of a bright border drawn after the texture, so
	//           the selection is unambiguous at a glance even when zoomed out.
	private static readonly Color RevealedModulate = Colors.White;
	private static readonly Color HoverModulate    = new Color(1.10f, 1.10f, 1.10f, 1.00f);
	private static readonly Color SelectedModulate = new Color(1.18f, 1.18f, 1.18f, 1.00f);

	// Selection border drawn on top of the texture for the selected tile only.
	// Warm cream — pulled from the palette family of the world map's parchment
	// tones so it sits inside the visual DNA instead of fighting it.
	private static readonly Color SelectedBorder = new Color(1.00f, 0.95f, 0.70f, 1.00f);
	private const float           SelectedBorderWidth = 3f;

	// POI -> (col, row), 0-indexed. Hardcoded at M1; will move to a Resource
	// or a backend-served lookup when the POI catalog stabilises.
	private static readonly Dictionary<string, Vector2I> _poiToCell = new()
	{
		{ "e1.halfgate",   new Vector2I(14,  9) },
		{ "e1.brescaille", new Vector2I( 6,  5) },
		{ "e1.fendelune",  new Vector2I(10, 12) },
	};

	private List<MissionData> _missions = new();

	// Selection state. Both are POI ids ("e1.halfgate"...) or null when no cell
	// is under the mouse / no cell is selected. Kept as strings rather than
	// MissionData references so a backend refresh that swaps mission instances
	// doesn't invalidate the selection.
	private string? _hoveredPoi;
	private string? _selectedPoi;

	// Atlas texture, loaded once in _Ready and reused across every draw. Loading
	// in _Ready (not as a [Export]) keeps the scene file free of art-pipeline
	// churn; we'll switch to [Export] when more than one atlas variant ships.
	private Texture2D _atlas = null!;

	// Read-only view for any future consumer that prefers polling over the
	// signal (e.g. a debug overlay).
	public string? SelectedPoi => _selectedPoi;

	public override void _Ready()
	{
		// Sit at WorldLayer-local (0,0). _Draw uses absolute map-space coordinates
		// computed from MapCenterX/Y, so we don't want a non-zero position skew.
		Position = Vector2.Zero;
		// Draw above WorldGround, FogOverlay, and WorldDetails. The fog (ZIndex 5)
		// dims the entire logical map; our revealed tiles (ZIndex 10) punch
		// "lit windows" through it at full brightness.
		ZIndex = 10;

		_atlas = GD.Load<Texture2D>("res://assets/world/revealed_tiles_atlas.png");
	}

	// Public entry point — called by Main whenever the mission list changes.
	// Stores a defensive copy so subsequent mutations on the caller's list don't
	// surprise us mid-draw. Also drops any selection that targets a POI no
	// longer in the active set, so the visual state stays consistent.
	public void RefreshMissions(List<MissionData> missions)
	{
		_missions = new List<MissionData>(missions);

		// If a selection was pruned by the refresh, emit Deselected so any UI
		// driven by the selection state (e.g. MissionIsoPawn) can hide cleanly.
		if (_selectedPoi != null && !ActivePoiSet().Contains(_selectedPoi))
		{
			_selectedPoi = null;
			EmitSignal(SignalName.MissionCellDeselected);
		}
		if (_hoveredPoi != null && !ActivePoiSet().Contains(_hoveredPoi))
		{
			_hoveredPoi = null;
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent @event)
	{
		// Mouse motion -> recompute hover. Cheap (one transform, ~3 contains
		// checks at M1), runs every motion event but only triggers QueueRedraw
		// when the hovered POI actually changes — _Draw stays cold during
		// straight-line cursor motion across empty cells.
		if (@event is InputEventMouseMotion mm)
		{
			var newHover = PoiAtViewportPosition(mm.Position);
			if (newHover != _hoveredPoi)
			{
				_hoveredPoi = newHover;
				QueueRedraw();
			}
			return;
		}

		// Left click -> selection. Press, not release, to feel snappy and to
		// pre-empt WorldLayer's drag-start (which also triggers on press, in
		// _UnhandledInput). When the click lands on a mission cell we consume
		// the event so the map doesn't start dragging under our feet.
		if (@event is InputEventMouseButton mb
			&& mb.ButtonIndex == MouseButton.Left
			&& mb.Pressed)
		{
			var hitPoi = PoiAtViewportPosition(mb.Position);
			if (hitPoi != null)
			{
				_selectedPoi = hitPoi;
				EmitSignal(SignalName.MissionCellSelected, hitPoi);
				QueueRedraw();
				GetViewport().SetInputAsHandled();
			}
			else if (_selectedPoi != null)
			{
				// Click outside any mission cell clears selection. We notify
				// listeners via MissionCellDeselected so dependent UI (the iso
				// panel at J2) can collapse. We do NOT consume the event —
				// WorldLayer can use it to start a pan/drag.
				_selectedPoi = null;
				EmitSignal(SignalName.MissionCellDeselected);
				QueueRedraw();
			}
		}
	}

	// Converts a viewport-space mouse position to a POI id by going
	// viewport -> WorldLayer-local -> grid cell, then reverse-looking-up the
	// cell against the active mission set. Returns null when the cursor is
	// outside the logical map or over a cell with no active mission.
	//
	// Hit-test is still the 128x128 logical cell, NOT the 166x166 textured
	// footprint — the drop-shadow margin is purely visual; clicks on the
	// shadow should fall through to the map (and to the fog/pan handler).
	private string? PoiAtViewportPosition(Vector2 viewportPos)
	{
		// GetLocalMousePosition would also work but goes through Godot's input
		// pipeline; converting the event's own position is more deterministic
		// (no off-by-one-frame races on fast motion).
		var local = GetGlobalTransform().AffineInverse() * viewportPos;

		float mapLeft = MapCenterX - MapHalfSize;
		float mapTop  = MapCenterY - MapHalfSize;

		float relX = local.X - mapLeft;
		float relY = local.Y - mapTop;
		if (relX < 0 || relY < 0 || relX >= 16 * CellSize || relY >= 16 * CellSize)
		{
			return null;
		}

		var cell = new Vector2I((int)(relX / CellSize), (int)(relY / CellSize));

		foreach (var mission in _missions)
		{
			if (_poiToCell.TryGetValue(mission.TargetPoi, out var poiCell)
				&& poiCell == cell)
			{
				return mission.TargetPoi;
			}
		}
		return null;
	}

	private HashSet<string> ActivePoiSet()
	{
		var s = new HashSet<string>();
		foreach (var m in _missions)
		{
			s.Add(m.TargetPoi);
		}
		return s;
	}

	public override void _Draw()
	{
		if (_missions.Count == 0 || _atlas == null)
		{
			return;
		}

		// Top-left corner of the 2048x2048 logical map in WorldLayer-local coordinates.
		// Sprite (2560x2560) is centered at (960,540); the 256px frame shifts the
		// logical origin inward: (960-1280+256, 540-1280+256) = (-64,-484).
		float mapLeft = MapCenterX - MapHalfSize; // -64
		float mapTop  = MapCenterY - MapHalfSize; // -484

		foreach (var mission in _missions)
		{
			if (!_poiToCell.TryGetValue(mission.TargetPoi, out var cell))
			{
				// Unknown POI — skip silently. A warning per-frame would spam
				// the output console because _Draw can be triggered repeatedly.
				continue;
			}

			// Source rect inside the atlas — straight (col, row) -> (col*166, row*166).
			var srcRect = new Rect2(cell.X * TileSize, cell.Y * TileSize, TileSize, TileSize);

			// Destination rect — 166x166 centered on the 128x128 logical cell, so
			// the drop-shadow margin extends 19px outward on every side.
			float destX = mapLeft + cell.X * CellSize - ShadowMargin;
			float destY = mapTop  + cell.Y * CellSize - ShadowMargin;
			var destRect = new Rect2(destX, destY, TileSize, TileSize);

			// Selected beats hover beats revealed. A cell can be both hovered
			// and selected; we render the stronger of the two states.
			Color modulate;
			bool drawSelectionBorder = false;
			if (mission.TargetPoi == _selectedPoi)
			{
				modulate = SelectedModulate;
				drawSelectionBorder = true;
			}
			else if (mission.TargetPoi == _hoveredPoi)
			{
				modulate = HoverModulate;
			}
			else
			{
				modulate = RevealedModulate;
			}

			DrawTextureRectRegion(_atlas, destRect, srcRect, modulate);

			if (drawSelectionBorder)
			{
				// Draw the border tight to the 128x128 content rect, not the
				// 166x166 shadow rect, so the border reads as "this cell is
				// selected" rather than "this shadow has a frame".
				var contentRect = new Rect2(
					mapLeft + cell.X * CellSize,
					mapTop  + cell.Y * CellSize,
					CellSize, CellSize);
				DrawRect(contentRect, SelectedBorder, filled: false, width: SelectedBorderWidth);
			}
		}
	}
}
