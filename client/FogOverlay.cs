using Godot;

// Dims the entire 2048x2048 logical map with a semi-transparent dark layer so
// only "revealed" tiles (drawn on top by MissionGridOverlay) read as bright.
// The 256px wooden frame outside the logical area stays at full brightness —
// this is intentional, the frame is diegetic chrome and should not be fogged.
//
// Z-stack within WorldLayer:
//   WorldGround   (Sprite2D, default ZIndex 0)        — full map art
//   FogOverlay    (this node,        ZIndex 5)        — dark wash over logical area
//   MissionGridOverlay (ZIndex 10)                    — revealed tiles + selection
//
// _Draw is the right hook (not a ColorRect): we want this to sit in WorldLayer-
// local space and pan with the map, and we don't need any Control-tree layout.
// The single DrawRect is one GPU quad per frame, only redrawn when QueueRedraw
// is called — and we never call it after _Ready since the fog never changes.
public partial class FogOverlay : Node2D
{
	// Mirrors MissionGridOverlay's constants. Kept duplicated rather than shared
	// via a static helper at M1 — two nodes, two const blocks, no premature
	// abstraction. If a third consumer appears, lift them.
	private const float MapCenterX = 960f;
	private const float MapCenterY = 540f;
	private const float MapHalfSize = 1024f;

	// Calibrated for the warm-earth palette: a cool desaturated near-black with
	// ~75% alpha lets enough of the map art bleed through to keep cardinal
	// landmarks (rivers, coastline) readable as silhouettes, while flattening
	// the saturation and contrast enough that a revealed 166x166 tile drawn at
	// full alpha on top reads as a clear "lit window" into the world.
	private static readonly Color FogColor = new Color(0.05f, 0.05f, 0.10f, 0.75f);

	public override void _Ready()
	{
		Position = Vector2.Zero;
		ZIndex = 5;
	}

	public override void _Draw()
	{
		float mapLeft = MapCenterX - MapHalfSize; // -64
		float mapTop  = MapCenterY - MapHalfSize; // -484
		var rect = new Rect2(mapLeft, mapTop, 2 * MapHalfSize, 2 * MapHalfSize);
		DrawRect(rect, FogColor, filled: true);
	}
}
