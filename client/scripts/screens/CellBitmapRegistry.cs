using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# registry for the slice 3.5 livrable 4 per-tile bitmap API
/// (M3 / Phase 9 / 2026-05-08). Models the "one cell can have one bitmap
/// override at a time" semantics independently of the bitmap type, so
/// xUnit can pin the set/clear/replace contract without a Godot
/// <see cref="object"/> reference. The runtime
/// <see cref="Wayfinders.Client.Components.FogTileLayer"/> uses this
/// registry typed on <c>Godot.Texture2D</c> to drive the per-cell
/// Polygon2D's <c>Texture</c> field.
///
/// <para>
/// <b>Why a separate type rather than a raw <c>Dictionary</c>.</b>
/// Three load-bearing contracts to pin :
/// <list type="bullet">
///   <item><b>Set</b> with a known coord and a non-null bitmap stores
///         the reference. <see cref="TryGet"/> returns it.</item>
///   <item><b>Clear</b> on a known coord drops the entry. The next
///         <see cref="TryGet"/> returns false (placeholder fallback at
///         the runtime layer).</item>
///   <item><b>Re-set</b> on a coord that already has a bitmap replaces
///         the reference, no leak — <see cref="Count"/> stays at 1 even
///         after N re-sets, because the dictionary holds the latest
///         value only. Critical for the runtime where the same cell
///         might be re-bitmaped per-frame in some animation scenario.</item>
/// </list>
/// Encoding these as a typed wrapper makes the contract explicit and
/// catches drift if a future refactor "optimises" the renderer's bitmap
/// path (e.g. caching textures by hash) — the test suite catches the
/// regression at the seam.
/// </para>
///
/// <para>
/// <b>Tolerance to unknown coords.</b> The registry does not validate
/// coords against any grid dimensions. The runtime layer is responsible
/// for that gate (it logs a warning and returns when the coord is not
/// in its <c>_cells</c> map). The registry just holds whatever it's
/// told to hold ; this keeps the helper purely about the lifetime
/// semantic, not the validity gate.
/// </para>
/// </summary>
/// <typeparam name="TBitmap">Bitmap reference type. The runtime types it
/// on <c>Godot.Texture2D</c> ; tests type it on <c>object</c> or a
/// stand-in.</typeparam>
public sealed class CellBitmapRegistry<TBitmap> where TBitmap : class
{
    private readonly Dictionary<GridCoord, TBitmap> _store = new();

    /// <summary>
    /// Number of cells currently bound to a bitmap. Diagnostic surface
    /// for the runtime HUD overlay (a future slice can show "N cells
    /// have a bitmap override") and for xUnit's no-leak invariant.
    /// </summary>
    public int Count => _store.Count;

    /// <summary>
    /// Bind <paramref name="bitmap"/> to <paramref name="coord"/>. If
    /// the coord already has a bitmap, the prior reference is dropped
    /// (no leak) and the new one takes its place.
    ///
    /// <para>
    /// A null bitmap is treated as an explicit clear : same effect as
    /// <see cref="Clear"/>. The runtime layer can either branch on
    /// nullability or call this method uniformly.
    /// </para>
    /// </summary>
    public void Set(GridCoord coord, TBitmap? bitmap)
    {
        if (bitmap is null)
        {
            _store.Remove(coord);
            return;
        }
        _store[coord] = bitmap;
    }

    /// <summary>
    /// Drop the binding for <paramref name="coord"/>. Idempotent : a
    /// clear on a coord with no binding is a silent no-op (returns
    /// false to signal "nothing was there").
    /// </summary>
    public bool Clear(GridCoord coord) => _store.Remove(coord);

    /// <summary>
    /// Look up the binding for <paramref name="coord"/>. Returns false
    /// if no binding exists (caller falls back to placeholder).
    /// </summary>
    public bool TryGet(GridCoord coord, out TBitmap? bitmap)
    {
        if (_store.TryGetValue(coord, out var existing))
        {
            bitmap = existing;
            return true;
        }
        bitmap = null;
        return false;
    }

    /// <summary>
    /// Reset the registry. Used by the runtime layer's <c>DespawnAll</c>
    /// path when the consumer reconfigures the grid.
    /// </summary>
    public void ClearAll() => _store.Clear();
}
