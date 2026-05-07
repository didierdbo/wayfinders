using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload Godot wrapper around the pure-C# <see cref="NpcCatalog"/>
/// helpers. The wrapper exists for two reasons:
/// <list type="number">
///   <item>Godot autoload semantics: callers do
///         <c>GetNode&lt;NpcRegistry&gt;("/root/NpcRegistry")</c>,
///         which requires a <see cref="Node"/> subclass.</item>
///   <item>Future J6+ stateful behaviour: when per-NPC mutable state
///         (current portrait state, dialogue progress) ships, the
///         autoload Node is the natural owner of that scene-tree-bound
///         data ; the static catalog stays pure data.</item>
/// </list>
///
/// <para>
/// The data + lookup logic lives in
/// <see cref="Wayfinders.Client.Scripts.Screens.NpcCatalog"/> so xUnit
/// pins the contract without spinning a Godot runtime. Same separation
/// pattern as <see cref="PoiDispatchLogic"/> +
/// <see cref="Wayfinders.Client.Data.PoiDefinition"/>.
/// </para>
/// </summary>
public partial class NpcRegistry : Node
{
    public override void _Ready()
    {
        GD.Print($"[NpcRegistry] ready, {NpcCatalog.Count} npc entries registered");
    }

    /// <summary>Display name for the NPC id, or the fallback default.</summary>
    public string DisplayNameFor(string? npcId) => NpcCatalog.LookupDisplayName(npcId);

    /// <summary>
    /// Compose the AssetResolver key for the NPC's portrait in the
    /// requested state. Stateless NPCs (Dorn, default) ignore the state
    /// argument and return the bare key base. Stateful NPCs (Kira)
    /// suffix the state lowercased.
    /// </summary>
    public string PortraitKeyFor(string? npcId, NpcPortraitState state)
        => NpcCatalog.LookupPortraitKey(npcId, state);
}
