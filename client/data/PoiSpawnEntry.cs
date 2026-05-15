using Godot;

namespace Wayfinders.Client.Data;

/// <summary>
/// PR6 — scene-side manifest entry pairing a <see cref="PoiData"/> with the
/// iso tile at which it should be spawned in <i>this particular scene</i>.
///
/// <para>
/// <b>Why a wrapper Resource and not a <c>Tile</c> field on <see cref="PoiData"/>
/// directly (locked PR6, 2026-05-15).</b> <see cref="PoiData"/> describes the
/// POI itself : its name, its sprite, its anchor pixel, its alpha-derived
/// footprint. None of that is scene-specific. The tile a POI lives on
/// <i>is</i> scene-specific — the same Halfgate POI might appear at
/// <c>(12, 12)</c> on M1Slice and at <c>(7, 9)</c> on a future E2 cadastre
/// view. Putting the tile on <see cref="PoiData"/> would force a fresh
/// <c>.tres</c> per scene-placement, which defeats the reuse the data
/// resource was created for. Wrapping <c>(PoiData, Tile)</c> in a thin
/// per-scene entry keeps both axes clean :
/// <list type="bullet">
///   <item><see cref="PoiData"/> = reusable POI definition (cross-scene).</item>
///   <item><see cref="PoiSpawnEntry"/> = "this POI goes at this tile <i>in this scene</i>"
///         (per-scene authoring).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Authoring (Option a, locked MVP).</b> The hosting scene (M1Slice
/// today, future iso scenes tomorrow) exposes an
/// <c>[Export] Godot.Collections.Array&lt;PoiSpawnEntry&gt; PoisToSpawn</c>.
/// A designer drag-and-drops <c>.tres</c> entries into that array from the
/// Godot inspector. No code edit needed to add a POI to a scene. Option b
/// (a separate <c>PoiManifest.tres</c> per scene) is the migration target
/// if we ever hit 50+ POI in E2 — option a stays simpler for the 6-8 POI
/// per E1 mission scope locked in the prebrief.
/// </para>
///
/// <para>
/// <b>Resource, not record / struct.</b> Godot only marshals
/// <see cref="Resource"/> subclasses through <c>[Export]</c> arrays in a
/// designer-editable way (the inspector knows how to instantiate, edit,
/// and serialize a Resource ; it does not know how to do that for a plain
/// C# record). <c>[GlobalClass]</c> exposes the type in the engine's
/// "create resource" picker.
/// </para>
/// </summary>
[GlobalClass]
public partial class PoiSpawnEntry : Resource
{
    /// <summary>
    /// The POI definition to spawn. Typically a <c>.tres</c> resource saved
    /// alongside the asset (e.g. <c>res://data/poi/halfgate.tres</c>) or
    /// constructed programmatically via <see cref="PoiSidecarLoader.Load"/>.
    /// </summary>
    [Export] public PoiData? Data { get; set; }

    /// <summary>
    /// Iso (col, row) tile coordinate at which the POI sprite's anchor pixel
    /// lands. The spawner projects this through <c>FogTileGridLogic</c> to
    /// world coordinates ; the POI sprite is offset so the anchor (rendered
    /// foot of the building) sits on the tile centre.
    /// </summary>
    [Export] public Vector2I Tile { get; set; }

    /// <summary>
    /// PR6 — alternative sidecar load path. When <see cref="Data"/> is null
    /// and this string is set, the spawner caller loads the POI from the
    /// PNG sidecar at this <c>res://</c> path via
    /// <see cref="PoiSidecarLoader.Load"/>. Convenient for MVP authoring
    /// without first saving a <c>.tres</c> ; the eventual M2 pipeline can
    /// drop this in favour of fully-authored <see cref="PoiData"/> resources.
    /// </summary>
    [Export] public string SidecarPngPath { get; set; } = "";

    /// <summary>
    /// Optional override of the POI's display name. Useful for spawning the
    /// same <see cref="PoiData"/> at multiple tiles in the same scene — the
    /// router uses <c>DisplayName</c> as the click-target identity, so two
    /// entries pointing at the same data with different tiles need
    /// distinct names to disambiguate downstream subscribers. Empty string
    /// keeps the data's own <c>DisplayName</c>.
    /// </summary>
    [Export] public string DisplayNameOverride { get; set; } = "";
}
