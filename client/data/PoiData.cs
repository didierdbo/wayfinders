using Godot;

namespace Wayfinders.Client.Data;

[GlobalClass]
public partial class PoiData: Resource
{
    [Export] public Vector2I AnchorPixel { get; set; }
    [Export] public string DisplayName { get; set; } = "";
    [Export] public string TexturePath { get; set; } = "";
    [Export] public Texture2D Texture { get; set; } = null!;

}
