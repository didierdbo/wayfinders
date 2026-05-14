using System.Diagnostics;
using Godot;
using Wayfinders.Client.Data;

namespace Wayfinders.Client.scenes.probes;

public partial class PoiFootprintProbe : Node
{
    public override void _Ready()
    {
        var sw = Stopwatch.StartNew();
        var data = PoiSidecarLoader.Load("res://assets/poi/e1/wf_e1_halfgate_poi.png");
        sw.Stop();

        // Preflight (trap §2)
        if (data.AlphaMask == null || data.FootprintTiles == null || data.Texture == null)
        {
            GD.PrintErr("[POI FOOTPRINT] PRELOAD FAILED — null fields after Load");
            return;
        }

        // Count opaque pixels (sanity)
        int opaque = 0;
        for (int i = 0; i < data.AlphaMask.Length; i++)
        {
            if (data.AlphaMask[i]) opaque++;
        }

        GD.Print($"[POI FOOTPRINT] {data.DisplayName} " +
                 $"bitmap={data.Texture.GetSize()} " +
                 $"opaque_pixels={opaque} " +
                 $"tile_count={data.FootprintTiles.Count} " +
                 $"build_ms={sw.Elapsed.TotalMilliseconds:F1}");
    }
}
