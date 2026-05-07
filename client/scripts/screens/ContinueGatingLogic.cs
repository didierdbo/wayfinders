namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for E1's <c>[02] Continuer</c> button. Maps
/// <c>HasSave</c> + the two label variants to a (Disabled, Label) pair.
/// Lives outside the Godot scene so xUnit can lock the rule without
/// spinning up an engine.
///
/// <para>
/// Why a helper, not a static const in <see cref="Wayfinders.Client.Scenes.Screens.E1Title"/>:
/// the rule is one line, but the rule is also <i>contractual</i>. Disabled
/// when HasSave is false ; label flips to the Cadastre-grisé variant.
/// Encoding the rule once in a place we can pin-test costs nothing and
/// makes the J5 save-system landing safer.
/// </para>
/// </summary>
public static class ContinueGatingLogic
{
    /// <summary>
    /// Compute the (Disabled, Label) pair for the Continuer button.
    /// </summary>
    /// <param name="hasSave">Whether <c>GameState.HasSave</c> is true.</param>
    /// <param name="enabledLabel">Label text when a save exists.</param>
    /// <param name="disabledLabel">Label text when no save exists.</param>
    public static ContinueGatingResult Compute(
        bool hasSave,
        string enabledLabel,
        string disabledLabel)
    {
        return new ContinueGatingResult(
            Disabled: !hasSave,
            Label: hasSave ? enabledLabel : disabledLabel);
    }
}

/// <summary>
/// Output of <see cref="ContinueGatingLogic.Compute"/>.
/// </summary>
public readonly record struct ContinueGatingResult(bool Disabled, string Label);
