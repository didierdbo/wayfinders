using System.Text.Json.Serialization;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for the §A.8.D5 mission action endpoints
/// (Varn-lock 2026-05-17 strict mode + Tess backend commit 5131d5f).
///
/// <para>
/// <b>What this file is.</b> Four POST endpoints on the FastAPI side :
/// <list type="bullet">
///   <item><c>POST /api/missions/&lt;id&gt;/accept</c> — Move a pending
///         mission into the accepted state. Idempotent on retry
///         (200 + no state change). 404 if mission not found.</item>
///   <item><c>POST /api/missions/&lt;id&gt;/decline</c> — Drop a
///         pending mission. 404 if not found, 409 if already
///         accepted.</item>
///   <item><c>POST /api/missions/&lt;id&gt;/conclude</c> — Conclude an
///         accepted mission with an <see cref="MissionConcludeOutcome"/>.
///         404 if not found, 409 if not in accepted state.</item>
///   <item><c>POST /api/missions/&lt;id&gt;/resolve</c> — Conclude an
///         accepted mission with a typed
///         <see cref="ResolutionOutcomeType"/>. Separate from
///         <c>/conclude</c> so the recruit panel resolution-outcome
///         contract stays typed.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a separate DTO file.</b> The legacy
/// <see cref="MissionResolveRequestDto"/> +
/// <see cref="MissionConcludeRequestDto"/> are the M1 / M2-advance
/// outcome wire format (full company snapshot, server-rolled outcome,
/// tags response). The §A.8 mission-action endpoints are the
/// state-transition wire format — minimal bodies, simple status
/// responses. Two different concerns ; two different DTO surfaces.
/// </para>
///
/// <para>
/// <b>Empty-body POST.</b> The accept / decline endpoints take an
/// empty Pydantic <c>MissionActionRequest</c> on the server.
/// <see cref="MissionActionEmptyRequestDto"/> mirrors that.
/// <c>System.Text.Json</c> serialises an empty record to <c>{}</c>
/// which Pydantic's <c>extra="forbid"</c> validates as an empty body.
/// </para>
///
/// <para>
/// <b>Closed lookups.</b> Two slot vocabularies are mirrored on the
/// client side : <see cref="MissionConcludeOutcome"/> (success /
/// failure / abandoned) and <see cref="MissionResolutionOutcomeType"/>
/// (same set for M1, kept separate so the typed contract can diverge
/// at M2+). Pinned as <c>const string</c> per the same rationale as
/// <see cref="WorldTickWireFormat"/>.
/// </para>
/// </summary>
public static class MissionActionWireFormat
{
    /// <summary>Conclude outcome (Varn-locked §A.8.D5).
    /// Server-side <c>Literal["success", "failure", "abandoned"]</c>.</summary>
    public static class MissionConcludeOutcome
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Abandoned = "abandoned";
    }

    /// <summary>Resolution-outcome type (Varn-locked §A.8.D5).
    /// Same string set as <see cref="MissionConcludeOutcome"/> for
    /// M1 ; the typed contract is kept separate so the recruit panel
    /// can use a richer vocabulary at M2+ (e.g. recruit-specific
    /// outcomes like <c>"npc_joined"</c>) without touching the
    /// outcome wire.</summary>
    public static class MissionResolutionOutcomeType
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Abandoned = "abandoned";
    }
}

/// <summary>
/// Empty POST body for the accept / decline endpoints. Pydantic
/// <c>MissionActionRequest(extra="forbid")</c> on the server accepts
/// <c>{}</c> only.
/// </summary>
public sealed record MissionActionEmptyRequestDto();

/// <summary>
/// Response from <c>POST /api/missions/&lt;id&gt;/accept</c>.
/// </summary>
/// <param name="MissionId">Echo of the mission id.</param>
/// <param name="Status">Always <c>"accepted"</c> on a 200 response.</param>
public sealed record MissionAcceptResponseDto(
    string MissionId,
    string Status
);

/// <summary>
/// Response from <c>POST /api/missions/&lt;id&gt;/decline</c>.
/// </summary>
/// <param name="MissionId">Echo of the mission id.</param>
/// <param name="Status">Always <c>"declined"</c> on a 200 response.</param>
public sealed record MissionDeclineResponseDto(
    string MissionId,
    string Status
);

/// <summary>
/// Request body for <c>POST /api/missions/&lt;id&gt;/conclude</c>.
/// </summary>
/// <param name="Outcome">One of
/// <see cref="MissionActionWireFormat.MissionConcludeOutcome"/>.</param>
public sealed record MissionConcludeActionRequestDto(
    string Outcome
);

/// <summary>
/// Response from <c>POST /api/missions/&lt;id&gt;/conclude</c>.
/// </summary>
/// <param name="MissionId">Echo of the mission id.</param>
/// <param name="Status">Always <c>"concluded"</c> on a 200 response.</param>
/// <param name="Outcome">Echo of the request outcome.</param>
public sealed record MissionConcludeActionResponseDto(
    string MissionId,
    string Status,
    string Outcome
);

/// <summary>
/// Request body for <c>POST /api/missions/&lt;id&gt;/resolve</c>.
/// </summary>
/// <param name="ResolutionOutcomeType">One of
/// <see cref="MissionActionWireFormat.MissionResolutionOutcomeType"/>.
/// Note the snake_case wire mapping
/// (<c>resolution_outcome_type</c>) is handled by the source-gen
/// naming policy.</param>
public sealed record MissionResolveActionRequestDto(
    string ResolutionOutcomeType
);

/// <summary>
/// Response from <c>POST /api/missions/&lt;id&gt;/resolve</c>.
/// </summary>
/// <param name="MissionId">Echo of the mission id.</param>
/// <param name="Status">Always <c>"resolved"</c> on a 200 response.</param>
/// <param name="ResolutionOutcomeType">Echo of the request value.</param>
public sealed record MissionResolveActionResponseDto(
    string MissionId,
    string Status,
    string ResolutionOutcomeType
);
