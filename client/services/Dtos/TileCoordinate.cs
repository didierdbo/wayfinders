namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Pure-C# 2D integer cell coordinate. Mirrors the shape of Godot's
/// <c>Vector2I</c> (X, Y) under different field names (<see cref="Col"/>,
/// <see cref="Row"/>) without taking the Godot dependency, so DTOs
/// holding cell sets can stay cherry-pickable into the xUnit test host.
///
/// <para>
/// <b>Varn-lock 2026-05-17 §D.7 (ratification, post-Rune commit a046ce4).</b>
/// Introduced to replace the <c>string</c>-key downgrade
/// (<c>"col,row"</c>) Rune originally shipped to keep
/// <see cref="NpcRuntimeState.KnownTilesAppliedTo"/> Godot-free. The
/// downgrade lost compile-time type safety on the idempotence anchor —
/// a future caller round-tripping a malformed key would only blow up at
/// parse time. This record-struct preserves the test seam invariant
/// (no <c>using Godot;</c>, no <c>Vector2I</c> field type) while
/// restoring value-typed semantics over the two integers.
/// </para>
///
/// <para>
/// <b>Why a <c>readonly record struct</c>, not a sealed class.</b> Two
/// reasons. First, value semantics : C# records auto-generate
/// <c>Equals</c> + <c>GetHashCode</c> over the two int fields, which is
/// exactly the membership-check contract the reveal projection (A4.2)
/// will use against the set. Second, allocation : cell coordinates land
/// in hot loops (the projection iterates one cell per NPC <c>known_tiles</c>
/// entry) — a struct stays on the stack and avoids garbage pressure.
/// </para>
///
/// <para>
/// <b>Bridge to <c>Vector2I</c>.</b> The Godot-bound side (any caller
/// reading a tile cell from the engine and storing it into the cell set)
/// converts via a one-liner :
/// <c>new TileCoordinate(cell.X, cell.Y)</c> outbound,
/// <c>new Vector2I(coord.Col, coord.Row)</c> inbound. A static helper
/// class lives next to the cell-iteration code when the first such
/// caller actually lands (concrete-first ; no fantôme bridge today).
/// </para>
/// </summary>
/// <param name="Col">Column index (matches the X axis of a Godot
/// <c>Vector2I</c> when bridged).</param>
/// <param name="Row">Row index (matches the Y axis of a Godot
/// <c>Vector2I</c> when bridged).</param>
public readonly record struct TileCoordinate(int Col, int Row);
