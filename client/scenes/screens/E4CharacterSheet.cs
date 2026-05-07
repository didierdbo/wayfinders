using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E4 Character Sheet -- modal overlay rendered above E3/E5. J4 stub
/// minimal (D-J4-07 option α, Rune J4 pre-brief §2.7) -- title + body
/// stub + close button + Esc/click-outside affordances.
///
/// <para>
/// J5 will ship the 4 onglets (Identité, Compétences, Dossier,
/// Caviardages), the iso fiche frame (key <c>e4.frame_card</c>), the
/// 3-state Kira portrait (calm/alert/wounded), the visual caviardages
/// (P9 hook surfaced as ColorRect black + Label), and richer copy.
/// </para>
///
/// <para>
/// <b>J4 stub: title template + body stub + close button.</b> The
/// title interpolates the locked Varn §5.1 E4.A template
/// <c>Fiche cadastrale n°[xxx] — Sujet : [Nom officiel].</c>
/// substituting <c>001</c> for the fiche number and the NpcId from
/// the <see cref="ScreenContext.Payload"/> bag for the sujet. The body
/// reads from the <see cref="OpeningStrings.E4ModalStubBody"/>
/// resource: <c>Contenu en cours de rédaction par les Arpenteurs.</c>.
/// </para>
///
/// <para>
/// <b>NpcId payload contract (J4 D-J4-07).</b> The dispatcher in
/// <see cref="PoiDispatchLogic"/> threads the clicked PNJ id through
/// <see cref="PoiDispatchResult.NpcId"/> ; E3 wraps it in a
/// <c>ScreenContext</c> with key
/// <see cref="E3CityHalfgate.NpcIdPayloadKey"/> = <c>"E4.NpcId"</c>.
/// On <see cref="OnOpen"/> we read that key, fall back to "?" if
/// missing.
/// </para>
///
/// <para>
/// <b>P9 caviardage hook (post-MVP slot):</b> J5 fiche will compose 2
/// static black <c>ColorRect</c> + <c>Label</c> "[Caviardage classe X —
/// accès refusé.]". Slot reserved as a comment ; no
/// <c>RedactionBlock</c> component in J4.
/// </para>
/// </summary>
public partial class E4CharacterSheet : Control, IModalOverlay
{
    public string ModalId => "E4_CHARACTER_SHEET";
    public bool CanCloseOnEsc => true;
    public bool CanCloseOnClickOutside => true;

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";

    private OpeningStrings _strings = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private Button _closeButton = null!;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _titleLabel = GetNode<Label>("Panel/VBox/TitleLabel");
        _bodyLabel = GetNode<Label>("Panel/VBox/BodyLabel");
        _closeButton = GetNode<Button>("Panel/VBox/CloseButton");

        _bodyLabel.Text = _strings.E4ModalStubBody;
        _closeButton.Text = _strings.E4ModalCloseButton;

        _closeButton.Pressed += OnClosePressed;
    }

    public override void _ExitTree()
    {
        if (_closeButton is not null) _closeButton.Pressed -= OnClosePressed;
    }

    private async void OnClosePressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.CloseModal();
    }

    public Task OnOpen(ScreenContext context, CancellationToken ct)
    {
        // Read the NpcId from the Payload bag (J4 D-J4-07 contract).
        // Fall back to "?" so the title still renders if the modal is
        // opened from the J1 demo button (no payload threading).
        var npcId = context?.Payload?.TryGetValue(E3CityHalfgate.NpcIdPayloadKey, out var raw) == true
            ? raw?.ToString() ?? "?"
            : "?";

        // Capitalize the displayed name for the Cadastre voice (the
        // .tres stores lower-case "kira" / "dorn" as PoiId). J5 will
        // resolve a real DisplayName from a PNJ resource ; J4 just
        // title-cases the id.
        var displaySubject = string.IsNullOrEmpty(npcId) || npcId == "?"
            ? "?"
            : char.ToUpperInvariant(npcId[0]) + npcId[1..];

        _titleLabel.Text = _strings.E4ModalTitleTemplate
            .Replace("[xxx]", "001")
            .Replace("[Nom officiel]", displaySubject);

        return Task.CompletedTask;
    }

    public Task OnClose(CancellationToken ct) => Task.CompletedTask;
}
