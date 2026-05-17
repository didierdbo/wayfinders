using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// R8 of the recruit cascade (Varn §A.8 strict mode, 2026-05-17) —
/// xUnit pins for the pure-C# seams the Godot-bound autoloads delegate
/// to. The Godot autoloads themselves (<c>MissionStore</c>,
/// <c>GameState</c>, <c>WorldSimTick</c>) are not exercised here
/// (would require a Godot runtime, same separation rationale as the
/// rest of the test host).
///
/// <para>
/// Pinned :
/// <list type="bullet">
///   <item><see cref="MissionStoreLogic.CountRecruitMissions"/> — the
///         cap-defense counter.</item>
///   <item><see cref="MissionStoreLogic.EnforceRecruitCap"/> — the
///         §A.8.D3 N=3 trim with non-recruit pass-through.</item>
///   <item><see cref="MissionStoreLogic.HasAnyMissionForLayer"/> —
///         the §A.8.D1 E1 reveal-gating predicate.</item>
///   <item><see cref="MissionStoreLogic.RecruitMissionCap"/> constant
///         pin (Varn-lock 2026-05-17 §A.8.D3).</item>
/// </list>
/// </para>
/// </summary>
public sealed class MissionStoreCascadePinsTests
{
    private static EmergentMissionDto MakeRecruit(string id, string npc, string targetPoi) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: $"hook-{id}",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi,
            RecruitTargetNpcId: npc);

    private static EmergentMissionDto MakeScout(string id, string targetPoi) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: $"hook-{id}",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);

    // ---- RecruitMissionCap constant ----

    [Fact]
    public void RecruitMissionCap_is_locked_at_three_per_varn_a8_d3()
    {
        // Anti-regression : a silent retune of the cap would let M1
        // emit a 4th recruit mission. Pin the constant value verbatim.
        Assert.Equal(3, MissionStoreLogic.RecruitMissionCap);
    }

    // ---- CountRecruitMissions ----

    [Fact]
    public void CountRecruitMissions_null_input_returns_zero()
    {
        Assert.Equal(0, MissionStoreLogic.CountRecruitMissions(null));
    }

    [Fact]
    public void CountRecruitMissions_empty_input_returns_zero()
    {
        Assert.Equal(0, MissionStoreLogic.CountRecruitMissions(
            System.Array.Empty<EmergentMissionDto>()));
    }

    [Fact]
    public void CountRecruitMissions_counts_only_recruit_type()
    {
        var missions = new[]
        {
            MakeRecruit("a", "kira", "e2.halfgate.gateway"),
            MakeScout("b", "e2.halfgate.intramuros"),
            MakeRecruit("c", "dorn", "e2.halfgate.intramuros"),
        };
        // 3 missions, 2 recruit, 1 scout.
        Assert.Equal(2, MissionStoreLogic.CountRecruitMissions(missions));
    }

    // ---- EnforceRecruitCap ----

    [Fact]
    public void EnforceRecruitCap_null_returns_empty()
    {
        Assert.Empty(MissionStoreLogic.EnforceRecruitCap(null));
    }

    [Fact]
    public void EnforceRecruitCap_under_cap_returns_same_reference_no_op()
    {
        // Caller can compare by reference to detect "nothing trimmed",
        // the runtime relies on this to skip the warning log.
        IReadOnlyList<EmergentMissionDto> input = new[]
        {
            MakeRecruit("a", "kira", "e2.halfgate.gateway"),
            MakeRecruit("b", "dorn", "e2.halfgate.intramuros"),
        };
        var result = MissionStoreLogic.EnforceRecruitCap(input);
        Assert.Same(input, result);
    }

    [Fact]
    public void EnforceRecruitCap_at_cap_returns_same_reference()
    {
        IReadOnlyList<EmergentMissionDto> input = new[]
        {
            MakeRecruit("a", "kira", "e2.halfgate.gateway"),
            MakeRecruit("b", "dorn", "e2.halfgate.intramuros"),
            MakeRecruit("c", "vell", "e2.halfgate.littoral"),
        };
        var result = MissionStoreLogic.EnforceRecruitCap(input);
        Assert.Same(input, result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void EnforceRecruitCap_over_cap_drops_surplus_keeps_first_N()
    {
        // The trim is "keep first N in source order, drop the rest".
        // Server-spawn order is the canonical ordering ; FIFO matches
        // the player's expectation if a server bug somehow emits 4+.
        var input = new[]
        {
            MakeRecruit("a", "kira", "e2.halfgate.gateway"),
            MakeRecruit("b", "dorn", "e2.halfgate.intramuros"),
            MakeRecruit("c", "vell", "e2.halfgate.littoral"),
            MakeRecruit("d", "unknown", "e2.halfgate.outskirts"),
        };
        var result = MissionStoreLogic.EnforceRecruitCap(input);
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "a", "b", "c" }, result.Select(m => m.Id));
    }

    [Fact]
    public void EnforceRecruitCap_non_recruit_missions_pass_through()
    {
        // Non-recruit missions don't count against the cap and don't
        // get trimmed. M2 will emit scout_route / parley_local alongside
        // recruit ; the cap applies only to recruit-type.
        var input = new[]
        {
            MakeRecruit("a", "kira", "e2.halfgate.gateway"),
            MakeScout("s1", "e2.halfgate.gateway"),
            MakeRecruit("b", "dorn", "e2.halfgate.intramuros"),
            MakeScout("s2", "e2.halfgate.intramuros"),
            MakeRecruit("c", "vell", "e2.halfgate.littoral"),
            MakeRecruit("d", "extra", "e2.halfgate.outskirts"), // surplus recruit
            MakeScout("s3", "e2.halfgate.littoral"),
        };
        var result = MissionStoreLogic.EnforceRecruitCap(input);
        // 3 recruit (a,b,c) + 3 scout (s1,s2,s3) = 6 ; the 4th recruit
        // ("d") is dropped, scouts are untouched.
        Assert.Equal(6, result.Count);
        Assert.Contains(result, m => m.Id == "s1");
        Assert.Contains(result, m => m.Id == "s2");
        Assert.Contains(result, m => m.Id == "s3");
        Assert.DoesNotContain(result, m => m.Id == "d");
    }

    // ---- HasAnyMissionForLayer (E1 reveal gating) ----

    [Fact]
    public void HasAnyMissionForLayer_empty_returns_false()
    {
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(
            System.Array.Empty<EmergentMissionDto>(), "e2.halfgate"));
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(null, "e2.halfgate"));
    }

    [Fact]
    public void HasAnyMissionForLayer_empty_prefix_returns_false()
    {
        var missions = new[] { MakeRecruit("a", "kira", "e2.halfgate.gateway") };
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, ""));
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, null!));
    }

    [Fact]
    public void HasAnyMissionForLayer_matches_e2_halfgate_descendant()
    {
        // §A.8.D1 — first recruit mission targeting e2.halfgate.*
        // triggers the E1 reveal-gate clearance.
        var missions = new[]
        {
            MakeRecruit("kira_001", "kira", "e2.halfgate.gateway"),
        };
        Assert.True(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate"));
        Assert.True(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate.gateway"));
    }

    [Fact]
    public void HasAnyMissionForLayer_matches_e1_halfgate_exact()
    {
        // Backward compat : a legacy e1.halfgate mission also clears
        // the E1 reveal gate (the §A.8.D1 spec mentions both shapes).
        var missions = new[]
        {
            new EmergentMissionDto(
                Id: "legacy",
                Type: WorldTickWireFormat.MissionType.Recruit,
                NarrativeHook: "h",
                EligiblePersonas: System.Array.Empty<string>(),
                Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
                Region: "halfgate",
                DeadlineTicks: null,
                Outcome: null,
                Seed: 0L,
                TargetPoi: "e1.halfgate",
                RecruitTargetNpcId: "kira"),
        };
        Assert.True(MissionStoreLogic.HasAnyMissionForLayer(missions, "e1.halfgate"));
    }

    [Fact]
    public void HasAnyMissionForLayer_skips_non_matching_regions()
    {
        // A mission targeting a different region (e.g. e1.veylant)
        // does NOT clear the Halfgate E1 reveal gate.
        var missions = new[]
        {
            new EmergentMissionDto(
                Id: "v",
                Type: WorldTickWireFormat.MissionType.ScoutRoute,
                NarrativeHook: "h",
                EligiblePersonas: System.Array.Empty<string>(),
                Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
                Region: "veylant",
                DeadlineTicks: null,
                Outcome: null,
                Seed: 0L,
                TargetPoi: "e1.veylant"),
        };
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate"));
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, "e1.halfgate"));
    }

    [Fact]
    public void HasAnyMissionForLayer_does_not_match_sibling_prefix()
    {
        // Defensive : "e2.halfgate2.x" must not match "e2.halfgate"
        // (same defensive rule as GetMissionsForLayer, mirrored here).
        var missions = new[]
        {
            MakeRecruit("x", "kira", "e2.halfgate2.gateway"),
        };
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate"));
    }

    [Fact]
    public void HasAnyMissionForLayer_skips_empty_target_poi()
    {
        // Legacy DTO with default TargetPoi="" must not match anything.
        var missions = new[]
        {
            new EmergentMissionDto(
                Id: "blank",
                Type: WorldTickWireFormat.MissionType.Recruit,
                NarrativeHook: "h",
                EligiblePersonas: System.Array.Empty<string>(),
                Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
                Region: "halfgate",
                DeadlineTicks: null,
                Outcome: null,
                Seed: 0L,
                TargetPoi: "",
                RecruitTargetNpcId: "kira"),
        };
        Assert.False(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate"));
    }

    // ---- Cross-pin : full roster scenario ----

    [Fact]
    public void Full_roster_clears_e2_halfgate_gate_and_obeys_cap()
    {
        // §A.8.D3 + §A.8.D1 joint scenario : 3 recruit missions, one
        // per district. Cap holds, layer gate clears, projection
        // surfaces all three.
        var missions = new[]
        {
            MakeRecruit("ml_kira", "kira", "e2.halfgate.gateway"),
            MakeRecruit("ml_dorn", "dorn", "e2.halfgate.intramuros"),
            MakeRecruit("ml_vell", "vell", "e2.halfgate.littoral"),
        };
        Assert.Equal(3, MissionStoreLogic.CountRecruitMissions(missions));
        Assert.True(MissionStoreLogic.HasAnyMissionForLayer(missions, "e2.halfgate"));
        Assert.Same(missions, MissionStoreLogic.EnforceRecruitCap(missions));
        Assert.Equal(3, MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate").Count);
    }
}
