// =============================================================================
// PoiLoadProbe (PR1) - scratch, keep until PR5.X.
// =============================================================================
// PR1 sidecar load smoke - data layer canary.
//
// PR6 decision (2026-05-15) : these probes stay in the tree even after the
// M1Slice integration ships. The PoiSpawnProbe in particular remains the
// canonical visual A/B harness for the PR5.X rim-shader work and any future
// "did the shadow / lift / parallax regress?" question. Deleting them would
// strand the F1-F4 tuning matrix and the heartbeat parallax diagnostic on a
// future re-revisit.
//
// Move-to-archive criterion : when PR5.X ships rim + shader-blurred shadow
// AND the visual A/B is locked in a screenshot reference, this probe (and
// the other two) may be moved under scenes/scratch/_archive/ alongside the
// Tile3DBackingProbe family. Until then : keep.
// =============================================================================

using Godot;
using System;
using Wayfinders.Client.Data;

public partial class PoiLoadProbe : Node
{

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
    {
        var data = PoiSidecarLoader.Load("res://assets/poi/e1/wf_e1_halfgate_poi.png");
        GD.Print($"[POI LOAD-A2] {data.DisplayName} anchor={data.AnchorPixel} texSize={data.Texture.GetSize()}");
    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
