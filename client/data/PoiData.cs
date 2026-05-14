using System.Collections.Generic;
using Godot;
using System.Collections;

namespace Wayfinders.Client.Data;

[GlobalClass]
public partial class PoiData: Resource
{
    [Export] public Vector2I AnchorPixel { get; set; }
    [Export] public string DisplayName { get; set; } = "";
    [Export] public string TexturePath { get; set; } = "";
    [Export] public Texture2D Texture { get; set; } = null!;

    // Runtime-only — populated by PoiSidecarLoader, never serialized to .tres.
    public BitArray? AlphaMask { get; set; }
    public int AlphaMaskWidth { get; set; }
    public HashSet<Vector2I>? FootprintTiles { get; set; }

}
