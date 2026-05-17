using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes.Scratch;

/// <summary>
/// M1 substep 4c probe — text-only Mission panel that proves the
/// mission-emergence chain runs end-to-end :
///
/// <list type="number">
///   <item><see cref="WorldSimTick"/> ticks → POST <c>/api/world/tick</c>
///         → <see cref="EmergentMissionDto"/> lands in
///         <see cref="GameState.PendingMissions"/>.</item>
///   <item>Probe subscribes to
///         <see cref="WorldSimTick.MissionEmerged"/> ; on signal, pulls
///         the head mission and renders it in 3 Labels + a persona
///         button list.</item>
///   <item>Player clicks a persona button → <c>Affect</c> enables.
///         Click <c>Affect</c> → POST <c>/api/world/mission/resolve</c>
///         → tags appended to
///         <see cref="GameState.PersonaLegacy"/> + mission removed
///         from <see cref="GameState.PendingMissions"/>.</item>
/// </list>
///
/// <para>
/// <b>Why text-only, not a SubViewport sub-iso panel.</b> The brief
/// Larry forwarded explicitly authorised "ultra-minimal text-only
/// (3 labels + 2 boutons) plutôt qu'un beau panel". The 8 SubViewport
/// traps from <c>feedback_godot_rendering_input_traps.md</c> are real
/// engineering cost ; M1's job is to prove the chain runs, not to
/// look pretty. The Compagnie panel probe (commit 0c028d1) already
/// holds the SubViewport pattern for when the Mission panel graduates
/// to its own sub-iso scene at M2+.
/// </para>
///
/// <para>
/// <b>Strict isolation.</b>
/// <c>metadata/skip_opening_bootstrap = true</c> on the root prevents
/// the OpeningBootstrap autoload from injecting cartographer's-office
/// content over the probe (lesson 1bis from the rendering traps
/// memory). Reversibility : delete
/// <c>scenes/scratch/MissionPanel*</c> +
/// <c>scripts/screens/MissionPanelLogic.cs</c> +
/// <c>tests/Phase9_3DBacking/MissionPanelLogicTests.cs</c>. No
/// production touch.
/// </para>
///
/// <para>
/// <b>Persona seeding.</b> The probe seeds
/// <see cref="GameState.CompanyPersonas"/> with 3 Varn-aligned
/// stub <see cref="CharacterStateDto"/> objects (Hodge / Mirelle /
/// Wattan, same names as <see cref="CompagniePanelLogic.DefaultPersonas"/>)
/// covering different DEX/WIS buckets so the ±1 filter has a
/// non-trivial result. This lets the e2e test see real
/// <see cref="EmergentMissionDto.EligiblePersonas"/> populated by the
/// server, not a Varn option (c) fallback.
/// </para>
/// </summary>
public partial class MissionPanelProbe : Node2D
{
    // Bindings : NodePaths default to the actual scene hierarchy in
    // MissionPanelProbe.tscn, where the Panel lives under a CanvasLayer
    // "UI" (layer=10) so it renders on top of the Background2D
    // CanvasLayer. The original "Panel/HeaderLabel" defaults silently
    // resolved to null because the CanvasLayer parent was missing from
    // the path -- this is the trap the binding preflight below now
    // catches. See feedback_godot_rendering_input_traps memory.
    [Export] public NodePath HeaderLabelPath { get; set; } = "UI/Panel/HeaderLabel";
    [Export] public NodePath HookLabelPath { get; set; } = "UI/Panel/HookLabel";
    [Export] public NodePath EligibleLabelPath { get; set; } = "UI/Panel/EligibleLabel";
    [Export] public NodePath PersonasContainerPath { get; set; } = "UI/Panel/PersonasContainer";
    [Export] public NodePath AffectButtonPath { get; set; } = "UI/Panel/AffectButton";
    [Export] public NodePath DeclineButtonPath { get; set; } = "UI/Panel/DeclineButton";
    [Export] public NodePath ToastLabelPath { get; set; } = "UI/Panel/ToastLabel";
    [Export] public NodePath QueueStatusLabelPath { get; set; } = "UI/Panel/QueueStatusLabel";
    [Export] public NodePath TickLabelPath { get; set; } = "UI/Panel/TickLabel";

    private Label? _headerLabel;
    private Label? _hookLabel;
    private Label? _eligibleLabel;
    private VBoxContainer? _personasContainer;
    private Button? _affectButton;
    private Button? _declineButton;
    private Label? _toastLabel;
    private Label? _queueStatusLabel;
    private Label? _tickLabel;

    private GameState? _gameState;
    private ApiClient? _apiClient;
    private WorldSimTick? _worldSimTick;
    private MissionStore? _missionStore;

    private string? _selectedPersonaId;
    private int _currentTick;

    public override void _EnterTree()
    {
        GD.Print("***** [PROBE Mission] _EnterTree -- M1 substep 4c text-only Mission panel");
    }

    public override void _Ready()
    {
        GD.Print("[PROBE Mission] _Ready -- wiring panel + autoload subscriptions");

        _headerLabel = GetNodeOrNull<Label>(HeaderLabelPath);
        _hookLabel = GetNodeOrNull<Label>(HookLabelPath);
        _eligibleLabel = GetNodeOrNull<Label>(EligibleLabelPath);
        _personasContainer = GetNodeOrNull<VBoxContainer>(PersonasContainerPath);
        _affectButton = GetNodeOrNull<Button>(AffectButtonPath);
        _declineButton = GetNodeOrNull<Button>(DeclineButtonPath);
        _toastLabel = GetNodeOrNull<Label>(ToastLabelPath);
        _queueStatusLabel = GetNodeOrNull<Label>(QueueStatusLabelPath);
        _tickLabel = GetNodeOrNull<Label>(TickLabelPath);

        _gameState = GetTree()?.Root?.GetNodeOrNull<GameState>("GameState");
        _apiClient = GetTree()?.Root?.GetNodeOrNull<ApiClient>("ApiClient");
        _worldSimTick = GetTree()?.Root?.GetNodeOrNull<WorldSimTick>("WorldSimTick");
        _missionStore = GetTree()?.Root?.GetNodeOrNull<MissionStore>("MissionStore");

        if (_gameState is null) GD.PushError("[PROBE Mission] GameState autoload missing");
        if (_apiClient is null) GD.PushError("[PROBE Mission] ApiClient autoload missing");
        if (_worldSimTick is null) GD.PushError("[PROBE Mission] WorldSimTick autoload missing");

        // Inv. preflight : verify every NodePath export resolved to a
        // real Label / Container / Button. The original 4c bug was that
        // the defaults missed the "UI/" CanvasLayer prefix, so every
        // GetNodeOrNull returned null silently and the panel stayed at
        // its placeholder text even after MissionEmerged fired. Defense
        // in depth, cohérent avec feedback_godot_rendering_input_traps :
        // if a binding is broken, surface it loudly + visually instead
        // of letting the probe look like the API/signal layer is wrong.
        RunBindingPreflight();

        SeedCompany();
        WireSignals();
        RenderEmpty();

        GD.Print($"[PROBE Mission] ready -- seeded {_gameState?.CompanyPersonas.Count ?? 0} personas, awaiting first MissionEmerged signal");
    }

    public override void _ExitTree()
    {
        // Disconnection discipline.
        if (_worldSimTick is not null)
        {
            _worldSimTick.MissionEmerged -= OnMissionEmerged;
            _worldSimTick.TickAdvanced -= OnTickAdvanced;
        }
        if (_affectButton is not null) _affectButton.Pressed -= OnAffectPressed;
        if (_declineButton is not null) _declineButton.Pressed -= OnDeclinePressed;
    }

    /// <summary>
    /// Inv. preflight for the Label/Button/Container bindings. Logs a
    /// PushError per broken binding and paints a red banner in
    /// <see cref="_toastLabel"/> if it itself resolved (so the bug is
    /// visible in the running scene, not buried in Output). Idempotent
    /// and side-effect free outside logging + the toast paint.
    ///
    /// <para>
    /// <b>Why this exists.</b> M1 substep 4c shipped with NodePath
    /// defaults that silently resolved to null ("Panel/HeaderLabel"
    /// instead of "UI/Panel/HeaderLabel"), which made the e2e test
    /// look like the signal/API layer was broken when in fact the bind
    /// was. Same pattern as the SubViewport / CanvasLayer / InputMap
    /// preflights in the rendering-traps memory : check the engine
    /// invariants at _Ready and shout if they don't hold.
    /// </para>
    /// </summary>
    private void RunBindingPreflight()
    {
        var broken = new System.Collections.Generic.List<string>();
        if (_headerLabel is null) broken.Add($"HeaderLabel ({HeaderLabelPath})");
        if (_hookLabel is null) broken.Add($"HookLabel ({HookLabelPath})");
        if (_eligibleLabel is null) broken.Add($"EligibleLabel ({EligibleLabelPath})");
        if (_personasContainer is null) broken.Add($"PersonasContainer ({PersonasContainerPath})");
        if (_affectButton is null) broken.Add($"AffectButton ({AffectButtonPath})");
        if (_declineButton is null) broken.Add($"DeclineButton ({DeclineButtonPath})");
        if (_toastLabel is null) broken.Add($"ToastLabel ({ToastLabelPath})");
        if (_queueStatusLabel is null) broken.Add($"QueueStatusLabel ({QueueStatusLabelPath})");
        if (_tickLabel is null) broken.Add($"TickLabel ({TickLabelPath})");

        if (broken.Count == 0)
        {
            GD.Print("[PROBE Mission] Inv. preflight OK -- all 9 bindings resolved");
            return;
        }

        var msg = $"[PROBE Mission] Inv. preflight FAILED -- {broken.Count} broken binding(s) : {string.Join(" ; ", broken)}";
        GD.PushError(msg);
        // If the toast itself resolved, paint the failure on it so the
        // running scene shows the bug instead of looking dead.
        if (_toastLabel is not null)
            _toastLabel.Text = $"[BINDING ERROR] {broken.Count} NodePath(s) unresolved -- check Output";
    }

    /// <summary>
    /// Seed <see cref="GameState.CompanyPersonas"/> with 3 stub
    /// <see cref="CharacterStateDto"/> covering different DEX/WIS
    /// buckets so the server-side ±1 filter has a non-trivial result.
    /// Idempotent : if the autoload already has personas (e.g. another
    /// probe already seeded), we don't double up.
    /// </summary>
    private void SeedCompany()
    {
        if (_gameState is null) return;
        if (_gameState.CompanyPersonas.Count > 0) return;

        // Hodge : DEX mid (matches scout_route mid filter), WIS low.
        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "hodge",
            CharClass = "scout",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.Mid,
            DexBucket = DescriptorBucketValues.Mid,
            WisBucket = DescriptorBucketValues.Low,
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
        });
        // Mirelle : DEX high (matches scout_route mid via +1), WIS high
        // (matches parley_local mid via +1).
        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "mirelle",
            CharClass = "diplomat",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.Low,
            DexBucket = DescriptorBucketValues.High,
            WisBucket = DescriptorBucketValues.High,
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
        });
        // Wattan : DEX very-low (out of mid window), WIS mid (matches
        // parley_local mid). Tests the per-bucket selectivity.
        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "wattan",
            CharClass = "warden",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.High,
            DexBucket = DescriptorBucketValues.VeryLow,
            WisBucket = DescriptorBucketValues.Mid,
            HpBucket = HpBucketValues.Scratched,
            StressBucket = StressBucketValues.Tense,
        });

        GD.Print("[PROBE Mission] seeded 3 personas : hodge (DEX:mid), mirelle (DEX:high WIS:high), wattan (WIS:mid)");
    }

    private void WireSignals()
    {
        if (_worldSimTick is not null)
        {
            _worldSimTick.MissionEmerged += OnMissionEmerged;
            _worldSimTick.TickAdvanced += OnTickAdvanced;
        }
        if (_affectButton is not null) _affectButton.Pressed += OnAffectPressed;
        if (_declineButton is not null) _declineButton.Pressed += OnDeclinePressed;
    }

    private void OnTickAdvanced(int tick)
    {
        _currentTick = tick;
        if (_tickLabel is not null) _tickLabel.Text = $"tick: {tick}";
    }

    /// <summary>
    /// Signal handler from <see cref="WorldSimTick.MissionEmerged"/>.
    /// Called on the main thread (the autoload uses CallDeferred to
    /// marshal the emit). Updates the panel from the head of
    /// <see cref="GameState.PendingMissions"/>.
    /// </summary>
    private void OnMissionEmerged(string missionId)
    {
        GD.Print($"[PROBE Mission] received MissionEmerged signal id={missionId}");
        RenderHeadMission();
    }

    private void RenderHeadMission()
    {
        if (_gameState is null) return;
        if (_gameState.PendingMissions.Count == 0)
        {
            RenderEmpty();
            return;
        }

        var mission = _gameState.PendingMissions[0];
        _selectedPersonaId = null;

        if (_headerLabel is not null) _headerLabel.Text = MissionPanelLogic.FormatMissionHeader(mission);
        if (_hookLabel is not null) _hookLabel.Text = mission.NarrativeHook;
        if (_eligibleLabel is not null) _eligibleLabel.Text = MissionPanelLogic.FormatEligiblePersonas(mission);

        RebuildPersonaButtons(mission);
        UpdateButtons(mission);
        UpdateQueueStatus();
    }

    private void RenderEmpty()
    {
        _selectedPersonaId = null;
        if (_headerLabel is not null) _headerLabel.Text = "[MISSION] (none — waiting for tick cadence)";
        if (_hookLabel is not null) _hookLabel.Text = "";
        if (_eligibleLabel is not null) _eligibleLabel.Text = "";
        ClearPersonaButtons();
        UpdateButtons(mission: null);
        UpdateQueueStatus();
    }

    private void RebuildPersonaButtons(EmergentMissionDto mission)
    {
        ClearPersonaButtons();
        if (_personasContainer is null) return;

        for (var i = 0; i < mission.EligiblePersonas.Count; i++)
        {
            var personaId = mission.EligiblePersonas[i];
            var btn = new Button { Text = personaId, ToggleMode = true };
            // Capture for closure.
            var captured = personaId;
            btn.Toggled += (bool pressed) => OnPersonaToggled(captured, pressed, btn);
            _personasContainer.AddChild(btn);
        }
    }

    private void ClearPersonaButtons()
    {
        if (_personasContainer is null) return;
        foreach (var child in _personasContainer.GetChildren())
            child.QueueFree();
    }

    private void OnPersonaToggled(string personaId, bool pressed, Button source)
    {
        if (_personasContainer is null) return;

        // Single-select : if a button was pressed, untoggle the
        // others. We can't use Godot's ButtonGroup easily on
        // dynamically created buttons without an extra resource ;
        // hand-rolling the radio behaviour here.
        if (pressed)
        {
            foreach (var child in _personasContainer.GetChildren())
            {
                if (child is Button other && other != source)
                    other.SetPressedNoSignal(false);
            }
            _selectedPersonaId = personaId;
        }
        else if (_selectedPersonaId == personaId)
        {
            _selectedPersonaId = null;
        }

        if (_gameState is not null && _gameState.PendingMissions.Count > 0)
            UpdateButtons(_gameState.PendingMissions[0]);
    }

    private void UpdateButtons(EmergentMissionDto? mission)
    {
        if (_affectButton is not null)
        {
            var affectState = MissionPanelLogic.DecideAffectButtonState(mission, _selectedPersonaId);
            _affectButton.Visible = affectState != MissionPanelLogic.AffectButtonState.Hidden;
            _affectButton.Disabled = affectState != MissionPanelLogic.AffectButtonState.Enabled;
        }
        if (_declineButton is not null)
        {
            var declineState = MissionPanelLogic.DecideDeclineButtonState(mission);
            _declineButton.Visible = declineState != MissionPanelLogic.DeclineButtonState.Hidden;
        }
    }

    private void UpdateQueueStatus()
    {
        if (_queueStatusLabel is null || _gameState is null) return;
        _queueStatusLabel.Text =
            $"queue: {_gameState.PendingMissions.Count} pending mission(s) | "
            + $"legacy tags: {TotalLegacyTagCount(_gameState)}";
    }

    private static int TotalLegacyTagCount(GameState gs)
    {
        var n = 0;
        foreach (var kv in gs.PersonaLegacy) n += kv.Value.Count;
        return n;
    }

    private async void OnAffectPressed()
    {
        if (_gameState is null || _apiClient is null) return;
        if (_gameState.PendingMissions.Count == 0) return;
        if (string.IsNullOrEmpty(_selectedPersonaId)) return;

        var mission = _gameState.PendingMissions[0];
        var request = MissionPanelLogic.BuildAffectRequest(mission, _selectedPersonaId, _currentTick);
        await ResolveAndApply(request, mission, isAffect: true);
    }

    private async void OnDeclinePressed()
    {
        if (_gameState is null || _apiClient is null) return;
        if (_gameState.PendingMissions.Count == 0) return;

        var mission = _gameState.PendingMissions[0];
        var request = MissionPanelLogic.BuildDeclineRequest(mission, _currentTick);
        await ResolveAndApply(request, mission, isAffect: false);
    }

    /// <summary>
    /// Common path for Affect / Décliner : POST resolve, drop the
    /// mission off the queue, append tags, render the toast. Safe to
    /// call from a Button.Pressed handler — the caller is async void
    /// (UI fire-and-forget) but the actual API hop awaits the
    /// resolution before mutating state.
    ///
    /// <para>
    /// <b>Main-thread discipline.</b> Even though
    /// <see cref="ApiClient.ResolveMissionAsync"/> uses
    /// <c>ConfigureAwait(false)</c>, the continuation here resumes on
    /// whatever thread Godot's <c>SynchronizationContext</c> was
    /// installed on (the main thread for engine-bound code paths). We
    /// still touch GameState and Labels directly because Godot's C#
    /// support installs a main-thread SynchronizationContext that
    /// captures correctly when an <c>async void</c> handler is fired
    /// from a Button signal. If a future Godot version changes that
    /// (4.7+ has improved threading internals), we'd switch to
    /// CallDeferred here too — same pattern as
    /// <see cref="WorldSimTick.OnMissionEmergedDeferred"/>.
    /// </para>
    /// </summary>
    private async Task ResolveAndApply(
        MissionResolveRequestDto request,
        EmergentMissionDto mission,
        bool isAffect)
    {
        if (_apiClient is null || _gameState is null) return;

        GD.Print($"[RESOLVE] posting mission_id={request.MissionId} outcome={request.Outcome} assigned=[{string.Join(",", request.AssignedPersonas)}]");

        var result = await _apiClient.ResolveMissionAsync(request);

        switch (result)
        {
            case Result<MissionResolveResponseDto, ApiError>.Success ok:
                ApplyResolutionSuccess(ok.Value, mission, isAffect);
                break;
            case Result<MissionResolveResponseDto, ApiError>.Failure fail:
                ApplyResolutionFailure(fail.Error);
                break;
        }
    }

    private void ApplyResolutionSuccess(
        MissionResolveResponseDto response,
        EmergentMissionDto mission,
        bool isAffect)
    {
        if (_gameState is null) return;

        // Append the tags.
        for (var i = 0; i < response.TagsCreated.Count; i++)
        {
            var tag = response.TagsCreated[i];
            _gameState.AppendPersonaLegacyTag(tag);
            GD.Print($"[LEGACY TAG] persona={tag.PersonaId} mission={tag.MissionId} type={tag.MissionType} outcome={tag.Outcome}");
        }

        // §A.8.D5 cascade — drop via MissionStore.RemoveById
        // (GameState.PendingMissions is now a read-only projection).
        _missionStore?.RemoveById(mission.Id);

        // Render the toast.
        if (_toastLabel is not null)
        {
            var verb = isAffect ? "AFFECTED" : "DECLINED";
            _toastLabel.Text = $"[{verb}] {MissionPanelLogic.FormatLegacyTagToast(response.TagsCreated)}";
        }

        // Re-render the next pending mission, or empty.
        RenderHeadMission();
    }

    private void ApplyResolutionFailure(ApiError error)
    {
        var msg = error switch
        {
            ApiError.NotReachable nr => $"NOT REACHABLE: {nr.Reason}",
            ApiError.ServerError se => $"SERVER {se.StatusCode}: {se.Body}",
            ApiError.DeserializationError de => $"PARSE: {de.Reason}",
            ApiError.Cancelled => "CANCELLED",
            _ => "(unknown)"
        };
        GD.PushWarning($"[RESOLVE FAILED] {msg}");
        if (_toastLabel is not null) _toastLabel.Text = $"[ERROR] {msg}";
    }
}
