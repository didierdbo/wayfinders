using System.Collections.Generic;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Per-NPC mutable runtime state held on the
/// <see cref="Wayfinders.Client.Services.NpcRegistry"/> autoload. The
/// static catalog (<see cref="Wayfinders.Client.Scripts.Screens.NpcCatalog"/>)
/// holds pure data (display name, portrait keys) ; this record holds
/// the mutable gameplay state that survives screen changes.
///
/// <para>
/// <b>Varn-lock 2026-05-17 Section D (I-10).</b> Schema locked here so
/// the E2.3 recruit panel (next milestone) can wire against a real
/// shape rather than re-inventing one at the call site.
/// </para>
///
/// <para>
/// <b>Authority (Varn §D.3).</b> Client-authoritative for M1 (no save
/// layer, no multiplayer ; the client owns the truth and the server's
/// tick payload <c>WorldTickRequest.company_personas</c> is built from
/// <see cref="Wayfinders.Client.Services.GameState.CompanyPersonas"/>,
/// not from this record — the two stores are kept consistent by the
/// recruit handler being the sole writer of both). Server-authoritative
/// reconciliation lands at M2 step 3 (save layer + faction layer).
/// </para>
///
/// <para>
/// <b>Mutation discipline.</b> Main thread only — same rule as
/// <see cref="Wayfinders.Client.Services.GameState"/>. Mutate via
/// <see cref="Wayfinders.Client.Services.NpcRegistry.SetState"/> or the
/// <c>MutateState</c> helper ; both fire <c>NpcStateChanged</c> on the
/// autoload exactly once per write.
/// </para>
///
/// <para>
/// <b>Lifecycle (Varn §D.4).</b> Entries are created lazily by
/// <see cref="Wayfinders.Client.Services.NpcRegistry.GetOrCreateState"/>
/// — the first read/mutation of an NpcId materialises a default-shaped
/// entry. <b>Never despawned</b> : entries persist for the session.
/// Memory cost is bounded by <c>NpcCatalog.Count</c> (currently 2 ;
/// M2 scales to ~30-50). M1 keeps everything in-memory only ; M2 step 3
/// snapshots to disk via the save system.
/// </para>
///
/// <para>
/// <b>Why a record (immutable), not a mutable class.</b> Same rationale
/// as <see cref="LocationContextState"/> — <c>with</c>-expression
/// mutation gives value equality for free, which the autoload's
/// "did this write actually change anything ?" gate uses to keep
/// downstream signal-fan-out clean. A mutable class would need
/// explicit per-field diff at the setter edge.
/// </para>
///
/// <para>
/// <b>Equality caveat on <see cref="KnownTilesAppliedTo"/>.</b> C#
/// record equality compares fields with <c>EqualityComparer&lt;T&gt;.Default</c>,
/// which falls back to reference equality for collection types like
/// <see cref="HashSet{T}"/>. Two records whose primitive fields all
/// match but whose <see cref="KnownTilesAppliedTo"/> sets are
/// different <i>references</i> with the same content will compare
/// <b>non-equal</b>. The default initialiser uses a shared empty
/// singleton (<see cref="EmptyTileSet"/>) so a default-shaped record
/// vs another default-shaped record DO compare equal — that's the
/// dominant case for the no-op suppression gate. Callers that mutate
/// the set explicitly (the reveal projection at A4.2) must reuse
/// the prior set reference if they want the no-op gate to fire on
/// "no actual change". The recruit handler today doesn't mutate this
/// field — it lands when A4.2 wires in.
/// </para>
///
/// <para>
/// <b>Field shape note (Didier 2026-05-17 brief).</b> The Varn-locked
/// spec §D.1 lists <c>KnownTilesAppliedTo : IReadOnlySet&lt;Vector2I&gt;</c>
/// and <c>PortraitState : NpcPortraitState</c> (the Godot enum). The
/// implementation brief downgrades both to <c>string</c>-keyed shapes
/// (HashSet&lt;string&gt; for tiles ; <c>string?</c> slug for portrait)
/// so the record stays Godot-free and cherry-pickable into the xUnit
/// test host. Translation seam: callers stringify cells via
/// <c>$"{cell.X},{cell.Y}"</c> at the recruit-panel edge ; portrait
/// slugs are <c>"calm"</c> / <c>"alert"</c> / <c>"wounded"</c> matching
/// <see cref="Wayfinders.Client.Scripts.Screens.NpcPortraitState"/>
/// suffixes. The semantic schema is preserved ; only the wire types
/// shift to keep the test seam clean.
/// </para>
/// </summary>
public sealed record NpcRuntimeState
{
    /// <summary>
    /// Shared empty set used as the default value of
    /// <see cref="KnownTilesAppliedTo"/>. A single reference is reused
    /// across every default-shaped record so two default-shaped
    /// records compare equal under the record's reference-equality
    /// semantics for collection fields. Wrapped in
    /// <see cref="IReadOnlySet{T}"/> via the implicit conversion of
    /// an empty <see cref="HashSet{T}"/> ; safe to expose because no
    /// caller has a writable reference.
    /// </summary>
    private static readonly IReadOnlySet<string> EmptyTileSet = new HashSet<string>();

    /// <summary>
    /// Stable NpcId matching the
    /// <see cref="Wayfinders.Client.Scripts.Screens.NpcCatalog"/> id
    /// convention. Required (no default — every entry must declare
    /// which NPC it represents).
    /// </summary>
    public required string NpcId { get; init; }

    /// <summary>
    /// True iff the player has recruited this NPC into their
    /// compagnie. Default false. Mutated by the E2.3 recruit panel
    /// and by mission-driven recruitment events (M2).
    /// </summary>
    public bool IsRecruited { get; init; } = false;

    /// <summary>
    /// Tick at which the NPC was recruited (or null if never
    /// recruited). Used by mission grammar for "recently recruited"
    /// filters (A4.2 success_effect rules).
    /// </summary>
    public int? RecruitedAtTick { get; init; }

    /// <summary>
    /// Set of cell-keys (canonical <c>"col,row"</c> string form) whose
    /// reveal-state has already been promoted to <c>Partial</c> or
    /// <c>Revealed</c> by THIS NPC's <c>known_tiles</c> projection.
    /// Used to prevent double-applying the projection across
    /// recruit / unrecruit cycles. Empty set when not yet applied or
    /// after explicit reset.
    ///
    /// <para>
    /// <b>String key seam.</b> Cells are stringified at the recruit-
    /// panel edge via <c>$"{cell.X},{cell.Y}"</c> so the record stays
    /// Godot-free. The reveal projection (A4.2) parses the keys back
    /// into <c>Vector2I</c> at write time. Same idempotence anchor
    /// either way.
    /// </para>
    ///
    /// <para>
    /// <b>Default = shared singleton.</b> The default value is
    /// <see cref="EmptyTileSet"/>, NOT a fresh <c>new HashSet</c> per
    /// instance. This is what makes two default-shaped records
    /// compare equal under C# record equality (which falls back to
    /// reference comparison for collection fields).
    /// </para>
    /// </summary>
    public IReadOnlySet<string> KnownTilesAppliedTo { get; init; } = EmptyTileSet;

    /// <summary>
    /// Current portrait-state slug for stateful NPCs (Kira:
    /// <c>"calm"</c> / <c>"alert"</c> / <c>"wounded"</c> ; future
    /// stateful NPCs follow). Null means "use the catalog default"
    /// (stateless NPCs ignore writes anyway). Matches the suffix
    /// taxonomy from
    /// <see cref="Wayfinders.Client.Scripts.Screens.NpcPortraitStateExtensions.ToKeySuffix(Wayfinders.Client.Scripts.Screens.NpcPortraitState)"/>.
    /// </summary>
    public string? PortraitState { get; init; }

    /// <summary>
    /// Last tick at which this NPC was rendered on a visible screen.
    /// Used by future "off-screen aging" mechanics (M2 mood drift).
    /// Null if never rendered.
    /// </summary>
    public int? LastSeenTick { get; init; }
}
