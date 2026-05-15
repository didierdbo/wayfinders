using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for <c>GET /api/world/poi_tree</c> (Tess commit
/// 6c57df5, 2026-05-15). Single flat manifest, FastAPI-served, client
/// cached at boot. See Varn-lock 2026-05-15 v2 §2.
///
/// <para>
/// <b>Why flat-dict, not nested.</b> The server returns the manifest
/// as one JSON object keyed by canonical POI id ; parent / children
/// links are encoded as fields on each entry, NOT as nesting. This
/// lets the client cache a single <see cref="Dictionary{TKey, TValue}"/>
/// keyed by id and answer the tooltip-aggregation question in O(n)
/// over the missions (prefix-match) without ever traversing the tree.
/// </para>
///
/// <para>
/// <b>Why a <c>version</c> field at top level.</b> The manifest is a
/// referential ; bumping the version lets Varn rev the closed lookup
/// (M-15/M-16 when the legacy <c>HalfgateMarchePois.cs</c> / similar
/// mocks retire) without breaking older clients silently. M1 always
/// pins version = 1.
/// </para>
///
/// <para>
/// <b>Naming policy.</b> The top-level dict mixes a <c>version</c>
/// scalar with POI-id-keyed entries. <c>System.Text.Json</c> source-gen
/// cannot model a mixed-shape JSON object directly ; the response is
/// parsed as an envelope (<see cref="PoiTreeResponseDto"/>) with
/// <c>version</c> + a <see cref="Dictionary{TKey, TValue}"/> of
/// <see cref="PoiTreeNodeDto"/>. The server splits the manifest into
/// that shape explicitly — see <c>wayfinders/api/poi_tree.py</c>.
/// </para>
/// </summary>
public sealed record PoiTreeNodeDto(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("layer")] int Layer,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("children")] IReadOnlyList<string> Children
);

/// <summary>
/// Envelope wrapping the <c>GET /api/world/poi_tree</c> payload —
/// pulls <c>version</c> out as a typed scalar and the rest of the
/// manifest into <see cref="Nodes"/>.
///
/// <para>
/// <b>Tess's wire shape.</b> The server emits a flat object :
/// <code>
/// {
///   "version": 1,
///   "e1.halfgate": {"display_name": "Halfgate", ...},
///   "e2.halfgate.docks": {...}
/// }
/// </code>
/// That is not directly modellable as a single source-gen record.
/// Two paths considered : (a) parse to <c>JsonDocument</c> on the
/// client and dispatch on key names ; (b) ask Tess to wrap entries
/// in a <c>"nodes"</c> field so the envelope is uniform. Option (b)
/// is the cleaner contract — already discussed with Tess (M-15 PR)
/// but for MVP we run with (a) inside
/// <see cref="Wayfinders.Client.Services.PoiTreeService.LoadAsync"/>
/// using a one-shot <c>JsonDocument</c> parse, NOT this record. The
/// record exists as a target shape once the envelope migration lands.
/// </para>
/// </summary>
public sealed record PoiTreeResponseDto(
    int Version,
    IReadOnlyDictionary<string, PoiTreeNodeDto> Nodes
);
