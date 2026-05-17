using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the typed <see cref="LocationContextState"/> record shape locked
/// by Varn 2026-05-17 (consolidated spec Section B ; I-23). Replaces the
/// free-form <c>Dictionary&lt;string, object&gt; LocationContext</c>
/// that lived on <c>GameState</c> with a 4-field nullable-string record.
///
/// <para>
/// <b>What this suite pins (Godot-free).</b>
/// <list type="bullet">
///   <item><see cref="LocationContextState.Empty"/> defaults all four
///         fields to null.</item>
///   <item><c>with</c>-expression mutation produces a NEW snapshot
///         (record immutability) ; the original instance is untouched.</item>
///   <item>Each field can be mutated independently via the
///         <c>with</c>-expression without disturbing the others.</item>
///   <item>Record value equality : two snapshots with the same four
///         field values compare equal (this is the property the
///         <c>GameState</c> setters rely on for "did this setter
///         actually change anything ?" no-op suppression).</item>
///   <item>Setting a field back to its current value (via
///         <c>with</c>-expr) produces a snapshot equal to the original
///         — the property that makes the no-op signal suppression on
///         <c>GameState.SetCurrentCityId(null)</c> when already null
///         provably correct.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this suite does NOT pin.</b> The Node-bound signal-firing
/// behaviour on <c>GameState</c> itself : signal emission is a Godot
/// runtime concern (<c>EmitSignal</c> needs the engine). Same rationale
/// as <see cref="ScreenScopedCtsContractTests"/> (which pins the CTS
/// shape, not the live ApiClient HTTP call) and the existing
/// <c>SetTileRevealState</c> path (Node-bound, signal-tested via F5
/// integration smoke). The record contract pinned here is sufficient
/// for the no-op suppression on the autoload to be correct by
/// construction.
/// </para>
/// </summary>
public sealed class LocationContextStateTests
{
    [Fact]
    public void Empty_defaults_all_four_fields_to_null()
    {
        // Locked default — readers MUST tolerate null. A fresh boot,
        // or a player on E1 with no city focus yet, sees Empty.
        var empty = LocationContextState.Empty;

        Assert.Null(empty.CurrentCityId);
        Assert.Null(empty.CurrentDistrictId);
        Assert.Null(empty.SelectedNpcId);
        Assert.Null(empty.SelectedPoiId);
    }

    [Fact]
    public void With_expression_on_CurrentCityId_mutates_only_that_field()
    {
        // Each setter on GameState calls "LocationContext with { Field = ... }"
        // and only that one field changes. The record's positional /
        // init-only shape gives this for free ; pin it explicitly so a
        // future refactor that adds non-record state to the snapshot
        // (e.g. a List<>) surfaces here.
        var snap = LocationContextState.Empty with { CurrentCityId = "halfgate" };

        Assert.Equal("halfgate", snap.CurrentCityId);
        Assert.Null(snap.CurrentDistrictId);
        Assert.Null(snap.SelectedNpcId);
        Assert.Null(snap.SelectedPoiId);
    }

    [Fact]
    public void With_expression_on_CurrentDistrictId_mutates_only_that_field()
    {
        var snap = LocationContextState.Empty with { CurrentDistrictId = "gateway" };

        Assert.Null(snap.CurrentCityId);
        Assert.Equal("gateway", snap.CurrentDistrictId);
        Assert.Null(snap.SelectedNpcId);
        Assert.Null(snap.SelectedPoiId);
    }

    [Fact]
    public void With_expression_on_SelectedNpcId_mutates_only_that_field()
    {
        var snap = LocationContextState.Empty with { SelectedNpcId = "kira" };

        Assert.Null(snap.CurrentCityId);
        Assert.Null(snap.CurrentDistrictId);
        Assert.Equal("kira", snap.SelectedNpcId);
        Assert.Null(snap.SelectedPoiId);
    }

    [Fact]
    public void With_expression_on_SelectedPoiId_mutates_only_that_field()
    {
        var snap = LocationContextState.Empty with { SelectedPoiId = "e2.halfgate.gateway" };

        Assert.Null(snap.CurrentCityId);
        Assert.Null(snap.CurrentDistrictId);
        Assert.Null(snap.SelectedNpcId);
        Assert.Equal("e2.halfgate.gateway", snap.SelectedPoiId);
    }

    [Fact]
    public void Empty_is_immutable_after_With_expression()
    {
        // The whole point of using a record (not a mutable class) is
        // that "LocationContext with { Field = newValue }" returns a
        // new instance and leaves the original intact. The GameState
        // setter pattern depends on this property : it captures the
        // old field, mutates the snapshot, and emits the signal with
        // the captured old value — the value cannot have leaked into
        // a shared mutable.
        var original = LocationContextState.Empty;
        _ = original with { CurrentCityId = "halfgate" };

        Assert.Null(original.CurrentCityId);
        Assert.Same(LocationContextState.Empty, original);
    }

    [Fact]
    public void Two_snapshots_with_same_fields_are_equal()
    {
        // Record value equality is what the no-op signal suppression on
        // GameState.SetCurrentCityId relies on : if the old field equals
        // the new value, the setter returns early and no signal fires.
        // Pin the equality semantics here so a future record-to-class
        // refactor that breaks equality is caught by the test, not by
        // a flood of duplicate signal emissions in production.
        var a = LocationContextState.Empty with
        {
            CurrentCityId = "halfgate",
            CurrentDistrictId = "gateway",
            SelectedNpcId = "kira",
            SelectedPoiId = "e2.halfgate.gateway",
        };
        var b = LocationContextState.Empty with
        {
            CurrentCityId = "halfgate",
            CurrentDistrictId = "gateway",
            SelectedNpcId = "kira",
            SelectedPoiId = "e2.halfgate.gateway",
        };

        Assert.Equal(a, b);
    }

    [Fact]
    public void Snapshots_with_different_fields_are_not_equal()
    {
        // Symmetric to the equality test : a different field value
        // breaks equality. The setter's "old != new" gate must FIRE
        // the signal in this case.
        var a = LocationContextState.Empty with { CurrentCityId = "halfgate" };
        var b = LocationContextState.Empty with { CurrentCityId = "other_city" };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Setting_field_to_same_value_via_with_is_equal_to_original()
    {
        // Validates the GameState setter's no-op suppression by
        // construction : if a caller does SetCurrentCityId("halfgate")
        // when CurrentCityId is already "halfgate", the new record
        // would equal the old one and the signal-fire gate skips.
        var snap = LocationContextState.Empty with { CurrentCityId = "halfgate" };
        var snapAgain = snap with { CurrentCityId = "halfgate" };

        Assert.Equal(snap, snapAgain);
    }

    [Fact]
    public void Setting_field_to_null_when_already_null_is_equal_to_original()
    {
        // ClearLocationContext() calls each setter with null. When a
        // field was already null, the setter must not fire a redundant
        // signal — this test pins the record-level property that
        // backs that suppression.
        var snap = LocationContextState.Empty;
        var snapCleared = snap with { CurrentCityId = null };

        Assert.Equal(snap, snapCleared);
    }
}
