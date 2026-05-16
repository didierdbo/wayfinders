using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# cross-layer projection engine for the reveal-substrate cascade
/// (AMENDMENT A4.7, 2026-05-16). Given a write at one layer, computes the
/// set of cells that must transition at the other layers to maintain
/// cross-layer consistency.
///
/// <para>
/// <b>Locked under A4.7.7 (Rune implementation notes).</b>
/// <list type="bullet">
///   <item>Pure-C# Godot-free seam — mirrors
///         <see cref="TileRevealStateLogic"/> and
///         <see cref="AreaGridLogic"/>. xUnit-pinned.</item>
///   <item>Cost : O(ratio²) per parent cell on top-down projection ;
///         O(1) per child on bottom-up aggregation (4 lookups for the
///         min-state). For the canonical Halfgate boot pop (16 E1
///         cells × 4 children = 64 E2 writes) the cascade is trivial.</item>
///   <item>No bidirectional infinite loop — R3 cascades only deeper
///         (L+1, L+2, …) ; R4 cascades only shallower (L-1, L-2, …).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>MVE scope (E2.1b).</b> Only R3 top-down projection from E1 to E2
/// is exercised. R4 bottom-up aggregation and E2 → E3 are reserved
/// post-MVE per A4.7.1 Q3. The class surface is shaped so adding the
/// bottom-up cascade later is a matter of a new method, not a refactor
/// of the existing one (see <see cref="ProjectTopDown"/> vs. the
/// reserved <c>AggregateBottomUp</c> sibling).
/// </para>
///
/// <para>
/// <b>No-downgrade clamp (R3 + R5, EC10).</b> Every projected cell's
/// target state goes through a <c>max(currentState, projectedState)</c>
/// clamp via <see cref="ClampNoDowngrade"/>. The projector itself does
/// not own the state map — the caller supplies a read-back delegate
/// (<see cref="ProjectTopDownArgs.GetCurrentTargetState"/>) and applies
/// the writes via <see cref="ProjectionResult.CellsToWrite"/>. This
/// keeps the projector Godot-free while honoring the EC1/EC2/EC3 +
/// EC10/EC11 no-downgrade guards.
/// </para>
///
/// <para>
/// <b>Granularity (A4.7.1 Q5, A4.7.4 formula).</b> For parent cell
/// <c>(x, y)</c> at layer L, the 4 child cells at layer L+1 are
/// <c>{(2x + dx, 2y + dy) : dx, dy ∈ {0, 1}}</c>. Inverse :
/// <c>parent(L, u, v) = (u // 2, v // 2)</c>. Hard-coded against
/// <see cref="LayerRatioMatrix.E1ToE2"/> = 2 for the MVE ; generalised
/// via <see cref="LayerRatioMatrix.ResolveRatio"/> so future ratio
/// shifts land here only.
/// </para>
/// </summary>
public static class TileRevealProjector
{
    /// <summary>
    /// Run a top-down R3 projection : given a write at
    /// <paramref name="args"/>.SourceLayer of every cell in
    /// <paramref name="args"/>.SourceCells transitioning to
    /// <paramref name="args"/>.TargetState, compute the set of cells at
    /// <paramref name="args"/>.TargetLayer that must transition, after
    /// applying the no-downgrade clamp.
    ///
    /// <para>
    /// <b>MVE constraint.</b> Today only adjacent-layer projection is
    /// supported (E1 → E2). A future recursive call from E1 → E3 will
    /// land via a second invocation with (E2, E3) ; the caller composes
    /// the cascade. Throws if SourceLayer / TargetLayer are not
    /// immediate parent/child.
    /// </para>
    ///
    /// <para>
    /// <b>Return contract.</b> The <see cref="ProjectionResult"/> lists
    /// the (cell, newState) pairs that the caller should write to the
    /// target-layer state store. Cells where the no-downgrade clamp
    /// produced no change are <b>omitted</b> from the list — the caller
    /// can iterate the list and call <c>SetTileRevealState</c> per entry
    /// without any "did this actually change ?" check, mirroring the
    /// idempotent <see cref="Wayfinders.Client.Services.GameState.SetTileRevealState"/>
    /// contract.
    /// </para>
    /// </summary>
    public static ProjectionResult ProjectTopDown(ProjectTopDownArgs args)
    {
        if (!LayerRatioMatrix.IsImmediateChild(args.SourceLayer, args.TargetLayer))
        {
            throw new System.ArgumentException(
                $"[TileRevealProjector] ProjectTopDown : target layer {args.TargetLayer} " +
                $"is not the immediate child of source layer {args.SourceLayer}. " +
                $"Only single-step cascades are supported ; the caller composes.",
                nameof(args));
        }

        int ratio = LayerRatioMatrix.ResolveRatio(args.SourceLayer, args.TargetLayer);
        var writes = new List<(GridCoord cell, TileRevealState newState)>();

        foreach (var parent in args.SourceCells)
        {
            for (int dy = 0; dy < ratio; dy++)
            {
                for (int dx = 0; dx < ratio; dx++)
                {
                    var child = new GridCoord(
                        parent.Col * ratio + dx,
                        parent.Row * ratio + dy);

                    var current = args.GetCurrentTargetState(child);
                    var clamped = ClampNoDowngrade(current, args.TargetState);
                    if (clamped == current) continue;
                    writes.Add((child, clamped));
                }
            }
        }

        return new ProjectionResult(writes);
    }

    /// <summary>
    /// Resolve the parent cell at layer <paramref name="parentLayer"/>
    /// for a child cell <paramref name="child"/> at the immediate child
    /// layer. Inverse of R3. <b>Reserved for the bottom-up aggregation
    /// cascade (R4)</b> — exposed now so the symmetry of the projector
    /// surface is visible, but the aggregation itself lands post-MVE.
    ///
    /// <para>
    /// Throws if <paramref name="parentLayer"/> is not the immediate
    /// parent of the layer that owns <paramref name="child"/>. The
    /// caller passes the child's layer implicitly via the
    /// <paramref name="childLayer"/> arg.
    /// </para>
    /// </summary>
    public static GridCoord ResolveParentCoord(
        RevealLayer childLayer, GridCoord child, RevealLayer parentLayer)
    {
        if (!LayerRatioMatrix.IsImmediateParent(childLayer, parentLayer))
        {
            throw new System.ArgumentException(
                $"[TileRevealProjector] ResolveParentCoord : {parentLayer} " +
                $"is not the immediate parent of {childLayer}.");
        }
        int ratio = LayerRatioMatrix.ResolveRatio(parentLayer, childLayer);
        return new GridCoord(child.Col / ratio, child.Row / ratio);
    }

    /// <summary>
    /// No-downgrade clamp on a single cell write : the cell's new state
    /// is the <b>max</b> of its current state and the projected state
    /// (A4.7 R5 + EC10). Honours EC1 (fog → partial → revealed never
    /// downgrades) by virtue of the enum's ordered integer encoding
    /// (<see cref="TileRevealState"/> 0 &lt; 1 &lt; 2).
    /// </summary>
    public static TileRevealState ClampNoDowngrade(
        TileRevealState current, TileRevealState projected) =>
        (int)projected > (int)current ? projected : current;
}

/// <summary>
/// Argument bag for <see cref="TileRevealProjector.ProjectTopDown"/>.
/// Read-only record so a future signature extension (e.g. mission origin
/// metadata for EC6 tooltip attribution per A4.7.5) lands as an extra
/// init-only field without breaking call sites.
///
/// <para>
/// <b>Why a record and not positional args.</b> The arg list has 5
/// fields and one of them is a delegate ; a positional call site would
/// be unreadable. The record form makes the call site self-documenting
/// (<c>new ProjectTopDownArgs { SourceLayer = ..., TargetLayer = ..., ... }</c>).
/// </para>
/// </summary>
public sealed record ProjectTopDownArgs
{
    /// <summary>Source layer of the original write (e.g. E1).</summary>
    public required RevealLayer SourceLayer { get; init; }

    /// <summary>
    /// Target layer for the projection (must be the immediate child of
    /// <see cref="SourceLayer"/> per
    /// <see cref="LayerRatioMatrix.IsImmediateChild"/>).
    /// </summary>
    public required RevealLayer TargetLayer { get; init; }

    /// <summary>
    /// Cells at <see cref="SourceLayer"/> that triggered the projection
    /// (e.g. the 16 cells of a mission's
    /// <c>pop_effect.tiles_to_partial</c>).
    /// </summary>
    public required IEnumerable<GridCoord> SourceCells { get; init; }

    /// <summary>
    /// State to project to the child cells (e.g.
    /// <see cref="TileRevealState.Partial"/> for a pop_effect cascade).
    /// </summary>
    public required TileRevealState TargetState { get; init; }

    /// <summary>
    /// Read-back of the target layer's current state for a given cell.
    /// Used by the no-downgrade clamp to skip cells whose current state
    /// already meets or exceeds the projected state (EC1 / EC2 / EC11).
    /// The caller supplies this delegate so the projector stays
    /// Godot-free — typically a thin wrapper around
    /// <see cref="Wayfinders.Client.Services.GameState.GetTileRevealState"/>
    /// in production, or a dictionary lookup in xUnit.
    /// </summary>
    public required System.Func<GridCoord, TileRevealState> GetCurrentTargetState { get; init; }
}

/// <summary>
/// Result of a <see cref="TileRevealProjector.ProjectTopDown"/> call.
/// Carries the (cell, new state) writes the caller should apply at the
/// target layer. Cells where the no-downgrade clamp produced no change
/// are NOT included — the list is the minimal write set.
///
/// <para>
/// <b>Why an explicit type and not a raw list.</b> A wrapper type lets
/// us add diagnostic counters (e.g. "total cells considered",
/// "cells skipped by no-downgrade") for future debug surfaces without
/// breaking callers. Today the only field is the write list.
/// </para>
/// </summary>
public sealed record ProjectionResult(
    IReadOnlyList<(GridCoord cell, TileRevealState newState)> CellsToWrite);
