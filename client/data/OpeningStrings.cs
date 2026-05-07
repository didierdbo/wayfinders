using Godot;

namespace Wayfinders.Client.Data;

/// <summary>
/// Centralised UI strings for the Opening Scenario, exposed as a Godot
/// <see cref="Resource"/> so designers can swap copy via a <c>.tres</c>
/// file without recompiling. Default values mirror the Varn-locked
/// libellés voix Cadastre v1 (2026-05-07).
///
/// <para>
/// <b>Why a Resource and not a static class.</b>
/// <list type="bullet">
///   <item>UTF-8 accents (<c>é</c>, <c>à</c>, <c>É</c>) are stored
///         natively in the <c>.tres</c> text format ; AOT analyzer cannot
///         flag string-literal encoding warnings on Resource fields.</item>
///   <item>Designer (Varn) can hot-edit a <c>.tres</c> in the editor
///         without C# rebuild ; copy iteration is a 5-second loop.</item>
///   <item>The class itself stays unit-testable from xUnit because every
///         field has a hard-coded <c>[Export]</c> default — instantiating
///         <c>new OpeningStrings()</c> in a test gives the J2 baseline
///         without needing a Godot runtime to load the <c>.tres</c>.</item>
///   <item>Forward-compat for i18n post-MVP: swap <c>OpeningStrings_fr.tres</c>
///         for <c>OpeningStrings_en.tres</c> on the loader. No code change.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Source of truth for J2 defaults.</b> Varn -- <c>UI Labels Voix
/// Cadastre v1 (2026-05-07)</c> §2.2 (E1 buttons, header, footer), §2.3
/// (modale Quitter), §2.4 (panneau Options stub).
/// </para>
///
/// <para>
/// <b>J3 additions.</b> Varn UI Labels v1 §3 (E2 essentials) ratified
/// for the World Map: title bandeau, subtitle, panel titles + bodies
/// (PanelLeftBody/PanelRightBody added 2026-05-07 by Varn), back
/// button, POI hover templates, blocked indicator, layer indicator.
/// E2.M (date in-game) and E2.N (palier compagnie) intentionally NOT
/// shipped — pre-brief D-J3-04 (date) and D-J3-03 alternative (jauge
/// palier punted with the date).
/// </para>
///
/// <para>
/// <b>Variants pending Didier playtest validation.</b> Varn flagged
/// several open tonal decisions in §9 of the spec ; J2/J3 ship the
/// chosen defaults but the swap targets are documented inline below
/// for fast hot-edit on the <c>.tres</c> if Didier pivots after manual
/// checklist.
/// </para>
/// </summary>
[GlobalClass]
public partial class OpeningStrings : Resource
{
    // -------------------------- Écran 1 (Titre) --------------------------
    // Source: Varn §2.2 table E1.

    /// <summary>E1.A -- marque centrale, typo-tampon. Locked Varn §2.2.</summary>
    [Export] public string E1Title { get; set; } = "WAYFINDERS";

    /// <summary>
    /// E1.B -- cartouche sous tampon, ligne 1.
    /// Default Varn §2.2: <c>Cadastre franc des marges habitées.</c>
    /// Variant Varn §9.1: <c>Cadastre des choses mal tenues.</c> (plus SCP).
    /// </summary>
    [Export] public string E1Subtitle { get; set; } = "Cadastre franc des marges habitées.";

    /// <summary>E1.C -- cartouche sous tampon, ligne 2 (plus petit). Locked Varn §2.2.</summary>
    [Export] public string E1Edition { get; set; } = "Édition de l'Ordre des Arpenteurs.";

    /// <summary>
    /// E1.D -- bouton 01.
    /// Default Varn §2.2: <c>[01] Ouvrir un nouveau registre.</c>
    /// Variant Varn §9.2: <c>[01] Établir un nouveau dossier.</c>
    /// </summary>
    [Export] public string E1NewGame { get; set; } = "[01] Ouvrir un nouveau registre.";

    /// <summary>E1.E -- bouton 02 (état avec save). Locked Varn §2.2.</summary>
    [Export] public string E1Continue { get; set; } = "[02] Reprendre un registre archivé.";

    /// <summary>E1.F -- bouton 02 (état grisé sans save). Locked Varn §2.2.</summary>
    [Export] public string E1ContinueDisabled { get; set; } = "[02] Aucun registre archivé.";

    /// <summary>
    /// E1.G -- bouton 03.
    /// Default Varn §2.2: <c>[03] Réglages techniques.</c>
    /// Variant Varn §9.3: <c>[03] Préférences.</c> (plus neutre).
    /// </summary>
    [Export] public string E1Options { get; set; } = "[03] Réglages techniques.";

    /// <summary>E1.H -- bouton 04. Locked Varn §2.2.</summary>
    [Export] public string E1Quit { get; set; } = "[04] Refermer le Cadastre.";

    /// <summary>E1.O -- footer pied de page (centré). Locked Varn §2.2.</summary>
    [Export] public string E1Footer { get; set; } =
        "Édition de l'Ordre des Arpenteurs — usage interne — ne pas reproduire.";

    /// <summary>
    /// E1.P -- version build (coin bas-droite, micro-texte).
    /// Default Varn §2.2: Pratchett-tinged.
    /// Variant Varn §9.6: <c>Cadastre v0.2 — édition courante.</c> (neutre).
    /// </summary>
    [Export] public string E1Build { get; set; } = "Cadastre v0.2 — Section M, Étage Pré-Aurore.";

    // ---------------- Modale Options (E1 stub J2) ----------------
    // Source: Varn §2.4.

    /// <summary>E1.U -- titre du panneau Options (voix A, signalétique). Locked Varn §2.4.</summary>
    [Export] public string OptionsTitle { get; set; } = "Réglages techniques";

    /// <summary>E1.V -- section Volume audio (voix S). Locked Varn §2.4.</summary>
    [Export] public string OptionsVolume { get; set; } = "Volume audio";

    /// <summary>E1.X -- section Plein écran (voix S). Locked Varn §2.4.</summary>
    [Export] public string OptionsFullscreen { get; set; } = "Plein écran";

    /// <summary>E1.Z -- section Langue (voix S). Locked Varn §2.4.</summary>
    [Export] public string OptionsLanguage { get; set; } = "Langue";

    /// <summary>E1.AA -- option dropdown Langue. Locked Varn §2.4.</summary>
    [Export] public string OptionsLanguageFrench { get; set; } = "Français";

    /// <summary>E1.AB -- bouton fermeture panneau (voix S). Locked Varn §2.4.</summary>
    [Export] public string OptionsClose { get; set; } = "Fermer";

    // ---------------- Modale Quitter (E1 -- voix mixte A+S) ----------------
    // Source: Varn §2.3.

    /// <summary>E1.Q -- titre de modale (voix A). Locked Varn §2.3.</summary>
    [Export] public string QuitTitle { get; set; } = "Refermer le Cadastre ?";

    /// <summary>
    /// E1.R -- texte de confirmation (voix A).
    /// Default Varn §2.3: pas de save auto en v1.
    /// Variant Varn §9.4 (si save auto activée):
    ///   <c>Le registre sera scellé. Vous pourrez le reprendre à la prochaine séance.</c>
    /// </summary>
    [Export] public string QuitBody { get; set; } =
        "Le registre sera scellé en l'état. Toute annotation non consignée sera perdue.";

    /// <summary>E1.S -- bouton confirmation (voix S). Locked Varn §2.3.</summary>
    [Export] public string Confirm { get; set; } = "Confirmer";

    /// <summary>E1.T -- bouton annulation (voix S). Locked Varn §2.3.</summary>
    [Export] public string Cancel { get; set; } = "Annuler";

    // -------------------- Écran 2 (Carte du monde) --------------------
    // Source: Varn §3.1 table E2 (added 2026-05-07 J3).

    /// <summary>E2.A -- titre écran (bandeau supérieur, cartouche). Locked Varn §3.1.</summary>
    [Export] public string E2Title { get; set; } = "Feuillet I — Carte des marges connues.";

    /// <summary>
    /// E2.B -- légende de section sous le titre (1 ligne, micro-texte).
    /// Default Varn §3.1: <c>Établi par l'Ordre des Arpenteurs. Tenu à jour selon les juridictions.</c>
    /// Variant Varn §3.1: <c>Établi par l'Ordre. Mis à jour partiellement.</c>
    /// </summary>
    [Export] public string E2Subtitle { get; set; } =
        "Établi par l'Ordre des Arpenteurs. Tenu à jour selon les juridictions.";

    /// <summary>
    /// E2.C -- onglet panneau gauche (rétractable, en J3 stub fixed-pos).
    /// Default Varn §3.1: <c>La compagnie</c>.
    /// Variant Varn §3.1: <c>Registre de la compagnie</c>.
    /// </summary>
    [Export] public string E2PanelLeftTitle { get; set; } = "La compagnie";

    /// <summary>E2.D -- onglet panneau droit. Locked Varn §3.1 + §10.7 vision n°5.</summary>
    [Export] public string E2PanelRightTitle { get; set; } = "Marges du Codex";

    /// <summary>
    /// E2.PanelLeftBody -- corps panneau gauche (état stub J3, voix A).
    /// Default Varn §3.1 (drafted 2026-05-07): <c>Aucune compagnie déclarée à ce feuillet. Le registre attend signature.</c>
    /// Variant Varn §3.1: <c>Aucune inscription. Le registre est ouvert mais vierge.</c>
    /// </summary>
    [Export] public string E2PanelLeftBody { get; set; } =
        "Aucune compagnie déclarée à ce feuillet. Le registre attend signature.";

    /// <summary>
    /// E2.PanelRightBody -- corps panneau droit (état stub J3, voix D Marges).
    /// Default Varn §3.1 (drafted 2026-05-07): <c>Les marges sont propres. Pour l'instant.</c>
    /// Variant Varn §3.1 (alt warmer ton): <c>Rien n'a encore été griffonné ici. Ça viendra.</c>
    /// — Didier may swap to the alt at tests via hot-edit. Both pass the
    /// Pratchett-warm filter ; the default's <c>Pour l'instant.</c> is the
    /// signed-off voice, the alt is documented here for fast swap.
    /// </summary>
    [Export] public string E2PanelRightBody { get; set; } =
        "Les marges sont propres. Pour l'instant.";

    /// <summary>
    /// E2.E -- bouton retour (bas-gauche).
    /// Default Varn §3.1: <c>Refermer le feuillet</c>.
    /// Variant Varn §3.1: <c>Quitter la carte</c> (voix S).
    /// </summary>
    [Export] public string E2BackButton { get; set; } = "Refermer le feuillet";

    /// <summary>
    /// E2.F instantiated for Halfgate -- the unique clickable POI in MVP
    /// (Varn §6.D6.10 lock 1-cité). Voix A. Composed from the §3.1
    /// template <c>[Nom]. Cité [statut]. Cadastre [tenu à jour | ...].</c>
    /// — Halfgate is the patented city Cadastre is up-to-date on, so:
    /// <c>Halfgate. Cité patentée. Cadastre tenu à jour.</c>
    /// </summary>
    [Export] public string E2PoiHalfgateTooltip { get; set; } =
        "Halfgate. Cité patentée. Cadastre tenu à jour.";

    /// <summary>
    /// E2.G -- tag template for grisée POI cities (Cadastre suspendu).
    /// Contains the <c>[Nom]</c> placeholder which E2WorldMap substitutes
    /// at runtime with <see cref="PoiDefinition.DisplayName"/>. Voix A.
    /// </summary>
    [Export] public string E2PoiCitySuspendedTooltipTemplate { get; set; } =
        "[Nom]. Cadastre suspendu — accès non encore patenté.";

    /// <summary>
    /// E2.G reused for the bottom-center flash label that flashes 2s
    /// after a click on a grisée POI. Same wording, no <c>[Nom]</c>
    /// because the click context is generic. Voix A.
    /// </summary>
    [Export] public string E2PoiBlockedIndicator { get; set; } =
        "Cadastre suspendu — accès non encore patenté.";

    /// <summary>
    /// E2.L -- indicateur layer P2 (coin bas-droite). Static label in J3
    /// (D-J3-03). Becomes dynamic when the molette layer-pile mechanic
    /// ships post-MVP. Voix A.
    /// </summary>
    [Export] public string E2LayerIndicator { get; set; } = "Pile cadastrale : monde.";
}
