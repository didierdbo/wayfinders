using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// Bootstrap autoload that wires the Opening Scenario screen registry
/// into <see cref="SceneManager"/> at startup, then navigates to E1.
///
/// <para>
/// <b>Why an autoload, not a main_scene script.</b> Godot's
/// <c>main_scene</c> is a single PackedScene root ; we want the first
/// thing that runs to be lookup registration, not a particular screen.
/// An autoload that runs after the others (deferred call into _Ready)
/// gives us a deterministic boot sequence: SceneManager._Ready first,
/// then OpeningBootstrap registers + pushes E1.
/// </para>
///
/// <para>
/// <b>Why _ExitTree does not unregister.</b> The autoload itself is
/// disposed last on app exit ; SceneManager goes with it. Unregistering
/// at that point would just race a disposing dictionary. Registration is
/// startup-only.
/// </para>
///
/// <para>
/// <b>J2 additions.</b> OPTIONS_MODAL and QUIT_CONFIRM_MODAL now ship as
/// real modal scenes registered alongside E4_CHARACTER_SHEET. The asset
/// resolver user-root path is also logged at boot so dev knows where to
/// drop hot-swap PNGs without a recompile.
/// </para>
///
/// <para>
/// <b>J1.1 scratch-scene gate (2026-05-09, Rune).</b>
/// Autoloads run regardless of which scene is set as <c>run/main_scene</c>.
/// When the active main scene is a scratch probe (e.g.
/// <c>Tile3DBackingProbe</c>), this autoload would still register all the
/// Opening Scenario screens AND fire <c>NavigateTo("E1_TITLE")</c>, which
/// instantiates E1's <c>BureauSprite</c> on a CanvasLayer that renders
/// independently of the probe's scene tree -- thus covering the probe's
/// 3D viewport with the cartographer's office background. Symptom:
/// "je ne vois pas de tuile, je vois juste l'image du bureau du
/// cartographe".
/// </para>
/// <para>
/// Fix: any scene whose root carries <c>metadata/skip_opening_bootstrap = true</c>
/// gates the bootstrap to a no-op (with an explicit log line so dev sees
/// the gate fired). Adding a future scratch scene = add the metadata flag
/// on its root, no code change here. The metadata absence means production
/// boot path is unchanged -- BootScene and the Opening Scenario flow get
/// the full registration + NavigateTo as before.
/// </para>
/// </summary>
public partial class OpeningBootstrap : Node
{
    private const string E1ScenePath = "res://scenes/screens/E1Title.tscn";
    private const string E2ScenePath = "res://scenes/screens/E2WorldMap.tscn";
    private const string E3ScenePath = "res://scenes/screens/E3CityHalfgate.tscn";
    private const string E4ScenePath = "res://scenes/screens/E4CharacterSheet.tscn";
    private const string E5ScenePath = "res://scenes/screens/E5DistrictMap.tscn";

    // J2 -- the two stub modals reachable from E1.
    private const string OptionsModalScenePath = "res://scenes/modals/OptionsModal.tscn";
    private const string QuitModalScenePath = "res://scenes/modals/QuitConfirmModal.tscn";

    // J1.1 scratch-scene gate -- see class docstring.
    private const string SkipBootstrapMetaKey = "skip_opening_bootstrap";

    public override void _Ready()
    {
        // Defer to give SceneManager._Ready a chance to attach the
        // modal CanvasLayer before any navigation kicks off. Godot
        // autoloads _Ready in the order they are listed in project.godot,
        // but using CallDeferred makes the dependency explicit and safe
        // against re-ordering accidents.
        CallDeferred(MethodName.WireAndBoot);
    }

    private void WireAndBoot()
    {
        // J1.1 gate: if the active main scene is a scratch probe, skip the
        // entire bootstrap sequence. The probe will own the screen entirely
        // -- no E1 background hijacking the 3D viewport.
        var currentScene = GetTree().CurrentScene;
        if (currentScene is not null && currentScene.HasMeta(SkipBootstrapMetaKey)
            && (bool)currentScene.GetMeta(SkipBootstrapMetaKey))
        {
            GD.Print(
                $"[OpeningBootstrap] gated by skip_opening_bootstrap on " +
                $"'{currentScene.Name}' -- registration AND NavigateTo skipped");
            return;
        }

        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        if (sceneManager is null)
        {
            GD.PushError("[OpeningBootstrap] SceneManager autoload missing -- cannot boot");
            return;
        }

        sceneManager.RegisterScreen("E1_TITLE", GD.Load<PackedScene>(E1ScenePath));
        sceneManager.RegisterScreen("E2_WORLD", GD.Load<PackedScene>(E2ScenePath));
        sceneManager.RegisterScreen("E3_CITY_HALFGATE", GD.Load<PackedScene>(E3ScenePath));
        sceneManager.RegisterScreen("E5_DISTRICT", GD.Load<PackedScene>(E5ScenePath));

        sceneManager.RegisterModal("E4_CHARACTER_SHEET", GD.Load<PackedScene>(E4ScenePath));
        sceneManager.RegisterModal("OPTIONS_MODAL", GD.Load<PackedScene>(OptionsModalScenePath));
        sceneManager.RegisterModal("QUIT_CONFIRM_MODAL", GD.Load<PackedScene>(QuitModalScenePath));

        GD.Print("[OpeningBootstrap] screens registered, booting E1");

        // Surface the user:// hot-swap root so dev knows exactly where to
        // drop overrides on Windows (%APPDATA%/Godot/app_userdata/Wayfinders/...).
        var userRootGlobal = ProjectSettings.GlobalizePath("user://wayfinders_visual_assets/");
        GD.Print($"[OpeningBootstrap] hot-swap user-root path: {userRootGlobal}");

        // Fire-and-forget: NavigateTo is async (lifecycle calls), but the
        // bootstrap is in _Ready and there is nothing meaningful to await.
        // The per-navigation cancellation token in SceneManager covers
        // robustness if E1's OnEnter throws.
        _ = sceneManager.NavigateTo("E1_TITLE");
    }
}
