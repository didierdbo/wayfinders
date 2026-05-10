// M1 mission-emergence (etape 4b) — pin tests for the typed
// CharacterState wire format. Every assertion defends a specific
// Pydantic field name, JSON shape, or Varn-locked Literal value
// from the server side (wire-format doc by Tess :
// Owner's Inbox/2026-05-10-Wayfinders-CharacterState-wire-format.md).
//
// What this file pins :
//   1. The 15 fields of CharacterStateDto round-trip cleanly through
//      the source-gen JSON path (snake_case names + correct types).
//   2. The action_counters wire shape : a Dict[str, int[]] where the
//      int[] is exactly 2 elements [successes, failures]. Tess flagged
//      this as the #1 hot-spot — Python tuple[int, int] becomes a JSON
//      array, NOT an object.
//   3. The location_familiarity values preserve "best of all" with the
//      space — anti-regression for a future enum refactor that would
//      collapse it to "best_of_all" or "BestOfAll".
//   4. Bond.sign is NOT mirrored — the JSON serialisation must not
//      emit a "sign" field (server would 422 with extra="forbid").
//   5. PronounSubject defaults to "she" — matches server-side default.
//   6. The full canonical Kira example (rich state) round-trips
//      without losing any field.
//   7. The minimal example (8 required fields, all optionals omitted)
//      deserialises cleanly — defends Pydantic's defaults.
//
// What this file deliberately does NOT pin :
//   - The Pydantic side itself. That is Coda/Tess's lane (pytest).
//   - The render_character() server-side prose. The C# client never
//     re-renders prose ; the server is the source of truth.

using System.Collections.Generic;
using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class CharacterStateDtoTests
{
    // ====================================================================
    // Closed-lookup constants (mirror tests for the constants files).
    // Catches a typo in DescriptorBucketValues / HpBucketValues / etc.
    // before it goes to the wire and gets 422'd by Pydantic.
    // ====================================================================

    [Fact]
    public void Descriptor_bucket_constants_match_server_literal()
    {
        Assert.Equal("very-low", DescriptorBucketValues.VeryLow);
        Assert.Equal("low", DescriptorBucketValues.Low);
        Assert.Equal("mid", DescriptorBucketValues.Mid);
        Assert.Equal("high", DescriptorBucketValues.High);
        Assert.Equal("very-high", DescriptorBucketValues.VeryHigh);
    }

    [Fact]
    public void Hp_bucket_constants_match_server_literal()
    {
        Assert.Equal("unhurt", HpBucketValues.Unhurt);
        Assert.Equal("scratched", HpBucketValues.Scratched);
        Assert.Equal("bleeding", HpBucketValues.Bleeding);
        Assert.Equal("down", HpBucketValues.Down);
    }

    [Fact]
    public void Stress_bucket_constants_match_server_literal()
    {
        Assert.Equal("composed", StressBucketValues.Composed);
        Assert.Equal("tense", StressBucketValues.Tense);
        Assert.Equal("frayed", StressBucketValues.Frayed);
        // Note the hyphen — not "near_breaking", not "nearBreaking".
        Assert.Equal("near-breaking", StressBucketValues.NearBreaking);
    }

    [Fact]
    public void Location_familiarity_constants_match_server_literal()
    {
        Assert.Equal("barely", LocationFamiliarityValues.Barely);
        Assert.Equal("moderately", LocationFamiliarityValues.Moderately);
        Assert.Equal("well", LocationFamiliarityValues.Well);
        // The space-bearing literal — preserve it byte-for-byte.
        Assert.Equal("best of all", LocationFamiliarityValues.BestOfAll);
    }

    // ====================================================================
    // Minimal CharacterStateDto — 8 required fields, optionals omitted
    // ====================================================================

    /// <summary>
    /// The minimum a client needs to send for the ±1-bucket filter to
    /// work. All optional fields use their declared defaults
    /// (empty collections, null stress_cause, "she" pronoun). This
    /// pins the contract from Tess's wire-format doc §
    /// "Exemple complet minimaliste".
    /// </summary>
    [Fact]
    public void Minimal_state_serializes_with_required_fields_only()
    {
        var minimal = new CharacterStateDto
        {
            Name = "kira",
            CharClass = "scout",
            Faction = "Ridgewatch company",
            StrBucket = DescriptorBucketValues.Mid,
            DexBucket = DescriptorBucketValues.High,
            WisBucket = DescriptorBucketValues.Mid,
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
        };

        var json = JsonSerializer.Serialize(
            minimal, ApiJsonContext.Default.CharacterStateDto);

        Assert.Contains("\"name\":\"kira\"", json);
        Assert.Contains("\"char_class\":\"scout\"", json);
        Assert.Contains("\"faction\":\"Ridgewatch company\"", json);
        Assert.Contains("\"str_bucket\":\"mid\"", json);
        Assert.Contains("\"dex_bucket\":\"high\"", json);
        Assert.Contains("\"wis_bucket\":\"mid\"", json);
        Assert.Contains("\"hp_bucket\":\"unhurt\"", json);
        Assert.Contains("\"stress_bucket\":\"composed\"", json);

        // PronounSubject default is "she" — should serialise it.
        Assert.Contains("\"pronoun_subject\":\"she\"", json);

        // No PascalCase leak — anti-regression for naming policy
        // drift on a record-init property.
        Assert.DoesNotContain("\"Name\":", json);
        Assert.DoesNotContain("\"DexBucket\":", json);
        Assert.DoesNotContain("\"StressCause\":", json);
    }

    // ====================================================================
    // action_counters — the [int, int] wire shape (Tess hot-spot #1)
    // ====================================================================

    /// <summary>
    /// Pydantic <c>tuple[int, int]</c> serialises as a JSON 2-element
    /// array. Mirroring it as <c>int[]</c> on the C# side gives the
    /// exact wire shape the server expects. An object form
    /// <c>{"successes": 8, "failures": 3}</c> would be rejected.
    /// </summary>
    [Fact]
    public void Action_counters_serialize_as_two_int_array()
    {
        var state = MinimalKira() with
        {
            ActionCounters = new Dictionary<string, int[]>
            {
                ["stealth approaches"] = new[] { 8, 3 },
            },
        };

        var json = JsonSerializer.Serialize(
            state, ApiJsonContext.Default.CharacterStateDto);

        // The wire form is the array notation, NOT an object.
        Assert.Contains("\"action_counters\":{\"stealth approaches\":[8,3]}", json);
        Assert.DoesNotContain("\"successes\"", json);
        Assert.DoesNotContain("\"failures\"", json);
    }

    /// <summary>
    /// Server emits the 2-element array on the response side too (in
    /// theory — M1 only sends, doesn't receive CharacterStates, but
    /// pin the deser path so a future endpoint that returns one
    /// works). Catches a regression where someone changes int[] to
    /// a typed record without updating the deser.
    /// </summary>
    [Fact]
    public void Action_counters_deserialize_from_two_int_array()
    {
        const string json = """
            {
              "name": "kira",
              "char_class": "scout",
              "faction": "Ridgewatch company",
              "str_bucket": "mid",
              "dex_bucket": "high",
              "wis_bucket": "mid",
              "hp_bucket": "unhurt",
              "stress_bucket": "composed",
              "action_counters": {"stealth approaches": [8, 3]}
            }
            """;

        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.CharacterStateDto);

        Assert.NotNull(parsed);
        Assert.Equal(8, parsed.ActionCounters["stealth approaches"][0]);
        Assert.Equal(3, parsed.ActionCounters["stealth approaches"][1]);
        Assert.Equal(2, parsed.ActionCounters["stealth approaches"].Length);
    }

    // ====================================================================
    // location_familiarity — preserve "best of all" with the space
    // ====================================================================

    /// <summary>
    /// The Pydantic Literal includes "best of all" with two spaces.
    /// Anti-regression : if a future refactor introduces an enum
    /// with a name converter, the wire value MUST stay
    /// "best of all" — not "best_of_all", not "bestOfAll".
    /// </summary>
    [Fact]
    public void Location_familiarity_preserves_best_of_all_with_space()
    {
        var state = MinimalKira() with
        {
            LocationFamiliarity = new Dictionary<string, string>
            {
                ["forest terrain"] = LocationFamiliarityValues.Well,
                ["town streets"] = LocationFamiliarityValues.BestOfAll,
            },
        };

        var json = JsonSerializer.Serialize(
            state, ApiJsonContext.Default.CharacterStateDto);

        Assert.Contains("\"forest terrain\":\"well\"", json);
        // The space MUST round-trip on the wire.
        Assert.Contains("\"town streets\":\"best of all\"", json);
        // Anti-regression for an underscore version.
        Assert.DoesNotContain("\"best_of_all\"", json);
    }

    // ====================================================================
    // Bond.sign — NOT on the wire (would be rejected by extra="forbid")
    // ====================================================================

    /// <summary>
    /// Bond.sign is a server-side @property derived from bond_value.
    /// The C# DTO must NOT emit a "sign" field — the server's
    /// extra="forbid" Pydantic config rejects unknown keys.
    /// </summary>
    [Fact]
    public void Bond_does_not_emit_sign_field_on_wire()
    {
        var state = MinimalKira() with
        {
            Bonds = new[]
            {
                new BondDto("Hodge", 5, false),
                new BondDto("Wattan", -3, false),
            },
        };

        var json = JsonSerializer.Serialize(
            state, ApiJsonContext.Default.CharacterStateDto);

        // The fields we DO send.
        Assert.Contains("\"partner_name\":\"Hodge\"", json);
        Assert.Contains("\"bond_value\":5", json);
        Assert.Contains("\"partner_is_dead\":false", json);

        // The field we MUST NOT send (server rejects with 422).
        Assert.DoesNotContain("\"sign\":", json);
    }

    // ====================================================================
    // EpisodicEvent — the 3-field shape
    // ====================================================================

    [Fact]
    public void Episodic_memory_round_trips_event_id_salience_description()
    {
        var state = MinimalKira() with
        {
            EpisodicMemory = new[]
            {
                new EpisodicEventDto(
                    EventId: 1,
                    Salience: 0.95,
                    Description: "her first kill, a goblin scout in the Hollowwood"),
            },
        };

        var json = JsonSerializer.Serialize(
            state, ApiJsonContext.Default.CharacterStateDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.CharacterStateDto);

        Assert.NotNull(parsed);
        Assert.Single(parsed.EpisodicMemory);
        var ev = parsed.EpisodicMemory[0];
        Assert.Equal(1, ev.EventId);
        Assert.Equal(0.95, ev.Salience);
        Assert.Equal("her first kill, a goblin scout in the Hollowwood", ev.Description);
    }

    // ====================================================================
    // Round-trip identity — full Kira canonical example (15 fields)
    // ====================================================================

    /// <summary>
    /// The full canonical Kira example from Tess's wire-format doc §
    /// "Exemple complet riche". Round-trip every field. If a future
    /// regression drops a field from the source-gen context or a
    /// JsonSerializable attribute is missed, this is where it shows
    /// up first.
    /// </summary>
    [Fact]
    public void Full_kira_canonical_round_trips_all_fields()
    {
        var kira = new CharacterStateDto
        {
            Name = "Kira",
            CharClass = "scout",
            Faction = "Ridgewatch company",
            StrBucket = DescriptorBucketValues.Mid,
            DexBucket = DescriptorBucketValues.High,
            WisBucket = DescriptorBucketValues.Mid,
            Traits = new[] { "cautious" },
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
            StressCause = null,
            Conditions = System.Array.Empty<string>(),
            Equipment = new[] { "short bow", "leather armor" },
            CombatEngagements = new Dictionary<string, int>
            {
                ["goblin engagements"] = 47,
                ["beast hunts"] = 12,
            },
            ActionCounters = new Dictionary<string, int[]>
            {
                ["stealth approaches"] = new[] { 8, 3 },
            },
            ProfessionHours = new Dictionary<string, int>
            {
                ["scouting"] = 80,
            },
            LocationFamiliarity = new Dictionary<string, string>
            {
                ["forest terrain"] = LocationFamiliarityValues.Well,
                ["town streets"] = LocationFamiliarityValues.BestOfAll,
            },
            EpisodicMemory = new[]
            {
                new EpisodicEventDto(1, 0.95,
                    "her first kill, a goblin scout in the Hollowwood"),
            },
            Bonds = new[]
            {
                new BondDto("Hodge", 5, false),
            },
            PronounSubject = "she",
        };

        var json = JsonSerializer.Serialize(
            kira, ApiJsonContext.Default.CharacterStateDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.CharacterStateDto);

        Assert.NotNull(parsed);
        Assert.Equal("Kira", parsed.Name);
        Assert.Equal("scout", parsed.CharClass);
        Assert.Equal("Ridgewatch company", parsed.Faction);
        Assert.Equal("mid", parsed.StrBucket);
        Assert.Equal("high", parsed.DexBucket);
        Assert.Equal("mid", parsed.WisBucket);
        Assert.Single(parsed.Traits);
        Assert.Equal("cautious", parsed.Traits[0]);
        Assert.Equal("unhurt", parsed.HpBucket);
        Assert.Equal("composed", parsed.StressBucket);
        Assert.Null(parsed.StressCause);
        Assert.Empty(parsed.Conditions);
        Assert.Equal(2, parsed.Equipment.Count);
        Assert.Equal("short bow", parsed.Equipment[0]);
        Assert.Equal(47, parsed.CombatEngagements["goblin engagements"]);
        Assert.Equal(12, parsed.CombatEngagements["beast hunts"]);
        Assert.Equal(8, parsed.ActionCounters["stealth approaches"][0]);
        Assert.Equal(3, parsed.ActionCounters["stealth approaches"][1]);
        Assert.Equal(80, parsed.ProfessionHours["scouting"]);
        Assert.Equal("well", parsed.LocationFamiliarity["forest terrain"]);
        Assert.Equal("best of all", parsed.LocationFamiliarity["town streets"]);
        Assert.Single(parsed.EpisodicMemory);
        Assert.Equal(1, parsed.EpisodicMemory[0].EventId);
        Assert.Single(parsed.Bonds);
        Assert.Equal("Hodge", parsed.Bonds[0].PartnerName);
        Assert.Equal(5, parsed.Bonds[0].BondValue);
        Assert.False(parsed.Bonds[0].PartnerIsDead);
        Assert.Equal("she", parsed.PronounSubject);
    }

    // ====================================================================
    // company_personas in WorldTickRequestDto — the integration shape
    // ====================================================================

    /// <summary>
    /// The whole point of 4b : <c>company_personas</c> now carries
    /// typed CharacterStateDto records. Pin the round-trip via the
    /// <see cref="WorldTickRequestDto"/> shape — same path
    /// <see cref="WorldSimTick"/> uses at runtime.
    /// </summary>
    [Fact]
    public void Tick_request_with_typed_company_personas_round_trips()
    {
        var request = new WorldTickRequestDto(
            Tick: 7,
            Seed: 42L,
            ContextProse: "The forest is quiet.",
            CompanyPersonas: new[]
            {
                MinimalKira(),
                MinimalKira() with { Name = "hodge", DexBucket = DescriptorBucketValues.Mid },
            });

        var json = JsonSerializer.Serialize(
            request, ApiJsonContext.Default.WorldTickRequestDto);

        // Two personas serialised as a JSON array of two objects.
        Assert.Contains("\"company_personas\":[", json);
        Assert.Contains("\"name\":\"kira\"", json);
        Assert.Contains("\"name\":\"hodge\"", json);

        // Round-trip back.
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickRequestDto);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.CompanyPersonas.Count);
        Assert.Equal("kira", parsed.CompanyPersonas[0].Name);
        Assert.Equal("hodge", parsed.CompanyPersonas[1].Name);
    }

    // ====================================================================
    // MissionResolveRequestDto — wire format pin
    // ====================================================================

    [Fact]
    public void Mission_resolve_request_serializes_with_snake_case()
    {
        var req = new MissionResolveRequestDto(
            MissionId: "abc-123",
            MissionType: WorldTickWireFormat.MissionType.ScoutRoute,
            Region: "halfgate",
            ActorTarget: null,
            Outcome: WorldTickWireFormat.ResolutionOutcome.Success,
            Tick: 42,
            AssignedPersonas: new[] { "kira", "hodge" });

        var json = JsonSerializer.Serialize(
            req, ApiJsonContext.Default.MissionResolveRequestDto);

        Assert.Contains("\"mission_id\":\"abc-123\"", json);
        Assert.Contains("\"mission_type\":\"scout_route\"", json);
        Assert.Contains("\"region\":\"halfgate\"", json);
        Assert.Contains("\"actor_target\":null", json);
        Assert.Contains("\"outcome\":\"success\"", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"assigned_personas\":[\"kira\",\"hodge\"]", json);
    }

    [Fact]
    public void Mission_resolve_response_deserializes_tags_created()
    {
        const string json = """
            {
              "tags_created": [
                {
                  "persona_id": "kira",
                  "mission_id": "abc-123",
                  "mission_type": "scout_route",
                  "region": "halfgate",
                  "actor_target": null,
                  "outcome": "success",
                  "earned_at_tick": 42
                }
              ]
            }
            """;

        var resp = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionResolveResponseDto);

        Assert.NotNull(resp);
        Assert.Single(resp.TagsCreated);
        var tag = resp.TagsCreated[0];
        Assert.Equal("kira", tag.PersonaId);
        Assert.Equal("abc-123", tag.MissionId);
        Assert.Equal("scout_route", tag.MissionType);
        Assert.Equal("halfgate", tag.Region);
        Assert.Null(tag.ActorTarget);
        Assert.Equal("success", tag.Outcome);
        Assert.Equal(42, tag.EarnedAtTick);
    }

    [Fact]
    public void Mission_resolve_response_with_empty_tags_deserializes_to_empty()
    {
        // The declined-mission path : assigned_personas was empty,
        // server returns tags_created=[]. The client must accept
        // the empty array cleanly.
        const string json = """{"tags_created": []}""";

        var resp = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionResolveResponseDto);

        Assert.NotNull(resp);
        Assert.Empty(resp.TagsCreated);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static CharacterStateDto MinimalKira() => new()
    {
        Name = "kira",
        CharClass = "scout",
        Faction = "Ridgewatch company",
        StrBucket = DescriptorBucketValues.Mid,
        DexBucket = DescriptorBucketValues.High,
        WisBucket = DescriptorBucketValues.Mid,
        HpBucket = HpBucketValues.Unhurt,
        StressBucket = StressBucketValues.Composed,
    };
}
