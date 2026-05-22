using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scripts.Poi;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// Placeholder pawn for one Company member standing on the iso desk
/// (roadmap milestone J3c-1, made interactive at J3c-2). One
/// <see cref="DeskCompanyPawn"/> is one miniature on a desk slot cell.
///
/// <para>
/// <b>Why a procedural placeholder, not a Sprite2D.</b> Mira's
/// desk-scale Company pawn bitmaps (J3-assets) have not landed. A
/// procedural parchment-and-umber silhouette reads as "a figure standing
/// on the board" while we wait, and reads the locked
/// <i>pion-sur-plateau</i> grammar so the desk does not look like a
/// "jouet". The swap is a one-line change: replace the <see cref="_Draw"/>
/// override with a child <see cref="Sprite2D"/> at the same foot anchor —
/// the slot layout, the Y-sort, and the desk wiring do not change.
/// </para>
///
/// <para>
/// <b>Pion-sur-plateau pattern (memo project_wayfinders_pion_sur_plateau,
/// locked 2026-05-14).</b> The locked grammar for a piece standing on the
/// board is exactly four ingredients — applied here as far as a desk
/// pawn allows:
/// <list type="number">
///   <item><b>Diegetic shadow, SW at 30°.</b> An ellipse cast down-left
///     from the foot, drawn <i>before</i> the body so the body occludes
///     it.</item>
///   <item><b>Lift, 2 px.</b> The body is drawn 2 px above the foot
///     anchor — the locked "posed" target (3 px = the "jouet" edge,
///     &gt; 3 forbidden). Run through
///     <see cref="PoiVisualLogic.ClampLift"/> so a slipped constant
///     cannot ship the toy look.</item>
///   <item><b>Terracotta rim.</b> A thin warm-terracotta outline on the
///     lit (north-east) edge of the body — a rim light, not a full
///     cartoon outline.</item>
///   <item><b>Parallax 3–5%.</b> <i>Deferred</i> — the desk camera is
///     immobile, so the parallax offset is identically zero.</item>
/// </list>
/// <b>Forbidden (memo):</b> no bobbing animation, no full cartoon
/// outline, no soft round blob shadow, no pop-saturated colour.
/// </para>
///
/// <para>
/// <b>Selection feedback — J3c-2, within the pion-sur-plateau grammar.</b>
/// When this pawn's member is the selected one, the feedback is
/// deliberately discreet (Didier's brief: "pas d'outline criard, pas de
/// pop-color ; un rim ou un lift léger acceptable"). It is exactly two
/// things, both already part of the locked grammar — nothing new, no
/// pop-colour, no cartoon outline:
/// <list type="bullet">
///   <item>The terracotta rim, normally a partial rim-light on the NE
///     edge only, is drawn around the <i>whole</i> silhouette and one
///     pixel thicker. It reads as "this piece is picked up", the way a
///     board-game piece catches the light when you lift it. Same hue,
///     same material — a stronger rim, not a new colour.</item>
///   <item>A small extra lift. The selected pawn rises a few px more —
///     the "I have this piece in hand" cue. The selection lift is a
///     transient interaction state, distinct from the locked 2 px
///     <i>resting</i> lift the memo caps; it is kept modest
///     (<see cref="SelectionExtraLiftPx"/>) so the pawn never reads as
///     a floating toy.</item>
/// </list>
/// The selected state is NOT decided by this pawn. It is reflected from
/// <c>GameState.SelectedCompanyMemberId</c> via the
/// <c>CompanyMemberSelected</c> signal — the pawn is a view, the
/// authority is <c>GameState</c> (NPC-autonomy lock 2026-05-09).
/// </para>
///
/// <para>
/// <b>Placement — the pawn animates, it does not relocate (J3c-2).</b>
/// A desk click that targets a destination cell is a tactical intent;
/// <c>GameState</c> validates it and, only if accepted, emits
/// <c>CompanyMemberPlacementConfirmed</c>. This pawn listens for that
/// confirmation and, when it names this pawn's member, tweens its
/// <see cref="Node2D.Position"/> toward the new cell. The pawn never
/// teleports, never re-parents, and never moves in reaction to the raw
/// click — only to the authority's confirmation.
/// </para>
///
/// <para>
/// <b>Signal-disconnection discipline (Rune-coaching Risk #1).</b> The
/// pawn connects to the two <c>GameState</c> signals in
/// <see cref="_Ready"/> and disconnects them in <see cref="_ExitTree"/>.
/// <c>GameState</c> is an autoload — it outlives this pawn. A pawn freed
/// on a scene change while still subscribed would leave <c>GameState</c>
/// holding a delegate over a freed <c>GodotObject</c>: a leak, and a
/// crash the next time the signal fires. Disconnect on exit, always.
/// </para>
///
/// <para>
/// <b>Foot anchor — trap #8.</b> The pawn node's <i>origin</i> is its
/// foot point; everything it draws is positioned relative to that
/// origin. The desk places the node at the iso cell centre via
/// <c>IsoBoard.CellToPixel</c>.
/// </para>
/// </summary>
public partial class DeskCompanyPawn : Node2D
{
    /// <summary>
    /// Locked resting lift in pixels — the body is drawn this far above
    /// the foot anchor when the pawn is <i>not</i> selected. 2 px is the
    /// "posed" target of the pion-sur-plateau memo; run through
    /// <see cref="PoiVisualLogic.ClampLift"/> so it can never exceed the
    /// locked maximum of 3.
    /// </summary>
    private const int LiftPx = 2;

    /// <summary>
    /// Extra lift, in pixels, applied on top of <see cref="LiftPx"/> when
    /// this pawn is the selected one. A transient interaction cue ("piece
    /// in hand"), distinct from the locked resting lift — kept modest so
    /// the selected pawn never reads as a floating toy.
    /// </summary>
    private const int SelectionExtraLiftPx = 5;

    /// <summary>
    /// Body width in pixels for a formation pawn. The leader pawn is
    /// drawn at <see cref="LeaderScale"/> times this.
    /// </summary>
    private const float BodyWidthPx = 44f;

    /// <summary>Body height in pixels for a formation pawn.</summary>
    private const float BodyHeightPx = 96f;

    /// <summary>
    /// Size multiplier applied to the leader pawn (slot 0). A modest bump
    /// — the leader reads as foreground without breaking the iso scale.
    /// </summary>
    private const float LeaderScale = 1.25f;

    /// <summary>
    /// Duration, in seconds, of the placement-move tween. Short enough to
    /// feel responsive, long enough to read as "the piece slid there",
    /// not "the piece teleported".
    /// </summary>
    private const float MoveTweenSeconds = 0.25f;

    // Warm-earth Wayfinders palette (terracotta ochre + parchment cream +
    // umber). No pop-saturated colour — pion-sur-plateau memo.

    /// <summary>Body fill — parchment cream, the lit mass of the figure.</summary>
    private static readonly Color BodyColor = new(0.78f, 0.69f, 0.52f, 1f);

    /// <summary>Leader body fill — a touch warmer so the leader reads distinct.</summary>
    private static readonly Color LeaderBodyColor = new(0.82f, 0.66f, 0.44f, 1f);

    /// <summary>
    /// Rim light — warm terracotta. Drawn thin on the north-east edge
    /// when unselected; drawn around the whole silhouette, one pixel
    /// thicker, when selected (the discreet J3c-2 selection cue).
    /// </summary>
    private static readonly Color RimColor = new(0.80f, 0.42f, 0.26f, 1f);

    /// <summary>
    /// Diegetic cast shadow — translucent umber. Drawn as an ellipse
    /// offset south-west of the foot, before the body.
    /// </summary>
    private static readonly Color ShadowColor = new(0.20f, 0.13f, 0.09f, 0.40f);

    private int _slotIndex;
    private bool _isLeader;
    private bool _isSelected;

    /// <summary>
    /// The authoritative member id this pawn is a view of —
    /// <c>"company-slot-N"</c> in J3c-2 (see
    /// <c>GameState.DeskMemberIdForSlot</c>). Set by
    /// <see cref="Configure"/>; the pawn compares it against the member
    /// id carried by the <c>GameState</c> selection / placement signals
    /// to decide whether a signal is about this pawn.
    /// </summary>
    private string _memberId = string.Empty;

    /// <summary>
    /// The desk entities board this pawn is an occupant of — its own
    /// coordinate frame. Held so a placement confirmation can be turned
    /// into a target pixel via <c>IsoBoard.CellToPixel</c>. Set by
    /// <see cref="Configure"/>.
    /// </summary>
    private IsoBoard? _board;

    /// <summary>
    /// Cached <c>GameState</c> autoload reference. Resolved in
    /// <see cref="_Ready"/>; used to connect / disconnect the desk
    /// signals and (defensively) re-read the current selection.
    /// </summary>
    private GameState? _gameState;

    /// <summary>
    /// The live placement-move tween, if one is running. Killed before a
    /// new move starts so two quick placements do not fight over the
    /// position.
    /// </summary>
    private Tween? _moveTween;

    /// <summary>
    /// Configure the pawn for one desk slot. Called by <c>GameScreen</c>
    /// right after instancing and before adding the pawn to the desk
    /// board. Idempotent.
    /// </summary>
    /// <param name="slotIndex">
    /// The Company slot this pawn occupies (0 = leader).
    /// </param>
    /// <param name="isLeader">
    /// True for the leader pawn — drawn at <see cref="LeaderScale"/> and
    /// in the warmer <see cref="LeaderBodyColor"/>.
    /// </param>
    /// <param name="memberId">
    /// The authoritative member id this pawn is a view of (matches
    /// <c>GameState.DeskMemberIdForSlot(slotIndex)</c>).
    /// </param>
    /// <param name="board">
    /// The desk entities board this pawn occupies, for cell → pixel
    /// resolution on a placement confirmation.
    /// </param>
    public void Configure(
        int slotIndex, bool isLeader, string memberId, IsoBoard board)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(memberId);
        System.ArgumentNullException.ThrowIfNull(board);

        _slotIndex = slotIndex;
        _isLeader = isLeader;
        _memberId = memberId;
        _board = board;
        if (IsInsideTree())
        {
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        // Resolve the GameState autoload and subscribe to the two desk
        // signals. The pawn is a VIEW: it renders selection / placement
        // in reaction to GameState, it never decides them.
        _gameState = GetNodeOrNull<GameState>("/root/GameState");
        if (_gameState is not null)
        {
            _gameState.CompanyMemberSelected += OnCompanyMemberSelected;
            _gameState.CompanyMemberPlacementConfirmed +=
                OnCompanyMemberPlacementConfirmed;

            // Defensive: if this pawn is instanced after a selection was
            // already made (a scene re-enter), reflect the current state
            // rather than waiting for the next signal edge.
            _isSelected = _gameState.SelectedCompanyMemberId == _memberId;
        }

        QueueRedraw();
    }

    public override void _ExitTree()
    {
        // Signal-disconnection discipline (Rune-coaching Risk #1).
        // GameState is an autoload and outlives this pawn; a pawn freed
        // on a scene change while still subscribed would leave GameState
        // holding a delegate over a freed object.
        if (_gameState is not null && GodotObject.IsInstanceValid(_gameState))
        {
            _gameState.CompanyMemberSelected -= OnCompanyMemberSelected;
            _gameState.CompanyMemberPlacementConfirmed -=
                OnCompanyMemberPlacementConfirmed;
        }

        // Kill any in-flight move tween so no callback fires into a
        // freed node.
        _moveTween?.Kill();
        _moveTween = null;
    }

    /// <summary>
    /// React to a desk-selection change. Repaints only when this pawn's
    /// own selected state actually flips — a selection elsewhere on the
    /// desk does not redraw every pawn.
    /// </summary>
    private void OnCompanyMemberSelected(string memberId)
    {
        bool nowSelected = memberId == _memberId;
        if (nowSelected == _isSelected)
        {
            return;
        }
        _isSelected = nowSelected;
        QueueRedraw();
    }

    /// <summary>
    /// React to an accepted placement confirmation. If the confirmation
    /// names this pawn's member, tween the pawn's position to the new
    /// cell's pixel — the pawn animates toward where <c>GameState</c>
    /// has already moved it logically. A confirmation for another member
    /// is ignored.
    /// </summary>
    private void OnCompanyMemberPlacementConfirmed(
        string memberId, int targetCol, int targetRow)
    {
        if (memberId != _memberId || _board is null)
        {
            return;
        }

        var targetPixel = _board.CellToPixel(new Vector2I(targetCol, targetRow));

        // Kill any prior move so two quick placements do not fight.
        _moveTween?.Kill();
        _moveTween = CreateTween();
        _moveTween.TweenProperty(this, "position", targetPixel, MoveTweenSeconds)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Draw the placeholder figure: diegetic SW shadow, then the lifted
    /// parchment body, then the terracotta rim — a partial NE rim when
    /// unselected, a fuller, thicker rim when selected. All geometry is
    /// relative to the node origin (the foot anchor, trap #8). One pass,
    /// no per-frame work.
    /// </summary>
    public override void _Draw()
    {
        float scale = _isLeader ? LeaderScale : 1f;
        float w = BodyWidthPx * scale;
        float h = BodyHeightPx * scale;

        // Resting lift, locked + clamped. The selection lift is added on
        // top as a transient interaction cue (it is NOT the resting lift
        // the memo caps at 3).
        int lift = PoiVisualLogic.ClampLift(LiftPx);
        if (_isSelected)
        {
            lift += SelectionExtraLiftPx;
        }

        // --- Ingredient 1: diegetic shadow, SW at 30° ----------------
        float shadowRx = w * 0.55f;
        float shadowRy = shadowRx * 0.5f; // 2:1 iso-flattened ellipse
        var shadowCentre = new Vector2(-shadowRx * 0.45f, shadowRy * 0.55f);
        DrawSwShadowEllipse(shadowCentre, shadowRx, shadowRy);

        // --- Ingredient 2: lift --------------------------------------
        float footY = -lift;

        // --- Body: a simple rounded-shoulder torso silhouette --------
        var bodyColor = _isLeader ? LeaderBodyColor : BodyColor;
        var body = new[]
        {
            new Vector2(-w * 0.18f, footY - h),          // head, left
            new Vector2(w * 0.18f, footY - h),           // head, right
            new Vector2(w * 0.50f, footY - h * 0.62f),   // shoulder, right
            new Vector2(w * 0.30f, footY),               // foot, right
            new Vector2(-w * 0.30f, footY),              // foot, left
            new Vector2(-w * 0.50f, footY - h * 0.62f),  // shoulder, left
        };
        DrawColoredPolygon(body, bodyColor);

        // --- Ingredient 3: terracotta rim ----------------------------
        // Unselected: a thin rim-light on the north-east (lit) edge only
        // — head-right down to shoulder-right. NOT a full cartoon outline.
        // Selected: the SAME terracotta hue, drawn around the whole
        // silhouette one pixel thicker — the discreet "piece in hand"
        // cue. No new colour, no pop-saturation, no cartoon outline.
        if (_isSelected)
        {
            var closed = new[]
            {
                body[0], body[1], body[2], body[3], body[4], body[5], body[0],
            };
            DrawPolyline(closed, RimColor, 3f, antialiased: true);
        }
        else
        {
            var rimEdge = new[] { body[0], body[1], body[2] };
            DrawPolyline(rimEdge, RimColor, 2f, antialiased: true);
        }
    }

    /// <summary>
    /// Draw the diegetic cast shadow as a filled ellipse. Godot's
    /// immediate-mode drawing has no ellipse primitive, so the ellipse is
    /// tessellated into a fan of triangles.
    /// </summary>
    private void DrawSwShadowEllipse(Vector2 centre, float rx, float ry)
    {
        const int segments = 24;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(points, ShadowColor);
    }

    /// <summary>The Company slot this pawn occupies. 0 = leader.</summary>
    public int SlotIndex => _slotIndex;

    /// <summary>True for the leader pawn (slot 0).</summary>
    public bool IsLeader => _isLeader;

    /// <summary>The authoritative member id this pawn is a view of.</summary>
    public string MemberId => _memberId;

    /// <summary>
    /// True when this pawn's member is the selected one. Reflected from
    /// <c>GameState</c>; the pawn does not own this state.
    /// </summary>
    public bool IsSelected => _isSelected;
}
