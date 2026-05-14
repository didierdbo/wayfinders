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
