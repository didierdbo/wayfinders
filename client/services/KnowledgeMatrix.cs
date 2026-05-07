using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// P3 epistemic-fog autoload — placeholder for the Wayfinders fog
/// mechanic that is fed by the party's lived experience: combats, places
/// traversed, missions completed, levels gained, recruitments, world
/// events, time elapsed.
///
/// <para>
/// <b>J1 status: noop.</b> Created at jalon 1 to reserve the autoload slot
/// in <c>project.godot</c>, so adding fog-aware hotspots in J3+ does not
/// touch this file's autoload registration. Per Varn rule (pre-brief §2.4):
/// "mieux un slot inerte qu'un slot manquant".
/// </para>
///
/// <para>
/// Implementation lands at jalon 3 when E2 World Map gets its first
/// fog-aware hotspot. Will at minimum expose:
/// <list type="bullet">
///   <item><c>FogLevel</c> per location (0=unseen, 1=heard-of, 2=seen, 3=visited).</item>
///   <item><c>NotifyEvent(string kind, string locationId)</c> mutation entry.</item>
///   <item><c>FogChanged(string locationId, int newLevel)</c> signal.</item>
/// </list>
/// </para>
/// </summary>
public partial class KnowledgeMatrix : Node
{
    public override void _Ready()
    {
        GD.Print("[KnowledgeMatrix] ready (J1 noop hook)");
    }
}
