using System;
using System.Collections.Generic;

namespace BunnyLoader;

internal static unsafe class Log
{
    // Install() do resolver roda antes de Host.Api existir (Initialize ainda
    // nao publicou o ponteiro). Um Log.Info ali chamava Host.Native(), que
    // lancava InvalidOperationException -> TypeInitializationException no
    // construtor estatico de Host -> o runtime .NET inteiro nao subia e TODOS
    // os mods (.bl inclusive) falhavam com "the .NET runtime did not start".
    // Guardamos as primeiras linhas e despejamos assim que a API fica pronta.
    static readonly List<(int level, string text)> pending = new();
    static readonly object gate = new();

    public static void Info(string text) => Write(0, text);

    public static void Warn(string text) => Write(1, text);

    public static void Error(string text) => Write(2, text);

    /** Chamado por Host.Initialize logo depois de `Api = api`. */
    internal static void Flush()
    {
        List<(int level, string text)> lines;
        lock (gate)
        {
            lines = new List<(int, string)>(pending);
            pending.Clear();
        }
        foreach ((int level, string text) in lines) Write(level, text);
    }

    static void Write(int level, string text)
    {
        // Espelho na tela de carregamento. O arquivo continua sendo o registro
        // completo; isto alimenta as ultimas linhas que aparecem dentro do
        // jogo. Vem antes do nativo para que o painel nao perca justamente a
        // linha que precedeu uma falha.
        LoadingWindow.Line(level, text);

        if (Host.Api == null)
        {
            lock (gate)
            {
                if (pending.Count < 64) pending.Add((level, text));
            }
            return;
        }
        byte* utf8 = Host.Utf8(text);
        try { Host.Native()->Log(level, utf8); }
        finally { Host.FreeUtf8(utf8); }
    }
}
