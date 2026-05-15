using System.Collections.Generic;
using System.Linq;
using Godot;
using Wayfinders.Client.Scripts.Poi;

namespace Wayfinders.Client.Services;

/// <summary>
/// PR4 — Autoload that owns the POI hover/click input cascade.
///
/// <para>
/// <b>Autoload, not scene node (locked 2026-05-15, prebrief §5#2).</b>
/// Input dispatch must be global : a POI registered anywhere in the tree
/// must be considered by the same cascade regardless of which probe / slice
/// / world scene is active. The <c>PoiSpawner</c> stays scene-local because
/// it is stateless and per-scene ; the router is stateful (the registered
/// list) and process-wide, which is exactly what autoload is for. Same
/// rationale as <c>HoverTooltipController</c> and <c>NpcRegistry</c>.
/// </para>
///
/// <para>
/// <b>Why <c>_Input</c>, not <c>_UnhandledInput</c> (trap §9, locked memo
/// <c>project_wayfinders_poi_integration_pattern</c>).</b> If any
/// <c>Control</c> with <c>mouse_filter=Stop</c> passes in front of the
/// world (a tooltip, a HUD panel), it silently eats the event before
/// <c>_UnhandledInput</c> ever fires. POI hit-detection must see the raw
/// mouse stream. <c>_Input</c> runs before GUI consumption.
/// </para>
///
/// <para>
/// <b>Why skip during drag pan (trap §5).</b> Middle-click drag is the pan
/// gesture (Godot Coaching memo <c>feedback_godot_rendering_input_traps</c>
/// §5 ergonomie). During a drag the cursor travels across many tiles ; it
/// is cosmetically wrong to flash POI hovers underneath. The drag is the
/// active action — hover is suppressed for its duration.
/// </para>
///
/// <para>
/// <b>Why bitmap, not Area2D (anti-checklist).</b> The POI sprite is the
/// source of visual truth ; pixel-perfect input means reading the alpha
/// mask that was generated from the same texture. An <c>Area2D</c> with a
/// rectangular <c>CollisionShape2D</c> would falsely register hits on
/// transparent corners (Halfgate's pillar leaves obvious negative space).
/// A polygon collider would re-encode the same data the bitmap already
/// holds, duplicating the source of truth.
/// </para>
///
/// <para>
/// <b>Why Y-sort descending.</b> When two POIs overlap visually, the one
/// drawn in front (larger Y in Godot 2D iso convention) must claim the
/// click. We sort the registered list by <c>GlobalPosition.Y</c> descending
/// and take the first bitmap hit. MVP cost : one <c>OrderByDescending</c>
/// per input event ; with 6-8 POIs per E1 mission this is trivial. If the
/// E2 World Map raises POI count to ~50+ and the profiler flags it, swap
/// to a <c>SortedSet</c> updated on Register/Unregister.
/// </para>
///
/// <para>
/// <b>Screen → world coordinate.</b> The router is a <c>Node</c> autoload
/// (not <c>Node2D</c>) — it lives outside the world transform. To compare
/// the mouse position to a Sprite2D's <c>GlobalPosition</c>, we apply the
/// canvas inverse transform : <c>world = canvasTransform.AffineInverse() *
/// screen</c>. This accounts for camera pan / zoom. Equivalent to
/// <c>Node2D.GetGlobalMousePosition()</c> but available from a non-Node2D
/// host.
/// </para>
///
/// <para>
/// <b>Disconnection discipline (methodology trap #10).</b> Every
/// <see cref="Register"/> must be paired with an
/// <see cref="Unregister"/> in the POI's <c>_ExitTree</c>. Otherwise the
/// router keeps a freed-node reference and will dereference it on the next
/// input event (segfault or <c>ObjectDisposedException</c>). The list is
/// the leak surface ; the unregister call is the closer.
/// </para>
/// </summary>
public partial class PoiInputRouter : Node
{
    /// <summary>
    /// Emitted when the cursor moves over an opaque pixel of a registered
    /// POI sprite. Fires every <c>MouseMotion</c> event that lands on the
    /// same POI ; subscribers should de-dupe if they want hover-enter
    /// semantics (PR5).
    /// </summary>
    [Signal] public delegate void PoiHoveredEventHandler(string displayName);

    /// <summary>
    /// Emitted when a left-click lands on an opaque pixel of a registered
    /// POI sprite. The router consumes the event ; downstream tile-level
    /// click handlers never see it.
    /// </summary>
    [Signal] public delegate void PoiClickedEventHandler(string displayName);

    // List, not HashSet : we want insertion-order preservation as a stable
    // tiebreaker when two POIs share the same Y. Trap #14 — never expose a
    // Godot.Collections.Array here, it would marshal on every read.
    private readonly List<Poi> _registered = new();

    public override void _Ready()
    {
        GD.Print(
            $"[POI ROUTER] registered, autoload OK, signals declared " +
            $"({nameof(PoiHovered)}, {nameof(PoiClicked)})");
    }

    /// <summary>
    /// Add a POI to the input cascade. Called from <c>Poi._Ready</c>.
    /// Idempotent : registering the same node twice is a no-op (defends
    /// against scene re-entry edge cases).
    /// </summary>
    public void Register(Poi poi)
    {
        if (poi == null)
        {
            GD.PrintErr("[POI ROUTER] Register called with null POI — ignoring");
            return;
        }
        if (_registered.Contains(poi))
        {
            GD.Print($"[POI ROUTER] {poi.Name} already registered — ignoring");
            return;
        }
        _registered.Add(poi);
    }

    /// <summary>
    /// Remove a POI from the input cascade. Called from <c>Poi._ExitTree</c>.
    /// Safe to call on a not-registered node.
    /// </summary>
    public void Unregister(Poi poi)
    {
        if (poi == null) return;
        _registered.Remove(poi);
    }

    public override void _Input(InputEvent @event)
    {
        // Cheap filter — anything that is not a mouse position update or a
        // mouse button press can short-circuit. Keyboard, gamepad, etc. flow
        // through untouched.
        var isMouseMotion = @event is InputEventMouseMotion;
        var isMouseButton = @event is InputEventMouseButton mb && mb.Pressed
            && mb.ButtonIndex == MouseButton.Left;
        if (!isMouseMotion && !isMouseButton) return;

        // Trap §5 — middle button drag-pan is active : suppress POI hover/click.
        // The drag is the focal action ; flashing hovers underneath would be
        // ergonomic noise. Note we test the live input state (not the event
        // itself) so we cover the in-progress drag case where the originating
        // press happened frames ago.
        if (Input.IsMouseButtonPressed(MouseButton.Middle)) return;

        if (_registered.Count == 0) return;

        var viewport = GetViewport();
        // Convert screen → world. Equivalent to Node2D.GetGlobalMousePosition,
        // available from a Node host.
        var screen = viewport.GetMousePosition();
        var cursor = viewport.GetCanvasTransform().AffineInverse() * screen;

        // Y-sort descending — front (larger Y) wins. ToList() to materialise
        // once, since we may inspect every entry before finding a hit.
        // Insertion order is preserved as a stable tie-breaker for equal Y.
        var sorted = _registered
            .OrderByDescending(p => p.GlobalPosition.Y)
            .ToList();

        foreach (var poi in sorted)
        {
            if (!IsValidPoi(poi)) continue;
            if (poi.Texture == null) continue;

            // Build the global AABB in world coords. Locked Poi convention :
            // Centered=false, Scale=(1,1) (PR3). Top-left = GlobalPosition +
            // Offset ; size = Texture.GetSize(). If a future Poi variant
            // applies a Scale, this needs the scale factor applied to size.
            var texSize = poi.Texture.GetSize();
            var aabb = new Rect2(poi.GlobalPosition + poi.Offset, texSize);
            if (!aabb.HasPoint(cursor)) continue; // AABB pre-filter

            // Convert cursor (world) → local pixel (texture-space).
            //   Sprite top-left in world  = GlobalPosition + Offset
            //   With Centered=false, Offset = -AnchorPixel (set by Poi._Ready)
            //   ⇒ texture (0,0) is at GlobalPosition - AnchorPixel
            //   ⇒ localPixel = cursor - GlobalPosition + AnchorPixel
            //               = cursor - (GlobalPosition + Offset)
            // We read poi.Offset live so any future Poi variant with a
            // different pivot rule still gets a correct mapping.
            var local = cursor - (poi.GlobalPosition + poi.Offset);
            var localPixel = (X: (int)local.X, Y: (int)local.Y);

            if (poi.PoiData == null) continue;

            var hit = PoiHitTestLogic.HitTestPoiPure(
                poi.PoiData.AlphaMask,
                poi.PoiData.AlphaMaskWidth,
                (int)texSize.Y,
                localPixel);

            if (!hit) continue;

            // First hit wins. Mark the event consumed so tile-level
            // _UnhandledInput handlers never see it.
            viewport.SetInputAsHandled();

            if (isMouseButton)
            {
                EmitSignal(SignalName.PoiClicked, poi.PoiData.DisplayName);
            }
            else
            {
                EmitSignal(SignalName.PoiHovered, poi.PoiData.DisplayName);
            }
            return;
        }
        // No hit — do NOT SetInputAsHandled. Tile cascade gets the event
        // through the normal _UnhandledInput path. This is the
        // "fallthrough naturel" promised in the prebrief.
    }

    private static bool IsValidPoi(Poi? poi)
    {
        // GodotObject.IsInstanceValid catches the queue-freed-but-still-in-list
        // window (one frame). Defensive — the Unregister-on-ExitTree contract
        // should already prevent this, but a thrown exception in a POI's
        // _ExitTree would skip the unregister call and leave a dangling entry.
        return poi != null && GodotObject.IsInstanceValid(poi);
    }
}
