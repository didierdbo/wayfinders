namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry helper for the iso character placeholder rendered
/// by <c>IsoCharacterPlaceholder</c>. Computes the three visible faces
/// of a 2.5D iso parallelepiped (top losange + front-left
/// parallelogram + right-side parallelogram) from three scalar inputs
/// : projected width <c>w</c>, projected depth <c>d</c>, vertical
/// height <c>h</c>.
///
/// <para>
/// <b>Why a pure-C# helper.</b> Same separation pattern as
/// <see cref="IsoCharacterPaletteLogic"/> : the Godot-bound Control
/// reads its polygons via this helper and wraps them in
/// <c>Vector2[]</c> at the draw site, so the test surface stays
/// engine-free. The polygon points are returned as
/// <c>(float X, float Y)</c> tuples; the draw site converts to
/// <c>Godot.Vector2</c> in one line.
/// </para>
///
/// <para>
/// <b>Projection convention.</b> Standard 2:1 iso (a.k.a.
/// dimetric), Y axis points down (Godot screen convention). Origin
/// <c>(0, 0)</c> sits at the apex of the top losange (the highest
/// visible point of the box). Total bounding rect is
/// <c>w</c> wide by <c>(d + h)</c> tall.
/// </para>
///
/// <para>
/// <b>Shared vertices.</b> The three faces meet at a common front-top
/// apex <c>(w/2, d)</c> (the bottom point of the top losange = the
/// top-right corner of the front-left face = the top-left corner of
/// the right-side face). This is the geometric invariant that makes
/// the silhouette read as one solid block instead of three detached
/// shapes -- exactly the symptom of the d5c3705 first cut.
/// </para>
///
/// <para>
/// <b>Hidden edges.</b> Three back edges of the box are occluded by
/// the front faces and are never rendered (no back-top-right edge,
/// no back vertical edge, no back-bottom edges). Outline stroke at
/// the draw site walks each face's 4 vertices closed -- the shared
/// edges get stroked twice, which is acceptable at 1.5 px width.
/// </para>
/// </summary>
public static class IsoCharacterGeometryLogic
{
    /// <summary>
    /// Compute the top face (losange / rhombus) in counter-clockwise
    /// order starting from the top apex. Four points :
    /// <c>top</c> <c>(w/2, 0)</c>,
    /// <c>right</c> <c>(w, d/2)</c>,
    /// <c>bottom</c> <c>(w/2, d)</c>,
    /// <c>left</c> <c>(0, d/2)</c>.
    /// The bottom apex is the shared front-top vertex of the box.
    /// </summary>
    public static (float X, float Y)[] TopFace(float w, float d, float h)
    {
        _ = h; // height does not affect the top face
        return new (float, float)[]
        {
            (w * 0.5f, 0f),
            (w, d * 0.5f),
            (w * 0.5f, d),
            (0f, d * 0.5f),
        };
    }

    /// <summary>
    /// Compute the front-left face (parallelogram leaning down-right)
    /// in counter-clockwise order starting from the top-left vertex.
    /// Four points :
    /// <c>top-left</c> <c>(0, d/2)</c> = top losange's left apex,
    /// <c>top-right</c> <c>(w/2, d)</c> = top losange's bottom apex
    /// (the shared front-top vertex),
    /// <c>bottom-right</c> <c>(w/2, d + h)</c>,
    /// <c>bottom-left</c> <c>(0, d/2 + h)</c>.
    /// </summary>
    public static (float X, float Y)[] FrontFace(float w, float d, float h)
    {
        return new (float, float)[]
        {
            (0f, d * 0.5f),
            (w * 0.5f, d),
            (w * 0.5f, d + h),
            (0f, d * 0.5f + h),
        };
    }

    /// <summary>
    /// Compute the right-side face (parallelogram leaning down-left)
    /// in clockwise order starting from the top-left vertex. Four
    /// points :
    /// <c>top-left</c> <c>(w/2, d)</c> = the shared front-top vertex,
    /// <c>top-right</c> <c>(w, d/2)</c> = top losange's right apex,
    /// <c>bottom-right</c> <c>(w, d/2 + h)</c>,
    /// <c>bottom-left</c> <c>(w/2, d + h)</c>.
    /// </summary>
    public static (float X, float Y)[] RightFace(float w, float d, float h)
    {
        return new (float, float)[]
        {
            (w * 0.5f, d),
            (w, d * 0.5f),
            (w, d * 0.5f + h),
            (w * 0.5f, d + h),
        };
    }
}
