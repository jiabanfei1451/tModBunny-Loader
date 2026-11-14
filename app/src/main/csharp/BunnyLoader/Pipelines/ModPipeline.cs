using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace BunnyLoader.Pipelines;

/// <summary>Qual loader deve tratar o pacote.</summary>
internal enum ModKind { Bl, Tml }

/// <summary>Dados de um pacote antes de qualquer decisão de pipeline.</summary>
internal sealed record ModLoadRequest(
    string Uid, string File, string Folder, string Label,
    string InternalName, string[] Dependencies);

/// <summary>
/// Uma das DUAS rotas de carregamento do Bunny Loader.
///
///   BlPipeline  -> `.bl`: compilado contra o shim que ESTÁ AQUI. Sem IL
///                 rewrite, sem fachadas, contrato = `app/build/generated/csharp`.
///   TmlPipeline -> `.tmod`: compilado contra tModLoader 1.4.4.9 (a fonte em
///                 `Bunny-Loader-main/tmodloader/`). Precisa de IlPatcher,
///                 fachadas e do contrato 1.4.4.9; a IMPLEMENTAÇÃO equivalente
///                 sobre o jogo 1.4.5.8 fica em `tModLoader-1.4.5/`.
///
/// A separação é intencional: misturar as duas foi a origem de uma sequência
/// de bugs de assinatura (Color, SoundStyle, Tile, Condition...). Cada rota
/// agora tem o seu próprio ponto de decisão e o seu próprio contrato de API.
/// </summary>
internal abstract class ModPipeline
{
    internal abstract ModKind Kind { get; }

    /// <summary>Raiz da API que o mod DESTE pipeline esperava em tempo de compilação.</summary>
    internal abstract string ContractRoot { get; }

    /// <summary>Raiz com a implementação equivalente no jogo 1.4.5.8.</summary>
    internal abstract string ImplementationRoot { get; }

    /// <summary>Texto da faixa de progresso (diferencia as duas rotas no log).</summary>
    internal abstract string LoadingText(ModLoadRequest request);

    /// <summary>Instala o que esta rota precisa antes de carregar o assembly.</summary>
    internal abstract void Prepare();

    /// <summary>Carrega o assembly do mod.</summary>
    internal abstract Assembly LoadAssembly(ModLoadRequest request);

    /// <summary>Descobre as classes `Mod` segundo as regras desta rota.</summary>
    internal virtual IEnumerable<Type> DiscoverTypes(Assembly assembly, string uid) => ModTypes.Of(assembly, uid);

    /// <summary>Linha extra no log quando esta rota falha: onde olhar o contrato.</summary>
    internal virtual void ReportContract()
    {
        Log.Info($"pipeline {Kind}: contrato    = {ContractRoot}");
        Log.Info($"pipeline {Kind}: implement. = {ImplementationRoot}");
    }

    internal static ModPipeline For(string folder) =>
        folder.Replace('\\', '/').Contains("/TML/", StringComparison.OrdinalIgnoreCase)
            ? TmlPipeline.Instance
            : BlPipeline.Instance;
}
