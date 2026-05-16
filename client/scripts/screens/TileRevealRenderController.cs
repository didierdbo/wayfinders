using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Godot Node that adapts <see cref="TileRevealState"/> transitions to
/// the per-cell <c>reveal_level</c> float that feeds the face-B reveal
/// shader. Subscribes to <see cref="GameState.TileRevealStateChanged"/>
/// and, for every transition, runs a 300 ms cubic-out Tween from the
/// previous derived float to the new derived float (Varn reconciliation
/// 2026-05-16 §3.3).
///
/// <para>
/// <b>New class, NOT a rename of <see cref="TileRevealStateLogic"/>.</b>
/// The reconciliation explicitly rejects renaming the existing logic
/// class (§3.4) : <see cref="TileRevealStateLogic"/> is the pure-C#
/// state holder + R8 encoding + CPU box-blur engine (Godot-free,
/// xUnit-pinned), this controller is the Godot-side enum-to-Tween
/// adapter. Two distinct classes that compose : the controller drives
/// the float, the logic class encodes it for the shader.
/// </para>
///
/// <para>
/// <b>Scope of this controller — gameplay path only.</b> Varn §2 D3
/// arbitrates two reveal mechanisms by design :
/// </para>
/// <list type="bullet">
///   <item><b>Cinematic (scripted, one-off).</b> Owned by
///         <c>IsoMapE1Probe</c>. Hard <c>SetTileRevealLevel(.., 1.0)</c>
///         writes at flip midpoint, CPU blur diffuses the value. The
///         controller does NOT participate in the cinematic.</item>
///   <item><b>Gameplay (runtime, continuous).</b> Owned by this
///         controller. 300 ms cubic-out Tween on every
///         <see cref="GameState.TileRevealStateChanged"/> emission.</item>
/// </list>
///
/// <para>
/// <b>Shader-pipeline wiring contract — INDIRECTION ON PURPOSE.</b> The
/// controller does NOT touch the R8 <c>ImageTexture</c> nor the
/// <see cref="TileRevealStateLogic"/> instance directly. Instead it
/// invokes a caller-supplied delegate
/// (<see cref="OnRevealLevelChanged"/>) once per Tween step with the
/// cell coordinate and the freshly-interpolated float. The owner scene
/// (the future <c>E2WorldMapReveal</c> / E2.1 area scene, or any other
/// consumer) is responsible for translating that into a
/// <see cref="TileRevealStateLogic.SetTileRevealLevel"/> call plus the
/// R8 texture re-upload, exactly the way <c>IsoMapE1Probe</c> already
/// does for the cinematic.
/// </para>
///
/// <para>
/// <b>Why the indirection.</b> Today there is exactly ONE instance of
/// <see cref="TileRevealStateLogic"/> in the codebase, owned by
/// <c>IsoMapE1Probe</c> (scratch scene). Pushing the controller to
/// know about that instance would either (a) require promoting
/// <see cref="TileRevealStateLogic"/> to a GameState-level singleton
/// (a bigger refactor than C-bis scopes), or (b) hard-couple the
/// controller to the scratch scene (a worse coupling). The delegate
/// seam lets the consumer scene own the texture + logic pairing and
/// pull the controller into its render pipeline at <c>_Ready</c>.
/// Phase E2.1 / E2.2 will instantiate one controller per relevant
/// scene and wire the callback in there.
/// </para>
///
/// <para>
/// <b>Per-cell Tween discipline (Godot trap §10 — signal leaks /
/// Tween leaks).</b> Each cell has at most one active Tween at any
/// moment. When a new transition arrives for a cell that already has
/// a Tween in flight, the in-flight Tween is killed before a fresh one
/// is started — this prevents stacked animations from compounding the
/// derived float and avoids leaked Tween nodes when the scene is freed.
/// On <see cref="_ExitTree"/> the controller kills every remaining
/// Tween + disconnects from the GameState signal.
/// </para>
///
/// <para>
/// <b>Mutation contract.</b> Main-thread only. The signal is emitted
/// from <see cref="GameState.SetTileRevealState"/> which is documented
/// as main-thread-only (mirrors the other GameState slots). The Tween
/// callbacks land on the main thread by Godot's contract. No
/// <c>CallDeferred</c> needed inside this controller.
/// </para>
/// </summary>
public partial class TileRevealRenderController : Node
{
    /// <summary>
    /// Locked Tween duration for gameplay-triggered reveal transitions.
    /// 300 ms is the Varn-locked render contract (§3.3 + A4.1 render
    /// mapping). Same value applies to every transition direction
    /// (<c>fog → partial</c>, <c>partial → revealed</c>, etc.).
    /// </summary>
    public const float TweenDurationSeconds = 0.300f;

    /// <summary>
    /// Locked easing curve. <see cref="Tween.EaseType.Out"/> +
    /// <see cref="Tween.TransitionType.Cubic"/> produces a soft landing
    /// (fast start, gentle stop) — matches the diegetic "the tile fades
    /// in" feel. Varn A4.1 render mapping.
    /// </summary>
    public const Tween.TransitionType TweenTransition = Tween.TransitionType.Cubic;

    /// <summary>
    /// Locked ease direction. Cubic-OUT means the curve flattens near
    /// the target, so the steady-state value is visually committed
    /// before the animation formally ends — important for the
    /// "did the reveal land yet ?" affordance.
    /// </summary>
    public const Tween.EaseType TweenEase = Tween.EaseType.Out;

    /// <summary>
    /// Caller-supplied callback invoked on every Tween step with the
    /// freshly-interpolated reveal_level for one cell. The owner scene
    /// translates this into a
    /// <see cref="TileRevealStateLogic.SetTileRevealLevel"/> call plus
    /// the R8 <c>ImageTexture</c> re-upload — same shape the cinematic
    /// uses. Null until the owner sets it (typically in
    /// <see cref="Node._Ready"/>) ; a Tween step with a null callback
    /// silently no-ops (defensive : the controller can spin up before
    /// the owner has plugged the wiring).
    /// </summary>
    public Action<Vector2I, float>? OnRevealLevelChanged { get; set; }

    /// <summary>
    /// Reference to the GameState autoload. Resolved at
    /// <see cref="_Ready"/>. Stored so <see cref="_ExitTree"/> can
    /// disconnect the signal cleanly.
    /// </summary>
    private GameState? _gameState;

    /// <summary>
    /// Per-cell active-Tween registry. Used to kill any in-flight
    /// Tween on a cell before scheduling a new one (avoids stacking)
    /// and to walk + kill every remaining Tween on
    /// <see cref="_ExitTree"/>.
    /// </summary>
    private readonly Dictionary<Vector2I, Tween> _activeTweens = new();

    /// <summary>
    /// Per-cell "last committed" derived float, used as the Tween
    /// source value on the next transition. Necessary because Godot's
    /// <see cref="Tween"/> on a method requires explicit
    /// (from, to) endpoints — there is no "from the current visual
    /// state" mode. The controller is the only writer of this dict,
    /// so it stays consistent with what was actually painted to the
    /// shader through <see cref="OnRevealLevelChanged"/>.
    /// </summary>
    private readonly Dictionary<Vector2I, float> _lastCommittedLevel = new();

    /// <summary>
    /// Resolve the GameState autoload + subscribe to the reveal-state
    /// signal. The signal connection uses the C# event syntax
    /// (auto-generated from <c>[Signal]</c>) which integrates with
    /// Godot's signal teardown on
    /// <see cref="GodotObject.Disconnect"/> at
    /// <see cref="_ExitTree"/>.
    /// </summary>
    public override void _Ready()
    {
        _gameState = GetNodeOrNull<GameState>("/root/GameState");
        if (_gameState is null)
        {
            GD.PushWarning(
                "[TileRevealRenderController] GameState autoload not found at /root/GameState. "
              + "The controller is inert until a GameState is wired. This is normal in unit-test "
              + "scenes that exercise the controller without booting the full autoload graph.");
            return;
        }
        _gameState.TileRevealStateChanged += OnTileRevealStateChanged;
    }

    /// <summary>
    /// Disconnect from the GameState signal + kill every in-flight
    /// Tween. Mandatory : the GameState autoload outlives the
    /// controller's scene, so leaving the signal connected after the
    /// controller is freed would cause Godot to dispatch into a
    /// disposed Node (the classic "publisher outlives subscriber"
    /// leak — Rune-coaching mistake-pattern #10).
    /// </summary>
    public override void _ExitTree()
    {
        if (_gameState is not null)
        {
            _gameState.TileRevealStateChanged -= OnTileRevealStateChanged;
            _gameState = null;
        }
        foreach (var tween in _activeTweens.Values)
        {
            if (Godot.GodotObject.IsInstanceValid(tween) && tween.IsValid())
            {
                tween.Kill();
            }
        }
        _activeTweens.Clear();
        _lastCommittedLevel.Clear();
    }

    /// <summary>
    /// Signal receiver. Translates the integer-encoded
    /// <see cref="TileRevealState"/> wire values back into the enum,
    /// resolves the derived float endpoints via
    /// <see cref="TileRevealStateHelpers.ResolveRevealLevel"/>, and
    /// schedules a 300 ms Tween.
    /// </summary>
    /// <remarks>
    /// Receiver-side cast back from <c>int</c> mirrors the
    /// <see cref="TileKnowledgeStore.KnowledgeChanged"/> pattern :
    /// Godot 4.6's <c>[Signal]</c> binding accepts enums in declarations
    /// but not in wire payloads (the marshal loses the type).
    /// </remarks>
    private void OnTileRevealStateChanged(Vector2I cell, int oldState, int newState)
    {
        var oldEnum = (TileRevealState)oldState;
        var newEnum = (TileRevealState)newState;
        StartTransition(cell, oldEnum, newEnum);
    }

    /// <summary>
    /// Internal entry point used by both the signal path and direct
    /// callers (tests, debug commands). Exposed <c>internal</c> for
    /// xUnit reach without making the whole controller test-coupled.
    ///
    /// <para>
    /// Resolves the source endpoint from <see cref="_lastCommittedLevel"/>
    /// if a prior transition already ran for this cell (Tween mid-run
    /// then a new transition arrives ; the new source is the previously
    /// committed float, not the helper-resolved
    /// <see cref="TileRevealStateHelpers.ResolveRevealLevel(TileRevealState)"/>
    /// of <paramref name="oldState"/>). This makes mid-transition
    /// re-interruption visually smooth.
    /// </para>
    /// </summary>
    internal void StartTransition(Vector2I cell, TileRevealState oldState, TileRevealState newState)
    {
        // Resolve source : if an earlier transition is still running,
        // continue from its last committed value (not the helper-resolved
        // old enum, which would jump backwards). On the first transition
        // for this cell, fall back to the enum-resolved old value.
        var fromValue = _lastCommittedLevel.TryGetValue(cell, out var committed)
            ? committed
            : TileRevealStateHelpers.ResolveRevealLevel(oldState);
        var toValue = TileRevealStateHelpers.ResolveRevealLevel(newState);

        // Kill any in-flight Tween on this cell to prevent stacking.
        if (_activeTweens.TryGetValue(cell, out var existing))
        {
            if (Godot.GodotObject.IsInstanceValid(existing) && existing.IsValid())
            {
                existing.Kill();
            }
            _activeTweens.Remove(cell);
        }

        // Trivial transition (no animation needed) : commit + invoke
        // callback once and return. Saves a Tween allocation for the
        // (rare) case where source already equals target.
        if (Mathf.IsEqualApprox(fromValue, toValue))
        {
            _lastCommittedLevel[cell] = toValue;
            OnRevealLevelChanged?.Invoke(cell, toValue);
            return;
        }

        // Schedule the Tween. CreateTween() attaches the Tween to this
        // Node ; Godot tears it down automatically if the node is
        // freed (defence in depth on top of _ExitTree's explicit Kill).
        var tween = CreateTween();
        tween.SetTrans(TweenTransition).SetEase(TweenEase);

        // MethodCallback approach : the Tween calls a per-step lambda
        // that updates _lastCommittedLevel + invokes the consumer
        // callback. This is the Tween idiom for non-Property targets
        // (the reveal_level lives in a callback-driven shader pipeline,
        // not on a Node's Property).
        tween.TweenMethod(
            Callable.From<float>((value) =>
            {
                _lastCommittedLevel[cell] = value;
                OnRevealLevelChanged?.Invoke(cell, value);
            }),
            fromValue,
            toValue,
            TweenDurationSeconds);

        // When the Tween finishes naturally (not killed), drop the
        // registry entry so the cell is "clean" for the next transition.
        tween.Finished += () =>
        {
            if (_activeTweens.TryGetValue(cell, out var t) && t == tween)
            {
                _activeTweens.Remove(cell);
            }
        };

        _activeTweens[cell] = tween;
    }

    // ---------------------------------------------------------------
    // Test / diagnostic surface
    // ---------------------------------------------------------------

    /// <summary>
    /// Number of cells currently mid-transition. Exposed for xUnit
    /// anchoring + scene-graph diagnostics ("did I leak Tweens ?").
    /// </summary>
    public int ActiveTransitionCount => _activeTweens.Count;

    /// <summary>
    /// Read the last committed derived float for a cell (the value
    /// most recently pushed through <see cref="OnRevealLevelChanged"/>).
    /// Returns <see cref="TileRevealStateHelpers.RevealLevelFog"/> for
    /// cells that have never been transitioned. Useful for tests and
    /// for diagnostic readouts ; production callers should read the
    /// authoritative enum via <see cref="GameState.GetTileRevealState"/>.
    /// </summary>
    public float GetLastCommittedLevel(Vector2I cell) =>
        _lastCommittedLevel.TryGetValue(cell, out var v) ? v : TileRevealStateHelpers.RevealLevelFog;
}
