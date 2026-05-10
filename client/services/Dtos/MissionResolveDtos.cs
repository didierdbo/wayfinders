using System.Collections.Generic;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for the <c>POST /api/world/mission/resolve</c>
/// endpoint (M1 mission-emergence resolution). Mirrors Pydantic models
/// in <c>wayfinders/api/mission_resolve.py</c>.
///
/// <para>
/// <b>What this endpoint does.</b> When the player has assigned
/// personas to a <see cref="EmergentMissionDto"/> and the gameplay
/// layer decides the outcome (M1 stub : always
/// <see cref="WorldTickWireFormat.ResolutionOutcome.Success"/>), the
/// client posts a <see cref="MissionResolveRequestDto"/> with the
/// mission fields + outcome + assigned personas. The server replays
/// the <c>on_mission_resolved</c> hook against a transient (discarded)
/// GameState and returns the <see cref="PersonaLegacyTagDto"/> objects
/// produced. The client stores them in
/// <c>GameState.PersonaLegacy</c> (Godot autoload).
/// </para>
///
/// <para>
/// <b>Stateless server.</b> No <c>GameState</c> persists on the Python
/// side (NPC-autonomy lock 2026-05-09). The client is the source of
/// truth ; the server is an idempotent function. Same
/// <c>(mission_id, outcome, tick, assigned_personas)</c> always
/// produces the same tags. The client deduplicates across retries
/// using <c>(persona_id, mission_id)</c> as the natural key.
/// </para>
///
/// <para>
/// <b>Edge case — declined missions.</b> When the player declines a
/// mission (or no persona is eligible — Varn option (c)), the client
/// posts <see cref="MissionResolveRequestDto.AssignedPersonas"/> as
/// <c>[]</c>. The server returns <c>tags_created=[]</c>. M1 contract.
/// </para>
/// </summary>
public static class MissionResolveDtoNotes
{
    // Anchor type so the file-level docstring above attaches in
    // tooling. Intentionally empty.
}

/// <summary>
/// Input for <c>POST /api/world/mission/resolve</c>. Mirrors Pydantic
/// <c>MissionResolveRequest</c>.
///
/// <para>
/// <b>Idempotence contract.</b> Reposting the same request must
/// produce the same tags. The client side relies on this when
/// retrying after a transient
/// <see cref="ApiError.NotReachable"/> (TODO M2 : retry policy ; M1
/// surfaces the error to the UI). The server's hook is itself
/// idempotent within a single request — no duplicate tags for the
/// same <c>(persona_id, mission_id)</c>.
/// </para>
/// </summary>
/// <param name="MissionId">Stable UUID4-shaped id from the
/// <see cref="EmergentMissionDto"/> at emergence. Required and
/// non-empty.</param>
/// <param name="MissionType">Closed lookup, see
/// <see cref="WorldTickWireFormat.MissionType"/>.</param>
/// <param name="Region">Region id (free-form M1). Required and
/// non-empty.</param>
/// <param name="ActorTarget">Optional named NPC target for parley
/// missions. <c>null</c> for scout missions and for parley missions
/// without a named target. M1 templates : always <c>null</c>.</param>
/// <param name="Outcome">Closed lookup, see
/// <see cref="WorldTickWireFormat.ResolutionOutcome"/>. M1 stub :
/// always <c>"success"</c>.</param>
/// <param name="Tick">Tick at resolution time, non-negative.</param>
/// <param name="AssignedPersonas">Persona ids that participated in
/// the mission. Empty when the mission was declined or no persona
/// was eligible — server returns
/// <see cref="MissionResolveResponseDto.TagsCreated"/> = empty.</param>
public sealed record MissionResolveRequestDto(
    string MissionId,
    string MissionType,
    string Region,
    string? ActorTarget,
    string Outcome,
    int Tick,
    IReadOnlyList<string> AssignedPersonas
);

/// <summary>
/// Wire mirror of the server-side <c>PersonaLegacyTag</c> dataclass.
/// Mirrors Pydantic <c>PersonaLegacyTagWire</c> on the response.
///
/// <para>
/// <b>Storage contract.</b> The client stores these in
/// <c>GameState.PersonaLegacy : Dictionary&lt;string,
/// List&lt;PersonaLegacyTagDto&gt;&gt;</c> keyed by
/// <see cref="PersonaId"/>. Lifetime : session (saves are reported
/// out of M1 scope ; reload starts the dictionary empty). The natural
/// dedup key across retries is <c>(persona_id, mission_id)</c> — the
/// client should check before appending. M1 simple impl : append
/// blindly and dedup at read time if it ever becomes a problem.
/// </para>
///
/// <para>
/// <b>Architecture invariant.</b> This is data, NOT a Node3D.
/// <c>project_wayfinders_npc_autonomy.md</c> : "PersonaLegacyTag est
/// de la donnée GameState, pas de re-parenting Node3D". The
/// persona's visual on the Compagnie panel may flip a status
/// indicator based on the new tag (e.g. "veteran of Halfgate"), but
/// the persona Node3D stays where it is.
/// </para>
/// </summary>
/// <param name="PersonaId">Persona who earned the tag.</param>
/// <param name="MissionId">Mission that produced the tag.</param>
/// <param name="MissionType">Closed lookup, see
/// <see cref="WorldTickWireFormat.MissionType"/>.</param>
/// <param name="Region">Region of the mission.</param>
/// <param name="ActorTarget">Optional named NPC target.</param>
/// <param name="Outcome">Closed lookup, see
/// <see cref="WorldTickWireFormat.ResolutionOutcome"/>.</param>
/// <param name="EarnedAtTick">Tick at which the tag was earned.</param>
public sealed record PersonaLegacyTagDto(
    string PersonaId,
    string MissionId,
    string MissionType,
    string Region,
    string? ActorTarget,
    string Outcome,
    int EarnedAtTick
);

/// <summary>
/// Response from <c>POST /api/world/mission/resolve</c>. Mirrors
/// Pydantic <c>MissionResolveResponse</c>.
/// </summary>
/// <param name="TagsCreated">Tags produced by the resolution.
/// Possibly empty (declined mission, no personas, or no tags).</param>
public sealed record MissionResolveResponseDto(
    IReadOnlyList<PersonaLegacyTagDto> TagsCreated
);
