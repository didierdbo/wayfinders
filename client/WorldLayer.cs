using Godot;

public partial class WorldLayer : Node2D
{
	[Export] public float PanSpeed { get; set; } = 400f;
	[Export] public Vector2I MapSize { get; set; } = new Vector2I(2560, 2560);

	private Vector2 _minPos;
	private Vector2 _maxPos;
	private bool _dragging;

	public override void _Ready()
	{
		var viewport = GetViewport().GetVisibleRect().Size;
		var halfX = (MapSize.X - viewport.X) * 0.5f;
		var halfY = (MapSize.Y - viewport.Y) * 0.5f;
		_minPos = new Vector2(-halfX, -halfY);
		_maxPos = new Vector2(+halfX, +halfY);
		Position = ClampPosition(Position);
	}

	public override void _Process(double delta)
	{
		var dir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
		if (dir != Vector2.Zero)
		{
			Position = ClampPosition(Position + dir * PanSpeed * (float)delta);
		}
	}

	// _UnhandledInput (not _Input) so that interactive overlays drawn ON TOP of
	// the map — e.g. MissionGridOverlay's clickable mission cells — get first
	// crack at the mouse event in their own _Input and can consume it via
	// GetViewport().SetInputAsHandled() before the map starts dragging.
	//
	// Pattern: foreground widgets handle in _Input + consume; background
	// navigation handles in _UnhandledInput. Standard Godot input layering.
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			_dragging = mb.Pressed;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_dragging && @event is InputEventMouseMotion mm)
		{
			Position = ClampPosition(Position + mm.Relative);
			GetViewport().SetInputAsHandled();
		}
	}

	private Vector2 ClampPosition(Vector2 p)
	{
		return new Vector2(
			Mathf.Clamp(p.X, _minPos.X, _maxPos.X),
			Mathf.Clamp(p.Y, _minPos.Y, _maxPos.Y)
		);
	}
}
