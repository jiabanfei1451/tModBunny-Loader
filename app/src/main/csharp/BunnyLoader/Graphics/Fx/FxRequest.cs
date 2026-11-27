using System;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace BunnyLoader.Graphics;

/// <summary>
/// v60: destino do <c>callvirt AssetRepository::Request&lt;Effect&gt;</c> dos
/// mods, depois da reescrita de IL (ver <c>IlPatcher.RewriteEffectRequest</c>).
///
/// O porquê: no Mono do aparelho esse <c>callvirt</c> de método genérico
/// estoura <c>NullReferenceException</c> SEM frame nenhum — nem o corpo do
/// <c>Request&lt;Effect&gt;</code> do shim roda. Era exatamente o NRE do
/// <c>Luminance.Core.Graphics.ShaderManager.LoadShaders</code>, 13 ms depois de
/// <c>get_Assets()</c> (que loga) e sem uma única linha
/// <c>[fx] req T=...Effect</c>. Com o NRE, o <c>PostSetupContent</c> do
/// Luminance aborta no primeiro shader: o registro dele fica vazio, o
/// <c>AtlasManager</c>/<c>ParticleManager</c> dele nunca inicializam e o
/// <c>HasFinishedLoading</code> fica falso para sempre.
///
/// A chamada estática abaixo é NON-generic do ponto de vista do mod: o Mono
/// só precisa ligar um <c>call</c> comum e o <c>Request&lt;Effect&gt;</code>
/// é instanciado dentro DESTE assembly (o prewarm do v48 já provou que essa
/// instanciação funciona).
/// </summary>
public static class FxRequest
{
    static int logged;

    /// <summary>
    /// Mesma assinatura de pilha do membro substituído (receptor + 2 args),
    /// então a reescrita não mexe em mais nada do corpo do mod.
    ///
    /// v61: PUBLIC de propósito — o chamador é o assembly do mod
    /// (Luminance.dll) e um método interno dá
    /// <c>System.MethodAccessException</code> em vez de rodar.
    /// </summary>
    public static Asset<Effect> RequestEffect(AssetRepository repository, string assetName, AssetRequestMode mode)
    {
        Asset<Effect>? asset = null;
        try
        {
            asset = repository is ModAssets concrete
                ? concrete.RequestConcrete<Effect>(assetName, mode)
                : repository.Request<Effect>(assetName, mode);
        }
        catch (Exception e)
        {
            Log.Error($"[fx] RequestEffect({assetName}): {e.GetType().Name}: {e.Message}");
        }

        asset ??= new Asset<Effect>($"{assetName}", null);

        if (System.Threading.Interlocked.Increment(ref logged) <= 20)
            Log.Info($"[fx] RequestEffect({assetName}) mode={mode} mod={(repository is ModAssets m ? m.ModName : "?")}");

        return asset;
    }
}
