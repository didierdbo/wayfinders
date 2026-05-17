namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry helper for the flat iso socket rendered by
/// <c>IsoSocketPlaceholder</c> -- the central socle inside the Compagnie
/// panel (E2.2 step 5, Didier-lock 2026-05-17). The socket is a single
/// flat rhombus (losange iso, top-down view) sitting at the centre of
/// the panel ; at étape 6 a dragged-in character placeholder will land
/// on top of it.
///
/// <para>
/// <b>Why a pure-C# helper.</b> Mirrors the separation pattern of
/// <see cref="IsoCharacterGeometryLogic"/> : the Godot-bound Control
/// reads its polygon via this helper and wraps the result in
/// <c>Vector2[]</c> at the draw site, so the test surface stays
/// engine-free.
/// </para>
///
/// <para>
/// <b>Projection convention.</b> Standard 2:1 iso (a.k.a. dimetric),
/// Y axis points down (Godot screen convention). The rhombus has its
/// top apex at <c>(W/2, 0)</c>, right apex at <c>(W, H/2)</c>, bottom
/// apex at <c>(W/2, H)</c>, left apex at <c>(0, H/2)</c>. The locked
/// 2:1 ratio means <c>H = W / 2</c> -- the rhombus is always twice as
/// wide as tall on-screen, matching the iso losange shape used for E2
/// tile rendering and the top face of
/// <see cref="IsoCharacterGeometryLogic"/>.
/// </para>
///
/// <para>
/// <b>Order.</b> Vertices returned in counter-clockwise order starting
/// from the top apex : top, right, bottom, left. Same winding as
/// <see cref="IsoCharacterGeometryLogic.TopFace"/> so the draw site
/// applies the same outline-walk discipline.
/// </para>
/// </summary>
public static class IsoSocketGeometryLogic
{
    /// <summary>
    /// Locked iso projection ratio. The rhombus height (Y span) is
    /// always exactly half the rhombus width (X span). Pinned by
    /// <c>IsoSocketGeometryLogicTests.Locked_aspect_ratio_is_two_to_one</c>.
    /// </summary>
    public const float WidthOverHeight = 2f;

    /// <summary>
    /// Compute the projected rhombus (top apex / right apex / bottom
    /// apex / left apex) for a socket of projected width <paramref name="w"/>.
    /// Height is derived as <c>w / WidthOverHeight</c> -- callers must
    /// not pass an arbitrary height (the 2:1 ratio is the spec
    /// invariant).
    /// </summary>
    public static (float X, float Y)[] Rhombus(float w)
    {
        var h = w / WidthOverHeight;
        return new (float, float)[]
        {
            (w * 0.5f, 0f),
            (w, h * 0.5f),
            (w * 0.5f, h),
            (0f, h * 0.5f),
        };
    }

    /// <summary>
    /// Convenience accessor for the derived height. Callers sizing the
    /// Control's bounding rect use this instead of re-deriving the
    /// division everywhere.
    /// </summary>
    public static float HeightFor(float w) => w / WidthOverHeight;
}
