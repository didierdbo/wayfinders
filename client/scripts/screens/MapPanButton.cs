namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Identifies which physical mouse button drives the world-map drag-pan
/// gesture. Persisted via <see cref="Wayfinders.Client.Services.GameSettingsStore"/>
/// and read at <see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/>
/// runtime through the <see cref="Wayfinders.Client.Services.GameSettings"/>
/// autoload.
///
/// <para>
/// <b>Default and rationale (P8.3 D-P8.3-05).</b> <see cref="Middle"/> is
/// the MVP default, locked by Pax research (12 / 17 modern iso/strategy
/// titles ship MMB-pan as default) and Varn cohérence (preserves RMB for
/// the post-MVP context-menu surface on POI hover). <see cref="Right"/>
/// is the alternative for players who prefer ergonomic familiarity
/// (Sims / Battle Brothers / Civ VI) ; selecting it engages the 6 px
/// drag threshold so the same button can later carry a context-menu
/// click without leaking into pan.
/// </para>
///
/// <para>
/// <b>Why a dedicated file (P8.3 §3.2).</b> The enum is the typed seam
/// between three different sites: the persistence backend (int-cast in
/// ConfigFile), the autoload property API (typed C# accessor), and the
/// runtime input router (resolves to <c>MouseButton.Middle</c> /
/// <c>MouseButton.Right</c>). Keeping it in its own file keeps each
/// consumer's namespace import small and AOT-friendly (no extra reflection
/// surface pulled in transitively).
/// </para>
///
/// <para>
/// <b>Integer values are part of the persistence contract.</b> Do not
/// renumber. <c>Middle = 0</c> is the default ConfigFile-stored value
/// when no settings.cfg exists ; <c>Right = 1</c> is what the dropdown
/// writes when the player flips the option. Adding a third option
/// (e.g. trackpad-aware) post-MVP gets the next index (2) without
/// breaking saved preferences.
/// </para>
/// </summary>
public enum MapPanButton
{
    /// <summary>Middle mouse button (MMB). MVP default, no drag threshold.</summary>
    Middle = 0,

    /// <summary>Right mouse button (RMB). Drag threshold 6 px applies.</summary>
    Right = 1,
}
