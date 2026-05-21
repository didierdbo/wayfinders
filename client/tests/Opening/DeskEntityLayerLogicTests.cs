using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskEntityLayerLogic"/> — the J3c-1bis F6
/// fix #2 desk entity-layer geometry. Pins the contract that the desk
/// pawns, hosted on an un-clipped screen-space layer (so the triangular
/// clip shader cuts only the floor, never the pawns), still land on the
/// exact screen pixels the clipped floor draws their cells at — the two
/// layers share one coordinate frame.
///
/// <para>
/// <b>What this suite owns.</b> The entity-layer origin
/// (<c>screenSize/2 - deskCameraCentre</c>), the loud failure on a
/// non-positive screen, and the round-trip identity that a desk-local
/// pixel mapped through the layer origin lands where the immobile desk
/// camera would render it. The Godot-bound wiring — creating the
/// Y-sorted DeskEntities Node2D and parenting the pawns on it — lives in
/// <c>GameScreen.ConfigureDesk</c> and is validated via the F6 smoke.
/// </para>
/// </summary>
public sealed class DeskEntityLayerLogicTests
{
    private static readonly SysVec2 ScreenSize = new(1920f, 1080f);
    private static readonly SysVec2 DeskCameraCentre = new(640f, 420f);

    [Fact]
    public void LayerOrigin_is_screen_half_minus_camera_centre()
    {
        var origin = DeskEntityLayerLogic.LayerOrigin(DeskCameraCentre, ScreenSize);

        Assert.Equal(ScreenSize.X * 0.5f - DeskCameraCentre.X, origin.X, precision: 3);
        Assert.Equal(ScreenSize.Y * 0.5f - DeskCameraCentre.Y, origin.Y, precision: 3);
    }

    [Fact]
    public void DeskLocalToScreen_puts_the_camera_centre_at_the_screen_centre()
    {
        // A desk-local point equal to the camera centre is, by definition,
        // shown at the centre of the full-screen desk viewport.
        var origin = DeskEntityLayerLogic.LayerOrigin(DeskCameraCentre, ScreenSize);

        var screen = DeskEntityLayerLogic.DeskLocalToScreen(DeskCameraCentre, origin);

        Assert.Equal(ScreenSize.X * 0.5f, screen.X, precision: 3);
        Assert.Equal(ScreenSize.Y * 0.5f, screen.Y, precision: 3);
    }

    [Fact]
    public void DeskLocalToScreen_matches_the_immobile_camera_projection()
    {
        // The clipped floor is rendered by the desk camera: a desk-local
        // point P appears on screen at P - cameraCentre + screenSize/2.
        // A pawn placed at P on the entity layer must land on that SAME
        // pixel — that is the whole "shared coordinate frame" guarantee
        // that lets the pawn sit on its floor cell while the clip differs.
        var origin = DeskEntityLayerLogic.LayerOrigin(DeskCameraCentre, ScreenSize);

        var deskLocal = new SysVec2(900f, 600f);
        var viaLayer = DeskEntityLayerLogic.DeskLocalToScreen(deskLocal, origin);
        var viaCamera = deskLocal - DeskCameraCentre + ScreenSize * 0.5f;

        Assert.Equal(viaCamera.X, viaLayer.X, precision: 3);
        Assert.Equal(viaCamera.Y, viaLayer.Y, precision: 3);
    }

    [Fact]
    public void DeskLocalToScreen_preserves_relative_offsets_between_pawns()
    {
        // The layer is a pure translation, so the on-screen vector between
        // two pawns equals the desk-local vector between their cells — the
        // formation keeps its shape, no per-pawn scale or skew.
        var origin = DeskEntityLayerLogic.LayerOrigin(DeskCameraCentre, ScreenSize);

        var pawnA = new SysVec2(800f, 500f);
        var pawnB = new SysVec2(950f, 560f);
        var screenA = DeskEntityLayerLogic.DeskLocalToScreen(pawnA, origin);
        var screenB = DeskEntityLayerLogic.DeskLocalToScreen(pawnB, origin);

        Assert.Equal(pawnB.X - pawnA.X, screenB.X - screenA.X, precision: 3);
        Assert.Equal(pawnB.Y - pawnA.Y, screenB.Y - screenA.Y, precision: 3);
    }

    [Theory]
    [InlineData(0f, 1080f)]
    [InlineData(1920f, 0f)]
    [InlineData(-1f, 1080f)]
    [InlineData(1920f, -2f)]
    public void LayerOrigin_throws_on_a_non_positive_screen(float w, float h)
    {
        // A non-positive screen dimension is a caller bug — fail loud.
        Assert.Throws<System.ArgumentException>(
            () => DeskEntityLayerLogic.LayerOrigin(DeskCameraCentre, new SysVec2(w, h)));
    }
}
