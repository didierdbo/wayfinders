// Pin tests for RecruitTargetLayerLogic (Étape C, Rune 2026-05-23,
// roadmap J8).
//
// What this file pins :
//   1. ComputePersoCentrePx — happy path : centre + offset on the 1600x800
//      eT common-frame mesh.
//   2. ComputePersoCentrePx — degenerate mesh sizes return Vector2.Zero
//      so the caller's "ready" guard sees a consistent sentinel.
//   3. IsClickOnTarget — inside the disc returns true.
//   4. IsClickOnTarget — outside the disc returns false.
//   5. IsClickOnTarget — exactly on the radius boundary is inclusive
//      (boundary clicks land — generous PoC).
//   6. IsClickOnTarget — zero / negative radius always returns false
//      (defensive : a no-op pick window is never a hit).

using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

public sealed class RecruitTargetLayerLogicTests
{
    // ----- ComputePersoCentrePx -------------------------------------------

    [Fact]
    public void ComputePersoCentrePx_eT_common_frame_centres_and_offsets()
    {
        // The eT district common-frame size is 1600 x 800. Centre is
        // (800, 400) ; the perso foot offsets +CentreToFootOffsetYPx
        // (90 px down) so the foot lands at (800, 490).
        var meshSize = new SysVec2(1600f, 800f);

        var result = RecruitTargetLayerLogic.ComputePersoCentrePx(meshSize);

        Assert.Equal(800f, result.X);
        Assert.Equal(400f + RecruitTargetLayerLogic.CentreToFootOffsetYPx, result.Y);
    }

    [Theory]
    [InlineData(0f, 800f)]
    [InlineData(1600f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(-100f, 800f)]
    public void ComputePersoCentrePx_degenerate_mesh_returns_zero(float w, float h)
    {
        // A non-positive dimension signals "the mesh has not loaded yet"
        // or "the texture swap is in flight". The function returns
        // Vector2.Zero verbatim so the caller's readiness guard sees a
        // consistent sentinel and does not spawn the perso on a blank board.
        var result = RecruitTargetLayerLogic.ComputePersoCentrePx(new SysVec2(w, h));

        Assert.Equal(SysVec2.Zero, result);
    }

    // ----- IsClickOnTarget ------------------------------------------------

    [Fact]
    public void IsClickOnTarget_click_at_centre_is_a_hit()
    {
        var foot = new SysVec2(800f, 490f);
        var click = new SysVec2(800f, 490f);

        Assert.True(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, pickRadiusPx: 90f));
    }

    [Fact]
    public void IsClickOnTarget_click_inside_disc_is_a_hit()
    {
        var foot = new SysVec2(800f, 490f);
        // 50 px right of foot — well inside a 90 px radius.
        var click = new SysVec2(850f, 490f);

        Assert.True(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, pickRadiusPx: 90f));
    }

    [Fact]
    public void IsClickOnTarget_click_outside_disc_is_a_miss()
    {
        var foot = new SysVec2(800f, 490f);
        // 200 px right of foot — way outside a 90 px radius.
        var click = new SysVec2(1000f, 490f);

        Assert.False(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, pickRadiusPx: 90f));
    }

    [Fact]
    public void IsClickOnTarget_click_exactly_on_radius_is_inclusive()
    {
        // A click exactly at the pick radius distance is a hit — the
        // boundary is inclusive (squared distance <= radius squared).
        // Generous PoC discipline matches MissionPoiLayerLogic.PickClosestPin.
        var foot = new SysVec2(800f, 490f);
        var click = new SysVec2(890f, 490f); // exactly 90 px to the right

        Assert.True(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, pickRadiusPx: 90f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-90f)]
    public void IsClickOnTarget_zero_or_negative_radius_never_hits(float radius)
    {
        var foot = new SysVec2(800f, 490f);
        var click = new SysVec2(800f, 490f); // co-located : would hit any positive radius

        Assert.False(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, radius));
    }

    [Fact]
    public void IsClickOnTarget_offdiagonal_click_uses_euclidean_distance()
    {
        // A click 60 px right + 60 px down is ~84.85 px away — inside a
        // 90 px disc. Pin the Euclidean distance (not Manhattan or
        // Chebyshev) to anti-regression an axis-aligned shortcut.
        var foot = new SysVec2(800f, 490f);
        var click = new SysVec2(860f, 550f);

        Assert.True(RecruitTargetLayerLogic.IsClickOnTarget(click, foot, pickRadiusPx: 90f));
    }
}
