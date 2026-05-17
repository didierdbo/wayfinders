using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 3+4 placeholder Control rendering a procedural
/// parallelepiped iso silhouette for one M1 roster NPC (kira / dorn /
/// vell). Lives inside the <see cref="MissionRecruitPanel"/>'s right
/// half ; also reusable in the future Compagnie panel (étape 5) for
/// the per-persona portrait row.
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
/// <b>Composition.</b>
/// <list type="bullet">
///   <item><b>Top face</b> -- iso losange, drawn as a 4-vertex polygon
///         in the placeholder's lightest variant of the unit tint.
///         Reads as the character's "head / cap" volume.</item>
///   <item><b>Front face</b> -- 4-vertex polygon in the base unit
///         tint. Reads as the character's "torso" volume.</item>
///   <item><b>Right side face</b> -- 4-vertex polygon in a darker
///         variant of the unit tint (multiplied by 0.7). The classic
///         3D-iso shading trick : a single lateral face one step
///         darker is enough to read as 3D volume.</item>
///   <item><b>Name label</b> -- engine <see cref="Label"/> child sitting
///         above the top face, displaying the resolved
///         <see cref="NpcCatalog.LookupDisplayName"/>. Centred,
///         readable typography (24px, outlined).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>API surface.</b> One setter -- <see cref="SetNpc"/>. The Control
/// resolves the unit tint via
/// <see cref="IsoCharacterPaletteLogic.ResolveTintRgb"/> + the display
/// name via <see cref="NpcCatalog.LookupDisplayName"/>, calls
/// <see cref="CanvasItem.QueueRedraw"/>, and updates the label text.
/// Calling <see cref="SetNpc"/> with the same id twice is idempotent.
/// </para>
///
/// <para>
/// <b>Sizing.</b> Default custom-minimum-size = 180 x 260 px. Width is
/// dominated by the iso silhouette base (200px) ; height accommodates
/// the silhouette (240px) + the label band (20px). Caller may override
/// via <see cref="Control.CustomMinimumSize"/> ; the draw scales
/// against <see cref="Control.Size"/> at every paint so the silhouette
/// fills whatever room it gets.
/// </para>
///
/// <para>
/// <b>Trap discipline (memo feedback_godot_rendering_input_traps).</b>
/// MouseFilter = Ignore -- this is a decorative element ; the parent
/// panel owns the hit-testing surface. Leaving it as Stop would steal
/// mouse events from the surrounding panel's close-on-click-outside
/// logic.
/// </para>
/// </summary>
public partial class IsoCharacterPlaceholder : Control
{
    /// <summary>Default cmin size -- can be overridden per instance.</summary>
    private static readonly Vector2 DefaultMinSize = new(180, 260);

    /// <summary>Default name label height band reserved above the
    /// silhouette.</summary>
    private const float LabelBandPx = 32f;

    /// <summary>Padding inside the Control's rect, around the
    /// silhouette + label.</summary>
    private const float PaddingPx = 12f;

    /// <summary>
    /// Multiplier applied to the unit tint to derive the darker right
    /// side face. 0.7 reads as a single light step darker without going
    /// muddy.
    /// </summary>
    private const float SideFaceMultiplier = 0.7f;

    /// <summary>
    /// Multiplier applied to the unit tint to derive the lighter top
    /// face. 1.15 reads as a single light step brighter while staying
    /// inside [0,1] for the M1 roster tints.
    /// </summary>
    private const float TopFaceMultiplier = 1.15f;

    private Label? _nameLabel;
    private string? _npcId;
    private Color _baseTint = new(0.5f, 0.5f, 0.5f, 1f);

    public override void _Ready()
    {
        CustomMinimumSize = DefaultMinSize;
        // Decorative -- do not capture mouse events ; the parent panel
        // owns close-on-click-outside semantics. Stop would steal them.
        MouseFilter = MouseFilterEnum.Ignore;

        _nameLabel = new Label
        {
            Name = "NpcNameLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = false,
        };
        // Anchor the label to the top of the Control's rect, full
        // width, reserved height = LabelBandPx.
        _nameLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        _nameLabel.OffsetTop = 0;
        _nameLabel.OffsetBottom = LabelBandPx;
        _nameLabel.OffsetLeft = 0;
        _nameLabel.OffsetRight = 0;
        // Slightly bigger font + dark text with bright outline so it
        // reads against any background tint.
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

    public override void _Draw()
    {
        // Compute the silhouette bounding rect : the Control's rect
        // minus the top label band and side padding.
        var rect = new Rect2(
            new Vector2(PaddingPx, LabelBandPx + PaddingPx),
            new Vector2(
                Mathf.Max(0f, Size.X - 2f * PaddingPx),
                Mathf.Max(0f, Size.Y - LabelBandPx - 2f * PaddingPx)));

        if (rect.Size.X <= 4f || rect.Size.Y <= 4f) return;

        // Geometric anchors for the iso parallelepiped. The silhouette
        // reads as a "block-shaped character" : a top losange (head),
        // a tall front face (torso), and a side face (volume hint).
        //
        // Use ~30% of the silhouette height for the head losange, 70%
        // for the torso volume. Width = 90% of rect.
        var siloW = rect.Size.X * 0.9f;
        var siloLeft = rect.Position.X + (rect.Size.X - siloW) * 0.5f;
        var siloRight = siloLeft + siloW;
        var siloTop = rect.Position.Y;
        var siloBottom = siloTop + rect.Size.Y;
        var headHeight = rect.Size.Y * 0.30f;
        var headMid = siloTop + headHeight * 0.5f;
        var torsoTop = siloTop + headHeight;

        // Side face inset -- the right face's depth (how far it leans
        // to the right of the silhouette body). Locked at 15% of width
        // for a recognisable 3D hint without distorting the iso.
        var depth = siloW * 0.15f;

        // Colours -- top face is lighter, side face is darker.
        var topTint = LightenClamp(_baseTint, TopFaceMultiplier);
        var frontTint = _baseTint;
        var sideTint = DarkenClamp(_baseTint, SideFaceMultiplier);

        // -------- Front face (torso) : 4-vertex polygon
        // Rectangle from torsoTop to siloBottom, full silhouette width.
        var frontPolygon = new Vector2[]
        {
            new(siloLeft, torsoTop),
            new(siloRight - depth, torsoTop), // leaves room for the side face's top edge
            new(siloRight - depth, siloBottom),
            new(siloLeft, siloBottom),
        };
        DrawColoredPolygon(frontPolygon, frontTint);

        // -------- Right side face : 4-vertex polygon
        // A parallelogram leaning to the right ; reads as the
        // "thickness" of the character.
        var sidePolygon = new Vector2[]
        {
            new(siloRight - depth, torsoTop),
            new(siloRight, torsoTop + depth * 0.5f),
            new(siloRight, siloBottom - depth * 0.5f),
            new(siloRight - depth, siloBottom),
        };
        DrawColoredPolygon(sidePolygon, sideTint);

        // -------- Top face (head losange) : iso 4-vertex polygon
        // Pointed at top + bottom, wide in the middle.
        var headLeft = siloLeft + siloW * 0.25f;
        var headRight = siloRight - depth - siloW * 0.10f;
        var topPolygon = new Vector2[]
        {
            new((headLeft + headRight) * 0.5f, siloTop),     // top point
            new(headRight, headMid),                          // right
            new((headLeft + headRight) * 0.5f, torsoTop),    // bottom point (sits flush on torsoTop)
            new(headLeft, headMid),                           // left
        };
        DrawColoredPolygon(topPolygon, topTint);

        // -------- Subtle outline -- one umber stroke around the
        // silhouette so the shape reads against any panel background.
        // Drawn last so it sits on top of the fills.
        var outlineColour = new Color(0.18f, 0.14f, 0.11f, 1f);
        const float outlineWidth = 1.5f;
        DrawPolyline(ClosePolyline(frontPolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(sidePolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(topPolygon), outlineColour, outlineWidth);
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
}
