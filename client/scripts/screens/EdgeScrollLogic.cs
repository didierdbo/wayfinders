namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper for the e1 grille 64x64 edge-scroll caméra (livrable
/// pan probe, 2026-05-13). Same Godot-free pattern as
/// <see cref="CameraPanLogic"/> and <see cref="MapPanInputLogic"/> : the
/// runtime <see cref="Wayfinders.Client.Scenes.Scratch.IsoMapE1Probe.IsoMapE1Probe"/>
/// resolves the cursor viewport position + the viewport rect, feeds them
/// to <see cref="ResolveEdgeDirection"/>, and gets back a unit-direction
/// vector ready for <see cref="CameraPanLogic.AdvanceCameraCenter"/>.
///
/// <para>
/// <b>Why a separate helper.</b> Edge-scroll is a third pan input source
/// after MMB-drag (event-driven, <see cref="MapPanInputLogic"/>) and ZQSD
/// (frame-polled, <see cref="CameraPanLogic.ResolvePanDirection"/>). The
/// two existing helpers are battle-tested for E2WorldMap ; rather than
/// extend their signatures with an edge-scroll mode, we add a sibling
/// that emits the same <see cref="PanVec2"/> shape and lets the runtime
/// sum the keyboard + edge-scroll directions (then re-normalise once)
/// before feeding <see cref="CameraPanLogic.AdvanceCameraCenter"/>.
/// </para>
///
/// <para>
/// <b>Margin semantics.</b> The cursor triggers edge-scroll when within
/// <see cref="DefaultEdgeMarginPx"/> pixels of any viewport edge. The
/// trigger is binary, not graduated -- speed comes from
/// <see cref="CameraPanLogic.AdvanceCameraCenter"/>'s speed argument,
/// not from how close to the edge we are. Graduated speed feels nice
/// in RTS demos but breaks the "this is the same pan as ZQSD" mental
/// model the player has on first contact. We can revisit if Didier
/// playtest flags it ; for now, parity with keyboard wins.
/// </para>
///
/// <para>
/// <b>Out-of-viewport gate.</b> If the cursor is outside the viewport
/// (e.g. on a second monitor, or off-screen during a windowed-mode
/// alt-tab), no edge-scroll fires. This avoids the classic "camera
/// runs away while I'm in Discord" RTS bug.
/// </para>
/// </summary>
public static class EdgeScrollLogic
{
    /// <summary>
    /// Distance from a viewport edge, in screen pixels, that triggers
    /// edge-scroll. Default 8 px : tight enough that an intentional
    /// mouse drift past the play area is required (not just a casual
    /// camera-panning gesture that happens to graze the edge), wide
    /// enough that a player reaching for the edge does not need pixel-
    /// perfect aim. Tuneable per call so the consumer can override
    /// per-screen if needed (e.g. tactical scene with a HUD chrome that
    /// already eats the bottom 64 px would want a different value).
    /// </summary>
    public const float DefaultEdgeMarginPx = 8f;

    /// <summary>
    /// Resolve a unit-direction pan input vector from the current cursor
    /// position + viewport rectangle. The output convention matches
    /// <see cref="CameraPanLogic.ResolvePanDirection"/> :
    /// <list type="bullet">
    ///   <item><c>X &lt; 0</c> -- pan camera left (cursor near LEFT edge).</item>
    ///   <item><c>X &gt; 0</c> -- pan camera right (cursor near RIGHT edge).</item>
    ///   <item><c>Y &lt; 0</c> -- pan camera up (cursor near TOP edge).</item>
    ///   <item><c>Y &gt; 0</c> -- pan camera down (cursor near BOTTOM edge).</item>
    /// </list>
    /// Returns <see cref="PanVec2.Zero"/> if no edge is triggered, or
    /// if the cursor is outside the viewport.
    /// </summary>
    /// <param name="cursorViewportPosition">
    /// Cursor position in viewport pixel coords (top-left = 0,0).
    /// Typically <c>GetViewport().GetMousePosition()</c>.
    /// </param>
    /// <param name="viewportSize">Viewport dimensions in pixels.</param>
    /// <param name="edgeMarginPx">
    /// Distance from edge that triggers scroll. Use
    /// <see cref="DefaultEdgeMarginPx"/> unless overriding for a
    /// HUD-aware screen.
    /// </param>
    /// <returns>A unit-direction vector along the triggered edge(s), or zero.</returns>
    public static PanVec2 ResolveEdgeDirection(
        PanVec2 cursorViewportPosition,
        PanVec2 viewportSize,
        float edgeMarginPx = DefaultEdgeMarginPx)
    {
        // Out-of-viewport gate : cursor on a second monitor, off-screen,
        // or just-released-from-the-window. No edge-scroll.
        if (cursorViewportPosition.X < 0f
            || cursorViewportPosition.Y < 0f
            || cursorViewportPosition.X > viewportSize.X
            || cursorViewportPosition.Y > viewportSize.Y)
        {
            return PanVec2.Zero;
        }

        var x = 0f;
        var y = 0f;

        if (cursorViewportPosition.X <= edgeMarginPx) x -= 1f;
        else if (cursorViewportPosition.X >= viewportSize.X - edgeMarginPx) x += 1f;

        if (cursorViewportPosition.Y <= edgeMarginPx) y -= 1f;
        else if (cursorViewportPosition.Y >= viewportSize.Y - edgeMarginPx) y += 1f;

        if (x == 0f && y == 0f) return PanVec2.Zero;

        // Normalise so a corner-trigger (both axes active) does not
        // scroll sqrt(2) faster than a pure-edge trigger. Same shape
        // as CameraPanLogic.ResolvePanDirection for the keyboard.
        var length = System.MathF.Sqrt(x * x + y * y);
        return new PanVec2(x / length, y / length);
    }

    /// <summary>
    /// Sum two unit-direction vectors and re-normalise the result.
    /// Used to fuse the keyboard direction (<see cref="CameraPanLogic.ResolvePanDirection"/>)
    /// with the edge-scroll direction (<see cref="ResolveEdgeDirection"/>)
    /// into a single unit vector. A raw sum without re-normalisation
    /// would have a player who holds both keyboard W and pushes the
    /// cursor against the top edge scroll exactly twice as fast as one
    /// alone -- which feels wrong for what is conceptually "two ways to
    /// say the same intent".
    /// </summary>
    /// <param name="a">First unit direction.</param>
    /// <param name="b">Second unit direction.</param>
    /// <returns>The unit-normalised sum, or zero if both are zero / they cancel.</returns>
    public static PanVec2 FuseUnitDirections(PanVec2 a, PanVec2 b)
    {
        var sx = a.X + b.X;
        var sy = a.Y + b.Y;
        if (sx == 0f && sy == 0f) return PanVec2.Zero;
        var length = System.MathF.Sqrt(sx * sx + sy * sy);
        return new PanVec2(sx / length, sy / length);
    }
}
