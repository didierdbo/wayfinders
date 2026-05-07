using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E4 Character Sheet -- modal overlay rendered above E3/E5. J5 promotes
/// this from the J4 stub minimal (title + body + close) to the real
/// fiche cadastrale: portrait + 4 onglets latéraux + 6 sections + 2
/// caviardages overlay (D-J5-02 + D-J5-11 + D-J5-12).
///
/// <para>
/// <b>Architectural shape (J5).</b> Reuse maximal of J4 plumbing:
/// <list type="bullet">
///   <item><see cref="IModalOverlay"/> contract verbatim
///         (<see cref="OnOpen"/> / <see cref="OnClose"/>) -- no
///         lifecycle change.</item>
///   <item>Backdrop semi-transparent + Esc/click-outside affordances
///         preserved.</item>
///   <item>NpcId payload contract verbatim
///         (<see cref="E3CityHalfgate.NpcIdPayloadKey"/>) -- E3 and E5
///         dispatchers thread the same key.</item>
///   <item>OpeningStrings extension only (no resource removed) --
///         backward compat with J4 strings preserved.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>NpcRegistry seam (J5 NEW).</b> Display name + portrait key
/// resolution moves to the <see cref="NpcRegistry"/> autoload. The J4
/// hardcoded title-case fallback ("Kira" / "Dorn" derived from
/// <see cref="char.ToUpperInvariant"/>) is replaced by a real registry
/// lookup so post-MVP additions land in one place. Unknown NPCs return
/// <see cref="NpcCatalog.DefaultDisplayName"/> + the default portrait
/// placeholder (cohérent J3/J4 blocked wording).
/// </para>
///
/// <para>
/// <b>Portrait state (J5 stateful seam, J5 stateless behaviour).</b>
/// The state-driven portrait switch (<see cref="NpcPortraitState"/>) is
/// wired through <see cref="NpcRegistry.PortraitKeyFor"/> but J5
/// always feeds <see cref="NpcPortraitState.Calm"/> (D-J5-02 +
/// pre-brief Risk #2). When J6+ ships state-driven portraits, the
/// callsite flips one line + the test
/// <c>Default_portrait_state_is_calm_for_kira</c> surfaces the
/// behaviour change.
/// </para>
///
/// <para>
/// <b>Caviardage P9 (J5 NEW, D-J5-11 + Risk #1).</b> Two sections
/// (<c>Origine déclarée</c> + <c>Mentions et incidents</c>) are
/// caviardées via a <see cref="TextureRect"/> overlay textured with
/// <c>e4.redaction_overlay</c> + a centered <see cref="Label"/> bearing
/// the verbatim Varn §5 E4.M wording. <see cref="Control.MouseFilter"/>
/// on each overlay node + the inner body label is set to
/// <see cref="Control.MouseFilterEnum.Ignore"/> so the redaction is
/// purely visual -- it does NOT eat hover events for sibling sections
/// (Risk #1 mitigation specifically). Tests
/// <c>Redaction_overlay_present_on_origine_section</c> +
/// <c>Redaction_overlay_present_on_mentions_section</c> +
/// <c>Redaction_overlay_does_not_capture_mouse</c> pin this contract.
/// </para>
///
/// <para>
/// <b>Section-per-onglet (D-J5-12).</b> The <see cref="TabContainer"/>
/// natif Godot owns visibility per onglet -- we use the engine
/// affordance, no custom switching code. Mapping locked Varn §3.4.2:
/// <list type="bullet">
///   <item>Identité = section 1 (Identification) + section 2 (Origine).</item>
///   <item>Compétences = section 3 (Compétences attestées).</item>
///   <item>Dossier = section 4 (Mentions) + section 5 (Annexes).</item>
///   <item>Caviardages = section 6 (Zones masquées).</item>
/// </list>
/// Onglet <c>Caviardages</c> cliquable libre J5 (D-J5-13) -- TODO J6+ gate.
/// </para>
///
/// <para>
/// <b>Hot-swap discipline.</b> Portrait / overlay / panel textures are
/// resolved once on <see cref="OnOpen"/> via the
/// <see cref="AssetResolver"/> autoload -- the J3/J4 hot-swap pattern
/// (drop a PNG in user://wayfinders_visual_assets and reload the
/// modal). Cache lives in the resolver, not here.
/// </para>
/// </summary>
public partial class E4CharacterSheet : Control, IModalOverlay
{
    public string ModalId => "E4_CHARACTER_SHEET";
    public bool CanCloseOnEsc => true;
    public bool CanCloseOnClickOutside => true;

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";
    private const string FrameCardAssetKey = "e4.frame_card";
    private const string RedactionOverlayAssetKey = "e4.redaction_overlay";

    private OpeningStrings _strings = null!;
    private NpcRegistry? _npcRegistry;
    private AssetResolver _assetResolver = null!;

    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private TextureRect _portraitTextureRect = null!;
    private TabContainer _sectionsTabContainer = null!;

    private Label _identificationHeaderLabel = null!;
    private Label _identificationBodyLabel = null!;
    private Label _origineHeaderLabel = null!;
    private Label _origineBodyLabel = null!;
    private TextureRect _origineRedactionOverlay = null!;
    private Label _origineRedactionLabel = null!;

    private Label _competencesHeaderLabel = null!;
    private Label _competencesBodyLabel = null!;

    private Label _mentionsHeaderLabel = null!;
    private Label _mentionsBodyLabel = null!;
    private TextureRect _mentionsRedactionOverlay = null!;
    private Label _mentionsRedactionLabel = null!;
    private Label _marginsHeaderLabel = null!;
    private Label _marginsBodyLabel = null!;

    private Label _redactionZonesHeaderLabel = null!;
    private Label _redactionZonesBodyLabel = null!;

    private Button _closeButton = null!;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _npcRegistry = GetNodeOrNull<NpcRegistry>("/root/NpcRegistry");

        _titleLabel = GetNode<Label>("Panel/VBox/HeaderRow/HeaderVBox/TitleLabel");
        _subtitleLabel = GetNode<Label>("Panel/VBox/HeaderRow/HeaderVBox/SubtitleLabel");
        _portraitTextureRect = GetNode<TextureRect>("Panel/VBox/HeaderRow/PortraitTextureRect");
        _sectionsTabContainer = GetNode<TabContainer>("Panel/VBox/SectionsTabContainer");

        _identificationHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Identite/IdentificationHeaderLabel");
        _identificationBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Identite/IdentificationBodyLabel");
        _origineHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Identite/OrigineHeaderLabel");
        _origineBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Identite/OrigineBodyContainer/OrigineBodyLabel");
        _origineRedactionOverlay = GetNode<TextureRect>("Panel/VBox/SectionsTabContainer/Identite/OrigineBodyContainer/OrigineRedactionOverlay");
        _origineRedactionLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Identite/OrigineBodyContainer/OrigineRedactionLabel");

        _competencesHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Competences/CompetencesHeaderLabel");
        _competencesBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Competences/CompetencesBodyLabel");

        _mentionsHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Dossier/MentionsHeaderLabel");
        _mentionsBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Dossier/MentionsBodyContainer/MentionsBodyLabel");
        _mentionsRedactionOverlay = GetNode<TextureRect>("Panel/VBox/SectionsTabContainer/Dossier/MentionsBodyContainer/MentionsRedactionOverlay");
        _mentionsRedactionLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Dossier/MentionsBodyContainer/MentionsRedactionLabel");
        _marginsHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Dossier/MarginsHeaderLabel");
        _marginsBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Dossier/MarginsBodyLabel");

        _redactionZonesHeaderLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Caviardages/RedactionZonesHeaderLabel");
        _redactionZonesBodyLabel = GetNode<Label>("Panel/VBox/SectionsTabContainer/Caviardages/RedactionZonesBodyLabel");

        _closeButton = GetNode<Button>("Panel/VBox/FooterRow/CloseButton");

        // Pre-warm static asset slot. The frame_card placeholder ships as
        // a wallpaper-style fill BEHIND the panel via the .tscn StyleBox ;
        // the asset key is registered for hot-swap when Mira lands the
        // real iso parchment frame. Calling Resolve here ensures the
        // resolver logs the path on first open.
        _ = _assetResolver.Resolve(FrameCardAssetKey);

        // Section content (static stubs J5 -- these never change at
        // runtime in J5 because there is no per-NPC content variation
        // wired yet -- pre-brief §3.1.2 voix A neutre stubs).
        _identificationHeaderLabel.Text = _strings.E4SectionIdentificationHeader;
        _identificationBodyLabel.Text = _strings.E4SectionIdentificationBody;
        _origineHeaderLabel.Text = _strings.E4SectionOriginHeader;
        // Body text under the redaction overlay -- visible only if the
        // overlay is hidden (J6+ gate post-palier compagnie). Keeping
        // a placeholder string here means the section is non-empty in
        // the layout calculator, so the overlay sits on a real box not
        // a zero-height container.
        _origineBodyLabel.Text = "(contenu réservé aux Arpenteurs autorisés)";

        _competencesHeaderLabel.Text = _strings.E4SectionSkillsHeader;
        _competencesBodyLabel.Text = _strings.E4SectionSkillsBody;

        _mentionsHeaderLabel.Text = _strings.E4SectionMentionsHeader;
        _mentionsBodyLabel.Text = "(dossier sensible — contenu non communiqué)";
        _marginsHeaderLabel.Text = _strings.E4SectionMarginsHeader;
        _marginsBodyLabel.Text = _strings.E4SectionMarginsBody;

        _redactionZonesHeaderLabel.Text = _strings.E4SectionRedactionHeader;
        _redactionZonesBodyLabel.Text = _strings.E4SectionRedactionBody;

        // Caviardage overlays. The TextureRect carries the redaction
        // pattern (Mira spec §6 E4.5) and the label sits on top with the
        // verbatim Varn E4.M wording. Both are MouseFilter.Ignore so the
        // redaction is purely visual and does not eat hover events for
        // surrounding controls (Risk #1 mitigation, pinned by
        // E4FicheRealContractTests).
        SetupRedactionOverlay(_origineRedactionOverlay, _origineRedactionLabel, _origineBodyLabel);
        SetupRedactionOverlay(_mentionsRedactionOverlay, _mentionsRedactionLabel, _mentionsBodyLabel);

        // TabContainer label override -- Godot reads each child Node's
        // Name as the tab label (J4 quirk, doc'd in E3CityHalfgate
        // Risk #1). We rename child nodes to the Varn-locked libellés
        // so the .tscn (where ASCII-only node names are easier to
        // author) keeps the screen-displayed labels in sync with the
        // OpeningStrings resource.
        var identityTab = _sectionsTabContainer.GetNode<Control>("Identite");
        var skillsTab = _sectionsTabContainer.GetNode<Control>("Competences");
        var dossierTab = _sectionsTabContainer.GetNode<Control>("Dossier");
        var redactionTab = _sectionsTabContainer.GetNode<Control>("Caviardages");
        identityTab.Name = _strings.E4TabIdentity;
        skillsTab.Name = _strings.E4TabSkills;
        dossierTab.Name = _strings.E4TabDossier;
        redactionTab.Name = _strings.E4TabRedaction;

        _closeButton.Text = _strings.E4ModalCloseButton;
        _closeButton.Pressed += OnClosePressed;
    }

    public override void _ExitTree()
    {
        if (_closeButton is not null) _closeButton.Pressed -= OnClosePressed;
    }

    private void SetupRedactionOverlay(
        TextureRect overlay,
        Label overlayLabel,
        Label hiddenBodyLabel)
    {
        overlay.Texture = _assetResolver.Resolve(RedactionOverlayAssetKey);

        // Defense in depth (Risk #1): every node in the overlay stack is
        // MouseFilter.Ignore so a hover crossing this region does not
        // get captured by the redaction. The body label below is also
        // Ignore -- the section as a whole stays inert to hover (J5 has
        // no per-section tooltip). When J6+ wires per-section hovers,
        // we revisit by giving the parent container the desired filter
        // and keeping the overlay on Ignore.
        overlay.MouseFilter = MouseFilterEnum.Ignore;
        overlayLabel.MouseFilter = MouseFilterEnum.Ignore;
        hiddenBodyLabel.MouseFilter = MouseFilterEnum.Ignore;

        // Modulate keeps the overlay opaque enough to read as a
        // caviardage but lets the parchment palette breathe through ;
        // Mira will refine when she ships e4.redaction_overlay v1.
        overlay.Modulate = new Color(0.18f, 0.14f, 0.11f, 0.92f);
        overlay.Visible = true;

        overlayLabel.Text = _strings.E4RedactionBlockTemplate;
    }

    private async void OnClosePressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.CloseModal();
    }

    public Task OnOpen(ScreenContext context, CancellationToken ct)
    {
        // Read the NpcId from the Payload bag (J4 contract preserved).
        // Fall back to null so the registry returns the default entry
        // when no payload was threaded (e.g. modal opened from a future
        // demo button or a hot-edit accident).
        var npcId = context?.Payload?.TryGetValue(E3CityHalfgate.NpcIdPayloadKey, out var raw) == true
            ? raw?.ToString()
            : null;

        // Resolve the registry entry once -- the call site below uses
        // it for both display name and portrait key composition.
        var registry = _npcRegistry ?? GetNodeOrNull<NpcRegistry>("/root/NpcRegistry");

        var displayName = registry?.DisplayNameFor(npcId) ?? FallbackDisplayName(npcId);
        var portraitState = NpcPortraitState.Calm; // J5: hardcoded calm. TODO J6+: state-driven via real Kira state.
        var portraitKey = registry?.PortraitKeyFor(npcId, portraitState) ?? NpcCatalog.DefaultPortraitKey;

        var ficheNumber = ResolveFicheNumber(npcId);

        _titleLabel.Text = _strings.E4ModalTitleTemplate
            .Replace("[xxx]", ficheNumber)
            .Replace("[Nom officiel]", displayName);

        _subtitleLabel.Text = string.Format(
            _strings.E4PortraitStateTemplate,
            portraitState.ToFrenchLowercase());

        _portraitTextureRect.Texture = _assetResolver.Resolve(portraitKey);

        // Default the user back to onglet 1 (Identité) every open, so
        // a re-opened modal does not surface mid-Caviardages from a
        // previous session. The TabContainer is engine-owned, so the
        // currently-selected tab persists across instantiations of the
        // PackedScene unless we reset it explicitly.
        _sectionsTabContainer.CurrentTab = 0;

        return Task.CompletedTask;
    }

    public Task OnClose(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// J4 fallback when the autoload <see cref="NpcRegistry"/> is not
    /// present (xUnit cannot spin one up ; the Godot bootstrap sets it
    /// up at app start). Mirrors the J4 char-toupper trick verbatim so
    /// the J4 manual checklist behaviour is preserved when the autoload
    /// is missing.
    /// </summary>
    private static string FallbackDisplayName(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId) || npcId == "?")
            return "?";
        return char.ToUpperInvariant(npcId[0]) + npcId[1..];
    }

    /// <summary>
    /// Map an NPC id to its cadastre fiche number (the "[xxx]" slot in
    /// the Varn E4.A title template). Delegates to
    /// <see cref="NpcCatalog.LookupFicheNumber"/> so the mapping table
    /// lives in one Godot-free place and xUnit can pin the contract
    /// (E4FicheRealContractTests.ResolveFicheNumber_*).
    /// </summary>
    public static string ResolveFicheNumber(string? npcId) =>
        NpcCatalog.LookupFicheNumber(npcId);
}
