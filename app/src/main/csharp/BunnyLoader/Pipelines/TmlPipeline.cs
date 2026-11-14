using System.Reflection;

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

    internal override Assembly LoadAssembly(ModLoadRequest request)
    {
        Progress.ModStep($"正在重写 IL：{request.Label}…", 0.25);
        return IlPatcher.Load(request.File);
    }
}
