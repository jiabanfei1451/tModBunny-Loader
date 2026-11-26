using System;
using System.Collections.Generic;
using System.IO;
using BunnyLoader;
using Terraria.ModLoader;

namespace BunnyLoader.Graphics;

/// <summary>
/// Inventario de shaders no disco, uma vez por sessao, logo depois do
/// <c>PostSetupContent</c>.
///
/// Serve para distinguir duas causas que o log do jogo nao separa:
///   - o mod **nao tem** <c>.xnb</c>/<c>.fx</c> na pasta (extracao antiga, ou
///     extractor que descartou o arquivo) — nesse caso o
///     <c>ShaderManager::LoadShaders</c> do Luminance filtra zero caminhos e
///     registra zero shaders, e nao ha' o que desenhar;
///   - o mod **tem** os arquivos e o problema e' o nosso lado (tipo do tML
///     faltando, stub de <c>Effect</c>, bridge).
///
/// Uma linha por mod que tenha qualquer coisa; silencio para os outros.
/// </summary>
internal static class FxInventory
{
    static bool done;

    internal static void Report()
    {
        if (done) return;
        done = true;

        Mod[] mods;
        try { mods = Terraria.ModLoader.ModLoader.Mods; }
        catch (Exception) { return; }

        foreach (Mod mod in mods)
        {
            if (mod == null || string.IsNullOrEmpty(mod.Folder)) continue;

            var fx = new List<string>();
            var xnb = new List<string>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(mod.Folder, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(path);
                    string relative = path.Replace('\\', '/');
                    if (!relative.Contains("AutoloadedEffects", StringComparison.Ordinal)) continue;
                    if (name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)) fx.Add(relative);
                    else if (name.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)) xnb.Add(relative);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"[fx] {mod.Name}: inventario falhou ({e.GetType().Name}: {e.Message})");
                continue;
            }

            if (fx.Count == 0 && xnb.Count == 0) continue;

            Log.Info($"[fx] {mod.Name}: {fx.Count} .fx e {xnb.Count} .xnb em AutoloadedEffects ("
                + $"{FxRegistry.Count} programa(s) GLSL ja registrado(s))");

            // .fx sem .xnb e' o que o Luminance poe na fila de recompilacao: no
            // PC ele chama o compilador da FXC; aqui ninguem compila, entao
            // esses shaders simplesmente nao existem para o registro dele.
            if (fx.Count > 0 && xnb.Count == 0)
                Log.Warn($"[fx] {mod.Name}: tem .fx mas nenhum .xnb — a extracao do .tmod perdeu os "
                    + "shaders compilados; o registro do mod vai ficar vazio");
        }
    }
}
