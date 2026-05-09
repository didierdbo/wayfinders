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
/// The test asserts:
/// <list type="number">
///   <item>All four expected Button nodes are present.</item>
///   <item>Each Button declares <c>theme_override_styles/normal</c>.</item>
///   <item>All four point at the <b>same</b> SubResource id.</item>
///   <item>All four declare the <b>same</b> <c>font_outline_color</c> and
///     <c>outline_size</c> (Mira 2026-05-09 v3 follow-up: text outline
///     halo for figure/ground separation against the textured parchment
///     frame).</item>
/// </list>
/// A future edit that adds a 5th button without the override, or that
/// switches one button to a different SubResource / outline value, fails
/// loudly.
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
    /// Iterate the body of every <c>[node ... type="Button" parent="ButtonsLayer/ButtonsFrame/ButtonsList"]</c>
    /// block in the scene, returning a name -&gt; body map. Centralises
    /// the regex / filter so each per-property parser stays a one-liner.
    /// </summary>
    private static Dictionary<string, string> ParseButtonBodies(string sceneText)
    {
        var result = new Dictionary<string, string>();

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
            result[nameMatch.Groups[1].Value] = m.Groups["body"].Value;
        }

        return result;
    }

    /// <summary>
    /// Pull the value of <c>theme_override_styles/normal</c> from each
    /// <c>[node ... type="Button" parent="ButtonsLayer/ButtonsFrame/ButtonsList"]</c>
    /// block. Returns a name -&gt; SubResource id map.
    /// </summary>
    private static Dictionary<string, string?> ParseButtonNormalStyles(string sceneText)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (name, body) in ParseButtonBodies(sceneText))
        {
            var styleMatch = Regex.Match(
                body,
                @"theme_override_styles/normal\s*=\s*SubResource\(""([^""]+)""\)");
            result[name] = styleMatch.Success ? styleMatch.Groups[1].Value : null;
        }
        return result;
    }

    /// <summary>
    /// Pull the value of <c>theme_override_colors/font_outline_color</c>
    /// (raw <c>Color(...)</c> tuple text, normalised whitespace) from each
    /// Button. Missing override -&gt; <c>null</c>.
    /// </summary>
    private static Dictionary<string, string?> ParseButtonOutlineColors(string sceneText)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (name, body) in ParseButtonBodies(sceneText))
        {
            var m = Regex.Match(
                body,
                @"theme_override_colors/font_outline_color\s*=\s*(Color\([^\)]+\))");
            result[name] = m.Success ? Regex.Replace(m.Groups[1].Value, @"\s+", " ") : null;
        }
        return result;
    }

    /// <summary>
    /// Pull the integer value of <c>theme_override_constants/outline_size</c>
    /// from each Button. Missing override -&gt; <c>null</c>.
    /// </summary>
    private static Dictionary<string, int?> ParseButtonOutlineSizes(string sceneText)
    {
        var result = new Dictionary<string, int?>();
        foreach (var (name, body) in ParseButtonBodies(sceneText))
        {
            var m = Regex.Match(
                body,
                @"theme_override_constants/outline_size\s*=\s*(-?\d+)");
            result[name] = m.Success ? int.Parse(m.Groups[1].Value) : null;
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

    [Fact]
    public void All_four_buttons_share_the_same_font_outline_color()
    {
        var scene = ReadScene();
        var parsed = ParseButtonOutlineColors(scene);

        foreach (var name in ExpectedButtonNames)
        {
            Assert.True(
                parsed.TryGetValue(name, out var c) && !string.IsNullOrEmpty(c),
                $"Button '{name}' is missing " +
                "'theme_override_colors/font_outline_color'. All four E1 " +
                "menu buttons must declare an outline so the umber text " +
                "stays readable on the textured parchment frame backdrop " +
                "(Mira 2026-05-09 v3 follow-up : umber-on-umber contrast " +
                "fell under 3:1 on dark variations of the parchment grain).");
        }

        var distinct = ExpectedButtonNames
            .Select(n => parsed[n])
            .Distinct()
            .ToList();

        Assert.True(
            distinct.Count == 1,
            "All four E1 menu buttons must share the SAME " +
            "'font_outline_color'. The outline is a constant " +
            "figure/ground detachment, not a state-aware accent. Found " +
            "distinct values: [" + string.Join(", ", distinct.Select(c => c ?? "<missing>")) + "]");
    }

    [Fact]
    public void All_four_buttons_share_the_same_outline_size()
    {
        var scene = ReadScene();
        var parsed = ParseButtonOutlineSizes(scene);

        foreach (var name in ExpectedButtonNames)
        {
            Assert.True(
                parsed.TryGetValue(name, out var size) && size.HasValue,
                $"Button '{name}' is missing " +
                "'theme_override_constants/outline_size'.");
        }

        var distinct = ExpectedButtonNames
            .Select(n => parsed[n])
            .Distinct()
            .ToList();

        Assert.True(
            distinct.Count == 1,
            "All four E1 menu buttons must share the SAME " +
            "'outline_size'. Found distinct values: [" +
            string.Join(", ", distinct.Select(s => s.HasValue ? s.Value.ToString() : "<missing>")) + "]");

        // Hard ceiling: Rune's brief caps outline at 3px to avoid a
        // cartoon rendering that would betray the Bourgeon visual DNA.
        Assert.True(
            distinct[0]!.Value >= 1 && distinct[0]!.Value <= 3,
            $"E1 button outline_size must be in [1, 3] (got {distinct[0]}). " +
            "Above 3px the outline reads cartoon and breaks the Bourgeon " +
            "smooth-oil visual DNA (Wayfinders visual lock 2026-05-07).");
    }
}
