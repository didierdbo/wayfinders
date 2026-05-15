using Godot;
using Wayfinders.Client.Data;

namespace Wayfinders.Client.Scripts.Poi;

/// <summary>
/// PR3 — Sprite2D MVP scene for a POI placed on the iso world tree.
///
/// <para>
/// <b>Scope (locked 2026-05-15).</b> Sprite2D only. NO shadow / lift / rim /
/// parallax (those four "pion sur plateau" ingredients are PR5). NO input or
/// hit-test (PR4). The node simply binds a <see cref="PoiData"/> to a
/// Sprite2D so the texture lands with its anchor pixel on the host
/// <c>Position</c> — i.e. the POI "stands" on its tile by construction.
/// </para>
///
/// <para>
/// <b>Anchor math.</b> Godot's <c>Sprite2D</c> draws its texture with the
/// origin offset by <c>Offset</c> from the node's <c>Position</c>. With
/// <c>Centered=false</c>, the top-left of the texture sits at
/// <c>Position + Offset</c>. To put the asset's anchor pixel
/// (e.g. (538, 573) — Halfgate's foot) on the node's <c>Position</c>, we
/// set <c>Offset = -AnchorPixel</c>. The sprite "falls" on its tile by
/// construction — no parent offset gymnastics needed.
/// </para>
///
/// <para>
/// <b>Pivot Sprite3D documented future.</b> If the 3D backing consolidates
/// (Trap §6 canary 3D pickup), this scene flips to <c>Sprite3D</c> with the
/// same offset trick (<c>Offset</c> in pixel space, <c>PixelSize</c>
/// controls world-space scale). Not in PR3 scope — owner decision §5#1.
/// </para>
///
/// <para>
/// <b>Preflight (trap §2).</b> <c>_Ready</c> logs the delivered values, not
/// just "spawned OK". Caller can read the Output panel and verify
/// <c>Texture != null</c>, <c>Centered=false</c>, <c>Offset</c> matches
/// <c>-AnchorPixel</c>, and parent type is NOT a <c>CanvasLayer</c> (trap §1).
/// </para>
/// </summary>
public partial class Poi : Sprite2D
{
    /// <summary>
    /// The data driving this POI's render. Required — must be assigned before
    /// the node enters the scene tree (either via the inspector on a saved
    /// <c>.tres</c> or programmatically by <see cref="PoiSpawner"/> before
    /// <c>AddChild</c>).
    /// </summary>
    [Export] public PoiData? PoiData { get; set; }

    public override void _Ready()
    {
        if (PoiData == null)
        {
            GD.PrintErr($"[POI SPAWN] {Name} — PoiData is null at _Ready. " +
                        "Caller must set PoiData before AddChild.");
            return;
        }

        if (PoiData.Texture == null)
        {
            GD.PrintErr($"[POI SPAWN] {Name} — PoiData.Texture is null at _Ready. " +
                        $"(DisplayName='{PoiData.DisplayName}', TexturePath='{PoiData.TexturePath}')");
            return;
        }

        // Bind the sprite to the data.
        Texture = PoiData.Texture;
        Centered = false;
        Offset = -(Vector2)PoiData.AnchorPixel; // pivot at the anchor pixel (foot)

        // Preflight (trap §2) — log delivered values, not "spawned OK".
        var parent = GetParent();
        var parentType = parent?.GetType().Name ?? "<no parent>";
        var isCanvasLayer = parent is CanvasLayer;
        var anchorAlignedOk = Offset == -(Vector2)PoiData.AnchorPixel;

        GD.Print(
            $"[POI SPAWN] {PoiData.DisplayName} " +
            $"at tile=(set by spawner via Position) " +
            $"screenPos=({Position.X:F1},{Position.Y:F1}) " +
            $"anchorPixel={PoiData.AnchorPixel} " +
            $"offset={Offset} " +
            $"centered={Centered} " +
            $"texSize={Texture.GetSize()} " +
            $"visible={Visible} " +
            $"parent={parentType}" +
            (isCanvasLayer ? " [TRAP §1 — parent is CanvasLayer!]" : "") +
            (anchorAlignedOk ? " anchorAlignedOK" : " ANCHOR-MISALIGNED"));
    }
}
