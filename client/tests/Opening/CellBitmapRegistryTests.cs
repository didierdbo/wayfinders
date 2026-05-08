using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="CellBitmapRegistry{T}"/> (slice 3.5 /
/// livrable 4 — M3 / Phase 9 / 2026-05-08). Pins the per-tile bitmap
/// API contract :
/// <list type="bullet">
///   <item>Set + TryGet returns the same reference.</item>
///   <item>Clear removes the binding.</item>
///   <item>Re-set replaces without leak (Count stays at 1).</item>
///   <item>Set with null is equivalent to Clear.</item>
///   <item>TryGet on an unknown coord returns false.</item>
///   <item>ClearAll resets the registry.</item>
/// </list>
///
/// <para>
/// <b>Why these tests matter.</b> The runtime
/// <see cref="Wayfinders.Client.Components.FogTileLayer"/> exposes
/// <c>SetCellBitmap</c> / <c>ClearCellBitmap</c> / <c>GetCellBitmap</c>
/// against the per-cell <c>Polygon2D.Texture</c> field directly ; this
/// suite pins the abstract semantics that the runtime mirrors. If a
/// future slice extracts the runtime path to use the registry directly
/// (e.g. for a debug overlay listing every cell with a bitmap override),
/// the tests catch any drift in the contract.
/// </para>
///
/// <para>
/// <b>Test bitmap stand-in.</b> The runtime types the registry on
/// <c>Godot.Texture2D</c>. Tests use a plain <see cref="FakeBitmap"/>
/// reference type, since the registry only cares about reference
/// identity, not bitmap semantics. Same pattern as
/// <see cref="FogPaletteSourceTests"/>'s inline fake source.
/// </para>
/// </summary>
public sealed class CellBitmapRegistryTests
{
    [Fact]
    public void New_registry_has_zero_count()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        Assert.Equal(0, reg.Count);
    }

    [Fact]
    public void TryGet_on_empty_registry_returns_false()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var found = reg.TryGet(new GridCoord(3, 5), out var bitmap);
        Assert.False(found);
        Assert.Null(bitmap);
    }

    [Fact]
    public void Set_then_TryGet_returns_same_reference()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var bitmap = new FakeBitmap("master");
        var coord = new GridCoord(3, 5);

        reg.Set(coord, bitmap);
        var found = reg.TryGet(coord, out var retrieved);

        Assert.True(found);
        Assert.Same(bitmap, retrieved);
        Assert.Equal(1, reg.Count);
    }

    [Fact]
    public void Set_for_one_coord_does_not_affect_other_coords()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var bitmap = new FakeBitmap("a");
        reg.Set(new GridCoord(0, 0), bitmap);

        Assert.False(reg.TryGet(new GridCoord(1, 0), out _));
        Assert.False(reg.TryGet(new GridCoord(0, 1), out _));
        Assert.True(reg.TryGet(new GridCoord(0, 0), out _));
    }

    [Fact]
    public void Clear_drops_binding()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var coord = new GridCoord(3, 5);
        reg.Set(coord, new FakeBitmap("a"));

        var dropped = reg.Clear(coord);

        Assert.True(dropped);
        Assert.Equal(0, reg.Count);
        Assert.False(reg.TryGet(coord, out _));
    }

    [Fact]
    public void Clear_on_unbound_coord_is_idempotent_no_op()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var dropped = reg.Clear(new GridCoord(99, 99));
        Assert.False(dropped);
        Assert.Equal(0, reg.Count);
    }

    [Fact]
    public void Re_set_replaces_without_leak()
    {
        // Critical no-leak invariant : the registry holds the latest
        // value only. Re-setting on the same coord N times keeps Count
        // at 1, the prior reference is dropped (Godot's RefCounted
        // releases at the runtime layer ; here we just verify the
        // single-slot semantic).
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var coord = new GridCoord(3, 5);
        var b1 = new FakeBitmap("first");
        var b2 = new FakeBitmap("second");
        var b3 = new FakeBitmap("third");

        reg.Set(coord, b1);
        reg.Set(coord, b2);
        reg.Set(coord, b3);

        Assert.Equal(1, reg.Count);
        var found = reg.TryGet(coord, out var current);
        Assert.True(found);
        Assert.Same(b3, current);
    }

    [Fact]
    public void Set_with_null_bitmap_clears_the_binding()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var coord = new GridCoord(3, 5);
        reg.Set(coord, new FakeBitmap("a"));

        reg.Set(coord, null);

        Assert.Equal(0, reg.Count);
        Assert.False(reg.TryGet(coord, out _));
    }

    [Fact]
    public void Set_with_null_on_unbound_coord_is_no_op()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        reg.Set(new GridCoord(7, 7), null);
        Assert.Equal(0, reg.Count);
    }

    [Fact]
    public void Multiple_coords_have_independent_bindings()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var coordA = new GridCoord(0, 0);
        var coordB = new GridCoord(5, 5);
        var coordC = new GridCoord(9, 9);
        var bA = new FakeBitmap("A");
        var bB = new FakeBitmap("B");
        var bC = new FakeBitmap("C");

        reg.Set(coordA, bA);
        reg.Set(coordB, bB);
        reg.Set(coordC, bC);

        Assert.Equal(3, reg.Count);
        reg.TryGet(coordA, out var rA);
        reg.TryGet(coordB, out var rB);
        reg.TryGet(coordC, out var rC);
        Assert.Same(bA, rA);
        Assert.Same(bB, rB);
        Assert.Same(bC, rC);
    }

    [Fact]
    public void ClearAll_resets_registry()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        for (int i = 0; i < 10; i++)
        {
            reg.Set(new GridCoord(i, 0), new FakeBitmap($"b{i}"));
        }
        Assert.Equal(10, reg.Count);

        reg.ClearAll();

        Assert.Equal(0, reg.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.False(reg.TryGet(new GridCoord(i, 0), out _));
        }
    }

    [Fact]
    public void Clear_one_coord_leaves_others_bound()
    {
        var reg = new CellBitmapRegistry<FakeBitmap>();
        var bA = new FakeBitmap("A");
        var bB = new FakeBitmap("B");
        reg.Set(new GridCoord(0, 0), bA);
        reg.Set(new GridCoord(1, 1), bB);

        reg.Clear(new GridCoord(0, 0));

        Assert.Equal(1, reg.Count);
        Assert.False(reg.TryGet(new GridCoord(0, 0), out _));
        var found = reg.TryGet(new GridCoord(1, 1), out var retrieved);
        Assert.True(found);
        Assert.Same(bB, retrieved);
    }

    /// <summary>Reference type stand-in for Godot's Texture2D in tests.</summary>
    private sealed class FakeBitmap
    {
        public string Tag { get; }
        public FakeBitmap(string tag) => Tag = tag;
    }
}
