using System.Collections.Generic;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for the <c>POST /api/world/mission/conclude</c>
/// endpoint (M2-advance step 2 — server-authority weighted outcome
/// roll). Mirrors Pydantic models in
/// <c>wayfinders/api/mission_conclude.py</c> (Tess, commit
/// d533373, 2026-05-10).
///
/// <para>
/// <b>What this endpoint does, vs <c>/resolve</c>.</b> Where
/// <see cref="MissionResolveRequestDto"/> ships a client-decided
/// <see cref="MissionResolveRequestDto.Outcome"/>, this endpoint
/// computes the outcome server-side from the assigned-persona
/// stat-buckets, the mission difficulty, and a deterministic
/// SHA-256-derived seed (<c>seed:tick_due:persona_id</c>). The
/// client posts the persona snapshots and trusts the server roll.
/// One round-trip : outcome + tags + roll breakdown. <c>/resolve</c>
/// stays available for backward-compat with M1 tests but is
/// deprecated in favour of <c>/conclude</c>.
/// </para>
///
/// <para>
/// <b>What is not mirrored.</b> The Pydantic
/// <c>CharacterStateWireDto</c> server-side carries only
/// <c>(persona_id, dex_bucket, wis_bucket)</c> — three fields, not
/// the full <see cref="CharacterStateDto"/>. Tess explicitly trimmed
/// the wire because <c>/conclude</c> never invokes a renderer ; only
/// the stat lanes are needed for the roll. We mirror that thin DTO
/// here as <see cref="CharacterStateWireDto"/>. The full
/// <see cref="CharacterStateDto"/> is for <c>/api/world/tick</c>
/// (renderer needs prose context), the wire DTO is for
/// <c>/conclude</c> (only the buckets).
/// </para>
///
/// <para>
/// <b>Stateless server contract.</b> Same as
/// <c>/api/world/mission/resolve</c> — no <c>GameState</c> persists
/// on the Python side ; tags are returned, the client stores them in
/// <see cref="GameState.PersonaLegacy"/>. Reposting the same
/// <c>(seed, tick_due, persona_id, mission_id)</c> tuple yields the
/// same outcome and same tags ; the roll is deterministic. The
/// client may safely retry on a transient
/// <see cref="ApiError.NotReachable"/> (M2 simple impl : surface to
/// UI, no auto-retry policy yet).
/// </para>
///
/// <para>
/// <b>Hash convention pin (cross-platform).</b> Server uses
/// <c>SHA-256(f"{seed}:{tick_due}:{persona_id}")</c> with the first
/// 8 bytes interpreted as a big-endian uint64 RNG seed. The client
/// does <i>not</i> re-roll in M2 (server is authoritative) but the
/// convention is documented in
/// <c>wayfinders/api/mission_conclude.py</c> module docstring so
/// Rune can write a parity test in C# later if a debug seam is
/// needed. <see cref="System.Security.Cryptography.SHA256.HashData"/>
/// + <see cref="System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian"/>
/// is the equivalent path.
/// </para>
/// </summary>
public static class MissionConcludeDtoNotes
{
    // Anchor type so the file-level docstring above attaches in
    // tooling. Intentionally empty.
}

/// <summary>
/// Thin character snapshot needed for the conclude weighted roll.
/// Mirrors Pydantic <c>CharacterStateWireDto</c> — only the persona
/// identity and the two stat lanes (DEX for scout_route, WIS for
/// parley_local) are sent on the wire ; the renderer is not invoked
/// by <c>/conclude</c> so the rest of <see cref="CharacterStateDto"/>
/// is dead weight.
///
/// <para>
/// <b>Why a separate DTO and not a slice of
/// <see cref="CharacterStateDto"/>.</b> Pydantic's
/// <c>extra="forbid"</c> on the server means we cannot post the
/// full character state to <c>/conclude</c> ; the server would 422
/// on every extra field. A typed thin DTO is the correct seam.
/// Build helpers (M2-advance step 2) project from
/// <see cref="CharacterStateDto"/> down to this shape at request
/// build time.
/// </para>
///
/// <para>
/// <b>Field naming.</b> Server-side <c>persona_id</c> matches the
/// <see cref="CharacterStateDto.Name"/> field on the larger DTO —
/// the M1 wire convention is "persona id is the character's name"
/// (lowercased Varn-aligned id). The
/// <c>SnakeCaseLower</c> policy on <see cref="ApiJsonContext"/>
/// maps <see cref="PersonaId"/> → <c>persona_id</c> in JSON.
/// </para>
/// </summary>
/// <param name="PersonaId">Stable persona identifier ; matches
/// <see cref="CharacterStateDto.Name"/>. Required and non-empty
/// (Pydantic <c>min_length=1</c>).</param>
/// <param name="DexBucket">DEX descriptor bucket. Closed lookup,
/// see <see cref="DescriptorBucketValues"/>. Drives the
/// <c>scout_route</c> stat lane.</param>
/// <param name="WisBucket">WIS descriptor bucket. Closed lookup,
/// see <see cref="DescriptorBucketValues"/>. Drives the
/// <c>parley_local</c> stat lane.</param>
public sealed record CharacterStateWireDto(
    string PersonaId,
    string DexBucket,
    string WisBucket
);

/// <summary>
/// Input for <c>POST /api/world/mission/conclude</c>. Mirrors Pydantic
/// <c>MissionConcludeRequest</c> in
/// <c>wayfinders/api/mission_conclude.py</c>.
///
/// <para>
/// <b>M2 strict 1-persona constraint.</b> Server-side Pydantic enforces
/// <c>min_length=1</c> on <see cref="AssignedPersonas"/> ; the M2
/// commit-binding rule (mission ⇒ exactly one persona) is enforced at
/// the call site, not in the DTO shape. The structure is a list (not
/// a single object) so M3 can extend to multi-persona without a
/// breaking schema change.
/// </para>
///
/// <para>
/// <b>Decline path is NOT routed through this endpoint.</b> Decline
/// keeps using <c>/api/world/mission/resolve</c> with empty
/// <see cref="MissionResolveRequestDto.AssignedPersonas"/> + outcome
/// = <c>"failure"</c> — the conclude endpoint requires at least one
/// persona because there is no roll to compute on a declined mission.
/// </para>
///
/// <para>
/// <b>Field semantics.</b>
/// <list type="bullet">
///   <item><see cref="MissionId"/> : stable UUID-shaped id from
///         <see cref="EmergentMissionDto.Id"/>.</item>
///   <item><see cref="MissionType"/> : Varn-locked closed lookup —
///         <see cref="WorldTickWireFormat.MissionType"/>.</item>
///   <item><see cref="Region"/> : free-form region id.</item>
///   <item><see cref="ActorTarget"/> : optional named NPC for
///         parley missions ; null for scout.</item>
///   <item><see cref="Difficulty"/> : Varn-locked closed lookup —
///         <see cref="WorldTickWireFormat.DifficultyBucket"/>.</item>
///   <item><see cref="AssignedPersonas"/> : exactly one persona in
///         M2.</item>
///   <item><see cref="Seed"/> : the
///         <see cref="EmergentMissionDto.Seed"/> from emergence —
///         passed through unchanged so the server's hash derivation
///         is reproducible.</item>
///   <item><see cref="TickDue"/> : tick at which the mission expires
///         (<c>tick_started + duration_ticks</c>). Combined with
///         <see cref="Seed"/> + persona id in the SHA-256 derivation
///         so two missions with the same spawn seed but different
///         deadlines yield different rolls.</item>
/// </list>
/// </para>
/// </summary>
public sealed record MissionConcludeRequestDto(
    string MissionId,
    string MissionType,
    string Region,
    string? ActorTarget,
    string Difficulty,
    IReadOnlyList<CharacterStateWireDto> AssignedPersonas,
    long Seed,
    int TickDue
);

/// <summary>
/// Per-persona roll debug breakdown. Mirrors Pydantic
/// <c>RollBreakdownEntry</c>. Included in the response for
/// transparency / debug logging — not used for any gameplay decision
/// (the authoritative outcome is on
/// <see cref="MissionConcludeResponseDto.Outcome"/>).
///
/// <para>
/// <b>Why on the wire.</b> When the player sees a "failure" on a
/// scout_route mid mission with a high-DEX scout, surfacing the
/// roll breakdown in the F12 debug overlay is the difference between
/// "this looks broken" and "the dice did this — see, delta=+2, you
/// got the unlucky 5%". Same rationale as Battle Brothers and XCOM 2
/// surfacing hit chances post-combat.
/// </para>
/// </summary>
/// <param name="PersonaId">Persona this entry describes.</param>
/// <param name="BucketUsed">Stat bucket selected for this persona
/// (<c>dex_bucket</c> for scout_route, <c>wis_bucket</c> for
/// parley_local).</param>
/// <param name="Delta">
/// <c>ordinal(persona_bucket) - ordinal(difficulty)</c>. Raw value,
/// not clamped. The server clamps to <c>[-2, +1]</c> for the table
/// lookup but reports the raw delta here for debugging.
/// </param>
/// <param name="RollValue">Raw <c>random()</c> draw in
/// <c>[0, 1)</c> used for the outcome determination.</param>
/// <param name="ProbabilityTable">3-element array
/// <c>[p_success, p_partial, p_failure]</c> applied for this
/// persona's clamped delta. Wire is a JSON array, not an object —
/// mirroring the Pydantic <c>tuple[float, float, float]</c>.</param>
public sealed record RollBreakdownEntryDto(
    string PersonaId,
    string BucketUsed,
    int Delta,
    double RollValue,
    double[] ProbabilityTable
);

/// <summary>
/// Response from <c>POST /api/world/mission/conclude</c>. Mirrors
/// Pydantic <c>MissionConcludeResponse</c>.
///
/// <para>
/// <b>Three payloads in one round-trip.</b>
/// <list type="bullet">
///   <item><see cref="Outcome"/> — Varn-locked closed lookup
///         (<see cref="WorldTickWireFormat.ResolutionOutcome"/>).
///         Server-authoritative ; the client trusts it without
///         re-rolling.</item>
///   <item><see cref="TagsCreated"/> — same shape as
///         <see cref="MissionResolveResponseDto.TagsCreated"/> ; the
///         client appends them into
///         <see cref="GameState.PersonaLegacy"/>.</item>
///   <item><see cref="RollBreakdown"/> — per-persona debug. Always
///         non-null but may be empty if the server stub-bumps it
///         (M2 always has 1 persona, so 1 entry).</item>
/// </list>
/// </para>
/// </summary>
public sealed record MissionConcludeResponseDto(
    string Outcome,
    IReadOnlyList<PersonaLegacyTagDto> TagsCreated,
    IReadOnlyList<RollBreakdownEntryDto> RollBreakdown
);
