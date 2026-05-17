using Godot;
using Wayfinders.Client.Scripts.Screens;

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
///
/// <para>
/// <b>J4 double-spawn fix (2026-05-09 evening, Rune).</b>
/// A second case surfaced after J4 generalised the 3D backing : Didier
/// reported the <c>[FogTileLayer J4 CANARY] spawned 1089 cells</c> log
/// firing TWICE at boot, with two MapPan2DComponent instances live at
/// the same time -- pan stuck on the wrong WorldCamera, F debug
/// dispatching to the wrong knowledge store, every singleton-shaped piece
/// of E2 state doubled. Root cause : Didier ran <c>F6 Run Current Scene</c>
/// on <c>E1WorldMap.tscn</c> while it was open in the editor. F6
/// temporarily swaps <c>run/main_scene</c> to the active scene, but
/// autoloads (including this one) still fire. The skip_opening_bootstrap
/// meta gate above only fires for probes ; production screens have no
/// meta, so the autoload still ran <c>NavigateTo("E1_TITLE")</c>.
/// E1 rendered above the F6-launched E2, the player clicked
/// "Nouvelle Partie", and E2 was instantiated a SECOND time.
/// </para>
/// <para>
/// Fix : when the active main scene is itself a registered production
/// screen (i.e., <c>currentScene is IScreen</c>), still register every
/// screen and modal (drill / climb / POI dispatch must keep working from
/// the F6-launched screen) but SKIP the <c>NavigateTo("E1_TITLE")</c>
/// step. The decision lives in <see cref="OpeningBootstrapLogic.Decide"/>
/// (pure-C# seam, pinned by xUnit) so the contract has a regression
/// guard outside the engine.
/// </para>
/// </summary>
public partial class OpeningBootstrap : Node
{
    private const string E1ScenePath = "res://scenes/screens/E1Title.tscn";
    private const string AreaScenePath = "res://scenes/screens/E2AreaMap.tscn";
    private const string E4ScenePath = "res://scenes/screens/E4CharacterSheet.tscn";
    private const string DistrictScenePath = "res://scenes/screens/E3DistrictMap.tscn";

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
        var currentScene = GetTree().CurrentScene;

        // Fire PoiTree boot load early — independent of the
        // registration / NavigateTo path. The POI tree is a
        // referential needed by any scene that renders POI tooltips
        // (production E1 OR a scratch probe like IsoMapE1Probe). The
        // load is fire-and-forget : it tolerates the backend being
        // down (logs a warning, keeps the cache empty, the tooltip
        // aggregator degrades gracefully). See Varn-lock 2026-05-15
        // v2 §2.
        var poiTreeService = GetNodeOrNull<PoiTreeService>("/root/PoiTreeService");
        if (poiTreeService is not null)
        {
            _ = poiTreeService.LoadAsync();
        }
        else
        {
            GD.PushWarning("[OpeningBootstrap] PoiTreeService autoload missing -- POI tooltip aggregation will be empty");
        }

        var hasSkipMeta = currentScene is not null
            && currentScene.HasMeta(SkipBootstrapMetaKey)
            && (bool)currentScene.GetMeta(SkipBootstrapMetaKey);
        var isRegisteredScreen = currentScene is IScreen;

        var action = OpeningBootstrapLogic.Decide(hasSkipMeta, isRegisteredScreen);

        if (action == OpeningBootstrapAction.SkipBootstrap)
        {
            GD.Print(
                $"[OpeningBootstrap] gated by skip_opening_bootstrap on " +
                $"'{currentScene?.Name}' -- registration AND NavigateTo skipped (PoiTree load still fired)");
            return;
        }

        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        if (sceneManager is null)
        {
            GD.PushError("[OpeningBootstrap] SceneManager autoload missing -- cannot boot");
            return;
        }

        sceneManager.RegisterScreen("E1_TITLE", GD.Load<PackedScene>(E1ScenePath));
        sceneManager.RegisterScreen("E2_AREA", GD.Load<PackedScene>(AreaScenePath));
        sceneManager.RegisterScreen("E3_DISTRICT", GD.Load<PackedScene>(DistrictScenePath));

        sceneManager.RegisterModal("E4_CHARACTER_SHEET", GD.Load<PackedScene>(E4ScenePath));
        sceneManager.RegisterModal("OPTIONS_MODAL", GD.Load<PackedScene>(OptionsModalScenePath));
        sceneManager.RegisterModal("QUIT_CONFIRM_MODAL", GD.Load<PackedScene>(QuitModalScenePath));

        // Surface the user:// hot-swap root so dev knows exactly where to
        // drop overrides on Windows (%APPDATA%/Godot/app_userdata/Wayfinders/...).
        var userRootGlobal = ProjectSettings.GlobalizePath("user://wayfinders_visual_assets/");
        GD.Print($"[OpeningBootstrap] hot-swap user-root path: {userRootGlobal}");

        if (action == OpeningBootstrapAction.RegisterOnly)
        {
            // F6 / "Run Current Scene" path : the active main scene is
            // already a registered production screen. Skip NavigateTo to
            // avoid pushing E1 on top and double-instantiating the screen
            // on the next "Nouvelle Partie" click. The screen owns its
            // own _Ready -> Configure path -- nothing else for the
            // bootstrap to do.
            var screenId = currentScene is IScreen iscreen ? iscreen.ScreenId : "(unknown)";
            GD.Print(
                $"[OpeningBootstrap] screens registered, NavigateTo skipped " +
                $"(F6 path -- main scene is already a registered screen '{screenId}'). " +
                $"Drill / climb / POI dispatch are wired ; doubling guard active.");
            return;
        }

        GD.Print("[OpeningBootstrap] screens registered, booting E1");

        // Fire-and-forget: NavigateTo is async (lifecycle calls), but the
        // bootstrap is in _Ready and there is nothing meaningful to await.
        // The per-navigation cancellation token in SceneManager covers
        // robustness if E1's OnEnter throws.
        _ = sceneManager.NavigateTo("E1_TITLE");
    }
}
