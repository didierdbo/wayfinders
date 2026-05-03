namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTO for a unit entry returned by <c>GET /api/units</c>.
///
/// <para>
/// Mirrors Coda's Pydantic <c>Unit</c> model in
/// <c>wayfinders/api/models.py</c>. Any change here must be coordinated
/// with the FastAPI side of the boundary — this is the contract between
/// the two services.
/// </para>
///
/// <para>
/// Why <c>record</c>: this is an immutable wire-shape. <c>record</c> gives
/// us value-equality (two DTOs with the same fields compare equal — useful
/// for tests, caching, and diffing roster snapshots), <c>init</c>-only
/// properties (the deserializer can write them, nothing else can), and a
/// concise primary-constructor syntax. C# records are a different beast
/// from Java records — they're reference types with reference identity but
/// value semantics on equality, and they support <c>with</c>-expressions
/// for non-destructive copies.
/// </para>
///
/// <para>
/// Property names are PascalCase by C# convention. The wire is
/// snake_case (FastAPI default). The mapping is performed by the
/// <see cref="ApiJsonContext"/> source-gen context's naming policy —
/// no per-property <c>[JsonPropertyName]</c> attributes needed because
/// every property follows the same rule.
/// </para>
/// </summary>
/// <param name="DisplayName">
/// Human-readable unit name shown in the UI. Maps from <c>display_name</c>.
/// Pydantic enforces non-empty; we trust the server.
/// </param>
/// <param name="MaxHp">
/// Maximum hit points. Maps from <c>max_hp</c>. Server enforces &gt; 0.
/// </param>
/// <param name="BaseSpeed">
/// Base movement speed in tiles per turn. Maps from <c>base_speed</c>.
/// Server enforces &gt; 0.
/// </param>
/// <param name="Portrait">
/// Path or filename of the unit's portrait asset. Maps from <c>portrait</c>.
/// Resolution against <c>res://</c> happens client-side when the L5
/// roster scene actually displays portraits.
/// </param>
public sealed record UnitDto(
    string DisplayName,
    int MaxHp,
    int BaseSpeed,
    string Portrait
);
