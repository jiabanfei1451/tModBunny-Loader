using System;
using System.Collections.Generic;
using BunnyLoader.Graphics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

// Collects the draws produced by mod MiscShaderData during a frame and submits
// them once per frame, after Main.DoDraw. The native side reads the whole frame
// from screenSubmit, so one submit per frame keeps the protocol happy.
internal static class MiscShaderFrame
{
    // Cada desenho custa uma copia da tela + um passe de tela inteira no GLES.
    // Trails/auras chamam Apply uma vez por segmento, entao o teto existe para
    // o quadro nao virar 200 passes (o nativo tambem corta em 256).
    const int MaxDrawsPerFrame = 96;

    static readonly object Gate = new();

    static readonly List<ScreenDraw> Pending = new();

    static bool submitted;

    static int truncated;

    static int framesSinceWarning;

    static bool announced;

    internal static void Install() =>
        ItemHooks.Once("MiscShaderFrame", () =>
        {
            On_Main.DoDraw += DoDraw;
            // A porta do HookGate fica fechada durante a carga, e o `__n` do
            // HookGen le `Chain == null`: sem esta excecao o DoDraw volta direto
            // ao original e nenhum quadro e' entregue ao `screenSubmit`. Mesma
            // receita do LoadingWindow (que funciona: painel desenhado).
            Hooks.HookChain<On_Main.hook_DoDraw, On_Main.orig_DoDraw>.Always(true);
        });

    internal static void Add(ScreenDraw draw)
    {
        lock (Gate) Pending.Add(draw);
    }
    static int frameTick;
    static void DoDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        try { Submit(); }
        catch (Exception e) { ItemHooks.Report("MiscShaderFrame (frame)", e); }

        // v58: resumo periodico. "nenhum #alvo no log" sozinho nao diz se o
        // caminho nunca foi percorrido ou se passou reto; com o numero, diz.
        if (++frameTick % 300 == 0)
            Log.Info($"[fx] resumo: apply={EffectShaderBridge.Applied} submit={EffectShaderBridge.Submitted} "
                + $"alvosMod={Microsoft.Xna.Framework.Graphics.GraphicsDevice.ModTargetBindings} "
                + $"clarosMod={Microsoft.Xna.Framework.Graphics.GraphicsDevice.ModTargetClears}");
    }

    static void Submit()
    {
        ScreenDraw[] draws;
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                if (!submitted) return;
                submitted = false;
                ScreenFrame.SubmitEmpty();
                return;
            }

            draws = Pending.ToArray();
            Pending.Clear();
        }

        if (draws.Length > MaxDrawsPerFrame)
        {
            // Corta os excedentes (os primeiros desenhos sao os mais antigos, e
            // sao os que o jogador ve primeiro no caso de trails).
            truncated = draws.Length - MaxDrawsPerFrame;
            draws = draws[..MaxDrawsPerFrame];
        }
        else
        {
            truncated = 0;
        }

        var frame = new ScreenFrame();
        frame.Draws.AddRange(draws);
        frame.Submit();
        submitted = true;

        // Uma linha por sessao: e' a prova de que o quadro saiu do C# e chegou
        // ao `screenSubmit` (antes desta rodada nao havia nenhuma).
        if (!announced)
        {
            announced = true;
            Log.Info($"shader: primeiro quadro entregue ao GLES ({draws.Length} desenho(s))");
        }

        if (truncated > 0 && ++framesSinceWarning % 120 == 1)
            Log.Warn($"shader: {truncated} desenho(s) de MiscShaderData cortados neste quadro (teto {MaxDrawsPerFrame})");
    }
}
