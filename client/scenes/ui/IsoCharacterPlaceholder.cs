using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 3+4 placeholder Control rendering a procedural 2.5D iso
/// parallelepiped silhouette for one M1 roster NPC (kira / dorn /
/// vell). Lives inside the <see cref="MissionRecruitPanel"/>'s right
/// half ; also reusable in the <see cref="CompanyPanel"/>'s socle as
/// the mini "occupied" silhouette once a recruit lands (étape 6).
///
/// <para>
/// <b>Why procedural Draw, not a Sprite2D.</b> Mira has not shipped iso
/// character bitmaps for the M1 roster at recruit landing time
/// (2026-05-17). A procedural unit-tinted parallelepiped reads as
/// "character A vs character B" via colour alone while we wait for the
/// real assets. The swap is a one-line change : replace the
/// <see cref="_Draw"/> override with a child <see cref="Sprite2D"/> at
/// the same anchor and the panel layout is unchanged.
/// </para>
///
/// <para>
/// <b>Geometry.</b> Three visible faces of a 2:1 iso box, all
/// computed by <see cref="IsoCharacterGeometryLogic"/> (pure-C#,
/// engine-free, xUnit-pinned). Two visible faces meet at a common
/// front-top apex, pinned by the geometry test
/// <c>Three_faces_meet_at_common_front_top_vertex</c>.
/// </para>
///
/// <para>
/// <b>API surface.</b>
/// <list type="bullet">
///   <item><see cref="SetNpc"/> -- pick the unit tint + display name.</item>
///   <item><see cref="SetSourceContext"/> (étape 6) -- declare the
///         drag-drop role (host vs decorative) and the mission id
///         that travels in the drag payload.</item>
///   <item><see cref="SetGreyedOut"/> (étape 6) -- desaturate the
///         silhouette + label when the perso is currently on the
///         Compagnie socle (the perso visually "left" the panel) AND
///         suppress the drag source for the duration.</item>
///   <item><see cref="SetCompactMode"/> (étape 6) -- hide the name
///         label and render at the size given by the parent (used by
///         the socle's occupied-state mini silhouette).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Drag/drop discipline (memo feedback_godot_rendering_input_traps).</b>
/// When configured as <see cref="SourceRole.RecruitPanelHost"/>, this
/// Control acts as BOTH a drag source (when not greyed out) AND a
/// reverse-drop target (always, so the player can drag the perso back
/// even while it sits on the socle). MouseFilter flips from Ignore to
/// Stop -- Godot will NOT initialize a drag from a Control that does
/// not capture mouse events. Default (Decorative) keeps Ignore so the
/// parent panel's hit-testing is undisturbed.
/// </para>
/// </summary>
public partial class IsoCharacterPlaceholder : Control
{
    /// <summary>Default cmin size -- can be overridden per instance.</summary>
    private static readonly Vector2 DefaultMinSize = new(180, 260);

    /// <summary>Default cmin size in compact mode (no label band, used
    /// by the company socle's occupied-state mini silhouette).</summary>
    private static readonly Vector2 CompactMinSize = new(120, 160);

    /// <summary>Default name label height band reserved above the
    /// silhouette.</summary>
    private const float LabelBandPx = 32f;

    /// <summary>Padding inside the Control's rect, around the
    /// silhouette + label.</summary>
    private const float PaddingPx = 12f;

    private const float DepthOverWidth = 1f / 3f;
    private const float SideFaceMultiplier = 0.7f;
    private const float TopFaceMultiplier = 1.15f;

    /// <summary>Modulate colour applied to the entire Control when
    /// greyed-out (step 6). Half-alpha desaturated parchment so the
    /// silhouette reads as "still here, but inactive".</summary>
    private static readonly Color GreyedOutModulate = new(0.55f, 0.55f, 0.55f, 0.45f);

    /// <summary>Drag preview alpha. Half-transparent so the ghost
    /// reads as "what you are carrying" without occluding the drop
    /// targets underneath.</summary>
    private const float DragPreviewAlpha = 0.55f;

    private Label? _nameLabel;
    private string? _npcId;
    private Color _baseTint = new(0.5f, 0.5f, 0.5f, 1f);

    // -------- Step 6 additions --------

    /// <summary>The role this placeholder plays in the étape 6
    /// drag&amp;drop seam.</summary>
    private SourceRole _role = SourceRole.Decorative;

    /// <summary>Mission id that owns this placeholder (recruit panel
    /// only). Travels with the drag payload so the drop site can log
    /// + correlate.</summary>
    private string? _missionId;

    /// <summary>Hide the name label + override cmin to the compact
    /// size (used inside the company socle's occupied state).</summary>
    private bool _compactMode;

    /// <summary>True iff <see cref="SetGreyedOut"/> last set true.
    /// Suppresses the drag source (the perso is "on the socle" -- you
    /// pick it up from the socle, not from here, while in this state).</summary>
    private bool _greyedOut;

    /// <summary>
    /// Fired when a reverse drag (from the Compagnie socle back to
    /// this placeholder) lands. Payload carries the NPC id so the
    /// owner (the MissionRecruitPanel) can fire the state-machine
    /// CharacterReturnedFromSocle event.
    /// </summary>
    [Signal]
    public delegate void CharacterReturnedFromSocleEventHandler(string npcId);

    public override void _Ready()
    {
        CustomMinimumSize = _compactMode ? CompactMinSize : DefaultMinSize;
        MouseFilter = ResolveMouseFilter();

        _nameLabel = new Label
        {
            Name = "NpcNameLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = false,
            Visible = !_compactMode,
        };
        _nameLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        _nameLabel.OffsetTop = 0;
        _nameLabel.OffsetBottom = LabelBandPx;
        _nameLabel.OffsetLeft = 0;
        _nameLabel.OffsetRight = 0;
        _nameLabel.AddThemeFontSizeOverride("font_size", 22);
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.10f, 0.08f, 0.06f, 1f));
        _nameLabel.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f, 1f));
        _nameLabel.AddThemeConstantOverride("outline_size", 4);
        AddChild(_nameLabel);
    }

    /// <summary>
    /// Configure the placeholder for the given NPC id. Resolves the
    /// unit tint via <see cref="IsoCharacterPaletteLogic"/> + the
    /// display name via <see cref="NpcCatalog.LookupDisplayName"/>,
    /// updates the Label child, and requests a redraw. Idempotent --
    /// calling with the same id is cheap.
    /// </summary>
    public void SetNpc(string? npcId)
    {
        if (_npcId == npcId && _nameLabel is not null) return;

        _npcId = npcId;
        var (r, g, b) = IsoCharacterPaletteLogic.ResolveTintRgb(npcId);
        _baseTint = new Color(r, g, b, 1f);

        if (_nameLabel is not null)
        {
            _nameLabel.Text = NpcCatalog.LookupDisplayName(npcId);
        }
        QueueRedraw();
    }

    /// <summary>
    /// Configure the étape 6 drag-drop role + the mission id that
    /// travels in the drag payload. Call before or after
    /// <see cref="_Ready"/> -- the MouseFilter is reconciled either
    /// way so the drag source initializes correctly.
    /// </summary>
    public void SetSourceContext(SourceRole role, string? missionId)
    {
        _role = role;
        _missionId = missionId;
        MouseFilter = ResolveMouseFilter();
    }

    /// <summary>
    /// Toggle the greyed-out visual state (step 6). True = the perso
    /// is currently on the Compagnie socle ; render desaturated to
    /// signal "left the panel" AND suppress the drag source (the
    /// perso is grabbed from the socle, not from here, while in this
    /// state). False = restore full opacity + drag-source behaviour.
    /// </summary>
    public void SetGreyedOut(bool greyed)
    {
        if (_greyedOut == greyed) return;
        _greyedOut = greyed;
        Modulate = greyed ? GreyedOutModulate : Colors.White;
    }

    /// <summary>
    /// Toggle compact mode (step 6). True = hide the name label,
    /// switch to the smaller cmin (the company socle's occupied state
    /// hosts a smaller silhouette than the recruit panel's hero one).
    /// False = restore full size + label.
    /// </summary>
    public void SetCompactMode(bool compact)
    {
        if (_compactMode == compact) return;
        _compactMode = compact;
        CustomMinimumSize = compact ? CompactMinSize : DefaultMinSize;
        if (_nameLabel is not null) _nameLabel.Visible = !compact;
        QueueRedraw();
    }

    /// <summary>
    /// Godot drag-source hook. Fires when the player starts a drag
    /// over this Control. Returns the snake_case drag payload the
    /// company socle's drop target consumes via
    /// <see cref="IsoSocketDropLogic.CanAccept"/>. Returns
    /// <c>default(Variant)</c> when the role is not a drag source or
    /// when the placeholder is currently greyed out -- Godot
    /// interprets that as "no drag from here".
    /// </summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        _ = atPosition;
        if (_role != SourceRole.RecruitPanelHost) return default;
        // Greyed out = perso is on the socle ; reverse drag has to
        // start from the socle, not from the panel placeholder.
        if (_greyedOut) return default;
        if (string.IsNullOrEmpty(_npcId)) return default;
        if (string.IsNullOrEmpty(_missionId)) return default;

        var payload = new Godot.Collections.Dictionary
        {
            [IsoSocketDropLogic.PayloadKeyNpcId] = _npcId,
            [IsoSocketDropLogic.PayloadKeyMissionId] = _missionId,
            [IsoSocketDropLogic.PayloadKeySource] = IsoSocketDropLogic.DragSourceRecruitPanel,
        };

        // Drag preview = a half-alpha copy of the same silhouette.
        var preview = new IsoCharacterPlaceholder
        {
            Name = "DragPreview",
            Modulate = new Color(1f, 1f, 1f, DragPreviewAlpha),
        };
        preview.SetCompactMode(true);
        preview.SetNpc(_npcId);
        SetDragPreview(preview);

        return payload;
    }

    /// <summary>
    /// Godot drop-target hook -- accept the reverse drag from the
    /// company socle. Only fires when the role is the recruit-panel
    /// host (the placeholder is always the reverse-drop target while
    /// hosted in the panel, regardless of greyed state -- the player
    /// returns the perso TO this slot).
    /// </summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        if (_role != SourceRole.RecruitPanelHost) return false;
        var payload = TryReadDragPayload(data);
        var decision = IsoSocketDropLogic.CanAcceptReverse(payload, RosterM1);
        return decision == IsoSocketDropLogic.DropDecision.Accept;
    }

    /// <summary>
    /// Godot drop-target hook -- consume the reverse drag and emit
    /// <see cref="CharacterReturnedFromSocleEventHandler"/> so the
    /// owning panel can fire the state-machine event.
    /// </summary>
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        var payload = TryReadDragPayload(data);
        if (payload is null) return;
        EmitSignal(SignalName.CharacterReturnedFromSocle, payload.NpcId);
    }

    public override void _Draw()
    {
        var availW = Mathf.Max(0f, Size.X - 2f * PaddingPx);
        var labelBand = _compactMode ? 0f : LabelBandPx;
        var availH = Mathf.Max(0f, Size.Y - labelBand - 2f * PaddingPx);
        if (availW <= 4f || availH <= 4f) return;

        float w = availW;
        float d = w * DepthOverWidth;
        float h = availH - d;
        if (h < 8f)
        {
            w = (availH - 8f) / DepthOverWidth;
            d = w * DepthOverWidth;
            h = 8f;
        }

        var offX = PaddingPx + (availW - w) * 0.5f;
        var offY = labelBand + PaddingPx;

        var topTint = LightenClamp(_baseTint, TopFaceMultiplier);
        var frontTint = _baseTint;
        var sideTint = DarkenClamp(_baseTint, SideFaceMultiplier);

        var topPolygon = Translate(IsoCharacterGeometryLogic.TopFace(w, d, h), offX, offY);
        var frontPolygon = Translate(IsoCharacterGeometryLogic.FrontFace(w, d, h), offX, offY);
        var rightPolygon = Translate(IsoCharacterGeometryLogic.RightFace(w, d, h), offX, offY);

        DrawColoredPolygon(frontPolygon, frontTint);
        DrawColoredPolygon(rightPolygon, sideTint);
        DrawColoredPolygon(topPolygon, topTint);

        var outlineColour = new Color(0.18f, 0.14f, 0.11f, 1f);
        const float outlineWidth = 1.5f;
        DrawPolyline(ClosePolyline(frontPolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(rightPolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(topPolygon), outlineColour, outlineWidth);
    }

    // -------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------

    private MouseFilterEnum ResolveMouseFilter()
        => _role == SourceRole.Decorative
            ? MouseFilterEnum.Ignore
            : MouseFilterEnum.Stop;

    /// <summary>The M1 roster fed into the reverse-drop validator. Same
    /// set as <c>IsoSocketDropLogicTests.RosterM1</c> ; staying in lock-
    /// step is a runtime invariant the recruit-cascade tests already pin
    /// (NpcCatalog.Count = 3 + per-key resolves).</summary>
    private static readonly System.Collections.Generic.HashSet<string> RosterM1 =
        new() { "kira", "dorn", "vell" };

    private static DragPayload? TryReadDragPayload(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return null;
        var dict = data.AsGodotDictionary();
        string? npcId = null, missionId = null, source = null;
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyNpcId, out var nv))
            npcId = nv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeyMissionId, out var mv))
            missionId = mv.AsString();
        if (dict.TryGetValue(IsoSocketDropLogic.PayloadKeySource, out var sv))
            source = sv.AsString();
        return new DragPayload(
            NpcId: npcId ?? string.Empty,
            MissionId: missionId ?? string.Empty,
            Source: source ?? string.Empty);
    }

    private static Vector2[] Translate((float X, float Y)[] points, float dx, float dy)
    {
        var result = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            result[i] = new Vector2(points[i].X + dx, points[i].Y + dy);
        }
        return result;
    }

    private static Color LightenClamp(Color c, float factor) => new(
        Mathf.Clamp(c.R * factor, 0f, 1f),
        Mathf.Clamp(c.G * factor, 0f, 1f),
        Mathf.Clamp(c.B * factor, 0f, 1f),
        c.A);

    private static Color DarkenClamp(Color c, float factor) => new(
        Mathf.Clamp(c.R * factor, 0f, 1f),
        Mathf.Clamp(c.G * factor, 0f, 1f),
        Mathf.Clamp(c.B * factor, 0f, 1f),
        c.A);

    private static Vector2[] ClosePolyline(Vector2[] polygon)
    {
        var closed = new Vector2[polygon.Length + 1];
        polygon.CopyTo(closed, 0);
        closed[polygon.Length] = polygon[0];
        return closed;
    }

    /// <summary>
    /// The two roles this placeholder can play in the étape 6
    /// drag&amp;drop seam. Default = Decorative (pre-step 6 callers
    /// stay unchanged).
    /// </summary>
    public enum SourceRole
    {
        /// <summary>No drag-drop, MouseFilter stays Ignore (default).
        /// Used by anyone embedding the placeholder for pure
        /// visualization (e.g. the company socle's occupied mini
        /// silhouette, where the parent socle owns the reverse drag).</summary>
        Decorative = 0,

        /// <summary>Acts as BOTH the drag source (when not greyed out)
        /// AND the reverse-drop target (always) for the recruit panel.
        /// MouseFilter = Stop. _GetDragData fires on drag start ;
        /// _CanDropData / _DropData accept the company_socle source
        /// and emit CharacterReturnedFromSocle.</summary>
        RecruitPanelHost = 1,
    }
}
