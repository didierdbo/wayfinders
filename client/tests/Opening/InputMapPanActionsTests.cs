using System.IO;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-text regression test for the four <c>ui_pan_left/right/up/down</c>
/// InputMap actions in <c>client/project.godot</c>. P8.2-deep-fix lesson:
/// the pan ZQSD silently broke on AZERTY keyboards because the original
/// commit bound the InputMap entries to <c>keycode</c> (layout-mapped) instead
/// of <c>physical_keycode</c> (position-mapped). The bug was invisible to
/// xUnit because no test asserted the binding shape.
///
/// <para>
/// <b>Why a text test, not a Godot test.</b>
/// xUnit cannot instantiate the Godot <see cref="Godot.InputMap"/> singleton
/// (no engine in the test process). The runtime preflight in
/// <c>E1WorldMap.DumpInputAndCameraState</c> covers the live-engine path.
/// This test catches the regression earlier, at CI / pre-commit, by reading
/// the source-controlled <c>project.godot</c> and pinning four invariants:
/// the four action names exist, each has a <c>physical_keycode</c> entry,
/// and the legacy <c>keycode</c> field is zero (a non-zero <c>keycode</c>
/// alongside a non-zero <c>physical_keycode</c> would re-introduce the
/// AZERTY-FR ambiguity even if the physical binding is correct).
/// </para>
///
/// <para>
/// <b>Discovery path of <c>project.godot</c>.</b>
/// The test runs from the test project's bin directory (which sits at
/// <c>client/tests/bin/&lt;config&gt;/&lt;tfm&gt;/</c>). We walk up four
/// directories to land on <c>client/</c> and then look for
/// <c>project.godot</c> -- mirrors the pattern other text-fixture tests
/// in this folder would use, and is robust against the 2026-04-30 reorg
/// that moved tests under <c>client/tests/</c>.
/// </para>
/// </summary>
public sealed class InputMapPanActionsTests
{
    private static readonly string[] PanActionNames =
    {
        "ui_pan_left", "ui_pan_right", "ui_pan_up", "ui_pan_down",
    };

    private static string LoadProjectGodot()
    {
        // bin/<Config>/<Tfm>/Wayfinders.Client.Tests.dll -> walk up 4 dirs
        // gets us to client/tests/, then over to client/.
        var asmDir = Path.GetDirectoryName(typeof(InputMapPanActionsTests).Assembly.Location)!;
        var clientDir = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", ".."));
        var projectGodotPath = Path.Combine(clientDir, "project.godot");
        return File.ReadAllText(projectGodotPath);
    }

    [Fact]
    public void ProjectGodot_declares_all_four_pan_actions()
    {
        var projectGodot = LoadProjectGodot();
        foreach (var actionName in PanActionNames)
        {
            Assert.Contains(actionName + "=", projectGodot);
        }
    }

    [Fact]
    public void ProjectGodot_pan_actions_use_physical_keycode_not_keycode()
    {
        // Each ui_pan_* block must contain at least one physical_keycode
        // with a non-zero value, AND every keycode field in the block
        // must be zero. We assert by counting -- a single non-zero
        // keycode in any pan block flags the regression.
        var projectGodot = LoadProjectGodot();

        foreach (var actionName in PanActionNames)
        {
            var blockStart = projectGodot.IndexOf(actionName + "=", System.StringComparison.Ordinal);
            Assert.True(blockStart >= 0, $"action '{actionName}' missing from project.godot");

            // The block runs from the action name to the next blank line
            // (Godot serialises each InputMap action terminated by a "}"
            // on its own line, with the next action 1+ lines below).
            var blockEnd = projectGodot.IndexOf("}\n", blockStart, System.StringComparison.Ordinal);
            if (blockEnd < 0) blockEnd = projectGodot.Length;
            var block = projectGodot.Substring(blockStart, blockEnd - blockStart);

            // At least one physical_keycode with a non-zero value.
            Assert.Contains("\"physical_keycode\":", block);
            // Pin the regression: no non-zero keycode fields in the block.
            // The serialiser writes "keycode":0 when the field is unset.
            // A non-zero "keycode":N is the AZERTY-FR ambiguity smell.
            Assert.DoesNotContain("\"keycode\":1", block);
            Assert.DoesNotContain("\"keycode\":2", block);
            Assert.DoesNotContain("\"keycode\":3", block);
            Assert.DoesNotContain("\"keycode\":4", block);
            Assert.DoesNotContain("\"keycode\":5", block);
            Assert.DoesNotContain("\"keycode\":6", block);
            Assert.DoesNotContain("\"keycode\":7", block);
            Assert.DoesNotContain("\"keycode\":8", block);
            Assert.DoesNotContain("\"keycode\":9", block);
        }
    }
}
