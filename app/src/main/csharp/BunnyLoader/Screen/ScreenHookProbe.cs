using System;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Graphics.Effects;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

/// <summary>
/// Prova de vida do <c>FilterManager.EndCapture</c>.
///
/// O Luminance nao desenha filtro de tela por conta propria: o
/// <c>ScreenModifierManager</c> dele assina
/// <c>On_FilterManager.EndCapture</c> e, de dentro desse gancho, chama
/// <c>ShaderManager::ApplyScreenFilters</c>, que e' quem faz
/// <c>ManagedScreenFilter.Apply()</c> -> <c>EffectPass.Apply()</c> -> nossa ponte.
/// Ou seja: sem esse gancho disparando, nenhum filtro sai, mesmo com todos os
/// shaders registrados.
///
/// Aqui a inscricao e' nossa (e nao so' a dele) por dois motivos: forca a
/// instalacao do detour nativo no boot (em vez de depender do ModSystem dele
/// ter carregado), e deixa uma linha no log dizendo se o gancho disparou —
/// antes de 2026-11-25 nao havia como saber.
///
/// O corpo e' passa-livre: chama o original e nao mexe em nada.
/// </summary>
internal static class ScreenHookProbe
{
    static int calls;

    internal static void Install() =>
        ItemHooks.Once("ScreenHookProbe", () =>
        {
            On_FilterManager.EndCapture += Handler;
            // Mesma receita do DoDraw: a porta do HookGate fecha durante a
            // carga, e sem `Always` o `Chain` volta nulo e o detour cai no
            // original (isto e', o gancho nunca roda).
            Hooks.HookChain<On_FilterManager.hook_EndCapture, On_FilterManager.orig_EndCapture>.Always(true);
            Log.Info("[fx] hook: EndCapture inscrito (filtros de tela do tModLoader)");
        });

    static void Handler(On_FilterManager.orig_EndCapture orig, FilterManager self,
        RenderTarget2D finalTexture, RenderTarget2D screenTarget1, RenderTarget2D screenTarget2,
        Color clearColor)
    {
        int n = Interlocked.Increment(ref calls);
        if (n == 1)
            Log.Info("[fx] FilterManager.EndCapture disparou: o caminho dos filtros de tela esta vivo");
        else if (n % 600 == 0)
            Log.Info($"[fx] FilterManager.EndCapture: {n} chamadas");

        orig(self, finalTexture, screenTarget1, screenTarget2, clearColor);
    }
}
