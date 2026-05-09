using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin test for the E1 title menu: every <c>Button</c> child of
/// <c>ButtonsList</c> must declare the <b>same</b> StyleBox SubResource on
/// its <c>theme_override_styles/normal</c> property. Catches the Mira
/// 2026-05-09 regression where button [01] rendered with the default Godot
/// skin while [02]-[04] sat on the parchment frame backdrop, breaking
/// uniformity.
///
/// <para>
/// <b>Why parse the .tscn as text.</b> Loading a Godot scene from xUnit
/// requires a live Godot runtime (same constraint as
/// <see cref="E1NavigationContractTests"/>, <see cref="SceneVisibilityContractTests"/>,
/// etc.). The .tscn file is a deterministic, line-based format — parsing
/// it as text gives us a Godot-free pin that catches the regression at the
/// level where it actually lives (the scene file).
/// </para>
///
/// <para>
/// The test asserts three things:
/// <list type="number">
///   <item>All four expected Button nodes are present.</item>
///   <item>Each Button declares <c>theme_override_styles/normal</c>.</item>
///   <item>All four point at the <b>same</b> SubResource id.</item>
/// </list>
/// A future edit that adds a 5th button without the override, or that
/// switches one button to a different SubResource, fails loudly.
/// </para>
/// </summary>
public sealed class E1ButtonSkinUniformityTests
{
    private static readonly string[] ExpectedButtonNames =
    {
        "NewGameButton",
        "ContinueButton",
        "OptionsButton",
        "QuitButton",
    };

    private const string ScenePathRelative =
        "client/scenes/screens/E1Title.tscn";

    private static string ReadScene()
    {
        // Walk up from the test bin output to the repo root, then descend.
        // The repo root is the first ancestor that contains pyproject.toml.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "pyproject.toml")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate repo root (pyproject.toml) from " + AppContext.BaseDirectory);

        var path = Path.Combine(dir.FullName, ScenePathRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException("E1Title.tscn not found", path);

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Pull the value of <c>theme_override_styles/normal</c> from each
    /// <c>[node ... type="Button" parent="ButtonsLayer/ButtonsFrame/ButtonsList"]</c>
    /// block. Returns a name -&gt; SubResource id map.
    /// </summary>
    private static Dictionary<string, string?> ParseButtonNormalStyles(string sceneText)
    {
        var result = new Dictionary<string, string?>();

        // A node block starts with a [node ...] header and ends at the
        // next [node|sub_resource|ext_resource|gd_scene] header (or EOF).
        // Regex captures the header attributes and the body (properties).
        var nodePattern = new Regex(
            @"^\[node\s+([^\]]+)\]\r?\n(?<body>(?:(?!^\[).*\r?\n?)*)",
            RegexOptions.Multiline);

        foreach (Match m in nodePattern.Matches(sceneText))
        {
            var header = m.Groups[1].Value;
            // Only Button nodes parented under the ButtonsList VBox.
            if (!header.Contains("type=\"Button\""))
                continue;
            if (!header.Contains("parent=\"ButtonsLayer/ButtonsFrame/ButtonsList\""))
                continue;

            var nameMatch = Regex.Match(header, @"name=""([^""]+)""");
            if (!nameMatch.Success) continue;
            var name = nameMatch.Groups[1].Value;

            var body = m.Groups["body"].Value;
            var styleMatch = Regex.Match(
                body,
                @"theme_override_styles/normal\s*=\s*SubResource\(""([^""]+)""\)");
            result[name] = styleMatch.Success ? styleMatch.Groups[1].Value : null;
        }

        return result;
    }

    [Fact]
    public void All_four_buttons_present_in_scene()
    {
        var scene = ReadScene();
        var parsed = ParseButtonNormalStyles(scene);

        foreach (var expected in ExpectedButtonNames)
        {
            Assert.True(
                parsed.ContainsKey(expected),
                $"Expected Button '{expected}' under ButtonsList ; found: " +
                string.Join(", ", parsed.Keys));
        }
    }

    [Fact]
    public void Every_button_declares_a_normal_stylebox_override()
    {
        var scene = ReadScene();
        var parsed = ParseButtonNormalStyles(scene);

        foreach (var name in ExpectedButtonNames)
        {
            Assert.True(
                parsed.TryGetValue(name, out var styleId) && !string.IsNullOrEmpty(styleId),
                $"Button '{name}' is missing 'theme_override_styles/normal'. " +
                "All four E1 menu buttons must declare a parchment skin so " +
                "the visual is uniform regardless of frame backdrop alignment " +
                "(Mira 2026-05-09 regression : button [01] rendered with the " +
                "default Godot skin).");
        }
    }

    [Fact]
    public void All_four_buttons_share_the_same_normal_stylebox()
    {
        var scene = ReadScene();
        var parsed = ParseButtonNormalStyles(scene);

        var ids = ExpectedButtonNames
            .Where(parsed.ContainsKey)
            .Select(n => parsed[n])
            .Distinct()
            .ToList();

        Assert.True(
            ids.Count == 1 && ids[0] is not null,
            "All four E1 menu buttons must share the SAME StyleBox " +
            "SubResource on 'theme_override_styles/normal'. Found distinct " +
            "ids: [" + string.Join(", ", ids.Select(i => i ?? "<missing>")) + "]");
    }
}
