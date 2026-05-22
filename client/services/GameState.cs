using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;

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
/// <b>J3c-2 addition (2026-05-22).</b> The interactive desk authority —
/// <see cref="DeskGrid"/>, <see cref="SelectedCompanyMemberId"/>, the
/// desk occupancy index, and the <see cref="CompanyMemberSelected"/> /
/// <see cref="CompanyMemberPlacementConfirmed"/> signals. See the boxed
/// "DESK AUTHORITY" comment further down.
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

    // =========================================================================
    //  DESK AUTHORITY — J3c-2 interactive desk (Rune, 2026-05-22)
    // =========================================================================
    //
    //  The interactive desk (roadmap J3c-2) needs an authority for two facts:
    //  which Company member is SELECTED, and which member stands on which desk
    //  CELL. NPC-autonomy lock 2026-05-09 puts that authority here, on the
    //  client GameState — not on the DeskCompanyPawn nodes. The pawns are
    //  views: they react to the signals below, they never decide their own
    //  selected state and they never relocate themselves.
    //
    //  THE TACTICAL-INTENT FLOW. A desk click that targets a destination cell
    //  is an INTENT, not a relocation:
    //    1. GameScreen resolves the click to a cell and calls
    //       RequestCompanyMemberPlacement(memberId, targetCell) — the intent.
    //    2. GameState (here) evaluates the intent against DeskGrid via
    //       DeskPlacementLogic, and if accepted mutates the grid through
    //       IsoGrid.SetOccupant (which re-gates validity — the authority does
    //       not trust an intent it did not re-check).
    //    3. GameState emits CompanyMemberPlacementConfirmed.
    //    4. The DeskCompanyPawn view animates toward the new cell IN REACTION
    //       to the confirmation — never to the click.
    //  Selection is the same shape: GameScreen calls SetSelectedCompanyMember,
    //  GameState emits CompanyMemberSelected, the pawn renders the highlight.
    //
    //  Why on the autoload and not on a desk-scoped Node. The desk grid and
    //  the slot bindings outlive the GameScreen scene instance (a recruit
    //  joining a slot in J8 is persisted Company state). They sit alongside
    //  CompanyPersonas here. The DeskGrid REFERENCE is adopted from the desk
    //  board's IsoBoard at scene-ready (IsoBoard's own doc: "the authoritative
    //  owner is GameState, which adopts this reference once the district
    //  loads").
    // =========================================================================

    /// <summary>
    /// Fired after the desk selection changes via
    /// <see cref="SetSelectedCompanyMember"/>. The desk pawn views
    /// subscribe once and render the discreet selection feedback (a
    /// light lift + terracotta rim — the pion-sur-plateau grammar, no
    /// pop-colour outline). Idempotent: a no-op selection write fires
    /// nothing. Disconnection discipline is the consumer's (Rune-coaching
    /// Risk #1, signal-leak trap) — the pawn disconnects on
    /// <c>_ExitTree</c>.
    ///
    /// <para>
    /// <b>Wire format.</b> <paramref name="memberId"/> is the newly
    /// selected member id, or the empty string when the selection is
    /// cleared (a click on bare desk floor, or a toggle-off). The C#
    /// <c>string -&gt; Variant</c> operator refuses a <c>null</c> at
    /// compile time, so the emit site converts a <c>null</c> selection
    /// to <see cref="string.Empty"/>; consumers compare against
    /// <see cref="string.IsNullOrEmpty(string)"/>.
    /// </para>
    /// </summary>
    [Signal]
    public delegate void CompanyMemberSelectedEventHandler(string memberId);

    /// <summary>
    /// Fired after a placement intent has been accepted by the authority
    /// and the desk grid has been mutated — <see cref="RequestCompanyMemberPlacement"/>
    /// emits this only on a <see cref="DeskPlacementLogic.PlacementVerdict.Accepted"/>
    /// verdict. The desk pawn view for <paramref name="memberId"/>
    /// reacts by animating its position toward the new cell; a rejected
    /// or no-op intent fires nothing, so the pawn never twitches for a
    /// move that did not happen.
    ///
    /// <para>
    /// <b>Wire format.</b> The cell is passed as two <c>int</c>s
    /// (<paramref name="targetCol"/>, <paramref name="targetRow"/>)
    /// rather than a <c>Vector2I</c> so the signal stays trivially
    /// marshallable and the consumer rebuilds the coord at the receiver
    /// edge — same shape discipline as the desk's Godot-free logic
    /// layer.
    /// </para>
    /// </summary>
    [Signal]
    public delegate void CompanyMemberPlacementConfirmedEventHandler(
        string memberId, int targetCol, int targetRow);

    /// <summary>
    /// The desk's layer-2 logical grid — the authoritative occupancy of
    /// the cartographer's-desk iso board. <c>null</c> until
    /// <see cref="AdoptDeskGrid"/> is called by <c>GameScreen</c> at
    /// scene-ready (the autoload boots before any GameScreen exists, so
    /// it cannot be eagerly built here). The desk pawn placement, the
    /// J3c-2 selection / placement flow, and the J8 recruit-into-a-slot
    /// path all read and mutate occupancy through this reference.
    /// </summary>
    public IsoGrid? DeskGrid { get; private set; }

    /// <summary>
    /// The currently selected Company member on the desk, or <c>null</c>
    /// if nothing is selected. Authoritative — the desk pawn views
    /// render their highlight off this, they do not own it. Mutate via
    /// <see cref="SetSelectedCompanyMember"/> only.
    /// </summary>
    public string? SelectedCompanyMemberId { get; private set; }

    /// <summary>
    /// Desk cell → occupying member id. The slot binding J3c-1 left as a
    /// pure layout fact (<c>DeskSlotLayoutLogic</c>) becomes authoritative
    /// state here at J3c-2: which member stands on which cell. Keyed by
    /// the cell's <see cref="TileCoordinate"/>. Populated by
    /// <see cref="AdoptDeskGrid"/> from the initial formation and mutated
    /// by <see cref="RequestCompanyMemberPlacement"/> on an accepted
    /// move. The reverse index of the grid's <c>OccupantId</c> — kept so
    /// "where is member X" is O(1) without scanning every cell.
    /// </summary>
    private readonly Dictionary<TileCoordinate, string> _deskCellOccupants = new();

    /// <summary>
    /// Read-only snapshot of the desk occupancy: cell → member id. The
    /// J3c-2 click-resolution (<c>DeskSelectionLogic.ResolveClickedMember</c>)
    /// reads this to answer "which pawn occupies the clicked cell".
    /// </summary>
    public IReadOnlyDictionary<TileCoordinate, string> DeskCellOccupants
        => _deskCellOccupants;

    /// <summary>
    /// Adopt the desk board's logical grid as the authoritative desk
    /// occupancy, and seed the initial Company formation onto it.
    /// Called once by <c>GameScreen.ConfigureDesk</c> after the desk
    /// boards are ready. Idempotent re-adoption is allowed (a scene
    /// reload re-seeds cleanly): it clears the prior occupancy first.
    ///
    /// <para>
    /// Each slot cell is stamped with its member id on the grid
    /// (<see cref="IsoGrid.SetOccupant"/>) and indexed in
    /// <see cref="_deskCellOccupants"/>. The member id is the synthetic
    /// J3c-2 slot id <c>"company-slot-N"</c> — J8 replaces these with
    /// real recruited-persona ids; the desk flow does not care about the
    /// id shape, only that it is a stable opaque string.
    /// </para>
    /// </summary>
    /// <param name="grid">The desk board's <c>IsoGrid</c>.</param>
    /// <param name="slotCells">
    /// The Company formation cells in slot order
    /// (<c>DeskSlotLayoutLogic.CompanySlotCells</c>).
    /// </param>
    public void AdoptDeskGrid(
        IsoGrid grid, IReadOnlyList<TileCoordinate> slotCells)
    {
        System.ArgumentNullException.ThrowIfNull(grid);
        System.ArgumentNullException.ThrowIfNull(slotCells);

        // Clear any prior occupancy (idempotent re-adoption on a scene
        // reload) before seeding the formation onto the fresh grid.
        foreach (var cell in _deskCellOccupants.Keys)
        {
            if (grid.HasCell(cell))
            {
                grid.SetOccupant(cell, null);
            }
        }
        _deskCellOccupants.Clear();
        SelectedCompanyMemberId = null;

        DeskGrid = grid;
        for (int i = 0; i < slotCells.Count; i++)
        {
            var cell = slotCells[i];
            var memberId = DeskMemberIdForSlot(i);
            grid.SetOccupant(cell, memberId);
            _deskCellOccupants[cell] = memberId;
        }
        GD.Print($"[GameState] desk grid adopted: {slotCells.Count} slots " +
                 $"seeded, grid has {grid.Count} cells.");
    }

    /// <summary>
    /// The synthetic member id for desk slot <paramref name="slotIndex"/>
    /// in J3c-2 — <c>"company-slot-N"</c>. A stable opaque string; J8
    /// swaps these for real recruited-persona ids without the desk flow
    /// changing.
    /// </summary>
    public static string DeskMemberIdForSlot(int slotIndex)
        => $"company-slot-{slotIndex}";

    /// <summary>
    /// Set the authoritative desk selection and emit
    /// <see cref="CompanyMemberSelected"/> if it actually changed. Pass
    /// <c>null</c> to clear the selection. Idempotent — selecting the
    /// already-selected member is a silent no-op (no signal, no
    /// allocation), the same discipline as
    /// <see cref="SetSelectedNpcId"/>.
    /// </summary>
    public void SetSelectedCompanyMember(string? memberId)
    {
        if (SelectedCompanyMemberId == memberId)
        {
            return;
        }
        SelectedCompanyMemberId = memberId;
        EmitSignal(
            SignalName.CompanyMemberSelected,
            memberId ?? string.Empty);
    }

    /// <summary>
    /// Receive a desk placement intent (NPC-autonomy tactical-intent
    /// flow, step 2). Evaluates it against <see cref="DeskGrid"/> via
    /// <see cref="DeskPlacementLogic.Evaluate"/>; on an
    /// <see cref="DeskPlacementLogic.PlacementVerdict.Accepted"/>
    /// verdict it mutates the grid (vacates the source cell, occupies
    /// the target), updates the occupancy index, and emits
    /// <see cref="CompanyMemberPlacementConfirmed"/>. Any other verdict
    /// is a silent no-op — the pawn view never moves for a rejected or
    /// degenerate intent.
    /// </summary>
    /// <param name="memberId">The member the intent wants to move.</param>
    /// <param name="targetCell">The destination cell.</param>
    /// <returns>
    /// The verdict, so the caller can surface a UI nudge on a rejected
    /// intent if it wants. The grid mutation and the signal are this
    /// method's responsibility, not the caller's.
    /// </returns>
    public DeskPlacementLogic.PlacementVerdict RequestCompanyMemberPlacement(
        string memberId, TileCoordinate targetCell)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(memberId);

        if (DeskGrid is null)
        {
            GD.PushWarning("[GameState] placement intent before the desk " +
                           "grid was adopted — ignored.");
            return DeskPlacementLogic.PlacementVerdict.OffGrid;
        }

        var verdict = DeskPlacementLogic.Evaluate(DeskGrid, memberId, targetCell);
        if (verdict != DeskPlacementLogic.PlacementVerdict.Accepted)
        {
            return verdict;
        }

        // Accepted — find the member's current cell, vacate it, occupy
        // the target. The grid's SetOccupant re-gates validity, so an
        // intent that slipped through is still rejected at the mutation.
        TileCoordinate? sourceCell = null;
        foreach (var pair in _deskCellOccupants)
        {
            if (pair.Value == memberId)
            {
                sourceCell = pair.Key;
                break;
            }
        }

        if (sourceCell is { } src)
        {
            DeskGrid.SetOccupant(src, null);
            _deskCellOccupants.Remove(src);
        }
        DeskGrid.SetOccupant(targetCell, memberId);
        _deskCellOccupants[targetCell] = memberId;

        EmitSignal(
            SignalName.CompanyMemberPlacementConfirmed,
            memberId, targetCell.Col, targetCell.Row);
        return verdict;
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
