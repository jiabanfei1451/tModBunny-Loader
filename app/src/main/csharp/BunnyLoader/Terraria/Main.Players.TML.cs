using System.Collections.Generic;

namespace Terraria;

public sealed partial class Main
{
    public static IEnumerable<WorldItem> ActiveItems
    {
        get
        {
            var items = item;
            for (int i = 0; i < maxItems && i < items.Length; i++)
            {
                WorldItem current = items[i];
                if (!current.IsNull && current.active) yield return current;
            }
        }
    }

    // v101: idem ActiveNPCs/ActiveProjectiles (ver Main.TML.cs). O iterador
    // clampa count em array.Length e pula slots vazios/inativos por IsActive.
    public static Terraria.ModLoader.ActiveEntityIterator<Player> ActivePlayers => new(player, maxPlayers);
}
