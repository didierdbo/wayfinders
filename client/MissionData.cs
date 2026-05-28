using System.Text.Json.Serialization;

// Minimal DTO for an EmergentMission as returned by the FastAPI backend.
// Snake-case JSON keys are mapped explicitly so the rest of the C# code can stay PascalCase.
// Kept as a record because missions are immutable values once received from the backend;
// any mutation belongs in GameState, not on the DTO itself.
//
// RecruitTargetNpcId: optional. Present on recruit-type missions (M1 backend exposes
// "kira", "dorn", "vell" Varn-locked); absent on non-recruit missions and on responses
// from a backend version that predates the field. Default = null so System.Text.Json
// happily deserializes either shape, and downstream callers route via a single
// null-tolerant mapping (see CharacterIsoPawn.NpcIdToAtlasIndex).
public record MissionData(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("narrative_hook")] string NarrativeHook,
	[property: JsonPropertyName("target_poi")] string TargetPoi,
	[property: JsonPropertyName("region")] string Region,
	[property: JsonPropertyName("recruit_target_npc_id")] string? RecruitTargetNpcId = null
);
