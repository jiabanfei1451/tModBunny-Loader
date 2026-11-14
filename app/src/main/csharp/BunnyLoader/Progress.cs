using System;
using System.IO;
using System.Threading;

namespace BunnyLoader;

/// <summary>
/// Progresso do carregamento para a tela do jogo.
///
/// O carregamento dos mods acontece no processo <c>:game</c> e quem olha o
/// aparelho não tem como saber se o jogo está trabalhando ou travado. O
/// Kotlin (GameActivity) lê este arquivo e desenha o painel central.
///
/// Formato: <c>porcentagem|texto</c>. A porcentagem pode faltar (o painel
/// então pulsa em vez de encher), e é sempre MONOTÔNICA: duas etapas que
/// calculem o mesmo trecho não fazem a barra andar para trás.
///
/// O arquivo fica em <c>filesDir/dotnet/</c> — o mesmo diretório de onde o
/// Mono carrega os assemblies (AppContext.BaseDirectory), visível para os
/// dois processos.
/// </summary>
internal static class Progress
{
    static readonly string? file = Resolve();
    static readonly int totalMods = ResolveTotal();
    static int lastPercent;
    static int modIndex;

    static string? Resolve()
    {
        try { return Path.Combine(AppContext.BaseDirectory, "tml-progress.txt"); }
        catch { return null; }
    }

    /// <summary>
    /// Quantos <c>.tmod</c> estão ligados nesta abertura. O Kotlin grava em
    /// <c>tml-count.txt</c> antes de subir a Unity; sem ele a barra trata a
    /// carga como um mod só e vai direto ao fim do trecho.
    /// </summary>
    static int ResolveTotal()
    {
        try
        {
            string side = Path.Combine(AppContext.BaseDirectory, "tml-count.txt");
            if (File.Exists(side) && int.TryParse(File.ReadAllText(side).Trim(), out int n) && n > 0)
                return n;
        }
        catch { /* o progresso é cosmético */ }
        return 0;
    }

    internal static void Write(string text) => Write(text, -1);

    internal static void Write(string text, int percent)
    {
        // A janela do jogo e o destino principal. Ela recebe primeiro para que
        // uma falha de escrita (cartao cheio, diretorio removido) nao deixe o
        // painel parado numa etapa antiga.
        LoadingWindow.Report(text, percent);
        if (file == null) return;
        try
        {
            string body = text;
            if (percent >= 0)
            {
                int value = Math.Clamp(percent, 0, 100);
                // Sem monotonicidade a barra recua quando um mod pequeno
                // termina depois de um grande.
                if (value < Volatile.Read(ref lastPercent)) value = Volatile.Read(ref lastPercent);
                else Volatile.Write(ref lastPercent, value);
                body = value.ToString() + "|" + text;
            }
            File.WriteAllText(file, body);
        }
        catch { /* progresso é cosmético: nunca derruba o carregamento */ }
    }

    /// <summary>Começa a carga de mais um mod (a barra reserva 5..55% para isso).</summary>
    internal static void BeginMod() => Interlocked.Increment(ref modIndex);

    /// <summary>
    /// Uma etapa dentro do mod atual. <paramref name="within"/> é 0..1 dentro
    /// da fatia do mod: compilar no começo, autoload no fim.
    /// </summary>
    internal static void ModStep(string text, double within)
    {
        int total = totalMods > 0 ? totalMods : 1;
        int index = Math.Min(modIndex, total) - 1;
        if (index < 0) index = 0;
        double span = 50.0 / total;
        int percent = (int)Math.Round(5 + index * span + span * Math.Clamp(within, 0, 1));
        Write(text, percent);
    }

    /// <summary>
    /// Apaga o arquivo de progresso. A janela do jogo NAO some aqui: quem a
    /// fecha e o 100% (ou o tempo sem noticias). Um mod terminar nao significa
    /// que a abertura acabou — os conteudos vem depois.
    /// </summary>
    internal static void Clear()
    {
        if (file == null) return;
        try { File.WriteAllText(file, ""); }
        catch { /* progresso e cosmetico */ }
    }
}
