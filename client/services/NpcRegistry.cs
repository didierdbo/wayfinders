using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload Godot wrapper around the pure-C# <see cref="NpcCatalog"/>
/// helpers plus the per-NPC mutable runtime state introduced by Varn
/// I-10 (2026-05-17 consolidated spec Section D). The wrapper exists
/// for three reasons:
/// <list type="number">
///   <item>Godot autoload semantics: callers do
///         <c>GetNode&lt;NpcRegistry&gt;("/root/NpcRegistry")</c>,
///         which requires a <see cref="Node"/> subclass.</item>
///   <item>Per-NPC mutable runtime state
///         (<see cref="NpcRuntimeState"/>) needs a scene-tree-bound
///         owner so the <see cref="NpcStateChanged"/> signal flows
///         through Godot's [Signal] machinery to scene-side
///         consumers (recruit panel UI, future E4 modal portrait
///         swap, etc.).</item>
///   <item>Symmetric pattern with
///         <see cref="GameState"/> : both autoloads expose typed
///         records as the canonical mutable state, gated by setter
///         methods that fire change signals exactly once.</item>
/// </list>
///
/// <para>
/// The static catalog data + lookup logic lives in
/// <see cref="Wayfinders.Client.Scripts.Screens.NpcCatalog"/> so xUnit
/// pins that contract without spinning a Godot runtime. Same separation
/// pattern as <see cref="PoiDispatchLogic"/> +
/// <see cref="Wayfinders.Client.Data.PoiDefinition"/>. The runtime
/// state record <see cref="NpcRuntimeState"/> is itself Godot-free
/// (Varn §D field-shape note : cells stringified, portrait state as
/// slug, no Vector2I dependency) so it cherry-picks into the test
/// host and xUnit pins the record contract here too.
/// </para>
///
/// <para>
/// <b>Lifecycle of runtime state (Varn §D.4).</b> Entries are created
/// lazily : <see cref="GetOrCreateState"/> returns the existing entry
/// or materialises a fresh default-shaped one and caches it. <b>Never
/// despawned</b> : entries persist for the whole session. Memory cost
/// is bounded by <see cref="NpcCatalog.Count"/> (currently 2 ; M2
/// scales to ~30-50). M1 keeps everything in-memory only ; M2 step 3
/// snapshots to disk via the save system.
/// </para>
///
/// <para>
/// <b>Authority shift watch (Varn §D.3).</b> M1 is client-authoritative
/// over <see cref="NpcRuntimeState"/> — the recruit panel mutates
/// freely and the server's <c>WorldTickRequest</c> never reads from
/// this dict. <b>M2 step 3</b> flips this : the server becomes the
/// authority and the client receives reconciliation snapshots. Any
/// new field added before then must be forward-compat (adding fields
/// is non-breaking ; removing them is breaking but unlikely).
/// </para>
/// </summary>
public partial class NpcRegistry : Node
{
    /// <summary>
    /// Per-NpcId runtime state cache. Populated lazily by
    /// <see cref="GetOrCreateState"/> on first access ; mutated via
    /// <see cref="SetState"/> or <see cref="MutateState"/>. Never
    /// cleared during the session (Varn §D.4 lifecycle rule).
    /// </summary>
    private readonly Dictionary<string, NpcRuntimeState> _states = new();

    /// <summary>
    /// Fired after any per-NPC <see cref="NpcRuntimeState"/> mutates
    /// via <see cref="SetState"/> or <see cref="MutateState"/>. The
    /// payload is the NpcId of the entry that changed ; consumers
    /// read <see cref="GetState"/> to fetch the new snapshot.
    ///
    /// <para>
    /// <b>No-op suppression.</b> Both setters compare the new state
    /// to the cached one via record equality and skip the signal
    /// emission when they are identical (idempotent write). This
    /// matches the discipline used by
    /// <see cref="GameState.LocationContextChanged"/> and
    /// <see cref="GameState.TileRevealStateChanged"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Disconnection discipline.</b> Consumer's responsibility
    /// — the autoload outlives every screen, so a screen that
    /// subscribes in <c>_Ready</c> MUST unsubscribe in
    /// <c>_ExitTree</c> (Rune-coaching Risk #1, signal-leak trap).
    /// </para>
    /// </summary>
    [Signal]
    public delegate void NpcStateChangedEventHandler(string npcId);

    public override void _Ready()
    {
        GD.Print($"[NpcRegistry] ready, {NpcCatalog.Count} npc entries registered");
    }

    /// <summary>Display name for the NPC id, or the fallback default.</summary>
    public string DisplayNameFor(string? npcId) => NpcCatalog.LookupDisplayName(npcId);

    /// <summary>
    /// Compose the AssetResolver key for the NPC's portrait in the
    /// requested state. Stateless NPCs (Dorn, default) ignore the state
    /// argument and return the bare key base. Stateful NPCs (Kira)
    /// suffix the state lowercased.
    /// </summary>
    public string PortraitKeyFor(string? npcId, NpcPortraitState state)
        => NpcCatalog.LookupPortraitKey(npcId, state);

    /// <summary>
    /// Read the runtime state for an NpcId. Returns <c>null</c> when
    /// the NPC has never been touched (no <see cref="GetOrCreateState"/>
    /// and no <see cref="SetState"/> call has materialised an entry).
    /// Pure read, no allocation, no signal.
    ///
    /// <para>
    /// <b>Why nullable instead of returning a default snapshot.</b>
    /// Callers that want to distinguish "never touched" from
    /// "touched and reset to default" can branch on null ; callers
    /// that want a default-shaped snapshot unconditionally use
    /// <see cref="GetOrCreateState"/> instead.
    /// </para>
    /// </summary>
    public NpcRuntimeState? GetState(string npcId)
        => _states.TryGetValue(npcId, out var state) ? state : null;

    /// <summary>
    /// Read the runtime state for an NpcId, materialising a fresh
    /// default-shaped entry if none exists yet. The materialised
    /// entry is cached so subsequent calls with the same NpcId
    /// return the SAME reference (lazy-spawn pattern, Varn §D.4).
    ///
    /// <para>
    /// <b>No signal fired by GetOrCreate.</b> Lazy materialisation
    /// is a read-shaped operation : consumers reading the state
    /// have no reason to know whether the entry was already cached
    /// or just freshly minted. The signal fires only on actual
    /// mutation (<see cref="SetState"/> / <see cref="MutateState"/>).
    /// </para>
    /// </summary>
    public NpcRuntimeState GetOrCreateState(string npcId)
    {
        if (_states.TryGetValue(npcId, out var existing))
            return existing;
        var fresh = new NpcRuntimeState { NpcId = npcId };
        _states[npcId] = fresh;
        return fresh;
    }

    /// <summary>
    /// Replace the cached runtime state for
    /// <see cref="NpcRuntimeState.NpcId"/> with the provided
    /// snapshot. Emits <see cref="NpcStateChanged"/> if the new
    /// snapshot differs from the cached one (record equality) ;
    /// silent no-op otherwise.
    /// </summary>
    public void SetState(NpcRuntimeState state)
    {
        if (_states.TryGetValue(state.NpcId, out var existing) && existing == state)
            return;
        _states[state.NpcId] = state;
        EmitSignal(SignalName.NpcStateChanged, state.NpcId);
    }

    /// <summary>
    /// Apply a delegate-driven mutation to the cached runtime state
    /// for <paramref name="npcId"/>. The current snapshot is fetched
    /// (or materialised default-shaped via the
    /// <see cref="GetOrCreateState"/> path), passed to the
    /// <paramref name="mutator"/>, and the returned snapshot replaces
    /// the cached one. Emits <see cref="NpcStateChanged"/> only when
    /// the post-mutation snapshot differs from the pre-mutation one
    /// (record equality) ; silent no-op otherwise.
    ///
    /// <para>
    /// <b>Idiomatic usage.</b> Callers express the mutation as a
    /// short <c>with</c>-expression :
    /// <code>
    /// _npcRegistry.MutateState("kira", s => s with {
    ///     IsRecruited = true,
    ///     RecruitedAtTick = _currentTick,
    /// });
    /// </code>
    /// This keeps the record-immutability invariant clean and
    /// matches the <see cref="GameState.SetCurrentCityId"/> setter
    /// pattern at the same level of indirection.
    /// </para>
    /// </summary>
    /// <param name="npcId">NpcId to mutate. The mutator runs against
    /// the cached entry, or against a fresh default-shaped one if
    /// the entry has not been materialised yet.</param>
    /// <param name="mutator">Pure function that takes the current
    /// snapshot and returns the new one (typically a
    /// <c>with</c>-expression). The returned snapshot's
    /// <c>NpcId</c> MUST equal <paramref name="npcId"/> ; the
    /// implementation enforces this with an
    /// <see cref="InvalidOperationException"/> guard.</param>
    public void MutateState(string npcId, Func<NpcRuntimeState, NpcRuntimeState> mutator)
    {
        var before = GetOrCreateState(npcId);
        var after = mutator(before);
        if (after.NpcId != npcId)
        {
            throw new InvalidOperationException(
                $"[NpcRegistry.MutateState] mutator returned NpcId='{after.NpcId}' " +
                $"but the call site asked to mutate '{npcId}'. The NpcId field is the " +
                $"primary key of the cache — changing it mid-mutate would leak the old " +
                $"entry under its prior key and silently corrupt the cache.");
        }
        if (before == after) return;
        _states[npcId] = after;
        EmitSignal(SignalName.NpcStateChanged, npcId);
    }

    /// <summary>
    /// Read-only enumeration of every cached runtime state. Useful
    /// for "for each recruited NPC, do X" passes (the recruit-panel
    /// roster preview, the save snapshot in M2 step 3). Order is
    /// insertion order (the underlying dictionary preserves it in
    /// .NET 8+).
    /// </summary>
    public IReadOnlyDictionary<string, NpcRuntimeState> AllStates => _states;
}
