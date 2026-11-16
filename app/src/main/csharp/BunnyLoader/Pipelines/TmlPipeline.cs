using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria.ModLoader.Core;

namespace BunnyLoader.Pipelines;

/// <summary>
/// Rota tModLoader (`.tmod`, AssemblyRef `tModLoader, Version=1.4.4.9`).
///
/// Contrato  = `Bunny-Loader-main/tmodloader/` (tModLoader 2026.09.3.1 = linha
///             estável 1.4.4.9, a API que o mod via quando foi compilado).
/// Implement = `tModLoader-1.4.5/` + shim gerado do dump do jogo 1.4.5.8.
///
/// Sempre que um `.tmod` der MissingMethod/TypeLoad, a resposta está no
/// contrato; como traduzir para o Android está na implementação.
/// </summary>
internal sealed class TmlPipeline : ModPipeline
{
    internal static readonly TmlPipeline Instance = new();

    internal override ModKind Kind => ModKind.Tml;
    internal override string ContractRoot => "tmodloader/patches/tModLoader (1.4.4.9 / 2026.09.3.1)";
    internal override string ImplementationRoot => "tModLoader-1.4.5/patches/tModLoader + app/build/generated/csharp";
    internal override string LoadingText(ModLoadRequest request) => $"正在加载 {request.Label}（PC 模组）…";

    internal override void Prepare()
    {
        // As fachadas (tModLoader.dll / FNA.dll / ReLogic.dll /
        // TerrariaHooks.dll / log4net.dll) são resolvidas por ShimAssembly,
        // instalado no Host. Aqui só registramos que a rota é a do PC.
    }

    static bool announced;

    internal override Assembly LoadAssembly(ModLoadRequest request)
    {
        // v68: deixa o ItemHooks saber a pasta TML para ler flags de diagnostico
        // (ex.: <TML>/no-owneronly).
        try
        {
            string? modsDir = Path.GetDirectoryName(request.Folder);
            string? tmlDir = modsDir == null ? null : Path.GetDirectoryName(modsDir);
            if (tmlDir != null)
            {
                ItemHooks.DebugDir = tmlDir;
                // Ledger dos patches de IL: <TML>/ilpatch. Sem isso o ILHook do
                // MonoMod (Luminance/FargoSouls) só grava no log, e o log é
                // rotativo/curto — o que interessa é o IL resultante por alvo.
                IlPatchLedger.Dir = Path.Combine(tmlDir, "ilpatch");
            }
        }
        catch { }
        AnnounceAll(request);
        Progress.ModStep($"正在重写 IL：{request.Label}（{request.InternalName}）…", 0.25);
        return IlPatcher.Load(request.File);
    }

    // v67: o painel mostrava so o mod da vez; como a reescrita e' por arquivo,
    // nao dava para saber quantos/qua*s* seriam. Lemos a lista de habilitados
    // (TML/mode.txt) uma vez e mostramos os NOMES INTERNOS de todos.
    static void AnnounceAll(ModLoadRequest request)
    {
        if (announced) return;
        announced = true;
        try
        {
            string? modsDir = Path.GetDirectoryName(request.Folder);
            string? tmlDir = modsDir == null ? null : Path.GetDirectoryName(modsDir);
            string mode = tmlDir == null ? "" : Path.Combine(tmlDir, "mode.txt");
            var list = new List<string>();
            if (mode.Length > 0 && File.Exists(mode))
            {
                foreach (string line in File.ReadAllLines(mode))
                {
                    string name = line.Trim();
                    if (name.Length > 0) list.Add(name);
                }
            }
            if (list.Count == 0) list.Add(request.InternalName);
            string joined = string.Join("、", list);
            Log.Info($"TML: reescrevendo IL de {list.Count} mod(s): {joined}");
            Progress.Write($"正在重写 IL：{joined}（共 {list.Count} 个）");
        }
        catch (Exception e) { Log.Warn("TML: lista de mods indisponivel (" + e.Message + ")"); }
    }
}
