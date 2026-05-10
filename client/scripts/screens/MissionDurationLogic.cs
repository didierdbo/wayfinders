using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helpers for the M2-advance "mission-prend-du-temps"
/// flow (Didier locks 2026-05-10). Owns the data-flow contracts that
/// govern an <see cref="ActiveMissionDto"/> while it counts down :
/// who is busy, what countdown suffix to render on a persona's
/// Label3D, when a mission is due to conclude, and how to project a
/// canonical <see cref="CharacterStateDto"/> down to the thin
/// <see cref="CharacterStateWireDto"/> the conclude endpoint expects.
///
/// <para>
/// <b>Why a logic seam, not embedded in M1Slice.</b> Same rationale
/// as <see cref="M1SliceLogic"/>, <see cref="MissionPanelLogic"/>,
/// <see cref="CompagniePanelLogic"/> : the runtime scene wires the
/// engine-bound parts (Label3D mutations, _Process accumulator,
/// signal connections) ; the data-flow contracts ("how do you
/// compute the countdown suffix from a tick number" etc.) are pure
/// C# and pinned by xUnit so they cannot drift silently. The seam
/// also lets the conclude pipeline be exercised end-to-end without
/// spinning up a Godot runtime in the test host.
/// </para>
///
/// <para>
/// <b>Locked constants (Didier 2026-05-10).</b>
/// <list type="bullet">
///   <item><see cref="DurationTicks"/> = 5. Fixed across all M2
///         missions. M3+ may make this per-mission-type variable but
///         M2 forces the simplest case so the gameplay loop is
///         observable in 25 wall-clock seconds (5 s/tick × 5 ticks).
///         NOT exposed via <c>[Export]</c> — exposing it would invite
///         "let's tune at 7 ticks for the boss mission" mid-M2 and
///         break the determinism contract. Locked here, locked
///         period.</item>
///   <item><see cref="MaxAssignedPersonasPerMission"/> = 1. M2
///         strict commit-binding (1 persona ⇒ 1 mission). M3+
///         multi-persona structurally supported by the list-shaped
///         <see cref="ActiveMissionDto.AssignedPersonas"/> but
///         enforced at request-build time in M2.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> The authoritative state lives in
/// <see cref="Wayfinders.Client.Services.GameState.ActiveMissions"/>
/// (Godot autoload, mutated only on the main thread, see
/// <c>GameState.cs</c> docstring for the
/// <c>CallDeferred</c> discipline). This file owns no state ; every
/// helper is a pure function over its inputs. The runtime
/// <c>M1Slice.cs</c> reads <c>GameState.ActiveMissions</c> at every
/// <c>TickAdvanced</c> signal, asks the helpers below "what is the
/// suffix for persona X" and "is this mission due", and acts on
/// the results.
/// </para>
///
/// <para>
/// <b>Architecture invariant : no Pydantic mirror server-side.</b>
/// <see cref="ActiveMissionDto"/> is a client-only construct. The
/// server stays stateless ; when a mission concludes the client
/// posts the (mission, personas, seed, tick_due) shape to
/// <c>POST /api/world/mission/conclude</c> via the wire-only
/// <see cref="MissionConcludeRequestDto"/>. No <c>ActiveMission</c>
/// Pydantic model exists on the server. NPC-autonomy lock 2026-05-09
/// already established that authoritative state is client-side ;
/// active missions extend that contract.
/// </para>
/// </summary>
public static class MissionDurationLogic
{
    /// <summary>Locked mission duration in ticks (Didier 2026-05-10,
    /// memory <c>project_wayfinders_mission_emergence.md</c>
    /// "M2-advance"). 5 ticks at the WorldSimTick default
    /// 5 s/tick = 25 s wall-clock per mission. Constant, not
    /// configurable — see class docstring for rationale.</summary>
    public const int DurationTicks = 5;

    /// <summary>M2 strict 1-persona-per-mission. The list-shaped
    /// AssignedPersonas slot lets the structure carry M3 multi-
    /// persona without a schema migration ; this constant enforces
    /// the M2 contract at request-build time.</summary>
    public const int MaxAssignedPersonasPerMission = 1;

    /// <summary>
    /// Build a fresh <see cref="ActiveMissionDto"/> for a mission
    /// the player just accepted. <see cref="ActiveMissionDto.TickDue"/>
    /// is computed as <c>tickStarted + DurationTicks</c> so the
    /// 5-tick lock is enforced at construction time, never at
    /// runtime via a "now-DurationTicks" trick.
    ///
    /// <para>
    /// <b>Defensive contract.</b> Throws on null mission, on empty
    /// persona id, on negative tick. Throws on more than one
    /// assigned persona — the M2 commit-binding lock surfaces here
    /// rather than 100 ms later when the conclude request 422s on
    /// the server. Caller is the cross-panel drop handler in
    /// <c>M1Slice.cs</c> ; if they ever pass two persona ids a
    /// throw at this seam catches the bug at the source.
    /// </para>
    /// </summary>
    /// <param name="mission">Pending mission accepted by the player.</param>
    /// <param name="assignedPersonaId">M2 single persona id.</param>
    /// <param name="tickStarted">Current tick at acceptance time.</param>
    public static ActiveMissionDto BuildActiveMission(
        EmergentMissionDto mission,
        string assignedPersonaId,
        int tickStarted)
    {
        if (mission is null)
            throw new System.ArgumentNullException(nameof(mission));
        if (string.IsNullOrEmpty(assignedPersonaId))
            throw new System.ArgumentException(
                "assignedPersonaId required (M2 commit-binding : exactly one persona)",
                nameof(assignedPersonaId));
        if (tickStarted < 0)
            throw new System.ArgumentOutOfRangeException(
                nameof(tickStarted),
                "tickStarted must be non-negative");

        return new ActiveMissionDto(
            Mission: mission,
            TickStarted: tickStarted,
            TickDue: tickStarted + DurationTicks,
            AssignedPersonas: new[] { assignedPersonaId });
    }

    /// <summary>
    /// Is <paramref name="personaId"/> currently bound to any active
    /// mission ? The drag-drop handler in
    /// <c>M1Slice.OnPersonaInputEvent</c> calls this at drag-start ;
    /// when it returns true the drag is aborted (no proxy spawn,
    /// log line emitted, no error toast — the spec is "pas d'erreur
    /// visuelle agressive").
    ///
    /// <para>
    /// <b>O(active_missions × assigned_personas).</b> M2 caps that
    /// at <c>active_missions × 1</c> because every mission has
    /// exactly one persona. Even at a wild M3+ rate of 10 active
    /// missions × 5 personas it stays trivial — no need for an
    /// index data structure.
    /// </para>
    ///
    /// <para>
    /// <b>Case-sensitive string match.</b> Same convention as
    /// <see cref="M1SliceLogic.IsPersonaInEligibleList"/> ; the
    /// xUnit pin catches a future regression that lower-cases one
    /// side of the comparison.
    /// </para>
    /// </summary>
    public static bool IsPersonaBusy(
        string personaId,
        IReadOnlyList<ActiveMissionDto> activeMissions)
    {
        if (string.IsNullOrEmpty(personaId)) return false;
        if (activeMissions is null) return false;

        for (var i = 0; i < activeMissions.Count; i++)
        {
            var mission = activeMissions[i];
            for (var j = 0; j < mission.AssignedPersonas.Count; j++)
            {
                if (mission.AssignedPersonas[j] == personaId) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Compute the countdown suffix to append to a persona's
    /// Label3D (Didier-locked format <c>[mission 3/5]</c>). Returns
    /// <c>null</c> when the persona is not in any active mission —
    /// the caller then falls back to the legacy-tag suffix from
    /// <see cref="M1SliceLogic.ComputeLegacyLabelSuffix"/>.
    ///
    /// <para>
    /// <b>Format pinned by xUnit :</b>
    /// <code>
    ///   not in active mission        -> null            (caller falls back to legacy suffix)
    ///   tick_due - currentTick = 5   -> " [mission 5/5]" (just started)
    ///   tick_due - currentTick = 3   -> " [mission 3/5]" (mid-flight)
    ///   tick_due - currentTick = 1   -> " [mission 1/5]" (last tick before conclude)
    ///   tick_due - currentTick = 0   -> " [mission 0/5]" (this tick concludes — see note)
    ///   tick_due - currentTick &lt; 0   -> " [mission 0/5]" (overdue, clamped)
    /// </code>
    /// The leading space is part of the suffix so the runtime can
    /// concatenate it after the canonical <c>"{Name} -- {Status}"</c>
    /// without a separator helper. The `/5` denominator is hard-coded
    /// from <see cref="DurationTicks"/> so a future bump to 7 ticks
    /// (M3) requires updating one constant, not the format string.
    /// </para>
    ///
    /// <para>
    /// <b>0/5 is a transient state.</b> When <c>currentTick == TickDue</c>
    /// the runtime should call <see cref="IsMissionDue"/>, fire the
    /// conclude pipeline, and (on success) remove the mission from
    /// <c>GameState.ActiveMissions</c>. The 0/5 frame is therefore
    /// only visible during the conclude round-trip ; if the response
    /// lands within one tick (5 s budget) the user does not see it.
    /// If the conclude fails (network error), the mission stays at
    /// 0/5 until the next tick scan retries — the suffix surfaces
    /// the stuck state diegetically.
    /// </para>
    ///
    /// <para>
    /// <b>Negative-remaining clamp.</b> If a tick is dropped (the
    /// <see cref="Wayfinders.Client.Services.WorldSimTick"/>
    /// backpressure cap kicks in) the cadence may briefly skip
    /// ahead so <c>currentTick &gt; TickDue</c>. We clamp to 0/5
    /// rather than render <c>[mission -1/5]</c> — the spec is
    /// "count-down to zero, then conclude", never go negative.
    /// </para>
    /// </summary>
    public static string? ComputeMissionCountdownSuffix(
        string personaId,
        IReadOnlyList<ActiveMissionDto> activeMissions,
        int currentTick)
    {
        if (string.IsNullOrEmpty(personaId)) return null;
        if (activeMissions is null) return null;

        for (var i = 0; i < activeMissions.Count; i++)
        {
            var mission = activeMissions[i];
            for (var j = 0; j < mission.AssignedPersonas.Count; j++)
            {
                if (mission.AssignedPersonas[j] != personaId) continue;

                var remaining = mission.TickDue - currentTick;
                if (remaining < 0) remaining = 0;
                return $" [mission {remaining}/{DurationTicks}]";
            }
        }
        return null;
    }

    /// <summary>
    /// Is this active mission due to conclude on this tick ?
    /// Pinned numerically so the comparison operator cannot drift
    /// to <c>&gt;</c> (mission concludes one tick early — silent
    /// data corruption) or <c>&lt;</c> (mission never concludes —
    /// stuck UI). The contract is "current tick has reached or
    /// passed tick_due" : <c>currentTick &gt;= mission.TickDue</c>.
    ///
    /// <para>
    /// <b>Why &gt;= and not ==.</b> Defensive against the dropped-
    /// tick scenario : if the runtime drops a tick and bumps the
    /// cadence by 2, a mission could go from "1 tick remaining" to
    /// "tick_due exceeded by 1" without ever hitting
    /// <c>currentTick == TickDue</c> exactly. <c>&gt;=</c> catches
    /// the overdue case too. The runtime's idempotent-conclude
    /// pipeline (one mission, one conclude call, popped at success)
    /// makes "did we already conclude" a separate concern handled
    /// by removing the mission from
    /// <c>GameState.ActiveMissions</c> at success — not by gating
    /// the due check.
    /// </para>
    /// </summary>
    public static bool IsMissionDue(ActiveMissionDto mission, int currentTick)
    {
        if (mission is null) throw new System.ArgumentNullException(nameof(mission));
        return currentTick >= mission.TickDue;
    }

    /// <summary>
    /// Project the canonical <see cref="CharacterStateDto"/> down to
    /// the thin <see cref="CharacterStateWireDto"/> the conclude
    /// endpoint expects on the wire. Pre-condition : the persona
    /// snapshot must be in
    /// <see cref="Wayfinders.Client.Services.GameState.CompanyPersonas"/>
    /// at request-build time. Throws if the projection cannot
    /// produce a non-empty persona id (defensive — would 422 on the
    /// server otherwise).
    ///
    /// <para>
    /// <b>Why a separate projection helper.</b> Two reasons. (1) The
    /// canonical persona id field is
    /// <see cref="CharacterStateDto.Name"/> on the M1 wire ; the
    /// conclude wire calls it <c>persona_id</c>. The mapping is
    /// boring but pinable so a future split (e.g. distinct
    /// <c>persona_id</c> column on the canonical DTO) does not
    /// drift silently. (2) The full
    /// <see cref="CharacterStateDto"/> carries 15 fields ; the wire
    /// needs 3. Pinning the projection means the conclude endpoint
    /// cannot accidentally start depending on
    /// <see cref="CharacterStateDto.Traits"/> via a leak through
    /// reflection or a future "let's just send the full state" lazy
    /// refactor.
    /// </para>
    /// </summary>
    public static CharacterStateWireDto ProjectToWireDto(CharacterStateDto persona)
    {
        if (persona is null)
            throw new System.ArgumentNullException(nameof(persona));
        if (string.IsNullOrEmpty(persona.Name))
            throw new System.ArgumentException(
                "persona.Name (= persona_id) must be non-empty",
                nameof(persona));
        if (string.IsNullOrEmpty(persona.DexBucket))
            throw new System.ArgumentException(
                "persona.DexBucket must be non-empty (closed lookup, see DescriptorBucketValues)",
                nameof(persona));
        if (string.IsNullOrEmpty(persona.WisBucket))
            throw new System.ArgumentException(
                "persona.WisBucket must be non-empty (closed lookup, see DescriptorBucketValues)",
                nameof(persona));

        return new CharacterStateWireDto(
            PersonaId: persona.Name,
            DexBucket: persona.DexBucket,
            WisBucket: persona.WisBucket);
    }

    /// <summary>
    /// Build the <see cref="MissionConcludeRequestDto"/> for a due
    /// active mission. Resolves each assigned persona id against
    /// the company snapshot and projects through
    /// <see cref="ProjectToWireDto"/>. Throws if any assigned
    /// persona is missing from the company — that is a server-bound
    /// contract violation and we want to fail loud at the source
    /// rather than send a malformed request.
    ///
    /// <para>
    /// <b>Why <c>IReadOnlyDictionary&lt;string, CharacterStateDto&gt;</c>
    /// rather than a list.</b> Calls into
    /// <see cref="Wayfinders.Client.Services.GameState.CompanyPersonas"/>
    /// at the runtime call site need O(1) lookups by id — three
    /// active missions × one persona each × dictionary lookup is
    /// cheap, but a list scan is O(n×m) and grows. The xUnit pin
    /// uses a plain <c>Dictionary&lt;string, CharacterStateDto&gt;</c>
    /// indexed by <see cref="CharacterStateDto.Name"/> ; the runtime
    /// call site builds it from
    /// <c>GameState.CompanyPersonas.ToDictionary(p =&gt; p.Name)</c>
    /// once per tick scan.
    /// </para>
    /// </summary>
    public static MissionConcludeRequestDto BuildConcludeRequest(
        ActiveMissionDto active,
        IReadOnlyDictionary<string, CharacterStateDto> companyById)
    {
        if (active is null)
            throw new System.ArgumentNullException(nameof(active));
        if (companyById is null)
            throw new System.ArgumentNullException(nameof(companyById));
        if (active.AssignedPersonas.Count == 0)
            throw new System.ArgumentException(
                "active.AssignedPersonas must be non-empty (server min_length=1)",
                nameof(active));

        var wires = new List<CharacterStateWireDto>(capacity: active.AssignedPersonas.Count);
        for (var i = 0; i < active.AssignedPersonas.Count; i++)
        {
            var personaId = active.AssignedPersonas[i];
            if (!companyById.TryGetValue(personaId, out var persona))
            {
                throw new System.InvalidOperationException(
                    $"persona id '{personaId}' is in active mission '{active.Mission.Id}' "
                    + "but missing from company snapshot — refusing to send a malformed conclude request");
            }
            wires.Add(ProjectToWireDto(persona));
        }

        return new MissionConcludeRequestDto(
            MissionId: active.Mission.Id,
            MissionType: active.Mission.Type,
            Region: active.Mission.Region,
            // M1 templates always render with a null actor_target.
            // M3+ parley_local-with-NPC missions will fill this from
            // EmergentMissionDto when Varn extends the schema. M2
            // unchanged.
            ActorTarget: null,
            Difficulty: active.Mission.Difficulty,
            AssignedPersonas: wires,
            Seed: active.Mission.Seed,
            TickDue: active.TickDue);
    }
}

/// <summary>
/// Client-only state for an in-flight mission counting down to
/// conclusion. Created at drop-accept time, removed at conclude-
/// success time. M2-advance lock 2026-05-10 (Didier).
///
/// <para>
/// <b>Why a sealed record, not a struct.</b> Reference semantics
/// match the <see cref="EmergentMissionDto"/> embedded inside it ;
/// passing an active mission around the runtime should not copy
/// the underlying mission data. <c>sealed</c> closes inheritance
/// so a future refactor cannot subclass an
/// <c>ActiveMissionWithExtra</c> behind everyone's back. The
/// equality / deconstruction features of records are useful in
/// xUnit (Assert.Equal on the whole shape).
/// </para>
///
/// <para>
/// <b>Why no <c>Outcome</c> or <c>TagsCreated</c> field.</b> Those
/// are server-authority and arrive in
/// <see cref="MissionConcludeResponseDto"/> at conclude time. An
/// in-flight mission has no outcome yet ; embedding a nullable
/// outcome here would invite "let's just write to it locally"
/// shortcuts that violate the server-authority contract for the
/// roll. The active mission's only job is to count down ; the
/// resolution is read-back-only via the conclude response.
/// </para>
///
/// <para>
/// <b>Why no Pydantic mirror server-side.</b> See
/// <see cref="MissionDurationLogic"/> class docstring : the server
/// stays stateless. <c>ActiveMissionDto</c> is a pure client
/// construct ; it never crosses the wire.
/// </para>
/// </summary>
/// <param name="Mission">The originally-emerged mission.
/// Captured by reference so the conclude pipeline can re-derive
/// difficulty / region / seed without an extra lookup.</param>
/// <param name="TickStarted">Tick at which the player accepted
/// the mission. Useful for the countdown computation and for
/// future analytics ("how long did the player wait before
/// accepting").</param>
/// <param name="TickDue">Tick at which the mission concludes.
/// Always <c>TickStarted + MissionDurationLogic.DurationTicks</c>
/// at construction time. Used as the timeline anchor in the
/// SHA-256-derived roll seed (server-side).</param>
/// <param name="AssignedPersonas">M2 strict 1-persona ; structure
/// list-shaped for M3 multi-persona.</param>
public sealed record ActiveMissionDto(
    EmergentMissionDto Mission,
    int TickStarted,
    int TickDue,
    IReadOnlyList<string> AssignedPersonas
);
