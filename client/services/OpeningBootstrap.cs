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
/// </summary>
public partial class OpeningBootstrap : Node
{
    private const string E1ScenePath = "res://scenes/screens/E1Title.tscn";
    private const string E2ScenePath = "res://scenes/screens/E2WorldMap.tscn";
    private const string E3ScenePath = "res://scenes/screens/E3CityHalfgate.tscn";
    private const string E4ScenePath = "res://scenes/screens/E4CharacterSheet.tscn";
    private const string E5ScenePath = "res://scenes/screens/E5DistrictMap.tscn";

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
        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        if (sceneManager is null)
        {
            GD.PushError("[OpeningBootstrap] SceneManager autoload missing — cannot boot");
            return;
        }

        sceneManager.RegisterScreen("E1_TITLE", GD.Load<PackedScene>(E1ScenePath));
        sceneManager.RegisterScreen("E2_WORLD", GD.Load<PackedScene>(E2ScenePath));
        sceneManager.RegisterScreen("E3_CITY_HALFGATE", GD.Load<PackedScene>(E3ScenePath));
        sceneManager.RegisterScreen("E5_DISTRICT", GD.Load<PackedScene>(E5ScenePath));
        sceneManager.RegisterModal("E4_CHARACTER_SHEET", GD.Load<PackedScene>(E4ScenePath));

        GD.Print("[OpeningBootstrap] screens registered, booting E1");

        // Fire-and-forget: NavigateTo is async (lifecycle calls), but the
        // bootstrap is in _Ready and there is nothing meaningful to await.
        // The per-navigation cancellation token in SceneManager covers
        // robustness if E1's OnEnter throws.
        _ = sceneManager.NavigateTo("E1_TITLE");
    }
}
