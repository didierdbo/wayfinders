using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton holding the canonical POI tree (Varn-lock
/// 2026-05-15 v2 §2). Loaded once at boot from
/// <c>GET /api/world/poi_tree</c> (Tess commit 6c57df5).
///
/// <para>
/// <b>Why an autoload.</b> The POI tree is referential — chargé une
/// fois, lecture seule en gameplay. Same lifetime pattern as
/// <see cref="GameState"/> / <see cref="ApiClient"/> : sits in the
/// scene tree, gets <c>_Ready</c> / <c>_ExitTree</c>, survives scene
/// reloads naturally. The tooltip aggregator
/// (<c>IsoMapE1Probe.OnPoiHoverEnter</c>, future M1Slice /
/// PoiSpawnProbe) reads the cached dictionary via
/// <c>GetNodeOrNull&lt;PoiTreeService&gt;("/root/PoiTreeService")</c>.
/// </para>
///
/// <para>
/// <b>Graceful degrade.</b> If the HTTP load fails (server down at
/// boot, network glitch, body parse error), the autoload keeps an
/// empty dictionary and logs a warning. Every public helper handles
/// the empty case : <see cref="IsDescendantOf"/> returns false,
/// <see cref="GetNode"/> returns null, <see cref="GetDescendantIds"/>
/// yields just the queried id (so the tooltip aggregator still works
/// for the exact-match case). MVP behaviour : no missions surface in
/// tooltip until the next boot with the backend up.
/// </para>
///
/// <para>
/// <b>Aggregation contract (Varn §2).</b> The tooltip of a POI <c>P</c>
/// at layer N displays missions <c>m</c> such that
/// <c>m.target_poi == P.id</c> OR <c>m.target_poi</c> starts with
/// <c>P.id + "."</c> (descendant). Two helpers serve that contract :
/// <see cref="IsDescendantOf"/> answers the boolean for a single
/// mission (cheap, callers filter with LINQ) ;
/// <see cref="GetDescendantIds"/> yields the prefix-match set if a
/// caller needs the inverse (e.g. M3+ "show all sub-POIs of region X").
/// O(1) per check via string-prefix, no tree traversal — the
/// hierarchical id format makes ancestry self-describing.
/// </para>
///
/// <para>
/// <b>Why one-shot JsonDocument, not source-gen.</b> The wire shape
/// is a flat dict that mixes a scalar <c>version</c> with POI-id
/// keys ; <c>System.Text.Json</c> source-gen does not model mixed
/// shapes directly. The boot path is one-off (~5 KB payload, parsed
/// once per session) so the trim/AOT cost of a reflection-y
/// <see cref="System.Text.Json.JsonDocument"/> here is negligible —
/// the rest of the boundary keeps source-gen. M-15+ migration : ask
/// Tess to wrap entries under a <c>"nodes"</c> field so the envelope
/// becomes uniform and source-gen-friendly (cf. comment on
/// <see cref="PoiTreeResponseDto"/>).
/// </para>
///
/// <para>
/// <b>Thread-safety.</b> The load is async ; the dictionary mutation
/// happens on the main thread (via CallDeferred from the
/// continuation). Public reads after boot are pure dictionary lookups
/// — safe to call from <c>_Process</c> or input handlers without
/// further marshalling. The autoload itself never touches scene-tree
/// state.
/// </para>
/// </summary>
public partial class PoiTreeService : Node
{
    private const string EndpointPath = "/api/world/poi_tree";

    private readonly Dictionary<string, PoiTreeNodeDto> _nodes = new();
    private int _version;
    private bool _loaded;
    private CancellationTokenSource _shutdownCts = null!;

    /// <summary>True once <see cref="LoadAsync"/> has completed
    /// successfully (even with an empty tree).</summary>
    public bool IsLoaded => _loaded;

    /// <summary>The manifest version pulled from the response, or 0
    /// if no load has succeeded yet.</summary>
    public int Version => _version;

    /// <summary>Total node count in the cached tree (0 if not yet
    /// loaded or load failed).</summary>
    public int NodeCount => _nodes.Count;

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();
        GD.Print("[PoiTreeService] ready (tree empty, awaiting boot load)");
    }

    public override void _ExitTree()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        GD.Print("[PoiTreeService] disposed");
    }

    /// <summary>
    /// Fire-and-forget boot load. Called by
    /// <see cref="OpeningBootstrap"/> after autoloads are stable.
    /// Idempotent : a second call replaces the cached tree (useful
    /// for editor hot-reload of the manifest in dev).
    ///
    /// <para>
    /// <b>Error handling.</b> Every failure path logs and leaves the
    /// cache empty ; the autoload never throws to the caller. The
    /// tooltip aggregator must handle <see cref="NodeCount"/> == 0
    /// gracefully (see <see cref="IsDescendantOf"/>).
    /// </para>
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var apiClient = GetNodeOrNull<ApiClient>("/root/ApiClient");
        if (apiClient is null)
        {
            GD.PushWarning("[PoiTreeService] ApiClient autoload missing — skipping POI tree load");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var (version, nodes, error) = await apiClient
            .FetchPoiTreeRawAsync(EndpointPath, linkedCts.Token)
            .ConfigureAwait(false);

        // Marshal back to the main thread before mutating the cache —
        // future callers may already be inspecting it via
        // _Process-driven paths (the tooltip aggregator). We use
        // Callable.From + a lambda capture so the dictionary payload
        // (a C# generic Dictionary, NOT a Godot Variant-compatible
        // type) crosses the deferred-call boundary by reference,
        // bypassing the engine marshaller entirely — same pattern as
        // capturing a closure for any non-Variant payload.
        var safeNodes = nodes ?? new Dictionary<string, PoiTreeNodeDto>();
        var safeError = error ?? "";
        Callable.From(() => ApplyLoadResult(version, safeNodes, safeError)).CallDeferred();
    }

    private void ApplyLoadResult(int version, Dictionary<string, PoiTreeNodeDto> nodes, string error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            GD.PushWarning($"[PoiTreeService] load failed: {error} — tree stays empty, tooltip aggregation degraded");
            _loaded = true;   // marked loaded-with-empty so callers stop waiting
            return;
        }

        _nodes.Clear();
        foreach (var kv in nodes)
        {
            _nodes[kv.Key] = kv.Value;
        }
        _version = version;
        _loaded = true;

        GD.Print($"[PoiTreeService] loaded {_nodes.Count} nodes from {EndpointPath} (version={_version})");
    }

    /// <summary>
    /// Returns the cached node for the canonical POI id, or
    /// <c>null</c> if absent (tree not loaded, id unknown, or load
    /// failed).
    /// </summary>
    public PoiTreeNodeDto? GetNode(string poiId)
    {
        if (string.IsNullOrEmpty(poiId)) return null;
        return _nodes.TryGetValue(poiId, out var node) ? node : null;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="candidate"/> is the
    /// same as <paramref name="ancestor"/>, or a descendant of it,
    /// per the canonical hierarchical-id rule
    /// (Varn-lock 2026-05-15 v2 §2 aggregation contract).
    ///
    /// <para>
    /// Implementation : pure string prefix-match — no tree traversal
    /// needed because the id <i>is</i> the path. Cheap (string ops,
    /// no allocation) and correct for arbitrary depth. Empty
    /// arguments return false (safe default for legacy POIs that
    /// don't yet carry a PoiId).
    /// </para>
    ///
    /// <para>
    /// <b>NB.</b> This method does NOT consult the cached
    /// <see cref="_nodes"/> dictionary — it only inspects the string
    /// shapes. So a freshly-emerged mission targeting an unknown
    /// future POI id still matches its ancestor correctly. The
    /// dictionary is consulted by <see cref="GetNode"/> and
    /// <see cref="GetDescendantIds"/> only.
    /// </para>
    /// </summary>
    public bool IsDescendantOf(string candidate, string ancestor)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(ancestor)) return false;
        if (candidate == ancestor) return true;
        return candidate.StartsWith(ancestor + ".", StringComparison.Ordinal);
    }

    /// <summary>
    /// Enumerates every descendant id of <paramref name="poiId"/>
    /// in the cached tree, INCLUDING <paramref name="poiId"/> itself.
    /// Order is insertion order of the cached dictionary (effectively
    /// server response order). Empty if the tree is not loaded or
    /// <paramref name="poiId"/> is unknown — falls back to yielding
    /// just <paramref name="poiId"/> so callers can still match the
    /// exact-id case.
    /// </summary>
    public IEnumerable<string> GetDescendantIds(string poiId)
    {
        if (string.IsNullOrEmpty(poiId)) yield break;

        // Empty cache fallback : yield only the exact id so callers
        // do not pull every-id-in-the-tree. This matches the §2
        // aggregation contract for a tree that has not loaded yet.
        if (_nodes.Count == 0)
        {
            yield return poiId;
            yield break;
        }

        foreach (var id in _nodes.Keys)
        {
            if (IsDescendantOf(id, poiId)) yield return id;
        }
    }
}
