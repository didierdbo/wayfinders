namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for <c>OpeningBootstrap</c>. Maps the active
/// main-scene shape (probe / production-screen / boot-scene) to the boot
/// action the autoload should take : full registration + NavigateTo, or
/// register-only (no NavigateTo), or full skip.
///
/// <para>
/// <b>Why this lives outside the autoload.</b> The original
/// <c>WireAndBoot</c> branched on a single piece of metadata
/// (<c>skip_opening_bootstrap</c>). The 2026-05-09 J4 double-spawn bug
/// (two FogTileLayer canaries at boot, two MapPan2DComponent instances,
/// only one Camera2D current -- pan visually frozen on the wrong instance)
/// surfaced a third case : the active main scene is itself a registered
/// production screen (Didier ran <c>F6</c> on <c>E2WorldMap.tscn</c>
/// while it was open in the editor). In that case the autoload must
/// still register the screen registry (so wheel-drill into E3 keeps
/// working) but must NOT fire <c>NavigateTo("E1_TITLE")</c> -- otherwise
/// the boot-pushed E1 sits on top of the F6-launched E2 and then a click
/// on "Nouvelle Partie" instantiates a SECOND E2 underneath, doubling the
/// FogTileLayer canary, both MapPan2DComponent's WorldCameras, and every
/// other singleton-shaped state in the screen.
/// </para>
///
/// <para>
/// <b>Three shapes, three actions.</b>
/// <list type="bullet">
///   <item><c>SkipBootstrap</c> -- the active main scene carries
///         <c>metadata/skip_opening_bootstrap = true</c>. Probes
///         (Tile3DBackingProbe, etc) take this path. No registration,
///         no NavigateTo. Pre-J4 single gate, kept.</item>
///   <item><c>RegisterOnly</c> -- the active main scene IS a registered
///         production screen (it implements <c>IScreen</c>). Register all
///         screens (so drill / climb / POI dispatch keep working) but
///         skip <c>NavigateTo</c>. F6 / "Run Current Scene" path.</item>
///   <item><c>RegisterAndBoot</c> -- the canonical path. Boot scene or
///         a non-screen main scene (RosterScene, BootScene). Full
///         registration + <c>NavigateTo("E1_TITLE")</c>.</item>
/// </list>
/// </para>
/// </summary>
public static class OpeningBootstrapLogic
{
    /// <summary>
    /// Decide which boot path <c>OpeningBootstrap</c> should take based
    /// on the shape of the active main scene.
    /// </summary>
    /// <param name="hasSkipMeta">Whether the current-scene root carries
    /// <c>metadata/skip_opening_bootstrap = true</c>. Probe scenes set
    /// this ; production screens do not.</param>
    /// <param name="isRegisteredScreen">Whether the current-scene root
    /// is itself a registered <see cref="IScreen"/> (i.e., the user
    /// F6-launched a production screen directly). The autoload checks
    /// this with <c>currentScene is IScreen</c>.</param>
    public static OpeningBootstrapAction Decide(
        bool hasSkipMeta,
        bool isRegisteredScreen)
    {
        if (hasSkipMeta) return OpeningBootstrapAction.SkipBootstrap;
        if (isRegisteredScreen) return OpeningBootstrapAction.RegisterOnly;
        return OpeningBootstrapAction.RegisterAndBoot;
    }
}

/// <summary>
/// The three possible boot actions for <see cref="OpeningBootstrapLogic.Decide"/>.
/// </summary>
public enum OpeningBootstrapAction
{
    /// <summary>Skip everything : no registration, no NavigateTo. Probe
    /// scenes take this path.</summary>
    SkipBootstrap,

    /// <summary>Register screens (so drill / climb / POI dispatch stay
    /// wired) but skip <c>NavigateTo</c>. The active main scene is itself
    /// a registered production screen ; pushing E1 on top of it would
    /// double-instantiate the screen on the next "Nouvelle Partie" click.</summary>
    RegisterOnly,

    /// <summary>Full canonical boot : register screens then
    /// <c>NavigateTo("E1_TITLE")</c>. Boot scene path.</summary>
    RegisterAndBoot,
}
