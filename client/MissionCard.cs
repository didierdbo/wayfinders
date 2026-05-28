using Godot;

// J7: a single carousel slot — parchment cartouche + Label, programmatically
// drawn, with three slot-level knobs (SlotScale / SlotAlpha / SlotOffset) so
// the parent MissionCarouselPanel can lay three of these out on the X axis
// without the card knowing it lives in a carousel.
//
// Renamed from MissionInfoPanel (J3c) — same passive-presenter responsibilities
// (SetText / Show / Hide / _Draw the parchment), with the panel-position logic
// moved out: the card no longer reads PawnLayer.MapToLocal(HomeCell). Its
// Position is now set by the parent (MissionCarouselPanel), which knows the
// carousel anchor cell and computes per-slot offsets. This keeps the card a
// reusable atom: three instances of MissionCard, three different SlotOffsets,
// one shared anchor on the parent.
//
// Why Node2D, not Control: same reasoning as before — the card lives in the
// iso scene tree under PawnLayer/BoardRoot via the carousel parent, so it
// inherits the BoardRoot transform for free. Modulate is also inherited by
// the Label child AND by any child Sprite2D (e.g. a CharacterIsoPawn), so a
// single Modulate.A on the card fades the whole slot uniformly — that's
// what makes Prev/Next visually secondary at alpha 0.45.
//
// [GlobalClass] for the same Godot-inspector caching reason as the rest of
// the custom Node2D scripts: makes the script the node's authoritative type
// so [Export] properties survive an editor refresh.
[GlobalClass]
public partial class MissionCard : Node2D
{
	// Visual size of the parchment rectangle, in pixels. 400x120 is wide enough
	// to fit a short narrative_hook on one or two lines, narrow enough not to
	// dominate the iso board at the current zoom. Tune in the inspector.
	[Export] public Vector2 PanelSize { get; set; } = new Vector2(400, 120);

	// Parchment cream. See MissionInfoPanel (J3c) for the palette rationale —
	// warm beige, leans yellow not pink so it doesn't clash with the terracotta
	// border below.
	[Export] public Color PanelColor { get; set; } = new Color(0.96f, 0.91f, 0.75f, 0.95f);

	// Border color — deep umber/terracotta. Same family as the painted art.
	[Export] public Color BorderColor { get; set; } = new Color(0.55f, 0.30f, 0.18f, 1.0f);

	// Border thickness in pixels. 3 reads clearly at the current zoom without
	// looking like a web button. Two filled rects (outer = border color, inner
	// = panel color) fake the thick border because DrawRect(filled:false) is
	// 1px regardless of any width parameter.
	[Export] public float BorderWidth { get; set; } = 3f;

	// Text rendering knobs. Defaults chosen to be legible on the parchment
	// without dominating it.
	[Export] public int FontSize { get; set; } = 18;
	[Export] public Color TextColor { get; set; } = new Color(0.20f, 0.13f, 0.08f, 1.0f);
	// Inner padding between the rectangle border and the text bounds.
	[Export] public float TextMargin { get; set; } = 12f;

	// J7: slot-level parameters. The parent MissionCarouselPanel sets these on
	// each card instance (Prev / Active / Next) to lay out the carousel.
	//
	//   SlotScale  — 1.0 for Active, 0.75 for Prev/Next. Applied to this
	//                Node2D's Scale, so it cascades to _Draw geometry AND to
	//                any child Sprite2D (e.g. the slot's CharacterIsoPawn).
	//   SlotAlpha  — 1.0 for Active, 0.45 for Prev/Next. Applied to Modulate.A,
	//                which Godot inherits down to every CanvasItem child. One
	//                value fades the whole slot.
	//   SlotOffset — relative offset from the carousel anchor (which the
	//                parent positions). +/- ~60% of PanelSize.X for Prev/Next,
	//                zero for Active. Applied to Position in _Ready.
	//
	// These are read-once at _Ready: the carousel doesn't mutate them at
	// runtime (the slot identity is fixed; only the mission bound to the slot
	// rotates). The tween between rotations animates Modulate.A directly, not
	// these [Export] values.
	[Export] public float SlotScale { get; set; } = 1.0f;
	[Export] public float SlotAlpha { get; set; } = 1.0f;
	[Export] public Vector2 SlotOffset { get; set; } = Vector2.Zero;

	private Label _label = null!;

	public override void _Ready()
	{
		// Apply slot-level layout. Scale cascades through the transform to all
		// child CanvasItems (Label + any pawn child). Modulate.A cascades the
		// same way. SlotOffset is in card-local space; the parent has already
		// positioned this Node2D at the carousel anchor, so adding SlotOffset
		// here gives the final per-slot position.
		Scale     = Vector2.One * SlotScale;
		Modulate  = new Color(1f, 1f, 1f, SlotAlpha);
		Position += SlotOffset;

		// Build the Label child in code so the .tscn stays minimal and Godot
		// doesn't need a Label sub-node wired up by hand. Single source of
		// truth for the Label's geometry: this method.
		_label = new Label
		{
			Name                = "TextLabel",
			Text                = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment   = VerticalAlignment.Center,
			AutowrapMode        = TextServer.AutowrapMode.WordSmart,
			ClipText            = true,
			// Park the Label at the top-left corner of the parchment rect
			// (-PanelSize/2 from this Node2D's origin) and size it to the
			// inner box (PanelSize minus 2*TextMargin on each side).
			Position = -PanelSize * 0.5f + new Vector2(TextMargin, TextMargin),
			Size     = PanelSize - new Vector2(TextMargin * 2f, TextMargin * 2f),
		};
		_label.AddThemeColorOverride("font_color", TextColor);
		_label.AddThemeFontSizeOverride("font_size", FontSize);
		AddChild(_label);

		// Hidden until the carousel shows the slot. The card's own visibility
		// is now toggled by MissionCarouselPanel.RebindSlot — empty slots
		// stay hidden, bound slots are shown.
		Visible = false;
	}

	// Filled parchment + thick border. The "thick border" is faked by drawing
	// a larger filled rectangle in the border color, then the panel rectangle
	// on top of it, inset by BorderWidth on every side.
	public override void _Draw()
	{
		Vector2 half = PanelSize * 0.5f;
		Rect2 outer = new Rect2(-half, PanelSize);
		DrawRect(outer, BorderColor, filled: true);
		Vector2 innerSize = PanelSize - new Vector2(BorderWidth * 2f, BorderWidth * 2f);
		Rect2 inner = new Rect2(-half + new Vector2(BorderWidth, BorderWidth), innerSize);
		DrawRect(inner, PanelColor, filled: true);
	}

	// Stable public API. The carousel calls SetText(narrativeHook) then Show()
	// when a mission is bound to this slot, Hide() when the slot is empty.
	public void SetText(string text)
	{
		if (_label != null)
		{
			_label.Text = text ?? "";
		}
	}

	public new void Show() => Visible = true;

	public new void Hide() => Visible = false;
}
