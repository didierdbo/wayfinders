namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper for the E2.2 étape 6 "socle MouseFilter follows
/// occupancy" rule (Didier UX bug 2 résiduel, 2026-05-17 soir). Companion
/// of the <c>SocleDropZone</c> drop-routing fix : because the drop zone
/// is now a Control PARENT (or sibling) of the socle that catches the
/// forward drop, the socle itself must stay TRANSPARENT to mouse events
/// while empty (so clicks pass through to the drop zone), and must
/// INTERCEPT mouse events while occupied (so the reverse-drag
/// <c>_GetDragData</c> hook fires on the socle's own
/// <see cref="Wayfinders.Client.Scenes.Ui.IsoSocketPlaceholder"/>).
///
/// <para>
/// <b>The truth table</b> (approach (a) from the brief, retained over
/// (b) because the socle is already the natural reverse-drag source --
/// delegating from the drop zone would double the source-of-truth) :
/// <code>
///   role = Decorative                              -> Ignore
///   role = CompanyHost + empty                     -> Ignore  (drop zone catches forward drop)
///   role = CompanyHost + occupied (unconfirmed)    -> Stop    (socle catches reverse drag)
///   role = CompanyHost + occupied (confirmed)      -> Stop    (socle blocks further drops ; _GetDragData returns default)
/// </code>
/// </para>
///
/// <para>
/// <b>Why pinned.</b> A regression that pins the socle to <c>Stop</c>
/// unconditionally would re-introduce the bug 2 symptom : the socle's
/// rect (240 px tall) would catch the drop instead of the drop zone
/// (488 px tall), and a top-click drag would silently refuse again.
/// A regression to <c>Ignore</c> unconditionally would kill the reverse
/// drag (the click on the occupant would pass to the drop zone, which
/// has no <c>_GetDragData</c>). The pin catches both.
/// </para>
/// </summary>
public static class SocleMouseFilterLogic
{
    /// <summary>The three MouseFilter values relevant to this seam.
    /// Mirrors <c>Godot.Control.MouseFilterEnum</c> without dragging the
    /// engine type into the test surface ; the runtime translates one
    /// way at the call site.</summary>
    public enum Filter
    {
        /// <summary>Godot.Control.MouseFilterEnum.Stop -- intercept events.</summary>
        Stop = 0,
        /// <summary>Godot.Control.MouseFilterEnum.Ignore -- pass through.</summary>
        Ignore = 1,
    }

    /// <summary>The role the socle plays. Mirrors
    /// <see cref="Wayfinders.Client.Scenes.Ui.IsoSocketPlaceholder.DropTargetRole"/>
    /// engine-free.</summary>
    public enum Role
    {
        Decorative = 0,
        CompanyHost = 1,
    }

    /// <summary>
    /// Resolve the MouseFilter the socle Control should set, given its
    /// role + current occupancy state. Pure function ; the runtime
    /// IsoSocketPlaceholder calls this on every state change (role set,
    /// occupancy set, confirmed flag set).
    /// </summary>
    public static Filter Resolve(Role role, bool occupied)
    {
        if (role == Role.Decorative) return Filter.Ignore;
        // CompanyHost :
        //   empty    -> Ignore (drop zone wins the forward route)
        //   occupied -> Stop   (socle wins the reverse-drag route)
        return occupied ? Filter.Stop : Filter.Ignore;
    }
}
