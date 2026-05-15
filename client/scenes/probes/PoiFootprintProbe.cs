// =============================================================================
// PoiFootprintProbe (PR2) - scratch, keep until PR5.X.
// =============================================================================
// PR2 footprint builder smoke - alpha-derived tile coverage.
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
