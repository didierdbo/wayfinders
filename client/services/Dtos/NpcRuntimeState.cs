using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Per-NPC mutable runtime state held on the
/// <see cref="Wayfinders.Client.Services.NpcRegistry"/> autoload. The
/// static catalog (<see cref="NpcCatalog"/>) holds pure data (display
/// name, portrait keys) ; this record holds the mutable gameplay state
/// that survives screen changes.
///
/// <para>
/// <b>Varn-lock 2026-05-17 Section D (I-10) + §D.7 amendment.</b> Schema
/// locked here so the E2.3 recruit panel (next milestone) can wire
/// against a real shape rather than re-inventing one at the call site.
/// The §D.7 ratification (post-Rune commit a046ce4) reversed two
/// downgrades that had landed in the initial implementation — see the
/// "Field types (Varn §D.7 ratification)" paragraph below.
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
/// the set explicitly (the reveal projection at A4.2) must reuse the
/// prior set reference if they want the no-op gate to fire on "no
/// actual change". The recruit handler today doesn't mutate this
/// field — it lands when A4.2 wires in.
/// </para>
///
/// <para>
/// <b>Field types (Varn §D.7 ratification, 2026-05-17).</b> The
/// initial implementation (commit a046ce4) had downgraded the locked
/// types to keep the record cherry-pickable into the xUnit host : the
/// cell set as <c>IReadOnlySet&lt;string&gt;</c> (keys
/// <c>"col,row"</c>), the portrait state as <c>string?</c> slug.
/// Varn reviewed and refused both downgrades : the cherry-pick
/// invariant is real, but a pure-C# value-typed mirror restores type
/// safety without breaking it. The cell set is now
/// <c>IReadOnlySet&lt;<see cref="TileCoordinate"/>&gt;</c> (a Godot-free
/// readonly record struct mirroring <c>Vector2I</c>) ; the portrait
/// state is now the <see cref="NpcPortraitState"/> enum (already
/// Godot-free in <see cref="NpcCatalog"/>'s file). See §D.7 of
/// <c>varn-mission-ml-and-invariants-consolidated-2026-05-17.md</c>
/// for the ratification record.
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
    private static readonly IReadOnlySet<TileCoordinate> EmptyTileSet = new HashSet<TileCoordinate>();

    /// <summary>
    /// Stable NpcId matching the
    /// <see cref="NpcCatalog"/> id convention. Required (no default —
    /// every entry must declare which NPC it represents).
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
    /// Set of cells whose reveal-state has already been promoted to
    /// <c>Partial</c> or <c>Revealed</c> by THIS NPC's
    /// <c>known_tiles</c> projection. Used to prevent double-applying
    /// the projection across recruit / unrecruit cycles. Empty set
    /// when not yet applied or after explicit reset.
    ///
    /// <para>
    /// <b>Godot-free seam (Varn §D.7).</b> Cells are
    /// <see cref="TileCoordinate"/> records (pure-C# mirror of
    /// <c>Vector2I</c>) so the field type stays cherry-pickable into
    /// the xUnit host. The reveal projection (A4.2) lives on the
    /// Godot-bound side and converts between <c>Vector2I</c> and
    /// <see cref="TileCoordinate"/> at the engine boundary via a
    /// one-line helper (the bridge lands with the first caller ;
    /// concrete-first, no fantôme).
    /// </para>
    ///
    /// <para>
    /// <b>Default = shared singleton.</b> The default value is
    /// <see cref="EmptyTileSet"/>, NOT a fresh <c>new HashSet</c>
    /// per instance. This is what makes two default-shaped records
    /// compare equal under C# record equality (which falls back to
    /// reference comparison for collection fields).
    /// </para>
    /// </summary>
    public IReadOnlySet<TileCoordinate> KnownTilesAppliedTo { get; init; } = EmptyTileSet;

    /// <summary>
    /// Current portrait state for stateful NPCs (Kira:
    /// <see cref="NpcPortraitState.Calm"/> /
    /// <see cref="NpcPortraitState.Alert"/> /
    /// <see cref="NpcPortraitState.Wounded"/> ; future stateful NPCs
    /// follow). Defaults to <see cref="NpcPortraitState.Calm"/> —
    /// stateless NPCs (Dorn, the fallback) effectively ignore the
    /// field because <see cref="NpcCatalog.LookupPortraitKey"/>
    /// short-circuits the state suffix when
    /// <c>HasPerStatePortraits == false</c>.
    /// </summary>
    public NpcPortraitState PortraitState { get; init; } = NpcPortraitState.Calm;

    /// <summary>
    /// Last tick at which this NPC was rendered on a visible screen.
    /// Used by future "off-screen aging" mechanics (M2 mood drift).
    /// Null if never rendered.
    /// </summary>
    public int? LastSeenTick { get; init; }
}
