using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="SocleMouseFilterLogic"/> -- the truth
/// table that drives the socle's dynamic MouseFilter based on its role
/// + occupancy state. Companion of the
/// <see cref="SocleDropZoneGeometryLogic"/> fix (Didier UX bug 2
/// résiduel, 2026-05-17 soir, option 1 of 4).
///
/// <para>
/// <b>Why pinned.</b> Two regressions silently break the étape 6
/// drag&amp;drop seam :
/// <list type="bullet">
///   <item>Stop unconditionally -> the 240-px-tall socle wins the
///         forward route over the 488-px-tall drop zone, top-click
///         drag silently refuses (= bug 2 returns).</item>
///   <item>Ignore unconditionally -> click on the occupant passes
///         through the socle to the drop zone, which has no
///         <c>_GetDragData</c> -> reverse drag dies silently.</item>
/// </list>
/// The pins make either regression surface red.
/// </para>
/// </summary>
public sealed class SocleMouseFilterLogicTests
{
    [Fact]
    public void Decorative_role_always_ignores()
    {
        // Decorative = legacy path, no drop / drag. MouseFilter stays
        // Ignore so the parent panel's hit-testing is undisturbed.
        Assert.Equal(SocleMouseFilterLogic.Filter.Ignore,
            SocleMouseFilterLogic.Resolve(
                SocleMouseFilterLogic.Role.Decorative, occupied: false));
        Assert.Equal(SocleMouseFilterLogic.Filter.Ignore,
            SocleMouseFilterLogic.Resolve(
                SocleMouseFilterLogic.Role.Decorative, occupied: true));
    }

    [Fact]
    public void CompanyHost_empty_ignores_so_drop_zone_wins_forward_route()
    {
        // The core of the option-1 fix : when the socle is empty,
        // forward drop must route through the larger SocleDropZone
        // (which catches the top-click case). The socle stays Ignore
        // so the click passes through to the drop zone parent.
        Assert.Equal(SocleMouseFilterLogic.Filter.Ignore,
            SocleMouseFilterLogic.Resolve(
                SocleMouseFilterLogic.Role.CompanyHost, occupied: false));
    }

    [Fact]
    public void CompanyHost_occupied_stops_so_socle_wins_reverse_route()
    {
        // When the socle hosts an occupant, the click on the occupant
        // must reach the socle's _GetDragData hook (reverse drag start).
        // MouseFilter = Stop intercepts the click before the drop zone
        // sees it.
        Assert.Equal(SocleMouseFilterLogic.Filter.Stop,
            SocleMouseFilterLogic.Resolve(
                SocleMouseFilterLogic.Role.CompanyHost, occupied: true));
    }

    [Fact]
    public void Reverse_drag_still_works_when_socket_is_occupied()
    {
        // Belt-and-braces alias for the bug-2-residual pin doc : the
        // "occupied -> Stop" branch is what keeps the reverse-drag
        // path alive after the option-1 fix moved _CanDropData to the
        // drop zone. A regression that flips this to Ignore kills the
        // reverse drag silently (the click would pass through the socle
        // to the drop zone, which has no _GetDragData hook).
        var filter = SocleMouseFilterLogic.Resolve(
            SocleMouseFilterLogic.Role.CompanyHost, occupied: true);
        Assert.Equal(SocleMouseFilterLogic.Filter.Stop, filter);
    }
}
