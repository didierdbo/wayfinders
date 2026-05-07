using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Per-navigation payload passed to
/// <see cref="IScreen.OnEnter"/> and <see cref="IModalOverlay.OnOpen"/>.
///
/// <para>
/// Plain C# record with a key/value bag rather than a typed hierarchy.
/// Rationale (concrete-first per pre-brief §4.5): J1 has zero per-screen
/// fields to ship. Adding a typed <c>E2WorldContext</c> /
/// <c>E3CityContext</c> hierarchy now would be 4 empty classes with one
/// field each, a textbook premature interface reflex. When a real field
/// appears (e.g. J3 needs <c>SelectedCityId</c>), promote that field to a
/// typed property on <see cref="ScreenContext"/> directly — single class
/// stays sufficient until it isn't.
/// </para>
///
/// <para>
/// <b>Provenance.</b> <see cref="CallerScreenId"/> is set automatically by
/// <see cref="Wayfinders.Client.Services.SceneManager"/> on every navigation.
/// It supports the "back to whichever screen opened me" pattern needed by
/// E4 modal (see pre-brief §5 modal example) without forcing every caller
/// to remember to pass it.
/// </para>
/// </summary>
public sealed record ScreenContext
{
    /// <summary>
    /// Identifier of the screen that initiated this navigation, or null
    /// if this is the boot navigation (no caller).
    /// </summary>
    public string? CallerScreenId { get; init; }

    /// <summary>
    /// Free-form payload bag for J1. Keys should be screen-prefixed
    /// (e.g. "E3.SelectedPnjId") to avoid collisions when J3+ starts
    /// using this for real.
    /// </summary>
    public IReadOnlyDictionary<string, object> Payload { get; init; } =
        new Dictionary<string, object>();

    /// <summary>Empty context for boot or no-data navigations.</summary>
    public static ScreenContext Empty { get; } = new();
}
