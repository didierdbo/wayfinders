using System.Collections.Generic;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// C# wire-format mirror of the server-side Pydantic
/// <c>CharacterState</c> model (<c>wayfinders/ml/schemas/character.py</c>,
/// Varn-locked 2026-04-30).
///
/// <para>
/// <b>What this file is for.</b> M1 substep 4b
/// (mission-emergence slice). The server's eligible-personas filter
/// (Varn-lock 2026-05-10 §1) reads <see cref="DexBucket"/> /
/// <see cref="WisBucket"/> from each <c>company_personas</c> entry to
/// compute the ±1-bucket window. Without a typed mirror, the client
/// can only post <c>[]</c> and every mission emerges with empty
/// <see cref="EmergentMissionDto.EligiblePersonas"/> — the test e2e is
/// then impossible to write meaningfully. The full mirror unblocks the
/// chain. (Decision Didier ratifié 2026-05-10, see memory
/// <c>project_wayfinders_mission_emergence.md</c>.)
/// </para>
///
/// <para>
/// <b>Source of truth.</b>
/// <c>Owner's Inbox/2026-05-10-Wayfinders-CharacterState-wire-format.md</c>
/// (Tess). Each field in this record matches a Pydantic field in
/// <c>CharacterState</c> by name (after the snake_case JSON naming
/// policy on <see cref="ApiJsonContext"/>). The 15-field invariant is
/// pinned by xUnit round-trip tests in
/// <c>tests/CharacterStateDtoTests.cs</c>.
/// </para>
///
/// <para>
/// <b>Wire-format hot-spots — read me before editing.</b>
/// <list type="bullet">
///   <item><b><see cref="ActionCounters"/>.</b> Pydantic
///         <c>tuple[int, int]</c> serialises as a JSON 2-element array,
///         <c>[successes, failures]</c>, NOT an object
///         <c>{"successes": ..., "failures": ...}</c>. We mirror it as
///         <c>int[]</c> — the simplest .NET shape that round-trips
///         correctly with the source-gen JSON path. A tuple-typed
///         alternative (<c>(int Successes, int Failures)</c>) would
///         require a custom <c>JsonConverter</c> and AOT-friendly
///         metadata wiring ; not worth the complexity for a 2-element
///         array. Pin test :
///         <c>action_counters_serialize_as_two_int_array</c>.</item>
///   <item><b><see cref="LocationFamiliarity"/>.</b> Pydantic Literal
///         restricts the values to four strings :
///         <c>"barely"</c>, <c>"moderately"</c>, <c>"well"</c>, and
///         <c>"best of all"</c> — note the space in the last one. We
///         keep the value type as <c>string</c> (NOT an enum) and pin
///         the four allowed strings as constants on
///         <see cref="LocationFamiliarityValues"/>. An enum would force
///         a <c>JsonStringEnumConverter</c> with custom name mapping
///         for "best of all" → <c>BestOfAll</c>, which adds a reflection
///         seam that conflicts with the AOT/source-gen path used
///         everywhere else. Pin test :
///         <c>location_familiarity_preserves_best_of_all_with_space</c>.</item>
///   <item><b><see cref="StressCause"/>.</b> Optional, defaults to
///         <c>null</c> on the server — when null, the prose renderer
///         omits the trailing "from a short night's sleep"-style
///         clause. The client should send <c>null</c> when there is
///         no cause, <i>not</i> the empty string (Pydantic accepts both
///         but the renderer treats <c>""</c> as "cause: empty
///         clause" — a regression vector).</item>
///   <item><b><c>Bond.sign</c>.</b> Server-side Python <c>@property</c>,
///         derived from <see cref="BondDto.BondValue"/>. NOT a stored
///         field. Do NOT mirror it on <see cref="BondDto"/> — the JSON
///         payload would carry a fifth bond key that the server's
///         <c>extra="forbid"</c> Pydantic config rejects. Documented
///         absence ; the C# side computes the sign locally if needed.</item>
///   <item><b><see cref="PronounSubject"/>.</b> Defaults to
///         <c>"she"</c> on the server. The client may send any value or
///         omit the field entirely (Pydantic uses the default). C# uses
///         <c>"she"</c> as the default for the <c>init</c> property, in
///         line with the server contract.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Naming policy.</b> Wire is snake_case ; C# PascalCase ;
/// <see cref="ApiJsonContext"/> applies <c>SnakeCaseLower</c> uniformly.
/// Field names in this record map 1:1 to Pydantic field names after the
/// policy.
/// </para>
///
/// <para>
/// <b>What is intentionally NOT mirrored.</b> The Pydantic
/// <c>model_config = ConfigDict(frozen=True)</c> immutability is
/// achieved here via <c>init</c>-only properties on the record — same
/// effect, idiomatic C#. The <c>render_character()</c> renderer logic
/// is server-side only ; the client never re-renders prose.
/// </para>
/// </summary>
public sealed record CharacterStateDto
{
    // ---- Group 1 — Identity (required) ----

    /// <summary>Persona id in M1 — this is the field that identifies
    /// the persona in <see cref="EmergentMissionDto.EligiblePersonas"/>
    /// results. Free-form string, not a closed lookup.</summary>
    public required string Name { get; init; }

    /// <summary>Free-form class string (e.g. "scout"). Not closed.</summary>
    public required string CharClass { get; init; }

    /// <summary>Free-form faction string (e.g. "Ridgewatch company").
    /// Not closed.</summary>
    public required string Faction { get; init; }

    // ---- Group 2 — Descriptor buckets (required, closed lookup) ----

    /// <summary>STR descriptor bucket. Closed lookup, see
    /// <see cref="DescriptorBucketValues"/>. Not used in the M1 filter
    /// but required by the schema (no default, non-nullable on the
    /// server).</summary>
    public required string StrBucket { get; init; }

    /// <summary>DEX descriptor bucket. Closed lookup, see
    /// <see cref="DescriptorBucketValues"/>. <b>Drives the
    /// <c>scout_route</c> filter</b> — server reads this when computing
    /// <see cref="EmergentMissionDto.EligiblePersonas"/> for a
    /// scout_route mission.</summary>
    public required string DexBucket { get; init; }

    /// <summary>WIS descriptor bucket. Closed lookup, see
    /// <see cref="DescriptorBucketValues"/>. <b>Drives the
    /// <c>parley_local</c> filter</b> — server reads this when computing
    /// <see cref="EmergentMissionDto.EligiblePersonas"/> for a
    /// parley_local mission.</summary>
    public required string WisBucket { get; init; }

    // ---- Group 3 — Traits (optional) ----

    /// <summary>Free-form trait strings. Defaults to empty array.
    /// Renderer takes the first 3 after alpha sort.</summary>
    public IReadOnlyList<string> Traits { get; init; } = System.Array.Empty<string>();

    // ---- Group 4 — Status vignette (required) ----

    /// <summary>HP bucket. Closed lookup, see
    /// <see cref="HpBucketValues"/>.</summary>
    public required string HpBucket { get; init; }

    /// <summary>Stress bucket. Closed lookup, see
    /// <see cref="StressBucketValues"/>.</summary>
    public required string StressBucket { get; init; }

    /// <summary>Optional cause phrase appended to the stress bucket
    /// word, e.g. "from a short night's sleep". Send <c>null</c> when
    /// there is no cause — NOT the empty string (regression vector,
    /// see class-level docstring).</summary>
    public string? StressCause { get; init; }

    /// <summary>Short-prose condition snippets, e.g. "with a fading
    /// bruise on her forearm". Defaults to empty.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = System.Array.Empty<string>();

    // ---- Group 5 — Equipment (optional) ----

    /// <summary>Equipment strings (full noun phrases). Defaults to
    /// empty.</summary>
    public IReadOnlyList<string> Equipment { get; init; } = System.Array.Empty<string>();

    // ---- Group 6 — Legacy biography (optional, defaults to empty) ----

    /// <summary>Combat engagement counters keyed by category, e.g.
    /// <c>{"goblin engagements": 47}</c>. Non-negative ints.</summary>
    public IReadOnlyDictionary<string, int> CombatEngagements { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Action counters keyed by action name. Wire format is a
    /// 2-element JSON array <c>[successes, failures]</c>, mirrored here
    /// as <c>int[]</c>. See <see cref="ActionCounter"/> helpers for
    /// safe access. Pin test : the array length is exactly 2.</summary>
    public IReadOnlyDictionary<string, int[]> ActionCounters { get; init; }
        = new Dictionary<string, int[]>();

    /// <summary>Profession hours keyed by profession, e.g.
    /// <c>{"scouting": 80}</c>.</summary>
    public IReadOnlyDictionary<string, int> ProfessionHours { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Location familiarity keyed by location. Values are
    /// closed lookup, see <see cref="LocationFamiliarityValues"/>.
    /// Watch out : <c>"best of all"</c> contains a space — preserve
    /// it on the wire.</summary>
    public IReadOnlyDictionary<string, string> LocationFamiliarity { get; init; }
        = new Dictionary<string, string>();

    // ---- Group 7 — Episodic memory (optional) ----

    /// <summary>Top-3-by-salience memories rendered in paragraph 7 of
    /// the canonical prose. Defaults to empty.</summary>
    public IReadOnlyList<EpisodicEventDto> EpisodicMemory { get; init; }
        = System.Array.Empty<EpisodicEventDto>();

    // ---- Group 8 — Bonds (optional) ----

    /// <summary>Bonds (signed bond_value, partner death flag). Defaults
    /// to empty. Sign is computed server-side from
    /// <see cref="BondDto.BondValue"/> ; do NOT mirror that property on
    /// <see cref="BondDto"/>.</summary>
    public IReadOnlyList<BondDto> Bonds { get; init; }
        = System.Array.Empty<BondDto>();

    // ---- Group 9 — Misc ----

    /// <summary>Subject pronoun. Defaults to <c>"she"</c> matching the
    /// server-side default. UI-only field — not used by the renderer
    /// (the encoder repeats the name on every paragraph except
    /// paragraph 6 by design).</summary>
    public string PronounSubject { get; init; } = "she";
}

/// <summary>
/// Sub-record for an episodic memory event (<see cref="CharacterStateDto.EpisodicMemory"/>).
/// Mirrors Pydantic <c>EpisodicEvent</c> in
/// <c>wayfinders/ml/schemas/character.py</c>.
///
/// <para>
/// <b>Determinism contract.</b> When two events have the same
/// <see cref="Salience"/>, the renderer ties on
/// <see cref="EventId"/> ascending. The client must therefore preserve
/// <see cref="EventId"/> identity across reloads (a fresh sequence each
/// session would shuffle the rendered top-3).
/// </para>
/// </summary>
/// <param name="EventId">Stable id, used as a deterministic tiebreak
/// in the top-3 renderer sort. Lower wins.</param>
/// <param name="Salience">[0..1] salience score. Higher = more
/// salient ; renderer takes the top 3.</param>
/// <param name="Description">Pratchett-flavoured EN prose describing
/// the event, e.g. "her first kill, a goblin scout in the
/// Hollowwood".</param>
public sealed record EpisodicEventDto(
    int EventId,
    double Salience,
    string Description
);

/// <summary>
/// Sub-record for a relational bond (<see cref="CharacterStateDto.Bonds"/>).
/// Mirrors Pydantic <c>Bond</c> in
/// <c>wayfinders/ml/schemas/character.py</c>.
///
/// <para>
/// <b>What is NOT mirrored : the <c>sign</c> property.</b> The
/// server's <c>Bond</c> exposes a <c>@property sign</c> derived from
/// <see cref="BondValue"/> ("positive" / "neutral" / "negative") for
/// the prose renderer. It is NOT a stored field — sending it on the
/// wire would produce an extra JSON key and the server's
/// <c>extra="forbid"</c> Pydantic config would reject the entire
/// payload. The C# side computes the sign locally from
/// <see cref="BondValue"/> if needed.
/// </para>
/// </summary>
/// <param name="PartnerName">Free-form name of the bond partner.</param>
/// <param name="BondValue">Signed integer. Positive = trust,
/// 0 = neutral, negative = wariness.</param>
/// <param name="PartnerIsDead">Defaults to <c>false</c>. When
/// <c>true</c>, the renderer appends ", though X is dead" to the
/// bond clause.</param>
public sealed record BondDto(
    string PartnerName,
    int BondValue,
    bool PartnerIsDead = false
);

/// <summary>
/// Closed-lookup values for the descriptor buckets
/// (<see cref="CharacterStateDto.StrBucket"/>,
/// <see cref="CharacterStateDto.DexBucket"/>,
/// <see cref="CharacterStateDto.WisBucket"/>).
///
/// <para>
/// Pydantic Literal lock from
/// <c>wayfinders.ml.schemas.vocabularies</c> (Varn-locked 2026-05-06).
/// Same hyphenated value vocabulary as
/// <see cref="WorldTickWireFormat.DifficultyBucket"/> — the difficulty
/// bucket of a mission and the descriptor bucket of a persona share
/// this vocabulary so the ±1-window filter operates on apples-to-apples
/// values.
/// </para>
/// </summary>
public static class DescriptorBucketValues
{
    public const string VeryLow = "very-low";
    public const string Low = "low";
    public const string Mid = "mid";
    public const string High = "high";
    public const string VeryHigh = "very-high";
}

/// <summary>
/// Closed-lookup values for <see cref="CharacterStateDto.HpBucket"/>.
/// Pydantic Literal lock in
/// <c>wayfinders.ml.schemas.vocabularies</c> (HP_BUCKETS map keys).
/// </summary>
public static class HpBucketValues
{
    public const string Unhurt = "unhurt";
    public const string Scratched = "scratched";
    public const string Bleeding = "bleeding";
    public const string Down = "down";
}

/// <summary>
/// Closed-lookup values for
/// <see cref="CharacterStateDto.StressBucket"/>. Pydantic Literal lock
/// in <c>wayfinders.ml.schemas.vocabularies</c> (STRESS_BUCKETS map
/// keys). Note the hyphen in <c>"near-breaking"</c>.
/// </summary>
public static class StressBucketValues
{
    public const string Composed = "composed";
    public const string Tense = "tense";
    public const string Frayed = "frayed";
    public const string NearBreaking = "near-breaking";
}

/// <summary>
/// Closed-lookup values for the values of
/// <see cref="CharacterStateDto.LocationFamiliarity"/>. Note the
/// space in <c>"best of all"</c> — see the
/// <see cref="CharacterStateDto"/> docstring for the rationale of
/// keeping these as <c>string</c> constants rather than an enum.
/// </summary>
public static class LocationFamiliarityValues
{
    public const string Barely = "barely";
    public const string Moderately = "moderately";
    public const string Well = "well";
    public const string BestOfAll = "best of all";
}
