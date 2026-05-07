using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="GameSettingsStore"/>. Pins the P8.3
/// (M3 / Arc 3 / Phase 8.3) persistence policy: defaults on fresh
/// install, roundtrip read+write, malformed file silent fallback,
/// out-of-range value clamp, idempotent same-value writes (signal
/// gate), and the warning-string surface for the autoload to forward
/// to <c>GD.PushWarning</c>.
///
/// <para>
/// <b>Test fixture pattern.</b> A <see cref="FakeConfigBackend"/> sits
/// in front of the store as the
/// <see cref="Wayfinders.Client.Services.IConfigBackend"/> seam. Each
/// test wires a fake configured for its scenario (file-not-found,
/// malformed, valid-with-value, out-of-range), and the assertions
/// pin both the cached <see cref="GameSettingsStore.MapPanButton"/>
/// after <see cref="GameSettingsStore.Load"/> and the warning string
/// surface.
/// </para>
///
/// <para>
/// <b>Why the autoload is not tested directly.</b> Same rationale as
/// SceneManager / AssetResolver / GameState: a <see cref="Godot.Node"/>
/// cannot be instantiated outside the engine. The Godot-bound side
/// (<c>GameSettings</c> autoload, <c>GodotConfigFileBackend</c>) is
/// validated through the P8.3 manual checklist (12 steps, fresh-install
/// boot, persistence inter-sessions). The pure store + interface seam
/// pinned here covers every observable persistence-policy bug surface.
/// </para>
/// </summary>
public sealed class GameSettingsStoreTests
{
    /// <summary>
    /// Fake <see cref="IConfigBackend"/> that lets tests drive any of
    /// the four observable backend states: file missing, file malformed,
    /// file valid with a specific stored value, file valid but with an
    /// out-of-range stored value. Also tracks Save calls so the
    /// idempotence test can assert that a no-op write does not flush
    /// to "disk".
    /// </summary>
    private sealed class FakeConfigBackend : IConfigBackend
    {
        public ConfigLoadResult LoadResult { get; set; } = ConfigLoadResult.NotFound;
        public Dictionary<string, int> Values { get; } = new();
        public int SaveCallCount { get; private set; }

        public ConfigLoadResult Load() => LoadResult;

        public int GetInt(string section, string key, int defaultValue)
        {
            var fullKey = $"{section}/{key}";
            return Values.TryGetValue(fullKey, out var v) ? v : defaultValue;
        }

        public void SetInt(string section, string key, int value)
        {
            Values[$"{section}/{key}"] = value;
        }

        public void Save() => SaveCallCount++;
    }

    [Fact]
    public void Defaults_when_no_file_returns_middle_no_warning()
    {
        // Fresh install: settings.cfg does not exist yet. The store
        // sits at the default Middle and emits no warning -- the
        // first launch is the common case and a warning would be noise.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.NotFound };
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Middle, store.MapPanButton);
        Assert.Null(store.LastWarning);
    }

    [Fact]
    public void Roundtrip_write_then_read_returns_same_value()
    {
        // Write Right -- subsequent Load sees the persisted value. This
        // is the "user toggles, quits, relaunches" path. The fake
        // backend is shared between the two store instances to simulate
        // disk persistence.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Ok };
        var store1 = new GameSettingsStore(backend);
        store1.Load();
        var changed = store1.TryWriteMapPanButton(MapPanButton.Right);
        Assert.True(changed);

        // Simulate relaunch: new store over the same backend, Load
        // reads back what was persisted.
        var store2 = new GameSettingsStore(backend);
        store2.Load();

        Assert.Equal(MapPanButton.Right, store2.MapPanButton);
    }

    [Fact]
    public void Malformed_file_falls_back_to_defaults_with_warning()
    {
        // Corrupted user://settings.cfg. The store stays at the
        // Middle default and surfaces a warning string for the
        // autoload to push to GD.PushWarning. The user is not
        // bothered ; the dev sees the issue in the editor console.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Malformed };
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Middle, store.MapPanButton);
        Assert.NotNull(store.LastWarning);
        Assert.Contains("malformed", store.LastWarning);
    }

    [Fact]
    public void Out_of_range_enum_value_falls_back_to_middle_with_warning()
    {
        // settings.cfg parses fine but contains a bogus int (e.g.
        // schema-incompatible after a hand-edit). Clamp to Middle and
        // log a warning. Pinned by Risk #3 mitigation in the pre-brief.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Ok };
        backend.Values["controls/map_pan_button"] = 99;
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Middle, store.MapPanButton);
        Assert.NotNull(store.LastWarning);
        Assert.Contains("out-of-range", store.LastWarning);
    }

    [Fact]
    public void Negative_value_is_treated_as_out_of_range()
    {
        // -1 is not a valid MapPanButton even though it is an int. Same
        // clamp + warning path as values > 1.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Ok };
        backend.Values["controls/map_pan_button"] = -1;
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Middle, store.MapPanButton);
        Assert.NotNull(store.LastWarning);
    }

    [Fact]
    public void Valid_zero_loads_as_middle()
    {
        // 0 is the canonical Middle int. Boundary case for the range
        // check (the >=0 lower bound).
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Ok };
        backend.Values["controls/map_pan_button"] = 0;
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Middle, store.MapPanButton);
        Assert.Null(store.LastWarning);
    }

    [Fact]
    public void Valid_one_loads_as_right()
    {
        // 1 is the canonical Right int. Boundary case for the range
        // check (the <=1 upper bound).
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Ok };
        backend.Values["controls/map_pan_button"] = 1;
        var store = new GameSettingsStore(backend);

        store.Load();

        Assert.Equal(MapPanButton.Right, store.MapPanButton);
        Assert.Null(store.LastWarning);
    }

    [Fact]
    public void Write_same_value_twice_does_not_resave()
    {
        // Idempotence gate: the autoload reads the return value of
        // TryWriteMapPanButton to decide whether to fire SettingsChanged.
        // A re-selection of the currently selected dropdown entry must
        // be a no-op end to end. We pin this by asserting the backend's
        // SaveCallCount.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.NotFound };
        var store = new GameSettingsStore(backend);
        store.Load();

        var first = store.TryWriteMapPanButton(MapPanButton.Right);
        var second = store.TryWriteMapPanButton(MapPanButton.Right);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, backend.SaveCallCount);
    }

    [Fact]
    public void Write_persists_to_backend()
    {
        // The store must call IConfigBackend.SetInt + Save on a real
        // value change, so the next Load sees the new value. Pins the
        // persistence-on-write contract.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.NotFound };
        var store = new GameSettingsStore(backend);
        store.Load();

        store.TryWriteMapPanButton(MapPanButton.Right);

        Assert.Equal(1, backend.Values["controls/map_pan_button"]);
        Assert.Equal(1, backend.SaveCallCount);
    }

    [Fact]
    public void Successive_loads_clear_previous_warning()
    {
        // Defense doc: a malformed-then-fresh sequence must not carry
        // the warning into the second Load. LastWarning is per-load,
        // not cumulative.
        var backend = new FakeConfigBackend { LoadResult = ConfigLoadResult.Malformed };
        var store = new GameSettingsStore(backend);
        store.Load();
        Assert.NotNull(store.LastWarning);

        backend.LoadResult = ConfigLoadResult.NotFound;
        store.Load();

        Assert.Null(store.LastWarning);
    }

    [Fact]
    public void Constants_section_and_key_are_stable()
    {
        // Pin the persistence-format constants so a future rename
        // breaks tests rather than silently invalidating saved
        // preferences. ConfigFile sections + keys are part of the
        // version-stable user-facing contract.
        Assert.Equal("controls", GameSettingsStore.ControlsSection);
        Assert.Equal("map_pan_button", GameSettingsStore.MapPanButtonKey);
    }

    [Fact]
    public void MapPanButton_enum_integer_values_are_stable()
    {
        // The persistence stores int casts of MapPanButton, so the int
        // values are part of the saved-data contract. A renumbering
        // would silently invalidate every saved preference. Pinned
        // here so a refactor surfaces in tests.
        Assert.Equal(0, (int)MapPanButton.Middle);
        Assert.Equal(1, (int)MapPanButton.Right);
    }
}
