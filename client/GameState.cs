using System.Collections.Generic;
using Godot;

// Pure C# game-state holder — deliberately NOT a Godot Node.
// Lives as a field on Main and is owned for the lifetime of the scene.
//
// Why not an autoload? Because at M1 the only consumer is Main, and an autoload
// would imply "shared singleton across scenes" which we don't need yet. When a
// second scene needs to read CurrentTick we'll promote it. Concrete first,
// extract later (cf. premature-interface reflex).
public class GameState
{
	public int CurrentTick { get; private set; }
	public int WorldSeed { get; private set; }
	public List<MissionData> ActiveMissions { get; } = new();

	public GameState()
	{
		// Seed derived from wall-clock at construction. Stable for the run,
		// reproducible if we ever need to override it from a config resource.
		WorldSeed = (int)Time.GetUnixTimeFromSystem();
		CurrentTick = 0;
	}

	// Resets the run to a clean slate. Does NOT touch the network — the caller
	// (Main) is responsible for sequencing the first tick POST after this.
	public void StartNewGame()
	{
		CurrentTick = 0;
		ActiveMissions.Clear();
		WorldSeed = (int)Time.GetUnixTimeFromSystem();
		GD.Print($"GameState: new game started (seed={WorldSeed})");
	}

	// Called by Main after a successful /api/world/tick. Increments tick and
	// appends any mission the backend emitted this turn.
	public void RecordTick(MissionData? mission)
	{
		CurrentTick += 1;
		if (mission != null)
		{
			ActiveMissions.Add(mission);
		}
	}
}
