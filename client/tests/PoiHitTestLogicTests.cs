// PR4 — pin tests for the POI bitmap hit-test pure-C# helper.
//
// The Godot-bound PoiInputRouter wires the input cascade (mouse position,
// viewport, Y-sort iteration, SetInputAsHandled, signal emit). The pixel
// decision — "is this local pixel opaque enough to count as a hit?" —
// lives in PoiHitTestLogic and is pinned here.
//
// What this file pins :
//   1. Null alpha mask → miss (defensive, never throws).
//   2. Zero / negative dimensions → miss.
//   3. Out-of-bounds local pixel (each side) → miss.
//   4. Opaque-aligned pixel → hit.
//   5. Transparent-aligned pixel → miss.
//   6. Stride snapping : odd-parity coordinate snaps down to even, so an
//      odd pixel that visually overlaps an opaque sample still hits.
//   7. Stride snapping does NOT introduce false hits on visually
//      transparent regions (the snapped-down sample is honored).
//   8. Boundary pixel (last valid index) → hit/miss as encoded.
//   9. Pixel just outside the valid sampling grid (e.g. width not a
//      multiple of stride) → graceful miss, no IndexOutOfRange.

using System.Collections;
using Wayfinders.Client.Scripts.Poi;

namespace Wayfinders.Client.Tests;

public sealed class PoiHitTestLogicTests
{
    // Build a tiny mask deterministically. Caller passes (width, height)
    // and a predicate "is (x,y) opaque?" — we set bits only at the
    // sampling-grid positions (mirrors PoiFootprintBuilder.Build).
    private static BitArray BuildMask(
        int width,
        int height,
        System.Func<int, int, bool> isOpaque)
    {
        var mask = new BitArray(width * height);
        for (var y = 0; y < height; y += PoiHitTestLogic.SamplingStride)
        {
            for (var x = 0; x < width; x += PoiHitTestLogic.SamplingStride)
            {
                if (isOpaque(x, y))
                {
                    mask[y * width + x] = true;
                }
            }
        }
        return mask;
    }

    // ====================================================================
    // Defensive returns
    // ====================================================================

    [Fact]
    public void Null_alpha_mask_returns_miss()
    {
        var hit = PoiHitTestLogic.HitTestPoiPure(null, 100, 100, (10, 10));
        Assert.False(hit);
    }

    [Fact]
    public void Zero_width_returns_miss()
    {
        var mask = new BitArray(0);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 0, 100, (10, 10));
        Assert.False(hit);
    }

    [Fact]
    public void Zero_height_returns_miss()
    {
        var mask = new BitArray(0);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 100, 0, (10, 10));
        Assert.False(hit);
    }

    [Fact]
    public void Negative_width_returns_miss()
    {
        var mask = new BitArray(10);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, -1, 10, (0, 0));
        Assert.False(hit);
    }

    // ====================================================================
    // Out-of-bounds
    // ====================================================================

    [Fact]
    public void Local_pixel_negative_x_returns_miss()
    {
        var mask = BuildMask(20, 20, (_, _) => true);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (-1, 10));
        Assert.False(hit);
    }

    [Fact]
    public void Local_pixel_negative_y_returns_miss()
    {
        var mask = BuildMask(20, 20, (_, _) => true);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (10, -1));
        Assert.False(hit);
    }

    [Fact]
    public void Local_pixel_at_width_returns_miss()
    {
        // width=20 means valid x in [0,19] — index 20 is out of bounds.
        var mask = BuildMask(20, 20, (_, _) => true);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (20, 10));
        Assert.False(hit);
    }

    [Fact]
    public void Local_pixel_at_height_returns_miss()
    {
        var mask = BuildMask(20, 20, (_, _) => true);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (10, 20));
        Assert.False(hit);
    }

    [Fact]
    public void Local_pixel_far_out_of_bounds_returns_miss()
    {
        var mask = BuildMask(20, 20, (_, _) => true);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (9999, 9999));
        Assert.False(hit);
    }

    // ====================================================================
    // Opaque / transparent at sampling-grid coordinates
    // ====================================================================

    [Fact]
    public void Opaque_aligned_pixel_returns_hit()
    {
        // Bit set at (4, 6) only.
        var mask = BuildMask(20, 20, (x, y) => x == 4 && y == 6);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (4, 6));
        Assert.True(hit);
    }

    [Fact]
    public void Transparent_aligned_pixel_returns_miss()
    {
        // No bits set anywhere.
        var mask = BuildMask(20, 20, (_, _) => false);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (4, 6));
        Assert.False(hit);
    }

    [Fact]
    public void Origin_pixel_when_opaque_returns_hit()
    {
        var mask = BuildMask(20, 20, (x, y) => x == 0 && y == 0);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (0, 0));
        Assert.True(hit);
    }

    // ====================================================================
    // Stride snapping — the core trap defended in PoiHitTestLogic
    // ====================================================================

    [Fact]
    public void Odd_x_snaps_down_to_even_and_hits()
    {
        // Bit set at (4, 6). Cursor lands on (5, 6) (odd x). Snap → (4, 6) → hit.
        var mask = BuildMask(20, 20, (x, y) => x == 4 && y == 6);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (5, 6));
        Assert.True(hit);
    }

    [Fact]
    public void Odd_y_snaps_down_to_even_and_hits()
    {
        var mask = BuildMask(20, 20, (x, y) => x == 4 && y == 6);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (4, 7));
        Assert.True(hit);
    }

    [Fact]
    public void Both_odd_snap_down_to_even_and_hits()
    {
        var mask = BuildMask(20, 20, (x, y) => x == 4 && y == 6);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (5, 7));
        Assert.True(hit);
    }

    [Fact]
    public void Snapped_position_at_transparent_returns_miss()
    {
        // Bit set at (4, 6). Cursor at (3, 5) — snap → (2, 4) — transparent.
        var mask = BuildMask(20, 20, (x, y) => x == 4 && y == 6);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (3, 5));
        Assert.False(hit);
    }

    // ====================================================================
    // Boundary / non-multiple width
    // ====================================================================

    [Fact]
    public void Last_valid_pixel_when_opaque_returns_hit()
    {
        // width=20, height=20. Valid last-even sample at (18, 18).
        var mask = BuildMask(20, 20, (x, y) => x == 18 && y == 18);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (18, 18));
        Assert.True(hit);
    }

    [Fact]
    public void Pixel_at_19_19_snaps_to_18_18_and_hits()
    {
        // Width=20, height=20. Cursor at (19,19) — within bounds, snaps → (18,18).
        var mask = BuildMask(20, 20, (x, y) => x == 18 && y == 18);
        var hit = PoiHitTestLogic.HitTestPoiPure(mask, 20, 20, (19, 19));
        Assert.True(hit);
    }

    [Fact]
    public void Odd_width_does_not_crash_on_high_pixel()
    {
        // width=21 (not a multiple of stride). Sample grid stops at x=20.
        // Cursor at (20, 0) is within bounds and snaps to (20, 0) — which is
        // a valid index. Cursor at (21, 0) is out of bounds — miss, no throw.
        var mask = BuildMask(21, 20, (x, y) => x == 20 && y == 0);
        var hitIn = PoiHitTestLogic.HitTestPoiPure(mask, 21, 20, (20, 0));
        var hitOut = PoiHitTestLogic.HitTestPoiPure(mask, 21, 20, (21, 0));
        Assert.True(hitIn);
        Assert.False(hitOut);
    }

    // ====================================================================
    // Realistic Halfgate-ish shape (sanity)
    // ====================================================================

    [Fact]
    public void Pillar_shape_hits_pillar_misses_corner()
    {
        // Synthetic 100x100 mask : opaque vertical pillar in the middle
        // 40..60 columns, full height. Corners and outer columns transparent.
        const int w = 100, h = 100;
        var mask = BuildMask(w, h, (x, _) => x >= 40 && x <= 60);

        var pillarHit = PoiHitTestLogic.HitTestPoiPure(mask, w, h, (50, 50));
        var leftCornerHit = PoiHitTestLogic.HitTestPoiPure(mask, w, h, (4, 4));
        var rightCornerHit = PoiHitTestLogic.HitTestPoiPure(mask, w, h, (96, 96));

        Assert.True(pillarHit);
        Assert.False(leftCornerHit);
        Assert.False(rightCornerHit);
    }
}
