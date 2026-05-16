using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton that exposes the player-facing preferences to the
/// rest of the client (P8.3, M3 / Arc 3 / Phase 8.3). Wraps a pure-C#
/// <see cref="GameSettingsStore"/> with a <see cref="GodotConfigFileBackend"/>
/// adapter so xUnit can pin the persistence policy without an engine
/// while the runtime still uses Godot's native <c>ConfigFile</c> API
/// (D-P8.3-09).
///
/// <para>
/// <b>MVP surface (P8.3 §3.3 + Out-of-scope §5.2).</b> Exactly one
/// preference is wired through this autoload in MVP:
/// <see cref="MapPanButton"/>. The Volume / Plein écran / Langue stubs
/// in <see cref="Wayfinders.Client.Scenes.Modals.OptionsModal"/> stay
/// log-only as locked at J2 D1 ; this class is the rampe-de-lancement
/// for them when they become real wirings post-MVP. Adding a property
/// here will involve: (1) extending <see cref="GameSettingsStore"/>
/// with the new typed accessor, (2) extending <see cref="IConfigBackend"/>
/// if a new value type is needed (e.g. float for Volume), (3) wiring
/// the new property and forwarding the signal through.
/// </para>
///
/// <para>
/// <b>Why an autoload not a static class (P8.3 §3.3).</b>
/// <see cref="SettingsChanged"/> must be a Godot signal so consumers
/// like <see cref="Wayfinders.Client.Scenes.Screens.E1WorldMap"/> can
/// subscribe via <c>+= / -=</c> with the disconnection discipline that
/// every other Godot signal in the codebase uses. A static C# event
/// would technically work but break the pattern parity with
/// <c>SceneManager</c>, <c>AssetResolver</c>, <c>GameState</c> -- all
/// autoloads listed in <c>project.godot</c>. The autoload is the DI
/// container of the game (cf. backend-brain "DI-container reflex" trap).
/// </para>
///
/// <para>
/// <b>Idempotence + signal gate (P8.3 §3.3 D-P8.3-03).</b> The
/// <see cref="MapPanButton"/> setter delegates the actual diff and
/// persistence to <see cref="GameSettingsStore.TryWriteMapPanButton"/>
/// -- which returns false when the new value equals the cached one.
/// In that case we skip <see cref="EmitSignal(StringName, Variant[])"/>
/// entirely, so a re-selection of the currently active dropdown entry
/// does not produce a phantom <see cref="SettingsChanged"/> notification.
/// Pinned by <c>Write_same_value_twice_does_not_emit_signal_twice</c>.
/// </para>
///
/// <para>
/// <b>Lifetime.</b> Autoload registered in <c>project.godot</c> under
/// <c>[autoload]</c> as <c>GameSettings="*res://services/GameSettings.tscn"</c>
/// (D-P8.3-08, cohérent avec SceneManager / AssetResolver / GameState).
/// <see cref="_Ready"/> calls <see cref="GameSettingsStore.Load"/>, which
/// either populates from <c>user://settings.cfg</c> or stays at defaults
/// silently if the file is missing. The autoload itself lives the
/// duration of the app -- no <c>_ExitTree</c> teardown needed.
/// </para>
/// </summary>
public partial class GameSettings : Node
{
    /// <summary>
    /// Path to the persisted preferences file. Resolved by Godot's
    /// <c>user://</c> protocol to a per-user, per-platform location:
    /// on Windows that is
    /// <c>%APPDATA%/Godot/app_userdata/Wayfinders/settings.cfg</c>.
    /// INI-style, hot-editable in Notepad for debug.
    /// </summary>
    public const string SettingsPath = "user://settings.cfg";

    private GameSettingsStore _store = null!;

    /// <summary>
    /// Emitted whenever a preference value successfully changes via a
    /// setter. Idempotent writes do not fire this signal -- see
    /// <see cref="MapPanButton"/> setter for the gating logic.
    /// Subscribers must follow the disconnection discipline (subscribe
    /// in <c>_Ready</c>, unsubscribe in <c>_ExitTree</c>) ;
    /// <see cref="Wayfinders.Client.Scenes.Screens.E1WorldMap"/> is the
    /// canonical example.
    /// </summary>
    [Signal]
    public delegate void SettingsChangedEventHandler();

    /// <summary>
    /// Currently selected map-pan button. Default
    /// <see cref="Wayfinders.Client.Scripts.Screens.MapPanButton.Middle"/>
    /// until <see cref="_Ready"/> loads a persisted value (if any).
    /// Setter is idempotent ; same-value writes are a no-op and do not
    /// emit <see cref="SettingsChanged"/>.
    /// </summary>
    public MapPanButton MapPanButton
    {
        get => _store.MapPanButton;
        set
        {
            if (_store.TryWriteMapPanButton(value))
            {
                EmitSignal(SignalName.SettingsChanged);
                GD.Print($"[GameSettings] MapPanButton = {value}");
            }
        }
    }

    public override void _Ready()
    {
        _store = new GameSettingsStore(new GodotConfigFileBackend(SettingsPath));
        _store.Load();
        if (_store.LastWarning is not null)
        {
            GD.PushWarning(_store.LastWarning);
        }
        GD.Print($"[GameSettings] ready, MapPanButton = {_store.MapPanButton}");
    }
}

/// <summary>
/// Godot-side <see cref="IConfigBackend"/> adapter wrapping
/// <see cref="ConfigFile"/>. Lives next to <see cref="GameSettings"/>
/// because this is the only consumer ; if a future autoload needs the
/// same shape we will move it to its own file at that point (YAGNI).
///
/// <para>
/// <b>Why this is in the same file.</b> The store + backend pair is one
/// logical seam ; splitting the adapter into its own file would force
/// two opens for any reader trying to follow the persistence chain.
/// The adapter is small (~30 lines), Godot-bound, untestable from xUnit
/// without an engine, so it stays close to the autoload that owns it.
/// </para>
/// </summary>
public sealed class GodotConfigFileBackend : IConfigBackend
{
    private readonly string _path;
    private readonly ConfigFile _cfg = new();

    /// <summary>
    /// Construct an adapter targeting a specific <c>user://</c> path.
    /// The path is held verbatim ; <see cref="Load"/> and
    /// <see cref="Save"/> use it on every call (no path mutation
    /// supported intentionally -- changing the persistence path
    /// mid-session is a class of bug we do not want to enable).
    /// </summary>
    public GodotConfigFileBackend(string path)
    {
        _path = path;
    }

    /// <inheritdoc />
    public ConfigLoadResult Load()
    {
        var err = _cfg.Load(_path);
        if (err == Error.FileNotFound) return ConfigLoadResult.NotFound;
        if (err != Error.Ok) return ConfigLoadResult.Malformed;
        return ConfigLoadResult.Ok;
    }

    /// <inheritdoc />
    public int GetInt(string section, string key, int defaultValue)
    {
        // ConfigFile.GetValue returns Variant ; cast back to int with the
        // default to absorb a "key absent" or "wrong type" outcome. AOT-clean
        // because we never reflect over Variant -- the (int)(Variant) cast
        // path is source-generated.
        var v = _cfg.GetValue(section, key, defaultValue);
        return (int)v;
    }

    /// <inheritdoc />
    public void SetInt(string section, string key, int value)
    {
        _cfg.SetValue(section, key, value);
    }

    /// <inheritdoc />
    public void Save()
    {
        _cfg.Save(_path);
    }
}
