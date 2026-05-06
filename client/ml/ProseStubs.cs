using System.Collections.Generic;

namespace Wayfinders.Client.Ml;

/// <summary>
/// L1 placeholder source for the three prose strings the classifier
/// expects. Hardcoded by unit Id; will be replaced in Phase 6 L4 by a
/// FastAPI fetch (Coda's UC1 prose renderers).
///
/// <para>
/// <b>Why a separate file, not inline in <see cref="Wayfinders.Client.Scenes.TacticalScene"/>.</b>
/// Three reasons. (1) The prose strings are <i>data</i>, not scene
/// logic — putting them next to TileMap stamping conflates concerns.
/// (2) Phase 6 L4 deletes this file wholesale and replaces it with a
/// Coda API call returning typed DTOs; isolating the stub now keeps
/// the L4 diff scoped. (3) Tonal lock (<c>project_wayfinders_tonal_dna.md</c>)
/// — Pratchett-only humor axis, no Naheulbeuk, no parodic register —
/// applies to these prose snapshots, and centralizing them makes the
/// audit cheap when Varn does a tonal pass.
/// </para>
///
/// <para>
/// <b>Contextual fidelity to the L5 ridge mission.</b> Character prose
/// is a wiry-build / quiet-step sketch that fits Kira; action prose is
/// a stealth-shape verb cluster; context prose names the ridge plateau
/// at dusk with starlight on scree (matches
/// <see cref="Wayfinders.Client.Simulation.SimulationGrid.BuildRidgeMission"/>
/// and the slice scoping doc). The strings are deliberately short — at
/// L4 the encoder will see the real prose pipeline; the stubs only need
/// to be plausible inputs that exercise the wiring without lying about
/// the shape. Closed-vocab Wayfinders English, no proper nouns of
/// real-world places, no out-of-tone humor.
/// </para>
///
/// <para>
/// <b>Default fallback.</b> If a unit Id is not in the table — should
/// not happen at L1 since the table is keyed on the same hardcoded
/// trio as <see cref="Wayfinders.Client.Scenes.TacticalScene"/> — the
/// fallback returns three generic placeholders rather than throwing.
/// L1's job is to ship the seam; a missing-key crash on a debug log
/// path would obscure the bug we actually care about.
/// </para>
/// </summary>
public static class ProseStubs
{
    private static readonly IReadOnlyDictionary<string, ProseTriple> ByUnitId =
        new Dictionary<string, ProseTriple>
        {
            ["kira"] = new(
                Character: "Kira moves with a wiry, careful step. " +
                           "Her shoulders stay low; her eyes read the ground.",
                Action:    "A stealth approach across the open ridge. " +
                           "Quiet feet, slow weight transfer, no straight lines.",
                Context:   "Dusk on the ridge plateau. Loose scree underfoot. " +
                           "Starlight catches the pale stones; cover is thin."),

            ["brann"] = new(
                Character: "Brann is broad and steady. He stands like " +
                           "something the wind has already given up on.",
                Action:    "A measured advance, shield held high. " +
                           "Each step is taken because the previous one held.",
                Context:   "Dusk on the ridge plateau. Loose scree underfoot. " +
                           "Starlight catches the pale stones; cover is thin."),

            ["edda"] = new(
                Character: "Edda watches more than she moves. Her staff " +
                           "rests light in her hand; her mind is somewhere ahead.",
                Action:    "A cautious advance, eyes on the wider field. " +
                           "She keeps the line of sight to her companions.",
                Context:   "Dusk on the ridge plateau. Loose scree underfoot. " +
                           "Starlight catches the pale stones; cover is thin."),
        };

    private static readonly ProseTriple Fallback = new(
        Character: "An unknown figure on the ridge.",
        Action:    "An advance of unspecified shape.",
        Context:   "Dusk on the ridge plateau. Loose scree underfoot.");

    public static ProseTriple ForUnit(string unitId)
    {
        return ByUnitId.TryGetValue(unitId, out ProseTriple? triple)
            ? triple
            : Fallback;
    }
}

/// <summary>
/// Bundle of the three prose snapshots the classifier consumes for one
/// (character, action, context) tuple. <c>record</c> for value-equality
/// in tests and <c>with</c>-friendly updates if a future caller wants
/// to override one field while keeping the other two.
/// </summary>
public sealed record ProseTriple(
    string Character,
    string Action,
    string Context);
