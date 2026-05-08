using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# persistence + read/write logic for the player-facing
/// <c>GameSettings</c> autoload (P8.3, M3 / Arc 3 / Phase 8.3). Hosts the
/// value cache, the load/save policy, and the value-change detection
/// gate. The <see cref="GameSettings"/> Node consumes a store instance,
/// adds the Godot-side <c>EmitSignal</c> on change, and handles the
/// autoload lifetime. Same pattern as <see cref="LadderResolutionLogic"/>
/// at P8.1: keep the testable contract Godot-free, push the engine seam
/// up to the wrapper Node.
///
/// <para>
/// <b>Why pure-C# (D-P8.3-09).</b> A <see cref="Godot.Node"/>-derived
/// type cannot be instantiated outside a Godot runtime, so xUnit cannot
/// directly construct one. Two ways to test the persistence policy:
/// (a) abstract <c>ConfigFile</c> behind an interface and mock-inject in
/// tests, or (b) extract the entire state-and-policy layer into a
/// Godot-free class consumed by the Node. This file is option (b) ;
/// <c>GameSettings.cs</c> is the thin Node wrapper. The interface in
/// <see cref="IConfigBackend"/> is the seam between the pure store and
/// the Godot <c>ConfigFile</c> implementation, so xUnit can swap a
/// fake backend for round-trip / malformed / out-of-range tests
/// without touching disk or the engine.
/// </para>
///
/// <para>
/// <b>Persistence shape (D-P8.3-01 = ConfigFile).</b> The MVP serialises
/// exactly one int (<see cref="MapPanButton"/> cast to int) under
/// <c>[controls] map_pan_button = 0|1</c>. INI-style, AOT-clean (no
/// reflection), hot-editable in Notepad for debug, no schema migration
/// needed. Future fields (Volume, Fullscreen, Language) extend the
/// same file with new keys -- this store grows by adding properties
/// and constants, not by swapping the backend.
/// </para>
///
/// <para>
/// <b>Robustness contract (D-P8.3-03 + Risk #3).</b>
/// <list type="bullet">
///   <item>File missing -&gt; defaults silently. First launch is the
///         common case ; logging a warning would be noise.</item>
///   <item>File present but unreadable / malformed -&gt; defaults
///         silently. Same rationale (do not punish the user for a
///         corrupted save).</item>
///   <item>Key present but value out-of-range -&gt; clamp to default,
///         emit a one-line warning via <see cref="LastWarning"/> so the
///         autoload can surface it through <c>GD.PushWarning</c>.</item>
///   <item>Same value written twice -&gt; <see cref="TryWriteMapPanButton"/>
///         returns false. The Node uses this to gate
///         <c>EmitSignal</c> -- avoids signal spam from idempotent
///         writes (a re-selection of the currently selected dropdown
///         entry, for instance).</item>
/// </list>
/// </para>
/// </summary>
public sealed class GameSettingsStore
{
    /// <summary>ConfigFile section name. Stable across versions.</summary>
    public const string ControlsSection = "controls";

    /// <summary>ConfigFile key for the map-pan-button preference. Stable across versions.</summary>
    public const string MapPanButtonKey = "map_pan_button";

    private readonly IConfigBackend _backend;
    private MapPanButton _mapPanButton = MapPanButton.Middle;
    private string? _lastWarning;

    /// <summary>
    /// Construct a store over the given backend. The backend is the
    /// only seam between this pure logic and the engine ; xUnit injects
    /// a <c>FakeConfigBackend</c>, the Godot autoload injects a
    /// ConfigFile-backed adapter.
    /// </summary>
    /// <param name="backend">Implementation of the load / save / read primitives.</param>
    public GameSettingsStore(IConfigBackend backend)
    {
        _backend = backend;
    }

    /// <summary>
    /// Last warning string emitted by <see cref="Load"/> or null if the
    /// last load completed clean. The Godot autoload reads this once
    /// after <see cref="Load"/> and surfaces it via
    /// <c>GD.PushWarning</c> if non-null. Cleared on successive loads
    /// (so a clean load resets the slot).
    /// </summary>
    public string? LastWarning => _lastWarning;

    /// <summary>
    /// Current cached <see cref="MapPanButton"/> preference. Defaults to
    /// <see cref="MapPanButton.Middle"/> until <see cref="Load"/> runs
    /// and finds a different value on disk.
    /// </summary>
    public MapPanButton MapPanButton => _mapPanButton;

    /// <summary>
    /// Read the persisted preferences from the backend into the cache.
    /// Idempotent ; safe to call multiple times. Does not throw on
    /// backend errors -- the cache stays at defaults and
    /// <see cref="LastWarning"/> describes the issue (or stays null
    /// if the file simply does not exist, since that is the common
    /// first-launch case).
    /// </summary>
    public void Load()
    {
        _lastWarning = null;

        var loadResult = _backend.Load();
        if (loadResult == ConfigLoadResult.NotFound)
        {
            // First launch or the user deleted settings.cfg -- defaults
            // are correct, no warning.
            _mapPanButton = MapPanButton.Middle;
            return;
        }
        if (loadResult == ConfigLoadResult.Malformed)
        {
            // Corrupted file -- be silent to the user, but record the
            // warning so the autoload can pump it once into the Godot
            // console for the dev.
            _mapPanButton = MapPanButton.Middle;
            _lastWarning = "[GameSettings] settings.cfg malformed, falling back to defaults.";
            return;
        }

        // Backend loaded fine. Read the int value with a defaulting
        // fallback (key absent or wrong type -> the default int wins).
        var raw = _backend.GetInt(ControlsSection, MapPanButtonKey, (int)MapPanButton.Middle);
        if (raw is < 0 or > 1)
        {
            _mapPanButton = MapPanButton.Middle;
            _lastWarning =
                $"[GameSettings] settings.cfg has out-of-range map_pan_button={raw}, " +
                "clamped to Middle.";
            return;
        }
        _mapPanButton = (MapPanButton)raw;
    }

    /// <summary>
    /// Try to update the cached preference and persist the new value.
    /// Returns true iff the value actually changed (and therefore was
    /// written to the backend) ; false if the new value matches the
    /// cached one (idempotent write skipped). The Godot autoload uses
    /// the return value to gate its <c>EmitSignal</c> call, so a
    /// re-selection of the currently selected dropdown entry does not
    /// fire a phantom <c>SettingsChanged</c>.
    /// </summary>
    /// <param name="newValue">The button preference to persist.</param>
    /// <returns>True iff the value changed and was written ; false if no-op.</returns>
    public bool TryWriteMapPanButton(MapPanButton newValue)
    {
        if (_mapPanButton == newValue) return false;
        _mapPanButton = newValue;
        _backend.SetInt(ControlsSection, MapPanButtonKey, (int)newValue);
        _backend.Save();
        return true;
    }
}

/// <summary>
/// Three-state outcome of a <see cref="IConfigBackend.Load"/> call. Lets
/// the store distinguish "no file yet" (silent fallback) from "file
/// exists but is corrupted" (silent fallback + log warning) from "ok".
/// </summary>
public enum ConfigLoadResult
{
    /// <summary>File loaded successfully ; subsequent <c>GetInt</c> calls are valid.</summary>
    Ok,

    /// <summary>File does not exist on disk ; first launch or deleted by the user.</summary>
    NotFound,

    /// <summary>File exists but could not be parsed ; corrupted or schema-incompatible.</summary>
    Malformed,
}

/// <summary>
/// Backend seam between <see cref="GameSettingsStore"/> (pure logic) and
/// the persistence implementation. The Godot autoload wires a
/// <see cref="GodotConfigFileBackend"/> (in <see cref="GameSettings.cs"/>),
/// xUnit wires a fake.
///
/// <para>
/// <b>Contract.</b>
/// <list type="bullet">
///   <item><see cref="Load"/> reads the backing file (if any) into
///         the backend's internal state. Subsequent
///         <see cref="GetInt"/> calls reflect that state.</item>
///   <item><see cref="GetInt"/> returns the value at <c>[section] key</c>
///         or <paramref name="defaultValue"/> if absent.</item>
///   <item><see cref="SetInt"/> writes the value into the backend's
///         internal state ; not persisted until
///         <see cref="Save"/>.</item>
///   <item><see cref="Save"/> flushes the internal state to the
///         backing file.</item>
/// </list>
/// The pair load + save is the only place that touches the disk. The
/// store is allowed to call <see cref="GetInt"/> at any time after
/// <see cref="Load"/>.
/// </para>
/// </summary>
public interface IConfigBackend
{
    /// <summary>Read the backing file. See <see cref="ConfigLoadResult"/> for the three outcomes.</summary>
    ConfigLoadResult Load();

    /// <summary>Read an int from the backend's internal state, with a fallback on absence.</summary>
    /// <param name="section">ConfigFile section (e.g. "controls").</param>
    /// <param name="key">ConfigFile key (e.g. "map_pan_button").</param>
    /// <param name="defaultValue">Returned if section/key is absent.</param>
    int GetInt(string section, string key, int defaultValue);

    /// <summary>Write an int into the backend's internal state. Not persisted until <see cref="Save"/>.</summary>
    void SetInt(string section, string key, int value);

    /// <summary>Persist the backend's internal state to the backing file.</summary>
    void Save();
}
