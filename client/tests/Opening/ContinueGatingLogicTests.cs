using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="ContinueGatingLogic"/>. Locks the J2
/// behaviour of the E1 Continuer button: disabled when no save, labels
/// flip between the Cadastre archived/non-archived variants.
/// </summary>
public sealed class ContinueGatingLogicTests
{
    private const string EnabledLabel = "[02] Reprendre un registre archivé.";
    private const string DisabledLabel = "[02] Aucun registre archivé.";

    [Fact]
    public void Continue_disabled_and_grisé_label_when_no_save()
    {
        var result = ContinueGatingLogic.Compute(
            hasSave: false,
            enabledLabel: EnabledLabel,
            disabledLabel: DisabledLabel);

        Assert.True(result.Disabled);
        Assert.Equal(DisabledLabel, result.Label);
    }

    [Fact]
    public void Continue_enabled_with_archived_label_when_save_exists()
    {
        var result = ContinueGatingLogic.Compute(
            hasSave: true,
            enabledLabel: EnabledLabel,
            disabledLabel: DisabledLabel);

        Assert.False(result.Disabled);
        Assert.Equal(EnabledLabel, result.Label);
    }

    [Fact]
    public void Toggling_HasSave_flips_both_disabled_and_label()
    {
        // Validates the F12 debug toggle path: a single boolean flip moves
        // the button between the two states atomically. Locks the J2
        // intent that label and disabled are siblings, never out of sync.
        var noSave = ContinueGatingLogic.Compute(false, EnabledLabel, DisabledLabel);
        var withSave = ContinueGatingLogic.Compute(true, EnabledLabel, DisabledLabel);

        Assert.NotEqual(noSave.Disabled, withSave.Disabled);
        Assert.NotEqual(noSave.Label, withSave.Label);
    }
}
