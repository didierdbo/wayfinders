using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// P4 onboarding-pops autoload — placeholder for the contextual UI nudges
/// the Wayfinders MVP surfaces during the first few sessions of play.
///
/// <para>
/// <b>J1 status: noop.</b> Slot reserved per Varn rule (pre-brief §2.4).
/// Hover-triggered pops are NOT wired in J1 — no
/// <c>_input(InputEvent.MouseMotion)</c> bindings (pre-brief §4.9).
/// </para>
///
/// <para>
/// Implementation lands at jalon 3 (E2 first hotspot pops). Will expose:
/// <list type="bullet">
///   <item><c>RegisterMessage(OnboardingMessage)</c> registry of triggerable pops.</item>
///   <item><c>RequestShow(string messageId)</c> respecting cool-downs and once-per-save flags.</item>
/// </list>
/// </para>
/// </summary>
public partial class OnboardingPops : Node
{
    public override void _Ready()
    {
        GD.Print("[OnboardingPops] ready (J1 noop hook)");
    }
}
