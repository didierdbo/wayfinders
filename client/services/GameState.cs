using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton holding cross-screen persisted state. Pre-brief §4.7.
///
/// <para>
/// J1 scope was tiny:
/// <list type="bullet">
///   <item><see cref="HasSave"/> — controls the [02] Continuer button on E1.</item>
///   <item><see cref="LastVisitedScreenId"/> — what Continuer points at (J3+ populated).</item>
///   <item><see cref="LocationContext"/> — current city/district context (J3+ populated).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>4b additions (M1 mission-emergence slice).</b> Three new slots
/// for the mission-emergence loop, all client-owned (NPC-autonomy
/// lock 2026-05-09 puts the authoritative GameState on the client) :
/// <list type="bullet">
///   <item><see cref="CompanyPersonas"/> — the typed roster snapshot
///         <see cref="WorldSimTick"/> packs into
///         <c>WorldTickRequest.company_personas</c> every tick. M1
///         starts empty ; populated by 4c (Compagnie panel) and / or
///         a debug seeder (4d test scene).</item>
///   <item><see cref="PendingMissions"/> — <b>§A.8.D5 R4 (2026-05-17)</b> :
///         read-only projection over <see cref="MissionStore.AllActive"/>.
///         The mutable backing list that lived here pre-§A.8 has been
///         retired ; writers now go through <see cref="MissionStore"/>
///         (<see cref="MissionStore.IngestEmerged"/> from
///         <see cref="WorldSimTick"/>, <see cref="MissionStore.RemoveById"/>
///         from the M1Slice accept / decline paths after their POST
///         to the new mission-action endpoints lands).</item>
///   <item><see cref="PersonaLegacy"/> — per-persona list of
///         <see cref="PersonaLegacyTagDto"/> earned across resolved
///         missions. Mirrors the server's
///         <c>game_state.persona_legacy</c> dict (the server is
///         stateless ; the client is the authority).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>M2-advance step 2 addition (2026-05-10).</b> One more
/// authoritative-on-client slot :
/// <list type="bullet">
///   <item><see cref="ActiveMissions"/> — list of
///         <see cref="ActiveMissionDto"/> in flight (player accepted,
///         server has not yet concluded). Replaces the M1
///         "drop = immediate resolve" path with "drop = countdown
///         start ; tick scan triggers conclude at <c>tick_due</c>".
///         Mission stays in this list throughout the
///         <see cref="MissionDurationLogic.DurationTicks"/>-tick
///         window ; popped on successful conclude
///         response.</item>
/// </list>
/// Server stays stateless ; no Pydantic mirror exists for
/// <see cref="ActiveMissionDto"/> — it is a pure client construct.
/// See <c>MissionDurationLogic.cs</c> for the decision contracts
/// over this slot.
/// </para>
///
/// <para>
/// <b>I-23 typed LocationContext promotion (Varn-lock 2026-05-17 Section B).</b>
/// The legacy <c>Dictionary&lt;string, object&gt; LocationContext</c>
/// slot is replaced by a typed <see cref="LocationContextState"/> record
/// plus four setter methods and the <see cref="LocationContextChanged"/>
/// signal. Migration was zero-cost (the dict had no readers and no
/// writers in the codebase). The new property has four nullable string
/// fields : <c>CurrentCityId</c>, <c>CurrentDistrictId</c>,
/// <c>SelectedNpcId</c>, <c>SelectedPoiId</c>. Writers populate fields
/// as the player navigates ; readers must tolerate null (a fresh boot
/// snapshot returns <see cref="LocationContextState.Empty"/>).
/// </para>
///
/// <para>
/// <b>Why an autoload, not static.</b> Same rationale as
/// <see cref="ApiClient"/>: the autoload lives in the scene tree, can be
/// remote-inspected, has predictable _Ready/_ExitTree lifetimes, and
/// survives scene reloads naturally. Static state would survive too but
/// without engine-driven teardown — opaque.
/// </para>
///
/// <para>
/// <b>Save persistence is hors-scope MVP (Varn §7).</b> J1 keeps the API
/// shape but does not write to disk ; HasSave starts false and can be
/// flipped via a debug button if needed. The 4b slots
/// (<see cref="CompanyPersonas"/>, <see cref="PendingMissions"/>,
/// <see cref="PersonaLegacy"/>) plus the M2-advance
/// <see cref="ActiveMissions"/> slot are session-only too — saves
/// land in M2 (post-step-2).
/// </para>
///
/// <para>
/// <b>Mutability discipline.</b> The list / dictionary slots are
/// returned as concrete <see cref="List{T}"/> /
/// <see cref="Dictionary{TKey, TValue}"/> rather than the read-only
/// interfaces because callers need to <c>.Add()</c> /
/// <c>.Remove()</c> directly (idiomatic for a session bag). Producers
/// MUST mutate from the main thread only — see
/// <see cref="WorldSimTick"/> for the canonical
/// <c>CallDeferred</c> pattern that marshals async results back to
/// the main thread before mutating these slots.
/// Exception : <see cref="PendingMissions"/> is now a read-only
/// projection over <see cref="MissionStore.AllActive"/> (§A.8.D5 R4).
/// Writers route through <see cref="MissionStore"/>.
/// </para>
/// </summary>
public partial class GameState : Node
{
    /// <summary>
    /// Fired after a cell's reveal-substrate state changes via
    /// <see cref="SetTileRevealState"/>. The render layer (a
    /// <c>TileRevealRenderController</c> in the scene that owns the
    /// face-B reveal subsystem) subscribes once at <c>_Ready</c> and
    /// animates the derived <c>reveal_level</c> float via a 300 ms
    /// cubic-out Tween per the Varn-locked render mapping
    /// (<see cref="TileRevealStateHelpers.ResolveRevealLevel(TileRevealState)"/>).
    /// Disconnection discipline is the consumer's responsibility
    /// (Rune-coaching Risk #1, signal-leak trap).
    ///
    /// <para>
    /// <b>Wire format.</b> The cell is a <see cref="Vector2I"/>
    /// (one of the few Godot-native struct types that marshal through
    /// the [Signal] binding in Godot 4.6) ; the states are integer
    /// casts of <see cref="TileRevealState"/> values. The consumer
    /// casts back at the receiver edge ; see
    /// <c>TileRevealRenderController</c> for the canonical receiver.
    /// </para>
    ///
    /// <para>
    /// <b>Why on GameState (not on a dedicated store).</b> The reveal
    /// substrate is a session-wide GameState authority per Varn
    /// reconciliation 2026-05-16 §3.2 : it lives alongside
    /// <see cref="PendingMissions"/> and
    /// <see cref="ActiveMissions"/> in the client-side authoritative
    /// snapshot. Promoting to a sub-Node store is reserved for the
    /// day the dictionary grows past O(thousands) of cells and the
    /// cost of holding it as a plain dict starts to matter.
    /// </para>
    /// </summary>
    [Signal]
    public delegate void TileRevealStateChangedEventHandler(Vector2I cell, int oldState, int newState);

    /// <summary>
    /// Fired after any field of <see cref="LocationContext"/> mutates
    /// via one of the typed setters
    /// (<see cref="SetCurrentCityId"/>,
    /// <see cref="SetCurrentDistrictId"/>,
    /// <see cref="SetSelectedNpcId"/>,
    /// <see cref="SetSelectedPoiId"/>) or
    /// <see cref="ClearLocationContext"/>.
    ///
    /// <para>
    /// <b>No-op suppression.</b> The setters compare the old field
    /// value to the new one (record equality, free for nullable
    /// strings) and skip the signal emission when they are identical.
    /// This matches the <see cref="SetTileRevealState"/> discipline
    /// (idempotent setter, silent no-op on unchanged value).
    /// </para>
    ///
    /// <para>
    /// <b>Wire format.</b> <paramref name="field"/> is one of
    /// <c>"CurrentCityId"</c> / <c>"CurrentDistrictId"</c> /
    /// <c>"SelectedNpcId"</c> / <c>"SelectedPoiId"</c> — the literal
    /// property name of <see cref="LocationContextState"/>. The
    /// old/new values are passed through the variadic
    /// <see cref="GodotObject.EmitSignal(StringName, Variant[])"/>
    /// surface ; null values are converted to <c>(Variant)default</c>
    /// at the emission site (Godot's <c>Variant</c> implicit operator
    /// rejects <c>null</c> strings at compile time even though the
    /// engine accepts a null-Variant at runtime). Consumers that want
    /// a typed dispatch on the field name should branch on the literal
    /// string ; consumers that want the full snapshot should just read
    /// <see cref="LocationContext"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Varn-lock 2026-05-17 Section B.</b> Replaces the implicit
    /// "writers and readers will marshal through a string-keyed dict"
    /// contract with one explicit signal at the autoload edge.
    /// Disconnection discipline is the consumer's responsibility
    /// (Rune-coaching Risk #1, signal-leak trap).
    /// </para>
    /// </summary>
    [Signal]
    public delegate void LocationContextChangedEventHandler(
        string field,
        string oldValue,
        string newValue);

    /// <summary>
    /// True if a save exists. J1: always false on boot, no persistence.
    /// J3+: replaced with a real save-system check.
    /// </summary>
    public bool HasSave { get; set; } = false;

    /// <summary>
    /// Screen id to return to when [02] Continuer is pressed. Null if no
    /// save. J1 stub.
    /// </summary>
    public string? LastVisitedScreenId { get; set; }

    /// <summary>
    /// Typed cross-screen "where am I / who's selected" snapshot.
    /// Replaces the free-form <c>Dictionary&lt;string, object&gt;</c>
    /// that this slot held before the I-23 typed promotion
    /// (Varn-lock 2026-05-17 Section B). All four fields nullable +
    /// default null ; readers MUST tolerate null. Mutate via the
    /// typed setter methods only :
    /// <list type="bullet">
    ///   <item><see cref="SetCurrentCityId"/></item>
    ///   <item><see cref="SetCurrentDistrictId"/></item>
    ///   <item><see cref="SetSelectedNpcId"/></item>
    ///   <item><see cref="SetSelectedPoiId"/></item>
    ///   <item><see cref="ClearLocationContext"/></item>
    /// </list>
    /// Each setter emits <see cref="LocationContextChanged"/> exactly
    /// once when the field actually changes ; no-op writes are
    /// silently suppressed (same idempotence discipline as
    /// <see cref="SetTileRevealState"/>).
    /// </summary>
    public LocationContextState LocationContext { get; private set; } = LocationContextState.Empty;

    /// <summary>Set <see cref="LocationContextState.CurrentCityId"/>.
    /// Pass null to clear. Emits <see cref="LocationContextChanged"/>
    /// with field name <c>"CurrentCityId"</c> only if the value
    /// actually changes.</summary>
    public void SetCurrentCityId(string? cityId)
    {
        var old = LocationContext.CurrentCityId;
        if (old == cityId) return;
        LocationContext = LocationContext with { CurrentCityId = cityId };
        EmitLocationContextChanged("CurrentCityId", old, cityId);
    }

    /// <summary>Set <see cref="LocationContextState.CurrentDistrictId"/>.
    /// Pass null to clear. Emits <see cref="LocationContextChanged"/>
    /// with field name <c>"CurrentDistrictId"</c> only if the value
    /// actually changes.</summary>
    public void SetCurrentDistrictId(string? districtId)
    {
        var old = LocationContext.CurrentDistrictId;
        if (old == districtId) return;
        LocationContext = LocationContext with { CurrentDistrictId = districtId };
        EmitLocationContextChanged("CurrentDistrictId", old, districtId);
    }

    /// <summary>Set <see cref="LocationContextState.SelectedNpcId"/>.
    /// Pass null to clear. Emits <see cref="LocationContextChanged"/>
    /// with field name <c>"SelectedNpcId"</c> only if the value
    /// actually changes.</summary>
    public void SetSelectedNpcId(string? npcId)
    {
        var old = LocationContext.SelectedNpcId;
        if (old == npcId) return;
        LocationContext = LocationContext with { SelectedNpcId = npcId };
        EmitLocationContextChanged("SelectedNpcId", old, npcId);
    }

    /// <summary>Set <see cref="LocationContextState.SelectedPoiId"/>.
    /// Pass null to clear. Emits <see cref="LocationContextChanged"/>
    /// with field name <c>"SelectedPoiId"</c> only if the value
    /// actually changes.</summary>
    public void SetSelectedPoiId(string? poiId)
    {
        var old = LocationContext.SelectedPoiId;
        if (old == poiId) return;
        LocationContext = LocationContext with { SelectedPoiId = poiId };
        EmitLocationContextChanged("SelectedPoiId", old, poiId);
    }

    /// <summary>
    /// Reset <see cref="LocationContext"/> to
    /// <see cref="LocationContextState.Empty"/> (all four fields null).
    /// Fires one <see cref="LocationContextChanged"/> per field that
    /// was non-null before the reset. Called by
    /// <c>OpeningBootstrap.OnNewGamePressed</c> per Varn-lock §B.3
    /// reset rule. Does NOT fire on E1 ↔ E2 ↔ E3 ladder transitions —
    /// those flows mutate individual fields surgically via the typed
    /// setters above.
    /// </summary>
    public void ClearLocationContext()
    {
        SetCurrentCityId(null);
        SetCurrentDistrictId(null);
        SetSelectedNpcId(null);
        SetSelectedPoiId(null);
    }

    /// <summary>
    /// Centralised emit helper that converts the two nullable
    /// <c>string?</c> old/new values to <see cref="Variant"/>
    /// arguments at the call site. The empty <c>Variant</c>
    /// (<c>default(Variant)</c>) is the Godot wire representation of
    /// "null" : the engine accepts it, but the C# implicit
    /// <c>string -&gt; Variant</c> operator refuses a <c>null</c>
    /// argument at compile time. Consumers reading the signal
    /// receive the empty-Variant slot when the old/new value was null
    /// ; deserialising back to <c>string?</c> at the receiver edge is
    /// <c>arg.AsString()</c> which returns an empty string for the
    /// empty Variant (consumers compare against
    /// <see cref="string.IsNullOrEmpty(string)"/>).
    /// </summary>
    private void EmitLocationContextChanged(string field, string? oldValue, string? newValue)
    {
        EmitSignal(
            SignalName.LocationContextChanged,
            field,
            oldValue is null ? default : Variant.From(oldValue),
            newValue is null ? default : Variant.From(newValue));
    }

    /// <summary>
    /// Typed roster snapshot the world-tick autoload packs into
    /// <c>WorldTickRequest.company_personas</c> every tick. Starts
    /// empty in M1 ; populated by the Compagnie panel (4c) or a
    /// debug seeder (4d test scene). The server reads this list to
    /// compute <see cref="EmergentMissionDto.EligiblePersonas"/>
    /// via the ±1-bucket window rule (Varn-lock 2026-05-10).
    ///
    /// <para>
    /// <b>Mutation contract.</b> Mutate from the main thread only
    /// (Add / Remove). The autoload <see cref="WorldSimTick"/>
    /// snapshots this list at request build time via
    /// <c>.ToArray()</c>, so a concurrent main-thread mutation
    /// during the snapshot window cannot corrupt an in-flight
    /// request.
    /// </para>
    /// </summary>
    public List<CharacterStateDto> CompanyPersonas { get; } = new();

    /// <summary>
    /// Read-only projection over <see cref="MissionStore.AllActive"/>
    /// (Varn §A.8.D5 R4, 2026-05-17). The mutable backing list that
    /// used to live here pre-§A.8 is retired ; writers now route
    /// through <see cref="MissionStore"/> :
    /// <list type="bullet">
    ///   <item><see cref="WorldSimTick.OnMissionEmergedDeferred"/>
    ///         calls <see cref="MissionStore.IngestEmerged"/> when a
    ///         tick produces a mission.</item>
    ///   <item>The M1Slice accept / decline paths call
    ///         <see cref="MissionStore.RemoveById"/> after they POST
    ///         the matching mission-action endpoint
    ///         (<c>/api/missions/&lt;id&gt;/{accept,decline}</c>) to
    ///         the backend.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Lazy MissionStore resolution.</b> The autoload boot order
    /// in <c>project.godot</c> places <c>GameState</c> BEFORE
    /// <c>MissionStore</c> so <c>_Ready</c> cannot eagerly cache a
    /// reference. The projection getter resolves the MissionStore via
    /// <c>GetTree().Root.GetNodeOrNull&lt;MissionStore&gt;("MissionStore")</c>
    /// on first read and caches the reference. If the autoload is
    /// missing (test harness, scene loaded before autoload), the
    /// projection returns
    /// <see cref="System.Array.Empty{EmergentMissionDto}"/> — safe
    /// empty list, no null surface.
    /// </para>
    ///
    /// <para>
    /// <b>Read-only seam.</b> The return type is
    /// <see cref="IReadOnlyList{T}"/> so legacy callers that did
    /// <c>PendingMissions.Add(...)</c> / <c>RemoveAt(0)</c> fail at
    /// compile time and get migrated to the
    /// <see cref="MissionStore"/> API. Index access (<c>[0]</c>,
    /// <c>Count</c>) keeps working unchanged.
    /// </para>
    /// </summary>
    public IReadOnlyList<EmergentMissionDto> PendingMissions
    {
        get
        {
            if (_missionStore is null || !IsInstanceValid(_missionStore))
            {
                _missionStore = GetTree()?.Root?.GetNodeOrNull<MissionStore>("MissionStore");
            }
            return _missionStore is null
                ? System.Array.Empty<EmergentMissionDto>()
                : _missionStore.AllActive;
        }
    }

    /// <summary>
    /// Cached reference to the <see cref="MissionStore"/> autoload.
    /// Resolved lazily on first <see cref="PendingMissions"/> read
    /// (autoload boot order places GameState before MissionStore).
    /// Re-resolved if the cached reference is freed (engine teardown
    /// races) -- <see cref="GodotObject.IsInstanceValid"/> check on
    /// every access.
    /// </summary>
    private MissionStore? _missionStore;

    /// <summary>
    /// Missions accepted by the player and counting down to
    /// conclusion. M2-advance step 2 addition (Didier 2026-05-10).
    /// Replaces the M1 immediate-resolve path : at drop-accept time
    /// an <see cref="ActiveMissionDto"/> is appended here ; at every
    /// <c>TickAdvanced</c> the runtime scans this list, calls
    /// <see cref="MissionDurationLogic.IsMissionDue"/> per entry,
    /// and fires <c>POST /api/world/mission/conclude</c> for the
    /// due ones. On a successful conclude response the mission is
    /// removed from this list and the resulting tags are appended
    /// to <see cref="PersonaLegacy"/>.
    ///
    /// <para>
    /// <b>Lifecycle.</b>
    /// <list type="number">
    ///   <item>Appended by <c>M1Slice.ResolveAcceptedAssignment</c>
    ///         at drop-accept (or <c>OnAffectPressed</c> on the
    ///         keyboard backup path).</item>
    ///   <item>Read at every tick by
    ///         <c>M1Slice.OnTickAdvanced</c> for the countdown
    ///         suffix render + the due-mission scan.</item>
    ///   <item>Removed by <c>M1Slice.OnConcludeSuccess</c> after
    ///         the conclude response lands and the tags have been
    ///         appended to <see cref="PersonaLegacy"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Why a list, not a dictionary keyed by mission id.</b> The
    /// runtime scans every entry on every tick (≤5 active missions
    /// in the M2 horizon) ; a list is O(n) but n is tiny. A
    /// dictionary would add a re-hashing cost per add/remove and
    /// would need a stable insertion order separately for the
    /// "render the active missions" UI hook (M3+). List is simpler
    /// and matches <see cref="PendingMissions"/>'s shape.
    /// </para>
    ///
    /// <para>
    /// <b>Mutation contract.</b> Same as
    /// <see cref="PendingMissions"/> : main-thread mutation only.
    /// The conclude response lands on a thread-pool thread (the
    /// HTTP continuation) ; <c>M1Slice</c> uses <c>CallDeferred</c>
    /// to marshal back before adding/removing entries here.
    /// </para>
    /// </summary>
    public List<ActiveMissionDto> ActiveMissions { get; } = new();

    /// <summary>
    /// Per-persona list of legacy tags. Server returns these in the
    /// <see cref="MissionResolveResponseDto.TagsCreated"/> response ;
    /// the client appends them here (the server is stateless —
    /// authoritative storage is client-side).
    ///
    /// <para>
    /// <b>Dedup contract.</b> The natural key is
    /// <c>(persona_id, mission_id)</c>. M1 does not enforce dedup
    /// at storage time (simple impl : append blindly). Callers that
    /// read this dictionary should dedup at read time if they need
    /// "distinct missions per persona". M2 may revisit if duplicate
    /// noise becomes a problem.
    /// </para>
    ///
    /// <para>
    /// <b>Architecture invariant.</b> Tags are pure data — they
    /// never re-parent any persona Node3D. The Compagnie panel
    /// reflects the new tag visually (e.g. "veteran of Halfgate"
    /// status flip), but the persona stays in its original
    /// container (NPC-autonomy lock 2026-05-09).
    /// </para>
    /// </summary>
    public Dictionary<string, List<PersonaLegacyTagDto>> PersonaLegacy { get; } = new();

    /// <summary>
    /// Convenience helper : append a tag under its
    /// <see cref="PersonaLegacyTagDto.PersonaId"/> key. Idiomatic for
    /// "got 5 tags from one resolution, store all 5". Creates the
    /// per-persona list lazily on first append.
    /// </summary>
    public void AppendPersonaLegacyTag(PersonaLegacyTagDto tag)
    {
        if (!PersonaLegacy.TryGetValue(tag.PersonaId, out var list))
        {
            list = new List<PersonaLegacyTagDto>();
            PersonaLegacy[tag.PersonaId] = list;
        }
        list.Add(tag);
    }

    /// <summary>
    /// Per-cell reveal-substrate state, GameState-authoritative per
    /// Varn reconciliation 2026-05-16 §3.2. Orthogonal to the
    /// <see cref="Wayfinders.Client.Scripts.Screens.TileKnowledgeLadder"/>
    /// 5-state ladder owned by <c>TileKnowledgeStore</c> (which is
    /// scene-scoped, not GameState-scoped).
    ///
    /// <para>
    /// <b>Default-state semantics.</b> Cells absent from the dictionary
    /// are treated as <see cref="TileRevealState.Fog"/> by
    /// <see cref="GetTileRevealState"/>. Mirrors the sparse-dict
    /// idiom of <see cref="TileKnowledgeStore"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Mutation contract.</b> Same as the other GameState slots :
    /// main-thread only. Producers running on a thread-pool thread
    /// (HTTP continuations, async event handlers) MUST hop back via
    /// <c>CallDeferred</c> before calling
    /// <see cref="SetTileRevealState"/>.
    /// </para>
    ///
    /// <para>
    /// <b>M1/M2 population.</b> The dictionary is empty at boot. No
    /// runtime gameplay path populates it yet ; population lands when
    /// Phase E2.1 / E2.2 wire NPC-recruit known_tiles and mission
    /// success_effect / reveal_on_fail to flip cells. The signal
    /// surface and the controller plumbing exist now so the day a
    /// gameplay event mutates a cell, the render path animates the
    /// face-B reveal end-to-end with no additional wiring.
    /// </para>
    /// </summary>
    public Dictionary<Vector2I, TileRevealState> TileRevealStates { get; } = new();

    /// <summary>
    /// Read the reveal-substrate state for a cell. Cells that have
    /// never been mutated return <see cref="TileRevealState.Fog"/> -
    /// the canonical default per Varn reconciliation §3.2.
    /// </summary>
    public TileRevealState GetTileRevealState(Vector2I cell) =>
        TileRevealStates.TryGetValue(cell, out var state) ? state : TileRevealState.Fog;

    /// <summary>
    /// Set the reveal-substrate state for a cell and emit
    /// <see cref="TileRevealStateChanged"/> if the value actually
    /// changes. Idempotent : setting a cell to its current state is a
    /// silent no-op (no signal fired, no allocation).
    ///
    /// <para>
    /// <b>Sparse-dict invariant.</b> Setting a cell back to
    /// <see cref="TileRevealState.Fog"/> removes it from the dictionary
    /// so the absent-key-default semantics stay consistent and the dict
    /// does not bloat with explicit-Fog entries.
    /// </para>
    /// </summary>
    public void SetTileRevealState(Vector2I cell, TileRevealState newState)
    {
        var current = GetTileRevealState(cell);
        if (current == newState) return;

        if (newState == TileRevealState.Fog)
        {
            TileRevealStates.Remove(cell);
        }
        else
        {
            TileRevealStates[cell] = newState;
        }
        EmitSignal(SignalName.TileRevealStateChanged, cell, (int)current, (int)newState);
    }

    public override void _Ready()
    {
        GD.Print("[GameState] ready");
    }

    public override void _ExitTree()
    {
        GD.Print("[GameState] disposed");
    }
}
