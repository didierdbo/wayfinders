using System.Collections.Generic;
using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton holding cross-screen persisted state. Pre-brief §4.7.
///
/// <para>
/// J1 scope is intentionally tiny:
/// <list type="bullet">
///   <item><see cref="HasSave"/> — controls the [02] Continuer button on E1.</item>
///   <item><see cref="LastVisitedScreenId"/> — what Continuer points at (J3+ populated).</item>
///   <item><see cref="LocationContext"/> — current city/district context (J3+ populated).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why an autoload, not static.</b> Same rationale as
/// <see cref="ApiClient"/>: the autoload lives in the scene tree, can be
/// remote-inspected, has predictable _Ready/_ExitTree lifetimes, and
/// survives scene reloads naturally. Static state would survive too but
/// without engine-driven teardown — opaque.
/// </para>
///
/// <para>
/// <b>Save persistence is hors-scope MVP (Varn §7).</b> J1 keeps the API
/// shape but does not write to disk ; HasSave starts false and can be
/// flipped via a debug button if needed.
/// </para>
/// </summary>
public partial class GameState : Node
{
    /// <summary>
    /// True if a save exists. J1: always false on boot, no persistence.
    /// J3+: replaced with a real save-system check.
    /// </summary>
    public bool HasSave { get; set; } = false;

    /// <summary>
    /// Screen id to return to when [02] Continuer is pressed. Null if no
    /// save. J1 stub.
    /// </summary>
    public string? LastVisitedScreenId { get; set; }

    /// <summary>
    /// Free-form bag of cross-screen state. J1: empty. J3+ keys include
    /// "current_city_id", "current_district_id", "selected_pnj_id", etc.
    /// Same rationale as <see cref="Wayfinders.Client.Scripts.Screens.ScreenContext"/> —
    /// concrete-first, promote to typed properties when usage becomes clear.
    /// </summary>
    public Dictionary<string, object> LocationContext { get; } = new();

    public override void _Ready()
    {
        GD.Print("[GameState] ready");
    }

    public override void _ExitTree()
    {
        GD.Print("[GameState] disposed");
    }
}
