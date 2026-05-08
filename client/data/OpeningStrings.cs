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
/// <b>J4 additions.</b> Varn UI Labels v1 §4 (E3 essentials) for the
/// City Halfgate screen: title bandeau + subtitle, NM jauges stub
/// labels (E3.C two static labels, mechanic post-MVP), 3-panel chrome
/// (left "La compagnie", right "Tableau des contrats" with TabContainer,
/// bottom "Journal de la compagnie" voix A neutre stub D-J4-06), back
/// button, layer indicator, 4 POI tooltip templates (District,
/// KeyBuilding QG, Candidat, generic blocked), and §5 E4 modal stub
/// libellés (modal title template, body stub, close button).
/// </para>
///
/// <para>
/// <b>Variants pending Didier playtest validation.</b> Varn flagged
/// several open tonal decisions in §9 of the spec ; J2/J3/J4 ship the
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

    /// <summary>
    /// E1.AC -- libellé de la nouvelle ligne Options "Bouton de pan de la carte"
    /// (P8.3, M3 / Arc 3 / Phase 8.3, voix S système). Premier vrai widget
    /// Options avec un effet réel en MVP -- positionné en première ligne du
    /// ContentSlot par convention industrie (Steam settings, CK3, XCOM 2 :
    /// les controls viennent avant audio/display). Voix système cohérente
    /// avec Volume / Plein écran / Langue (décision J2 D1).
    /// </summary>
    [Export] public string OptionsMapPanLabel { get; set; } = "Bouton de pan de la carte";

    /// <summary>
    /// E1.AD -- libellé de l'entrée dropdown "Bouton du milieu (défaut)"
    /// (P8.3, voix S). La parenthèse "(défaut)" rend l'état par défaut
    /// découvrable par le joueur sans avoir à le déduire de la position
    /// dans la liste. Cohérent UX Steam / CK3 (le défaut est toujours
    /// signalé dans les dropdowns à 2 entrées asymétriques).
    /// </summary>
    [Export] public string OptionsMapPanMiddle { get; set; } = "Bouton du milieu (défaut)";

    /// <summary>
    /// E1.AE -- libellé de l'entrée dropdown "Bouton droit" (P8.3, voix S).
    /// Pas de "(défaut)" -- l'absence de marqueur sur cette entrée est ce
    /// qui rend la parenthèse de l'autre entrée informative.
    /// </summary>
    [Export] public string OptionsMapPanRight { get; set; } = "Bouton droit";

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

    // -------------------- Écran 3 (Plan de cité) --------------------
    // Source: Varn §4.1 table E3 (added 2026-05-07 J4).

    /// <summary>
    /// E3.A -- titre écran (bandeau supérieur, cartouche). Locked Varn §4.1.
    /// J4 ships the Halfgate-instantiated form rather than the
    /// <c>[Nom de la cité]</c> template — when post-MVP unlocks more
    /// cités, this becomes a template and the screen interpolates.
    /// </summary>
    [Export] public string E3Title { get; set; } = "Halfgate. Feuillet II — plan cadastral.";

    /// <summary>E3.B -- légende de section sous le titre. Locked Varn §4.1.</summary>
    [Export] public string E3Subtitle { get; set; } =
        "Cadastre tenu par le bureau local des Arpenteurs.";

    /// <summary>
    /// E3.C (1/2) -- jauge Visibilité, sous-bandeau NM. Stub static J4
    /// (D-J4-04). Real binding to a <c>RegionalThreatLevel</c> resource
    /// + <c>NMChanged</c> signal lands post-MVP. Locked §11.6 vision.
    /// Voix A.
    /// </summary>
    [Export] public string E3VisibilityLabel { get; set; } = "Visibilité 0/10.";

    /// <summary>
    /// E3.C (2/2) -- jauge Méfiance, sous-bandeau NM. Stub static J4
    /// (D-J4-04). See <see cref="E3VisibilityLabel"/> for the post-MVP
    /// hook. Voix A.
    /// </summary>
    [Export] public string E3MenaceLabel { get; set; } = "Méfiance 0/10.";

    /// <summary>E3.D -- onglet panneau gauche. Locked Varn §4.1.</summary>
    [Export] public string E3PanelLeftTitle { get; set; } = "La compagnie";

    /// <summary>
    /// Panneau gauche corps -- état stub J4 (party vide post-E1
    /// nouvelle partie, conforme Varn §3.1.6). Voix A. Drafted by Rune
    /// for cohérence with E2.PanelLeftBody pattern. Variant flagged for
    /// Varn ratification at tests if grinçant.
    /// </summary>
    [Export] public string E3PanelLeftBody { get; set; } =
        "Aucun compagnon présent à Halfgate.";

    /// <summary>E3.E -- onglet panneau droit. Locked Varn §4.1.</summary>
    [Export] public string E3PanelRightTitle { get; set; } = "Tableau des contrats";

    /// <summary>E3.F -- sous-onglet contrats colonne 1. Locked Varn §4.1.</summary>
    [Export] public string E3TabRoutine { get; set; } = "Routine";

    /// <summary>E3.G -- sous-onglet contrats colonne 2. Locked Varn §4.1.</summary>
    [Export] public string E3TabMandate { get; set; } = "Mandats des Arpenteurs";

    /// <summary>
    /// Stub corps onglet Routine (J4). Voix A neutre. Real contracts
    /// system lands post-MVP. Drafted by Rune cohérent E2 panneau gauche
    /// stub pattern.
    /// </summary>
    [Export] public string E3TabRoutineStub { get; set; } = "Aucun contrat enregistré.";

    /// <summary>
    /// Stub corps onglet Mandats (J4). Voix A neutre. Real mandates
    /// system lands post-MVP.
    /// </summary>
    [Export] public string E3TabMandateStub { get; set; } = "Aucun mandat émis.";

    /// <summary>
    /// E3.H -- titre panneau bas Journal compagnie. Locked Varn §4.1
    /// titre voix A (le contenu en voix C lui est réservé post-MVP,
    /// D-J4-06). J4 ships a voix A neutre stub body — see
    /// <see cref="E3PanelBottomBody"/>.
    /// </summary>
    [Export] public string E3PanelBottomTitle { get; set; } = "Journal de la compagnie";

    /// <summary>
    /// Panneau bas Journal corps -- stub voix A NEUTRE (D-J4-06). Voix
    /// C polarisante réservée à la mécanique Journal post-MVP. La
    /// formulation suit le pattern voix A neutre cohérent E2 panneau
    /// gauche. Variant flagged si grinçant aux tests.
    /// </summary>
    [Export] public string E3PanelBottomBody { get; set; } =
        "Aucune entrée journal pour l'instant.";

    /// <summary>
    /// E3.I -- bouton retour (bas-gauche).
    /// Default Varn §4.1: <c>Quitter la cité</c>.
    /// Variant Varn §4.1: <c>Refermer le feuillet</c>.
    /// </summary>
    [Export] public string E3BackButton { get; set; } = "Quitter la cité";

    /// <summary>E3.S -- indicateur layer P2 (coin bas-droite). Locked Varn §4.1. Voix A.</summary>
    [Export] public string E3LayerIndicator { get; set; } = "Pile cadastrale : cité.";

    /// <summary>
    /// E3.J -- tooltip template POI Quartier. Contains <c>[nom]</c>
    /// placeholder substituted at runtime with the POI DisplayName.
    /// Locked Varn §4.1 (NM jauges static 0/10 in J4 per E3.C lock).
    /// Voix A.
    /// </summary>
    [Export] public string E3PoiDistrictTooltipTemplate { get; set; } =
        "Quartier de [nom]. Visibilité 0/10. Méfiance 0/10.";

    /// <summary>
    /// E3.M -- tooltip POI bâtiment QG. Locked Varn §4.1. Voix A.
    /// J4 stub: click flashes blocked indicator (real opening of the
    /// contracts panel lands post-MVP).
    /// </summary>
    [Export] public string E3PoiKeyBuildingHqTooltip { get; set; } =
        "Siège de la compagnie. Tableau des contrats consultable.";

    /// <summary>
    /// E3.P -- tooltip POI PNJ candidat-au-recrutement. Locked verbatim
    /// Varn §4.1 + §3.3.3 Scénario d'ouverture. Voix A glissant vers
    /// "presque humaine" (élément tonal critique à préserver, Varn §4.2).
    /// </summary>
    [Export] public string E3PoiCandidateTooltip { get; set; } =
        "Quelqu'un qui pourrait vouloir vous suivre.";

    /// <summary>
    /// J4 stub indicator key for KeyBuilding click flash. Voix A
    /// cohérent E2 blocked. Drafted by Rune (D-J4-09) ; Varn ratifies
    /// at tests if le wording demande ajustement par rapport à
    /// "Cadastre suspendu" (E2 voix A pattern).
    /// </summary>
    [Export] public string E3KeyBuildingBlockedIndicator { get; set; } =
        "Bâtiment non encore patenté.";

    // -------------------- Écran 4 (Fiche perso modal) --------------------
    // Source: Varn §5.1 table E4 (J4 stub minimal -- D-J4-07 option α).

    /// <summary>
    /// E4.A -- titre fiche template, locked verbatim Varn §5.1 + §3.4.4
    /// Scénario d'ouverture. Contains <c>[xxx]</c> (numéro fiche) and
    /// <c>[Nom officiel]</c> (sujet) placeholders. J4 stub interpolates
    /// fiche n°001 + the PoiId as Nom officiel placeholder.
    /// </summary>
    [Export] public string E4ModalTitleTemplate { get; set; } =
        "Fiche cadastrale n°[xxx] — Sujet : [Nom officiel].";

    /// <summary>
    /// J4 stub corps fiche cadastrale (D-J4-07). Voix A cohérent J3
    /// blocked indicator wording. Real content (4 onglets, portrait
    /// Kira, sections, caviardages) lands J5+. Drafted by Rune.
    /// </summary>
    [Export] public string E4ModalStubBody { get; set; } =
        "Contenu en cours de rédaction par les Arpenteurs.";

    /// <summary>
    /// E4.O -- bouton fermeture fiche.
    /// Default Varn §5.1: <c>Refermer la fiche</c>.
    /// Variant Varn §5.1: <c>Fermer</c> (voix S).
    /// </summary>
    [Export] public string E4ModalCloseButton { get; set; } = "Refermer la fiche";

    // -------------------- J5 additions: E4 fiche réelle --------------------
    // Source: Varn §5.1 + §5.2 table E4 (added 2026-05-07 J5).
    // Sections (6) repartitioned into 4 onglets per Rune J5 pre-brief §3.1
    // following Varn §3.4.2 mapping. Caviardage P9 wording locked verbatim
    // Varn §5 lock E4.M. Portrait state subtitle template Varn §5 E4.N.

    /// <summary>
    /// E4 onglet 1 / 4 -- titre <c>Identité</c> (Varn §5 E4.B). Voix A.
    /// </summary>
    [Export] public string E4TabIdentity { get; set; } = "Identité";

    /// <summary>
    /// E4 onglet 2 / 4 -- titre <c>Compétences</c> (Varn §5 E4.C). Voix A.
    /// </summary>
    [Export] public string E4TabSkills { get; set; } = "Compétences";

    /// <summary>
    /// E4 onglet 3 / 4 -- titre <c>Dossier</c> (Varn §5 E4.D). Voix A.
    /// </summary>
    [Export] public string E4TabDossier { get; set; } = "Dossier";

    /// <summary>
    /// E4 onglet 4 / 4 -- titre <c>Caviardages</c> (Varn §5 E4.E). Voix A.
    /// J5 D-J5-13: cliquable libre (no palier compagnie gate). Real
    /// gating ships post-MVP via the company tier mechanic.
    /// </summary>
    [Export] public string E4TabRedaction { get; set; } = "Caviardages";

    /// <summary>
    /// E4 section 1 (sous onglet Identité) -- titre. Voix A. Locked Varn §5 E4.G.
    /// </summary>
    [Export] public string E4SectionIdentificationHeader { get; set; } = "Identification";

    /// <summary>
    /// E4 section 1 corps -- stub voix A neutre J5 (Rune-drafted, flagged
    /// for Varn ratification at tests). Cohérent J3/J4 stub pattern.
    /// </summary>
    [Export] public string E4SectionIdentificationBody { get; set; } =
        "Relevé d'identification en cours par les Arpenteurs.";

    /// <summary>
    /// E4 section 2 (sous onglet Identité) -- titre <c>Origine déclarée</c>.
    /// Voix A. Locked Varn §5 E4.H. J5 caviardée P9 (D-J5-11 + Risk #1).
    /// </summary>
    [Export] public string E4SectionOriginHeader { get; set; } = "Origine déclarée";

    /// <summary>
    /// E4 section 3 (sous onglet Compétences) -- titre. Voix A. Locked Varn §5 E4.I.
    /// </summary>
    [Export] public string E4SectionSkillsHeader { get; set; } = "Compétences attestées";

    /// <summary>
    /// E4 section 3 corps -- stub voix A neutre J5 (Rune-drafted, flagged
    /// for Varn ratification at tests).
    /// </summary>
    [Export] public string E4SectionSkillsBody { get; set; } =
        "Relevé non encore consigné.";

    /// <summary>
    /// E4 section 4 (sous onglet Dossier) -- titre <c>Mentions et incidents</c>.
    /// Voix A. Locked Varn §5 E4.J. J5 caviardée P9 (D-J5-11 + Risk #1).
    /// </summary>
    [Export] public string E4SectionMentionsHeader { get; set; } = "Mentions et incidents";

    /// <summary>
    /// E4 section 5 (sous onglet Dossier) -- titre <c>Annexes — Marges du Codex</c>.
    /// Voix A J5 (Varn §5.2 dit voix D pour le contenu réel post-MVP ;
    /// stub voix A neutre cohérent D-J5-06 stub voix A).
    /// </summary>
    [Export] public string E4SectionMarginsHeader { get; set; } = "Annexes — Marges du Codex";

    /// <summary>
    /// E4 section 5 corps -- stub voix A neutre J5 (Rune-drafted, flagged
    /// for Varn ratification + voix D substitution post-MVP). Pre-brief §3.1.2.
    /// </summary>
    [Export] public string E4SectionMarginsBody { get; set; } =
        "Aucune marge n'a encore été portée à ce dossier.";

    /// <summary>
    /// E4 section 6 (sous onglet Caviardages) -- titre <c>Zones masquées</c>.
    /// Voix A. Locked Varn §5 E4.L.
    /// </summary>
    [Export] public string E4SectionRedactionHeader { get; set; } = "Zones masquées";

    /// <summary>
    /// E4 section 6 corps -- stub voix A neutre J5 (Rune-drafted). Recense
    /// le nombre de blocs caviardés présents dans la fiche. J5 hardcoded
    /// "2 caviardages" because the J5 fiche surfaces exactly 2 (D-J5-11) ;
    /// J6+ derives from the real palier compagnie gating.
    /// </summary>
    [Export] public string E4SectionRedactionBody { get; set; } =
        "2 caviardages présents (classe I).";

    /// <summary>
    /// E4 -- sous-titre fiche template, sous le titre. Locked Varn §5 E4.N.
    /// Contains <c>{0}</c> placeholder substituted at runtime with the
    /// portrait state French label ("calme", "tendu", "blessé"). J5 always
    /// feeds <c>calme</c> (D-J5-02 + Risk #2).
    /// </summary>
    [Export] public string E4PortraitStateTemplate { get; set; } =
        "Sujet observé : {0}.";

    /// <summary>
    /// E4 -- texte caviardage P9 verbatim Varn §5 E4.M. Voix A. Locked.
    /// Composé par-dessus le TextureRect overlay <c>e4.redaction_overlay</c>
    /// (D-J5-11) sur les sections <c>Origine déclarée</c> et
    /// <c>Mentions et incidents</c> (2 caviardages J5).
    /// </summary>
    [Export] public string E4RedactionBlockTemplate { get; set; } =
        "[Caviardage classe I — accès refusé. Voir vos privilèges de patente.]";

    // -------------------- J5 additions: E5 plan de quartier --------------------
    // Source: Varn §6 table E5 (added 2026-05-07 J5).

    /// <summary>
    /// E5.A -- titre écran (bandeau supérieur, cartouche). Locked Varn §6 E5.A.
    /// J5 ships the Halfgate-marché-instantiated form rather than the
    /// <c>[Cité] -- Quartier de [nom]</c> template — when post-MVP unlocks
    /// more quartiers, this becomes a template and the screen interpolates.
    /// Voix A.
    /// </summary>
    [Export] public string E5Title { get; set; } =
        "Halfgate — Quartier du marché. Feuillet II.b — plan détaillé.";

    /// <summary>
    /// E5.B -- légende de section sous le titre. Locked Varn §6 E5.B. Voix A.
    /// </summary>
    [Export] public string E5Subtitle { get; set; } =
        "Bureau local des Arpenteurs — relevé détaillé.";

    /// <summary>
    /// E5.C (1/2) -- jauge Visibilité, sous-bandeau NM local quartier.
    /// Stub static J5. Real binding to a quartier-scoped
    /// <c>RegionalThreatLevel</c> resource lands post-MVP (Phase 8+).
    /// Voix A.
    /// </summary>
    [Export] public string E5VisibilityLabel { get; set; } = "Visibilité 0/10.";

    /// <summary>
    /// E5.C (2/2) -- jauge Méfiance, sous-bandeau NM local quartier. Stub
    /// static J5. Voix A.
    /// </summary>
    [Export] public string E5MenaceLabel { get; set; } = "Méfiance 0/10.";

    /// <summary>
    /// E5.C bis -- bouton retour (bas-gauche).
    /// Default Varn §6 E5.C: <c>Revenir à la cité</c>.
    /// </summary>
    [Export] public string E5BackButton { get; set; } = "Revenir à la cité";

    /// <summary>
    /// E5.S -- indicateur layer P2 (coin bas-droite). Locked Varn §7.5
    /// extension 2026-05-07 J5: <c>quartier</c> ajouté au vocabulaire ferme
    /// comme sous-layer de <c>cité</c> (D-J5-07). Voix A.
    /// <para>
    /// Variante alt pré-listée Varn §7.5 (swap target aux tests si grinçant) :
    /// <c>Pile cadastrale : cité (zoom quartier).</c>
    /// </para>
    /// </summary>
    [Export] public string E5LayerIndicator { get; set; } = "Pile cadastrale : quartier.";

    /// <summary>
    /// E5.I -- tooltip POI PNJ ambient "homme qui semble très occupé à ne rien faire".
    /// Locked verbatim Varn §6 E5.I. Voix D Marges (première instance voix D
    /// en UI persistante MVP — pré-brief §11 cohérence locks).
    /// </summary>
    [Export] public string E5AmbientHommeTooltip { get; set; } =
        "Un homme qui semble très occupé à ne rien faire.";
}
