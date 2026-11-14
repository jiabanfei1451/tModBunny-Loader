using System;
using System.Collections.Generic;
using BunnyLoader;

namespace Terraria.ModLoader.Core;

// v63: diagnostico temporario. Nao muda comportamento nenhum — so escreve no
// log (1) quem pediu o NPC (com pilha gerenciada) e (2) por qual caminho de
// entrada o item foi usado. A ideia e responder duas perguntas que os logs de
// v62 nao respondem:
//   - "qual NPC aparece sozinho e quem o criou" (NPC.NewNPC)
//   - "o toque na barra passa por ItemCheck_Shoot?" (entrada do Shoot/UseItem)
internal static class Diag63
{
    static readonly Dictionary<string, int> Last = new();

    internal static void Once(string key, int intervalMs, Func<string> text)
    {
        int now = Environment.TickCount;
        lock (Last)
        {
            if (Last.TryGetValue(key, out int previous) && unchecked(now - previous) < intervalMs) return;
            Last[key] = now;
        }
        try { Log.Info(text()); } catch { }
    }

    internal static string Name(Item item)
    {
        try
        {
            if (item.IsNull) return "?";
            return ItemHooks.ModItemOf(item)?.FullName ?? ("item#" + item.type);
        }
        catch { return "?"; }
    }
}
