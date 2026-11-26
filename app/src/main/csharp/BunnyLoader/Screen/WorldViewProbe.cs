using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

/// <summary>
/// v42: sonda da visao do mundo + vigia do alvo de renderizacao.
///
/// O print da v41 mostra o mundo vazio (sem tile, sem ceu) com o jogador e as
/// tochas desenhados por cima. Duas familias de causa possiveis:
///
/// 1. a camera/mundo nao esta desenhando (tamanho de tela, camera, scissor);
/// 2. o quadro esta sendo desenhado num ALVO de renderizacao que nunca vai para
///    a tela (mod preso no alvo, sintoma classico de "tela branca com UI").
///
/// A sonda imprime uma linha por segundo com <c>screenWidth/Height</c>,
/// <c>screenPosition</c>, zoom, scissor, <c>Main.drawToScreen</c> e
/// <c>GetRenderTargets()</c> — o numero de alvos ligados no device NA HORA em
/// que a interface ja foi desenhada (deveria ser 0).
///
/// O vigia (<see cref="TownDebugSetting.ResetTargets"/>) faz o que o jogo faria
/// no fim do quadro: se houver alvo preso, volta para a tela. E' diagnostico e
/// correcao ao mesmo tempo, e da' para desligar na janela de depuracao.
/// </summary>
internal static class WorldViewProbe
{
    static int left = 24;
    static int frames;
    static int resetsLogged;

    // v47: o rastro GL morava no MiscShaderFrame.DoDraw, que so' era assinado
    // no primeiro desenho de shader — e com o registro do Luminance vazio isso
    // nunca acontecia (v46 saiu sem nenhuma linha "rastro GL"). Aqui a sonda
    // roda todo quadro de jogo (MouseText), entao serve de gatilho.
    static readonly int[] TraceFrames = { 12, 60, 120, 200 };

    static int traceNext;

    static unsafe void RequestGlTrace()
    {
        if (traceNext >= TraceFrames.Length || frames != TraceFrames[traceNext]) return;
        traceNext++;
        Log.Info($"rastro GL: pedido no quadro {frames} (tela {Main.screenWidth}x{Main.screenHeight} " +
                 $"drawToScreen={Main.drawToScreen})");
        Host.Native()->GlTrace();
    }

    internal static void Install() => ItemHooks.Once("WorldViewProbe", () =>
        On_Main.DrawInterface_33_MouseText += Draw);

    static void Draw(On_Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        orig(self);
        if (Main.gameMenu) return;
        frames++;

        if (frames >= 12) RequestGlTrace();
        // v42: as 8 primeiras linhas saem por quadro; depois, uma por segundo
        // (~60 quadros), para o log nao virar 2500 linhas.
        bool sample = frames <= 8 || frames % 60 == 0;
        if (!sample || left <= 0) return;
        left--;

        int attached = -1;
        string scissor = "?";
        try
        {
            GraphicsDevice? device = Main.graphics?.GraphicsDevice;
            if (device != null && device.Pointer != 0)
            {
                Rectangle s = device.ScissorRectangle;
                scissor = $"{s.X},{s.Y},{s.Width},{s.Height}";
                try { attached = device.GetRenderTargets()?.Length ?? 0; }
                catch (Exception e) { attached = -2; Log.Info($"[view] GetRenderTargets falhou: {e.GetType().Name}: {e.Message}"); }
            }
        }
        catch (Exception e) { scissor = e.GetType().Name; }

        string target = "?";
        try { target = $"{Main.targetSet}"; } catch { }

        Log.Info($"[view] screen={Main.screenWidth}x{Main.screenHeight} " +
            $"pos={Main.screenPosition.X:0},{Main.screenPosition.Y:0} zoom={Main.GameZoomTarget:0.00} " +
            $"scissor={scissor} drawToScreen={Main.drawToScreen} targetSet={target} alvos={attached}");

        // Vigia: no fim da interface o device tem de estar na tela.
        if (!TownDebugSetting.ResetTargets) return;
        try
        {
            GraphicsDevice? device = Main.graphics?.GraphicsDevice;
            if (device == null || device.Pointer == 0) return;
            if (Main.drawToScreen) return;
            if (attached > 0)
            {
                device.SetRenderTarget(null);
                if (resetsLogged < 8)
                {
                    resetsLogged++;
                    Log.Info($"[view] vigia: {attached} alvo(s) preso(s) no fim do quadro -> de volta para a tela");
                }
            }
        }
        catch (Exception e) { ItemHooks.Report("WorldViewProbe (vigia)", e); }
    }
}
