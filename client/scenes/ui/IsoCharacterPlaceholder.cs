using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// E2.2 step 3+4 placeholder Control rendering a procedural 2.5D iso
/// parallelepiped silhouette for one M1 roster NPC (kira / dorn /
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
/// <b>Geometry.</b> Three visible faces of a 2:1 iso box, all
/// computed by <see cref="IsoCharacterGeometryLogic"/> (pure-C#,
/// engine-free, xUnit-pinned) :
/// <list type="bullet">
///   <item><b>Top face</b> -- iso losange, lightest variant of the
///         unit tint. The four apexes touch the bounding-rect edges
///         (top, right at mid-depth, bottom at front-top apex, left
///         at mid-depth).</item>
///   <item><b>Front face</b> -- parallelogram leaning down-right,
///         base unit tint. Mid tone.</item>
///   <item><b>Right side face</b> -- parallelogram leaning down-left,
///         darker variant of the unit tint (multiplied by 0.7). The
///         classic 3D-iso shading trick : a single lateral face one
///         step darker is enough to read as 3D volume.</item>
///   <item><b>Name label</b> -- engine <see cref="Label"/> child sitting
///         above the top face, displaying the resolved
///         <see cref="NpcCatalog.LookupDisplayName"/>. Centred,
///         readable typography (22px, outlined).</item>
/// </list>
/// The three faces meet at a common front-top apex -- this is the
/// invariant pinned by
/// <c>IsoCharacterGeometryLogicTests.Three_faces_meet_at_common_front_top_vertex</c>,
/// catching any drift that would re-introduce the d5c3705 symptom
/// (cube-2D with a floating detached losange on top).
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
/// <b>Sizing.</b> Default custom-minimum-size = 180 x 260 px. This
/// matches a 2:1 iso box of width W=180, depth D=60, height H=200
/// (bounding box W by (D + H)), plus the reserved label band on top.
/// Caller may override via <see cref="Control.CustomMinimumSize"/> ;
/// the draw scales against <see cref="Control.Size"/> at every paint
/// so the silhouette fills whatever room it gets while keeping the
/// 2:1 iso ratio (depth = width / 3).
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
    /// 2:1 iso projection ratio. Depth = width * this. Locked to
    /// 1/3 to match a standard dimetric iso (top losange is wider
    /// than tall in a 3:1 visual ratio when D = W/3).
    /// </summary>
    private const float DepthOverWidth = 1f / 3f;

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
        // Available rect for the silhouette = Control's rect minus the
        // top label band and side padding.
        var availW = Mathf.Max(0f, Size.X - 2f * PaddingPx);
        var availH = Mathf.Max(0f, Size.Y - LabelBandPx - 2f * PaddingPx);
        if (availW <= 4f || availH <= 4f) return;

        // Derive (w, d, h) for a 2:1 iso box that fills the available
        // rect. Depth is locked at w/3 ; height takes whatever is left
        // after the depth band. If the available rect is wider than
        // tall enough, we cap width so the box does not exceed
        // available height.
        float w = availW;
        float d = w * DepthOverWidth;
        float h = availH - d;
        if (h < 8f)
        {
            // Available rect is squat -- scale the box down so it fits.
            // Bounding height = d + h = w * (DepthOverWidth) + h.
            // Solve w * DepthOverWidth + h_min = availH, but here we
            // just cap w so d + 8 <= availH.
            w = (availH - 8f) / DepthOverWidth;
            d = w * DepthOverWidth;
            h = 8f;
        }

        // Centre the silhouette horizontally inside the padded rect.
        var offX = PaddingPx + (availW - w) * 0.5f;
        var offY = LabelBandPx + PaddingPx;

        // Colours -- top face is lighter, side face is darker.
        var topTint = LightenClamp(_baseTint, TopFaceMultiplier);
        var frontTint = _baseTint;
        var sideTint = DarkenClamp(_baseTint, SideFaceMultiplier);

        // Pull the three face polygons from the pure-C# geometry
        // helper, translate into the padded draw area, and convert to
        // Godot Vector2[].
        var topPolygon = Translate(IsoCharacterGeometryLogic.TopFace(w, d, h), offX, offY);
        var frontPolygon = Translate(IsoCharacterGeometryLogic.FrontFace(w, d, h), offX, offY);
        var rightPolygon = Translate(IsoCharacterGeometryLogic.RightFace(w, d, h), offX, offY);

        // Draw order : front, right, top. The three faces do not
        // overlap (they share edges only) so any order is correct ;
        // we draw the top last so its outline sits cleanly above the
        // two vertical faces' shared apex edge.
        DrawColoredPolygon(frontPolygon, frontTint);
        DrawColoredPolygon(rightPolygon, sideTint);
        DrawColoredPolygon(topPolygon, topTint);

        // -------- Subtle outline -- one umber stroke around each face
        // so the silhouette reads against any panel background. The
        // shared interior edges get stroked twice (acceptable at
        // 1.5 px) ; the back-facing edges of the box are never drawn
        // because the helper never returns their vertices.
        var outlineColour = new Color(0.18f, 0.14f, 0.11f, 1f);
        const float outlineWidth = 1.5f;
        DrawPolyline(ClosePolyline(frontPolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(rightPolygon), outlineColour, outlineWidth);
        DrawPolyline(ClosePolyline(topPolygon), outlineColour, outlineWidth);
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
}
