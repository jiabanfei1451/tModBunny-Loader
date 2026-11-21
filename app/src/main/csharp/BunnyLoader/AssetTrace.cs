using System;
using System.Collections.Generic;

namespace BunnyLoader;

/// <summary>
/// Rastro de depuracao do caminho de desenho dos tiles de mod.
///
/// O crash do Omnistation e um aborto/SEGV NATIVO dentro do Mono, entao nao
/// da para pegar com try/catch: a unica pista e a ultima linha impressa.
/// Por isso cada mensagem DISTINTA aparece uma vez so (as de desenho, que se
/// repetem a cada quadro, nao inundam o log) e o texto nunca e montado por
/// interpolacao dentro de <c>Asset`1.Value</c> (corpo de tipo generico
/// compartilhado, onde montar string e o proprio suspeito).
/// </summary>
internal static class AssetTrace
{
    const int Cap = 4000;

    static readonly HashSet<string> seen = new(StringComparer.Ordinal);
    static readonly Dictionary<int, string> tiles = new();

    internal static bool Enabled = true;

    internal static void Step(string what)
    {
        if (!Enabled) return;
        try
        {
            lock (seen)
            {
                if (seen.Count >= Cap) return;
                if (!seen.Add(what)) return;
            }
            Log.Info("[asset] " + what);
        }
        catch { }
    }

    /// <summary>Passo de desenho de um tipo de tile (texto montado uma vez por tipo).</summary>
    internal static void Tile(int type, int step)
    {
        if (!Enabled) return;
        try
        {
            string key;
            lock (tiles)
            {
                if (!tiles.TryGetValue(type * 8 + step, out key!))
                {
                    key = "draw " + type + " passo " + step;
                    tiles[type * 8 + step] = key;
                }
            }
            Step(key);
        }
        catch { }
    }
}
