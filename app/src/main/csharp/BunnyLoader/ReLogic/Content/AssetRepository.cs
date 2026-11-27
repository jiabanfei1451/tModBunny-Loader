using System;

namespace ReLogic.Content;

/// <summary>
/// O repositorio de assets de um mod. No tML 1.4.4 <c>Mod.Assets</c> e do
/// tipo <c>ReLogic.Content.AssetRepository</c>, e nao de um tipo do shim.
///
/// O mod compilado no PC chama:
/// <c>callvirt ReLogic.Content.AssetRepository Terraria.ModLoader.Mod::get_Assets()</c>
/// e em seguida
/// <c>callvirt Asset`1&lt;!!0&gt; AssetRepository::Request&lt;T&gt;(string, AssetRequestMode)</c>.
/// Tipo do receptor e tipo de retorno fazem parte da assinatura: enquanto
/// <c>Mod.Assets</c> devolvia <c>BunnyLoader.ModAssets</c> (outro tipo) a
/// chamada nao casava com membro nenhum e o metodo inteiro do mod falhava ao
/// JITar. Por isso <see cref="BunnyLoader.ModAssets"/> deriva daqui.
///
/// A restricao generica e <c>where T : class</c>, como no tML. O
/// <c>IGameObject</c> do shim NAO pode entrar nesta restricao: o Luminance
/// pede <c>Request&lt;Effect&gt;</c> e <c>Effect</c> (FNA) nao implementa a
/// interface — o JIT do mod lancaria TypeLoadException por violacao de
/// restricao antes mesmo de executar a chamada.
///
/// No Android nao existe o pool de assets carregados do jogo: quem implementa
/// isto e o <see cref="BunnyLoader.ModAssets"/>, que le do disco do mod.
/// </summary>
public abstract class AssetRepository : IDisposable
{
    public Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad)
        where T : class
    {
        // v59: NAO e' virtual de proposito.
        //
        // O mod compilado no PC chama
        // `callvirt Asset`1<!!0> AssetRepository::Request<T>(string, AssetRequestMode)`.
        // Callvirt em metodo virtual generico precisa de um slot de vtable por
        // instanciacao; no aparelho o slot de `Request<Effect>` nao existia e o
        // callvirt estourava NullReferenceException SEM frame na pilha — era
        // exatamente o NRE do `Luminance.ShaderManager.LoadShaders`, na linha
        // `mod.Assets.Request<Effect>(...)`, sem nenhuma linha
        // `[fx] Request<Effect>` antes (o corpo do override nunca rodava).
        // Metodo concreto = chamada direta: sem slot, sem depender de
        // instanciacao de override generico.
        if (this is BunnyLoader.ModAssets mod)
            return mod.RequestConcrete<T>(assetName, mode);

        return new Asset<T>(assetName, null);
    }

    public virtual bool HasAsset<T>(string assetName) where T : class => false;

    public virtual void Dispose() { }
}
