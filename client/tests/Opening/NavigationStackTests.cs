using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="NavigationStack"/>. Validates the stack
/// shape that <see cref="SceneManager"/> wraps with Godot-aware
/// orchestration. No Godot runtime needed — by design.
/// </summary>
public sealed class NavigationStackTests
{
    private static ScreenStackEntry Entry(string id, string? caller = null) =>
        new(id, new ScreenContext { CallerScreenId = caller });

    [Fact]
    public void Empty_stack_has_zero_count_and_null_current()
    {
        var stack = new NavigationStack();
        Assert.Equal(0, stack.Count);
        Assert.Null(stack.Current);
    }

    [Fact]
    public void Push_advances_count_and_current()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));

        Assert.Equal(1, stack.Count);
        Assert.Equal("E1_TITLE", stack.Current!.ScreenId);
    }

    [Fact]
    public void Push_then_push_keeps_top_as_current()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD", "E1_TITLE"));

        Assert.Equal(2, stack.Count);
        Assert.Equal("E1_WORLD", stack.Current!.ScreenId);
        Assert.Equal("E1_TITLE", stack.Current.Context.CallerScreenId);
    }

    [Fact]
    public void Pop_returns_top_and_decrements()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD"));

        var popped = stack.Pop();

        Assert.NotNull(popped);
        Assert.Equal("E1_WORLD", popped!.ScreenId);
        Assert.Equal(1, stack.Count);
        Assert.Equal("E1_TITLE", stack.Current!.ScreenId);
    }

    [Fact]
    public void Pop_on_empty_returns_null_no_throw()
    {
        var stack = new NavigationStack();
        var popped = stack.Pop();

        Assert.Null(popped);
        Assert.Equal(0, stack.Count);
    }

    [Fact]
    public void Contains_finds_entries_at_any_depth()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD"));
        stack.Push(Entry("E2_AREA"));

        Assert.True(stack.Contains("E1_TITLE"));
        Assert.True(stack.Contains("E1_WORLD"));
        Assert.True(stack.Contains("E2_AREA"));
        Assert.False(stack.Contains("E3_DISTRICT"));
    }

    [Fact]
    public void Snapshot_returns_bottom_to_top_independent_copy()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD"));

        var snap = stack.Snapshot();

        Assert.Equal(2, snap.Count);
        Assert.Equal("E1_TITLE", snap[0].ScreenId);
        Assert.Equal("E1_WORLD", snap[1].ScreenId);

        // Mutating the stack does not mutate the snapshot.
        stack.Pop();
        Assert.Equal(2, snap.Count);
    }

    [Fact]
    public void PopUntilThenPush_pops_above_and_target_then_pushes()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD"));
        stack.Push(Entry("E2_AREA"));

        var popped = stack.PopUntilThenPush("E1_WORLD", Entry("E3_DISTRICT"));

        Assert.Equal(2, popped.Count);
        Assert.Contains(popped, e => e.ScreenId == "E2_AREA");
        Assert.Contains(popped, e => e.ScreenId == "E1_WORLD");

        // E1 base + new E5 top.
        Assert.Equal(2, stack.Count);
        Assert.Equal("E3_DISTRICT", stack.Current!.ScreenId);
        Assert.True(stack.Contains("E1_TITLE"));
        Assert.False(stack.Contains("E1_WORLD"));
    }

    [Fact]
    public void PopUntilThenPush_unknown_target_falls_back_to_simple_push()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));

        var popped = stack.PopUntilThenPush("DOES_NOT_EXIST", Entry("E1_WORLD"));

        Assert.Empty(popped);
        Assert.Equal(2, stack.Count);
        Assert.Equal("E1_WORLD", stack.Current!.ScreenId);
    }

    [Fact]
    public void Clear_resets_to_empty()
    {
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD"));

        stack.Clear();

        Assert.Equal(0, stack.Count);
        Assert.Null(stack.Current);
    }

    [Fact]
    public void Push_then_pop_then_push_preserves_state_isolation()
    {
        // Validates that successive navigations do not bleed context
        // across entries — each push lands its own ScreenContext.
        var stack = new NavigationStack();
        stack.Push(Entry("E1_TITLE"));
        stack.Push(Entry("E1_WORLD", caller: "E1_TITLE"));
        stack.Pop();
        stack.Push(Entry("E2_AREA", caller: "E1_TITLE"));

        Assert.Equal(2, stack.Count);
        Assert.Equal("E2_AREA", stack.Current!.ScreenId);
        Assert.Equal("E1_TITLE", stack.Current.Context.CallerScreenId);
    }
}
