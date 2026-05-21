using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="MapViewportPanLogic"/> — the J3a
/// map-viewport pan composition. Pins the contract that turns a stream of
/// mouse press / motion / release events into a clamped camera centre.
///
/// <para>
/// <b>What this suite owns, and what it does not.</b> The rectangular
/// clamp itself is pinned by <c>CameraPanLogicTests</c>; the three-state
/// drag machine is pinned by <c>MapPanInputLogicTests</c>.
/// <see cref="MapViewportPanLogic"/> owns neither — it owns the
/// <i>composition</i>: delta-from-press, the inverse "grab the world"
/// sign, and the routing of the result through the clamp. This suite
/// therefore tests the composition behaviour (a press alone moves
/// nothing; motion without a press moves nothing; a drag pulls the camera
/// the opposite way the cursor goes; a drag past the map edge is clamped)
/// and trusts the two underlying helpers' own suites for clamp maths and
/// state transitions.
/// </para>
///
/// <para>
/// <b>Coordinate convention.</b> All positions are <see cref="PanVec2"/>
/// in pixels. Mouse positions are in the SubViewport coordinate space;
/// camera centres are world coords with the maquette content occupying
/// <c>[0, contentSize]</c>. The fixtures use a 2000×1200 content and a
/// 1380×900 viewport (the J3a <c>GameScreen.tscn</c> shape), so the valid
/// camera-centre rectangle is [690, 1310] × [450, 750].
/// </para>
/// </summary>
public sealed class MapViewportPanLogicTests
{
    // J3a GameScreen.tscn maquette shape.
    private static readonly PanVec2 Content  = new(2000f, 1200f);
    private static readonly PanVec2 Viewport = new(1380f, 900f);

    // The camera-centre clamp rectangle derived from the fixtures:
    //   X in [690, 1310], Y in [450, 750].
    private const float MinCx = 690f;
    private const float MaxCx = 1310f;
    private const float MinCy = 450f;
    private const float MaxCy = 750f;

    [Fact]
    public void Press_alone_does_not_move_the_camera()
    {
        var pan = new MapViewportPanLogic();

        var outcome = pan.OnPress(new PanVec2(100f, 100f), new PanVec2(1000f, 600f));

        Assert.False(outcome.CameraMoved);
        Assert.Equal(new PanVec2(1000f, 600f), outcome.Centre);
        Assert.Equal(MapPanState.Dragging, pan.State); // MMB enters drag on press.
    }

    [Fact]
    public void Motion_without_a_press_is_a_no_op()
    {
        var pan = new MapViewportPanLogic();

        // No OnPress -> still Idle -> motion must not move the camera.
        var outcome = pan.OnMotion(new PanVec2(300f, 300f), Content, Viewport);

        Assert.False(outcome.CameraMoved);
        Assert.Equal(MapPanState.Idle, pan.State);
    }

    [Fact]
    public void Dragging_the_cursor_right_pulls_the_camera_left()
    {
        // "Grab the world" feel: moving the cursor +X drags the map with
        // it, which means the camera centre moves -X.
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));

        // Cursor moves 80 px right, 50 px down.
        var outcome = pan.OnMotion(new PanVec2(580f, 550f), Content, Viewport);

        Assert.True(outcome.CameraMoved);
        Assert.Equal(1000f - 80f, outcome.Centre.X); // camera pulled left
        Assert.Equal(600f - 50f, outcome.Centre.Y);  // camera pulled up
    }

    [Fact]
    public void Drag_delta_is_measured_from_the_press_point_not_incrementally()
    {
        // Anti-drift: two motion events report the FULL delta from press,
        // not a frame-to-frame increment. A dropped/coalesced event
        // therefore cannot make the pan drift.
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));

        pan.OnMotion(new PanVec2(540f, 500f), Content, Viewport); // +40 so far
        var second = pan.OnMotion(new PanVec2(560f, 500f), Content, Viewport); // +60 from press

        // Camera centre reflects the full 60 px delta from press, not
        // 40 + 60 = 100.
        Assert.Equal(1000f - 60f, second.Centre.X);
        Assert.Equal(600f, second.Centre.Y);
    }

    [Fact]
    public void Drag_past_the_left_edge_is_clamped_to_the_content_bound()
    {
        // Camera starts at the left clamp limit; dragging the cursor
        // further right would pull the camera below MinCx -> clamp holds.
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(MinCx, 600f));

        // A big rightward cursor move => desired camera X = MinCx - 400,
        // well past the left bound.
        var outcome = pan.OnMotion(new PanVec2(900f, 500f), Content, Viewport);

        Assert.True(outcome.CameraMoved);
        Assert.Equal(MinCx, outcome.Centre.X); // clamped, not MinCx - 400
        Assert.Equal(600f, outcome.Centre.Y);
    }

    [Fact]
    public void Drag_past_the_right_edge_is_clamped_to_the_content_bound()
    {
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(900f, 500f), new PanVec2(MaxCx, 600f));

        // Cursor moves far left => desired camera X = MaxCx + 400, past
        // the right bound.
        var outcome = pan.OnMotion(new PanVec2(500f, 500f), Content, Viewport);

        Assert.True(outcome.CameraMoved);
        Assert.Equal(MaxCx, outcome.Centre.X);
        Assert.Equal(600f, outcome.Centre.Y);
    }

    [Fact]
    public void Drag_past_the_top_and_bottom_edges_is_clamped()
    {
        var panUp = new MapViewportPanLogic();
        panUp.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, MinCy));
        // Cursor moves down => camera pulled up past MinCy.
        var up = panUp.OnMotion(new PanVec2(500f, 900f), Content, Viewport);
        Assert.Equal(MinCy, up.Centre.Y);

        var panDown = new MapViewportPanLogic();
        panDown.OnPress(new PanVec2(500f, 900f), new PanVec2(1000f, MaxCy));
        // Cursor moves up => camera pulled down past MaxCy.
        var down = panDown.OnMotion(new PanVec2(500f, 500f), Content, Viewport);
        Assert.Equal(MaxCy, down.Centre.Y);
    }

    [Fact]
    public void Release_ends_the_drag_and_does_not_move_the_camera()
    {
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));
        pan.OnMotion(new PanVec2(560f, 500f), Content, Viewport);

        var outcome = pan.OnRelease(new PanVec2(940f, 600f));

        Assert.False(outcome.CameraMoved);
        Assert.Equal(new PanVec2(940f, 600f), outcome.Centre);
        Assert.Equal(MapPanState.Idle, pan.State);
    }

    [Fact]
    public void Motion_after_release_is_a_no_op()
    {
        // Once the drag has ended, stray motion must not keep panning.
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));
        pan.OnRelease(new PanVec2(1000f, 600f));

        var outcome = pan.OnMotion(new PanVec2(700f, 700f), Content, Viewport);

        Assert.False(outcome.CameraMoved);
        Assert.Equal(MapPanState.Idle, pan.State);
    }

    [Fact]
    public void Reset_cuts_a_live_drag_back_to_idle()
    {
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));
        Assert.Equal(MapPanState.Dragging, pan.State);

        pan.Reset();

        Assert.Equal(MapPanState.Idle, pan.State);
        // After reset, motion no longer pans.
        var outcome = pan.OnMotion(new PanVec2(560f, 500f), Content, Viewport);
        Assert.False(outcome.CameraMoved);
    }

    [Fact]
    public void A_second_drag_after_release_pans_from_its_own_press_anchor()
    {
        // The press anchors must be re-captured per drag, not carried
        // over from a previous gesture.
        var pan = new MapViewportPanLogic();

        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(1000f, 600f));
        pan.OnMotion(new PanVec2(600f, 500f), Content, Viewport);
        pan.OnRelease(new PanVec2(900f, 600f));

        // Second drag starts fresh from a new camera position.
        pan.OnPress(new PanVec2(200f, 200f), new PanVec2(900f, 600f));
        var outcome = pan.OnMotion(new PanVec2(230f, 200f), Content, Viewport);

        // Delta is 30 px from the SECOND press, applied to the SECOND
        // camera start — not contaminated by the first gesture.
        Assert.Equal(900f - 30f, outcome.Centre.X);
        Assert.Equal(600f, outcome.Centre.Y);
    }

    [Fact]
    public void ClampToContent_corrects_an_out_of_bounds_start_position()
    {
        // The runtime calls this once at _Ready. A start authored past
        // the bound must snap back inside.
        var beyondLeft = MapViewportPanLogic.ClampToContent(
            new PanVec2(-500f, 600f), Content, Viewport);
        Assert.Equal(MinCx, beyondLeft.X);
        Assert.Equal(600f, beyondLeft.Y);

        var inside = MapViewportPanLogic.ClampToContent(
            new PanVec2(1000f, 600f), Content, Viewport);
        Assert.Equal(new PanVec2(1000f, 600f), inside);
    }

    [Fact]
    public void Content_smaller_than_viewport_snaps_that_axis_to_the_centre()
    {
        // Degenerate maquette: content narrower than the viewport on X.
        // The pan has no room on X, so the clamp snaps X to the content
        // centre (letterbox) — delegated to CameraPanLogic, asserted here
        // so the J3a composition is known to honour the edge case.
        var smallContent = new PanVec2(800f, 1200f); // 800 < 1380 viewport.X
        var pan = new MapViewportPanLogic();
        pan.OnPress(new PanVec2(500f, 500f), new PanVec2(400f, 600f));

        var outcome = pan.OnMotion(new PanVec2(700f, 500f), smallContent, Viewport);

        Assert.True(outcome.CameraMoved);
        Assert.Equal(400f, outcome.Centre.X); // 800 / 2, snapped to centre
    }

    [Fact]
    public void Pan_button_is_locked_to_the_middle_mouse_button()
    {
        // Consistency lock: the J3a maquette pans with the same gesture
        // as the P8.3 World Map. MMB keeps the left button free for the
        // selection milestones (J3c-2, J6, J8).
        Assert.Equal(MapPanButton.Middle, MapViewportPanLogic.PanButton);
    }
}
