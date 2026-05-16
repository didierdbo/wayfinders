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

    /// <summary>
    /// Canonical hierarchical POI id matching the server-side
    /// <c>EmergentMission.target_poi</c> contract — string of the form
    /// <c>e{layer}.{ancestor}.{...}.{local_id}</c> (regex
    /// <c>^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$</c>, Varn-lock
    /// 2026-05-15 v2 §1).
    ///
    /// <para>
    /// <b>Default empty.</b> Existing scenes (<c>M1Slice</c>,
    /// <c>PoiSpawnProbe</c>, legacy <c>E1WorldMap</c> mocks) carry no
    /// id ; the tooltip Pattern B aggregator treats an empty
    /// <see cref="PoiId"/> as "no missions filterable" — render path
    /// short-circuits, no regression. Backward-compat by design.
    /// </para>
    ///
    /// <para>
    /// <b>How it gets set.</b> Two paths supported :
    /// <list type="bullet">
    ///   <item>Resource authoring (<c>.tres</c>) — designer fills the
    ///         field in the inspector. Will land when the legacy
    ///         <c>HalfgateMarchePois.cs</c> / <c>CityHalfgatePois.cs</c>
    ///         mocks migrate to runtime instantiation from
    ///         <c>poi_tree.json</c> (M-15/M-16 PR, hors-scope).</item>
    ///   <item>Code override (scoped, MVP) — the scene that spawns the
    ///         POI assigns it post-load. See
    ///         <c>IsoMapE1Probe.SpawnHalfgatePoi</c> for the MVP
    ///         pattern : <c>_halfgatePoi.PoiData.PoiId = "e1.halfgate"</c>
    ///         after <c>PoiSidecarLoader.Load(...)</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Export] public string PoiId { get; set; } = "";

    // Runtime-only — populated by PoiSidecarLoader, never serialized to .tres.
    public BitArray? AlphaMask { get; set; }
    public int AlphaMaskWidth { get; set; }
    public HashSet<Vector2I>? FootprintTiles { get; set; }

}
