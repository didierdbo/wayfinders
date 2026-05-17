using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the <see cref="NpcRuntimeState"/> record contract locked by Varn
/// 2026-05-17 (consolidated spec Section D ; I-10) and amended by §D.7
/// the same day (post-Rune commit a046ce4 — wire types restored to
/// <see cref="TileCoordinate"/> + <see cref="NpcPortraitState"/>). This
/// record is the canonical shape of per-NPC mutable state on the
/// <c>NpcRegistry</c> autoload — the schema the E2.3 recruit panel
/// (next milestone) wires against.
///
/// <para>
/// <b>What this suite pins (Godot-free).</b>
/// <list type="bullet">
///   <item>Default-shaped entry : <c>IsRecruited=false</c>,
///         <c>RecruitedAtTick=null</c>, empty
///         <see cref="NpcRuntimeState.KnownTilesAppliedTo"/>,
///         <see cref="NpcPortraitState.Calm"/> default
///         <see cref="NpcRuntimeState.PortraitState"/>,
///         null <see cref="NpcRuntimeState.LastSeenTick"/>.</item>
///   <item><c>with</c>-expression mutation produces a NEW snapshot ;
///         the original is unchanged (record immutability).</item>
///   <item>Record value equality : two entries with the same fields
///         compare equal. Backs the <c>NpcRegistry.SetState</c> /
///         <c>MutateState</c> no-op suppression gate.</item>
///   <item>The <c>KnownTilesAppliedTo</c> set is set-typed
///         (duplicate inserts are idempotent at the
///         <see cref="HashSet{T}"/> level) and value-equal over
///         <see cref="TileCoordinate"/>.</item>
///   <item>The mutator-delegate pattern (used by
///         <c>NpcRegistry.MutateState</c>) round-trips correctly :
///         given a snapshot, a <c>with</c>-expression-shaped
///         mutator returns a new snapshot with only the targeted
///         fields changed.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this suite does NOT pin.</b> The Node-bound
/// <c>NpcStateChanged</c> signal-firing surface on <c>NpcRegistry</c>
/// itself : signal emission needs the Godot runtime. Same rationale
/// as <see cref="LocationContextStateTests"/> (record contract here ;
/// signal-fire validated via integration smoke at E2.3 recruit-panel
/// landing). Likewise the <c>Vector2I</c> bridge on the Godot-bound
/// side is exercised by the first caller that materialises it — no
/// fantôme bridge today.
/// </para>
/// </summary>
public sealed class NpcRuntimeStateTests
{
    [Fact]
    public void Default_shaped_entry_has_expected_field_values()
    {
        // Locked defaults — every NPC the player has not yet touched
        // sees this exact shape via NpcRegistry.GetOrCreateState. The
        // recruit panel reads IsRecruited=false as "show the recruit
        // button" ; the reveal projection reads
        // KnownTilesAppliedTo.Count==0 as "the idempotence anchor
        // says no tiles have been promoted yet". PortraitState
        // defaults to Calm so the E4 portrait composition has a
        // valid key suffix without a null-tolerance branch.
        var state = new NpcRuntimeState { NpcId = "kira" };

        Assert.Equal("kira", state.NpcId);
        Assert.False(state.IsRecruited);
        Assert.Null(state.RecruitedAtTick);
        Assert.Empty(state.KnownTilesAppliedTo);
        Assert.Equal(NpcPortraitState.Calm, state.PortraitState);
        Assert.Null(state.LastSeenTick);
    }

    [Fact]
    public void With_expression_on_IsRecruited_mutates_only_that_field()
    {
        // The recruit handler's canonical mutation : flip IsRecruited
        // to true and stamp the recruit tick. Pin the property that
        // other fields stay untouched (no accidental KnownTilesAppliedTo
        // reset, etc.).
        var before = new NpcRuntimeState { NpcId = "kira" };
        var after = before with { IsRecruited = true, RecruitedAtTick = 42 };

        Assert.Equal("kira", after.NpcId);
        Assert.True(after.IsRecruited);
        Assert.Equal(42, after.RecruitedAtTick);
        Assert.Empty(after.KnownTilesAppliedTo);
        Assert.Equal(NpcPortraitState.Calm, after.PortraitState);
        Assert.Null(after.LastSeenTick);
    }

    [Fact]
    public void With_expression_leaves_original_snapshot_unchanged()
    {
        // Record immutability invariant. NpcRegistry.MutateState
        // captures the pre-mutation snapshot for equality compare ;
        // a mutable record would let the mutator silently corrupt
        // the captured pre-state.
        var before = new NpcRuntimeState { NpcId = "kira" };
        _ = before with { IsRecruited = true };

        Assert.False(before.IsRecruited);
    }

    [Fact]
    public void Two_snapshots_with_same_fields_are_equal()
    {
        // Record value equality backs the no-op suppression on
        // NpcRegistry.SetState / MutateState : if the post-mutation
        // snapshot equals the pre-mutation one, the setter returns
        // early and no NpcStateChanged signal fires. Pin equality
        // explicitly so a future refactor that breaks it surfaces
        // here, not via a flood of duplicate signal fan-outs.
        var a = new NpcRuntimeState
        {
            NpcId = "kira",
            IsRecruited = true,
            RecruitedAtTick = 12,
            PortraitState = NpcPortraitState.Calm,
            LastSeenTick = 17,
        };
        var b = new NpcRuntimeState
        {
            NpcId = "kira",
            IsRecruited = true,
            RecruitedAtTick = 12,
            PortraitState = NpcPortraitState.Calm,
            LastSeenTick = 17,
        };

        Assert.Equal(a, b);
    }

    [Fact]
    public void Snapshots_with_different_fields_are_not_equal()
    {
        // Symmetric to the equality test. The setter's "before !=
        // after" gate MUST fire the signal in this case.
        var a = new NpcRuntimeState { NpcId = "kira", IsRecruited = false };
        var b = new NpcRuntimeState { NpcId = "kira", IsRecruited = true };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void KnownTilesAppliedTo_is_set_typed_idempotent_insertion()
    {
        // Set semantics : duplicate inserts of the same TileCoordinate
        // are no-ops at the HashSet level (record-struct value
        // equality powers the de-duplication). The reveal projection
        // (A4.2) relies on this : when a mission re-applies the same
        // NPC's known_tiles, the set already contains the coords, and
        // the projection skips the already-promoted cells.
        var tiles = new HashSet<TileCoordinate> { new(3, 4) };
        tiles.Add(new TileCoordinate(3, 4));
        tiles.Add(new TileCoordinate(3, 4));

        Assert.Single(tiles);
        Assert.Contains(new TileCoordinate(3, 4), tiles);
    }

    [Fact]
    public void KnownTilesAppliedTo_round_trips_via_with_expression()
    {
        // Pin that a with-expression that swaps the KnownTilesAppliedTo
        // set produces a new snapshot whose set holds exactly the
        // expected entries. This is the shape the reveal projection
        // takes when it appends a freshly-promoted cell to the set
        // (creating a new HashSet that includes the prior coords + the
        // new one).
        var before = new NpcRuntimeState { NpcId = "kira" };
        var after = before with
        {
            KnownTilesAppliedTo = new HashSet<TileCoordinate>
            {
                new(3, 4),
                new(5, 6),
            },
        };

        Assert.Equal(2, after.KnownTilesAppliedTo.Count);
        Assert.Contains(new TileCoordinate(3, 4), after.KnownTilesAppliedTo);
        Assert.Contains(new TileCoordinate(5, 6), after.KnownTilesAppliedTo);
        // The original is still empty — the with-expression replaced
        // the set rather than mutating the existing one.
        Assert.Empty(before.KnownTilesAppliedTo);
    }

    [Fact]
    public void Mutator_delegate_pattern_changes_only_targeted_fields()
    {
        // Pin the shape NpcRegistry.MutateState passes to its callers :
        // (NpcRuntimeState -> NpcRuntimeState). A short with-expression
        // is the idiomatic mutator ; pin that the seam preserves
        // untouched fields verbatim. This is what makes the recruit
        // handler readable :
        //
        //   _npcRegistry.MutateState("kira", s => s with {
        //       IsRecruited = true,
        //       RecruitedAtTick = _currentTick,
        //   });
        //
        // (xUnit cannot instantiate NpcRegistry here, so the test
        // exercises the delegate semantics standalone.)
        System.Func<NpcRuntimeState, NpcRuntimeState> recruitMutator =
            s => s with { IsRecruited = true, RecruitedAtTick = 7 };

        var before = new NpcRuntimeState
        {
            NpcId = "kira",
            PortraitState = NpcPortraitState.Calm,
            LastSeenTick = 3,
        };
        var after = recruitMutator(before);

        // Targeted fields changed.
        Assert.True(after.IsRecruited);
        Assert.Equal(7, after.RecruitedAtTick);
        // Untouched fields preserved verbatim.
        Assert.Equal("kira", after.NpcId);
        Assert.Equal(NpcPortraitState.Calm, after.PortraitState);
        Assert.Equal(3, after.LastSeenTick);
        Assert.Empty(after.KnownTilesAppliedTo);
    }

    [Fact]
    public void Mutator_returning_same_snapshot_compares_equal_to_before()
    {
        // No-op mutator : returns the input unchanged. Pin that
        // record equality treats the identity-mutator output as
        // equal to the input — the property that lets
        // NpcRegistry.MutateState short-circuit the signal emission
        // for no-op writes.
        System.Func<NpcRuntimeState, NpcRuntimeState> noOpMutator = s => s;
        var before = new NpcRuntimeState { NpcId = "kira", IsRecruited = true };
        var after = noOpMutator(before);

        Assert.Equal(before, after);
    }
}
