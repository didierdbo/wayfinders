using System.Collections.Generic;
using Godot;
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
///   <item><see cref="PendingMissions"/> — list of
///         <see cref="EmergentMissionDto"/> awaiting player
///         affectation. Locked as a list (NOT a single slot) by
///         Didier 2026-05-10 because the 5-10-tick cadence can stack
///         missions while the player is away from the panel.</item>
///   <item><see cref="PersonaLegacy"/> — per-persona list of
///         <see cref="PersonaLegacyTagDto"/> earned across resolved
///         missions. Mirrors the server's
///         <c>game_state.persona_legacy</c> dict (the server is
///         stateless ; the client is the authority).</item>
/// </list>
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
/// <see cref="PersonaLegacy"/>) are session-only too — saves land in M2.
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
/// </para>
/// </summary>
public partial class GameState : Node
{
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
    /// Free-form bag of cross-screen state. J1: empty. J3+ keys include
    /// "current_city_id", "current_district_id", "selected_pnj_id", etc.
    /// Same rationale as <see cref="Wayfinders.Client.Scripts.Screens.ScreenContext"/> —
    /// concrete-first, promote to typed properties when usage becomes clear.
    /// </summary>
    public Dictionary<string, object> LocationContext { get; } = new();

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
    /// Missions that have emerged and await player affectation. List
    /// (not a single slot) because the 5-10-tick cadence (Varn
    /// 2026-05-10) can stack missions while the player has the
    /// Mission panel closed. Locked as a list by Didier 2026-05-10
    /// (memory <c>project_wayfinders_mission_emergence.md</c>).
    ///
    /// <para>
    /// <b>Lifecycle.</b> Appended by
    /// <see cref="WorldSimTick.OnMissionEmergedDeferred"/> on the
    /// main thread when a tick produces a mission. Removed by the
    /// Mission panel after the player accepts or declines and the
    /// resolution endpoint succeeds.
    /// </para>
    /// </summary>
    public List<EmergentMissionDto> PendingMissions { get; } = new();

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

    public override void _Ready()
    {
        GD.Print("[GameState] ready");
    }

    public override void _ExitTree()
    {
        GD.Print("[GameState] disposed");
    }
}
