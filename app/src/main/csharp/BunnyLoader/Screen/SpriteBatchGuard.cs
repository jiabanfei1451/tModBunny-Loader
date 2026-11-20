using System;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;

namespace BunnyLoader;

/// <summary>
/// Rede de seguranca do lote de sprites.
///
/// Qualquer gancho de mod que abra um Begin e estoure no meio deixa o
/// <c>_beginCalled</c> do SpriteBatch em true. A partir dai TODO Begin do jogo
/// falha com "Begin cannot be called again until End has been successfully
/// called" e a tela nao desenha mais nada — foi exatamente a tela preta da v115
/// (o <c>new RenderTarget2D</c> do Luminance estourava entre Begin e End em
/// <c>PrimitivePixelationSystem.DrawTargetScaled</c>).
///
/// Na virada do quadro nada deveria estar aberto, entao fechar aqui e sempre
/// correto — e e o unico ponto em que fechar nao atropela um Begin/End legitimo
/// do proprio jogo (que pode estar no meio de um par quando um gancho de mod
/// roda). Chamado do <c>On_Main.DoDraw</c> antes do original.
/// </summary>
internal static class SpriteBatchGuard
{
    static int fixes;
    static int endFails;

    internal static void Unstick()
    {
        SpriteBatch? batch;
        try { batch = Terraria.Main.spriteBatch; }
        catch { return; }
        if (batch is null || batch.Pointer == 0) return;

        bool open;
        try { open = batch._beginCalled; }
        catch { return; }
        if (!open) return;

        int n = Interlocked.Increment(ref fixes);
        // O primeiro caso explica a tela preta; depois disso o numero so aparece
        // de vez em quando para nao inundar o log (o defeito pode ser por quadro).
        if (n <= 5 || n % 300 == 0)
            Log.Warn($"sprite batch: estava aberto na virada do quadro ({n}x); fechando para o desenho voltar");

        try
        {
            batch.End();
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref endFails) <= 3)
                Log.Warn($"sprite batch: End de emergencia falhou ({e.GetType().Name}: {e.Message}); forco _beginCalled=false");
            try { batch._beginCalled = false; } catch { /* nada mais a fazer */ }
        }
    }
}