using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Contract for a modal overlay rendered above the current
/// <see cref="IScreen"/> by <see cref="Wayfinders.Client.Services.SceneManager"/>.
///
/// <para>
/// Deliberately distinct from <see cref="IScreen"/> — see pre-brief §4.4.
/// A modal lives <i>beside</i> the navigation stack, not in it: pushing a
/// modal onto the IScreen stack would make E5 inaccessible after closing
/// E4. Two interfaces, two semantics, no possible confusion at the call
/// site (<c>SceneManager.OpenModal&lt;T&gt;()</c> vs
/// <c>SceneManager.NavigateTo&lt;T&gt;()</c>).
/// </para>
///
/// <para>
/// <b>J1 invariant: at most one active modal at a time.</b>
/// <see cref="Wayfinders.Client.Services.SceneManager.OpenModal{T}"/>
/// rejects modal-on-modal in J1 to keep the Suspend/Resume race surface
/// closed (Pre-brief Risk #3). Stacked modals can be re-evaluated at J5
/// when E4 actually ships if a real use case appears.
/// </para>
/// </summary>
public interface IModalOverlay
{
    /// <summary>Stable identifier, e.g. "E4_CHARACTER_SHEET".</summary>
    string ModalId { get; }

    /// <summary>
    /// Called when the modal becomes active. The host screen receives
    /// <see cref="IScreen.OnSuspend"/> immediately before this fires.
    /// </summary>
    Task OnOpen(ScreenContext context, CancellationToken ct);

    /// <summary>
    /// Called when the modal closes. The host screen receives
    /// <see cref="IScreen.OnResume"/> immediately after this returns.
    /// </summary>
    Task OnClose(CancellationToken ct);

    /// <summary>
    /// True when Esc-to-close is allowed right now. Implementations can
    /// flip this to false during open/close animations to prevent the
    /// race condition of "user spams Esc mid-animation."
    /// </summary>
    bool CanCloseOnEsc { get; }

    /// <summary>
    /// True when click-outside-to-close is allowed. Same animation-window
    /// rationale as <see cref="CanCloseOnEsc"/>.
    /// </summary>
    bool CanCloseOnClickOutside { get; }
}
