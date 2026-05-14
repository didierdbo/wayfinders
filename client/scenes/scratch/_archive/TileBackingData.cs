using Godot;

namespace Wayfinders.Client.Scenes.Scratch;

/// <summary>
/// Jalon 1 — data container describing the geometry of a single tile sprite
/// for the 3D backing layer (Wayfinders 3D Backing Architecture, decision
/// lockée 2026-05-09, doc <c>Owner's Inbox/Godot Coaching/Wayfinders - 3D
/// Backing Architecture - 2026-05-09.md</c>).
///
/// <para>
/// <b>Why a Resource (Pattern C — "hauteur dérivée de l'asset, pas codée
/// en dur").</b> Mira's tile assets ship with variable skirt thickness:
/// the iso-losange visual top (the playable surface) is fixed, but the
/// total bitmap height varies from one tile to the next as the painted
/// skirt and its drop-shadow grow. Hard-coding a <c>BoxShape3D.size.y</c>
/// in C# would force a recompile every time Didier wants to test a new
/// tile thickness. A <c>.tres</c> resource is hot-editable in the
/// inspector — Didier swaps a single <c>TileBackingData</c> reference
/// on the probe scene and the box geometry rebuilds on the next
/// <c>_Ready</c> without touching the C# code.
/// </para>
///
/// <para>
/// <b>Worked example (Didier's spec, 2026-05-09 §1).</b>
/// <list type="bullet">
///   <item><c>SpritePixelHeight = 134</c>, <c>SpriteTopHeightPx = 128</c>
///         → derived <c>BackingHeightPx = 6</c> (thick-skirt tile, tall
///         backing volume).</item>
///   <item><c>SpritePixelHeight = 130</c>, <c>SpriteTopHeightPx = 128</c>
///         → derived <c>BackingHeightPx = 2</c> (thin-skirt tile, short
///         backing volume).</item>
/// </list>
/// The "256" in the spec is the bitmap width (iso-losange convention,
/// width = 2 × top-half-height). The 3D box uses
/// <c>SpriteTopWidthPx</c> as the X/Z footprint and
/// <c>BackingHeightPx</c> as the Y extent — the click zone hugs the top
/// of the sprite no matter how thick the painted skirt is.
/// </para>
///
/// <para>
/// <b>Pixel-to-world units.</b> The probe scene runs at 1 world unit =
/// 1 pixel by setting Camera3D ortho size to the viewport width. This
/// keeps the brief math here in pixels (the same units Mira authors in)
/// and pushes the world-units conversion to the camera, where it
/// belongs. Production might pick a different ratio at the Jalon 2
/// camera lock — when that lands, expose a <c>WorldUnitsPerPixel</c>
/// constant on the probe component, not on this resource.
/// </para>
///
/// <para>
/// <b>Why <c>[GlobalClass]</c>.</b> Same reason as
/// <see cref="Wayfinders.Client.Data.PoiDefinition"/>: the attribute
/// registers the type with Godot's resource system so Didier can
/// author <c>.tres</c> files of this type via the inspector's "New
/// Resource" dropdown.
/// </para>
/// </summary>
[GlobalClass]
public partial class TileBackingData : Resource
{
    /// <summary>
    /// Total bitmap height in pixels (visible top + skirt). Mira's iso
    /// tile assets are typically 128 px (top-only, no skirt) up to
    /// ~140 px (thick skirt). Authored once per asset variant.
    /// </summary>
    [Export] public int SpritePixelHeight { get; set; } = 130;

    /// <summary>
    /// Height of the visible iso-losange top in pixels — i.e. the
    /// playable surface where a unit stands. For the standard
    /// 256-wide iso losange this is 128 px (width = 2 × top height).
    /// Stays constant across tile variants ; only the skirt grows.
    /// </summary>
    [Export] public int SpriteTopHeightPx { get; set; } = 128;

    /// <summary>
    /// Width of the visible iso-losange top in pixels. 256 px for the
    /// standard tile size. Drives the X/Z footprint of the
    /// <c>BoxShape3D</c> on the backing <c>Area3D</c>.
    /// </summary>
    [Export] public int SpriteTopWidthPx { get; set; } = 256;

    /// <summary>
    /// Derived backing-box vertical extent in pixels. Equals the skirt
    /// thickness — the part of the sprite that hangs below the playable
    /// top. Clamped to a minimum of 1 px so the <c>BoxShape3D</c> is
    /// never degenerate (Godot's collision system warns on zero-extent
    /// shapes ; a 1-px tile is logically a thin tile, not a broken one).
    /// </summary>
    public int BackingHeightPx => System.Math.Max(1, SpritePixelHeight - SpriteTopHeightPx);
}
