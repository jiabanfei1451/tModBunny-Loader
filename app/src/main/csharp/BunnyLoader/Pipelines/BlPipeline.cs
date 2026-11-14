using System.Reflection;
using System.Runtime.Loader;

namespace BunnyLoader.Pipelines;

/// <summary>
/// Rota NATIVA (`.bl`). O pacote já foi compilado contra este shim, então não
/// há reescrita de IL nem fachada de assembly: o contrato é o próprio código
/// gerado pelo ProxyGen.
/// </summary>
internal sealed class BlPipeline : ModPipeline
{
    internal static readonly BlPipeline Instance = new();

    internal override ModKind Kind => ModKind.Bl;
    internal override string ContractRoot => "app/build/generated/csharp (ProxyGen / shim atual)";
    internal override string ImplementationRoot => "app/src/main/csharp/BunnyLoader (shim atual)";
    internal override string LoadingText(ModLoadRequest request) => $"正在加载 {request.Label}…";

    internal override void Prepare()
    {
        // Nada: ShimAssembly.Install() é comum às duas rotas e fica no Host.
        // `.bl` não deve passar pelo Cecil — custo puro e diagnóstico confuso.
    }

    internal override Assembly LoadAssembly(ModLoadRequest request) =>
        AssemblyLoadContext.Default.LoadFromAssemblyPath(request.File);
}
