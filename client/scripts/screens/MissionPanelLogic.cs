using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for the M1 Mission panel. Owns the
/// "given a pending mission and a current persona selection, what
/// should the Affect / Décliner buttons do" contract — pinned by
/// xUnit so the engine-bound probe stays a thin transport layer.
///
/// <para>
/// <b>Why a logic seam, not a behemoth probe.</b> Same rationale as
/// <see cref="CompagniePanelLogic"/> : the runtime probe wires
/// Buttons + Labels + the async <see cref="Wayfinders.Client.Services.ApiClient"/>
/// call ; everything Godot-bound is engine-only and not xUnit-testable
/// without a Godot runtime. The contract — "Affect is enabled iff a
/// persona is selected", "Décliner posts assigned_personas=[]",
/// "after resolve we apply tags and pop the mission off the queue" —
/// lives here, free of <c>using Godot;</c>.
/// </para>
///
/// <para>
/// <b>M1 scope.</b> Text-only, single mission at a time (the head of
/// <see cref="Wayfinders.Client.Services.GameState.PendingMissions"/>),
/// outcome stubbed to
/// <see cref="WorldTickWireFormat.ResolutionOutcome.Success"/>. No
/// drag-drop yet — Affect is a button, persona pick is a button-list.
/// Drag-drop semantics from
/// <see cref="CompagniePanelLogic.AssignToMissionEventName"/> apply
/// when the UI is upgraded ; for M1 we prove the chain runs.
/// </para>
///
/// <para>
/// <b>Why "head of queue" and not pick-by-id.</b> M1 simplicity. Once
/// 2+ pending missions exist, a list-of-cards UI replaces this. The
/// logic surface won't change much — the helper just gets a parameter
/// for which mission was clicked.
/// </para>
/// </summary>
public static class MissionPanelLogic
{
    /// <summary>
    /// Decide what the Affect button can do given the current
    /// selection state. The runtime probe binds this to the button's
    /// <c>Disabled</c> property.
    /// </summary>
    public static AffectButtonState DecideAffectButtonState(
        EmergentMissionDto? mission,
        string? selectedPersonaId)
    {
        if (mission is null)
            return AffectButtonState.Hidden;

        // No persona selected : button visible but disabled. The
        // player should still see the affordance ("there's an
        // Affect, but you have to pick someone first").
        if (string.IsNullOrEmpty(selectedPersonaId))
            return AffectButtonState.VisibleDisabled;

        // Selected persona must be in the eligible list (server-filtered).
        // If it's NOT, that means the UI got out of sync (race against
        // a tick that mutated the mission, or a user clicked a stale
        // button). Defensive : treat as disabled.
        var found = false;
        for (var i = 0; i < mission.EligiblePersonas.Count; i++)
        {
            if (mission.EligiblePersonas[i] == selectedPersonaId)
            {
                found = true;
                break;
            }
        }
        return found ? AffectButtonState.Enabled : AffectButtonState.VisibleDisabled;
    }

    /// <summary>
    /// Decide what the Décliner button can do.
    ///
    /// <para>
    /// <b>Always enabled when a mission is present</b> — declining is
    /// the player's escape hatch (Varn option (c) : "no eligible
    /// persona → still emerges, player declines"). Hidden when no
    /// mission is showing.
    /// </para>
    /// </summary>
    public static DeclineButtonState DecideDeclineButtonState(
        EmergentMissionDto? mission)
    {
        return mission is null ? DeclineButtonState.Hidden : DeclineButtonState.Enabled;
    }

    /// <summary>
    /// Build the resolve-mission payload for the Affect path.
    /// Pre-condition : caller has checked
    /// <see cref="DecideAffectButtonState"/> returned
    /// <see cref="AffectButtonState.Enabled"/>.
    /// </summary>
    public static MissionResolveRequestDto BuildAffectRequest(
        EmergentMissionDto mission,
        string assignedPersonaId,
        int currentTick)
    {
        if (string.IsNullOrEmpty(assignedPersonaId))
            throw new System.ArgumentException("assignedPersonaId required for Affect", nameof(assignedPersonaId));

        return new MissionResolveRequestDto(
            MissionId: mission.Id,
            MissionType: mission.Type,
            Region: mission.Region,
            ActorTarget: null, // M1 templates have no named target
            Outcome: WorldTickWireFormat.ResolutionOutcome.Success, // M1 stub
            Tick: currentTick,
            AssignedPersonas: new[] { assignedPersonaId });
    }

    /// <summary>
    /// Build the resolve-mission payload for the Décliner path.
    /// AssignedPersonas is empty — the server returns
    /// <c>tags_created=[]</c> and the mission gets dropped from the
    /// queue.
    /// </summary>
    public static MissionResolveRequestDto BuildDeclineRequest(
        EmergentMissionDto mission,
        int currentTick)
    {
        return new MissionResolveRequestDto(
            MissionId: mission.Id,
            MissionType: mission.Type,
            Region: mission.Region,
            ActorTarget: null,
            // The server still expects a closed-lookup outcome ; we
            // record "failure" for the declined path so the legacy
            // tag (if any) reflects the player's choice. Empty
            // AssignedPersonas means tags_created=[] regardless.
            Outcome: WorldTickWireFormat.ResolutionOutcome.Failure,
            Tick: currentTick,
            AssignedPersonas: System.Array.Empty<string>());
    }

    /// <summary>State of the Affect button — drives the runtime
    /// probe's button visibility / disabled flag.</summary>
    public enum AffectButtonState
    {
        Hidden,
        VisibleDisabled,
        Enabled,
    }

    /// <summary>State of the Décliner button.</summary>
    public enum DeclineButtonState
    {
        Hidden,
        Enabled,
    }

    // ====================================================================
    // Status panel rendering helpers (text-only M1)
    // ====================================================================

    /// <summary>
    /// Format the mission header text for the M1 minimal panel.
    /// Pure function so xUnit pins the format and a future locale
    /// change rebuilds the cases here.
    /// </summary>
    public static string FormatMissionHeader(EmergentMissionDto mission)
    {
        return $"[MISSION {mission.Type}] {mission.Region} (difficulty: {mission.Difficulty})";
    }

    /// <summary>
    /// Format the eligible-personas line. Empty case is the Varn
    /// option (c) "aucun perso éligible — décliner ?" affordance.
    /// </summary>
    public static string FormatEligiblePersonas(EmergentMissionDto mission)
    {
        if (mission.EligiblePersonas.Count == 0)
            return "Aucun perso éligible — décliner ?";
        return $"Eligible: {string.Join(", ", mission.EligiblePersonas)}";
    }

    /// <summary>
    /// Format the legacy-tag toast text on resolution. Plural form
    /// keeps the line readable for 0 / 1 / 2+ tags.
    /// </summary>
    public static string FormatLegacyTagToast(IReadOnlyList<PersonaLegacyTagDto> tags)
    {
        if (tags.Count == 0) return "[LEGACY TAG] (none)";
        if (tags.Count == 1)
            return $"[LEGACY TAG] {tags[0].PersonaId} earned tag from {tags[0].MissionType} in {tags[0].Region}";
        return $"[LEGACY TAG] {tags.Count} tags earned ({string.Join(", ", PersonaIds(tags))})";
    }

    private static IEnumerable<string> PersonaIds(IReadOnlyList<PersonaLegacyTagDto> tags)
    {
        for (var i = 0; i < tags.Count; i++) yield return tags[i].PersonaId;
    }
}
