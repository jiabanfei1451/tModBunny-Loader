using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;

namespace BunnyLoader.Interop;

/// <summary>
/// Retomada automatica de <c>SetupContent</c> apos um crash NATIVO (SIGSEGV).
///
/// O estrago de 17/11 (libmonosgen-2.0.so +0xC8758) mata o processo dentro do
/// <see cref="ModNPC.SetupContent"/> de um NPC de mod, e um crash nativo nao
/// deixa stack confiavel nem excecao para o <c>catch</c> registrar. Sem um PC
/// com adb o ciclo de diagnostico ficava "instala &gt; trava &gt; adivinha".
///
/// Mecanica: antes de cada <c>SetupContent</c> o nome do tipo vai para
/// <c>&lt;pasta do mod&gt;/.setup.resume.txt</c> e, se ele voltar, a linha e
/// apagada. Se o processo morrer no meio, o arquivo fica com o nome do tipo que
/// estava rodando; na carga seguinte esse tipo entra em
/// <c>.setup.skip.txt</c> e passa a ser PULADO, dizendo no log. Assim cada
/// execucao atravessa mais um tipo e a lista de culpados se acumula sozinha.
///
/// Desligar: variavel de ambiente <c>BL_SETUP_RESUME=0</c>, ou apagar
/// <c>.setup.skip.txt</c> da pasta do mod (o arquivo e texto puro, uma chave
/// por linha, <c>#</c> comenta).
///
/// Custo: duas escritas pequenas por tipo instalado. Qualquer erro de I/O
/// desliga o recurso inteiro em vez de atrapalhar a carga.
/// </summary>
internal static class SetupResume
{
    const string ResumeName = ".setup.resume.txt";
    const string SkipName = ".setup.skip.txt";

    static readonly object gate = new();

    /// <summary>Chaves puladas, por pasta de mod (a pasta e o que identifica o mod).</summary>
    static readonly Dictionary<string, HashSet<string>> skipped = new(StringComparer.Ordinal);

    static bool broken;

    internal static bool Enabled =>
        !broken && Environment.GetEnvironmentVariable("BL_SETUP_RESUME") != "0";

    /// <summary>
    /// Le a lista acumulada e a tentativa pendente do mod. Idempotente: a
    /// segunda chamada para a mesma pasta nao faz I/O.
    /// </summary>
    internal static void Prime(Mod? mod)
    {
        string folder = FolderOf(mod);
        if (!Enabled || folder.Length == 0) return;

        lock (gate)
        {
            if (skipped.ContainsKey(folder)) return;

            var set = new HashSet<string>(StringComparer.Ordinal);
            string skipFile = Path.Combine(folder, SkipName);
            string resumeFile = Path.Combine(folder, ResumeName);
            try
            {
                if (File.Exists(skipFile))
                {
                    foreach (string raw in File.ReadAllLines(skipFile))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        set.Add(line);
                    }
                }

                if (File.Exists(resumeFile))
                {
                    string key = File.ReadAllText(resumeFile).Trim();
                    File.WriteAllText(resumeFile, string.Empty);
                    if (key.Length > 0)
                    {
                        if (set.Add(key)) File.AppendAllText(skipFile, key + Environment.NewLine);
                        Log.Warn($"[resume] {Name(folder)}: {key} derrubou a carga anterior; pulando " +
                                 $"(apague {SkipName} para tentar de novo)");
                    }
                }
            }
            catch (Exception e) { Break(e); }

            skipped[folder] = set;
        }
    }

    internal static bool Skip(Mod? mod, string key)
    {
        string folder = FolderOf(mod);
        if (!Enabled || folder.Length == 0) return false;
        lock (gate) return skipped.TryGetValue(folder, out HashSet<string>? set) && set.Contains(key);
    }

    /// <summary>Marca "estou entrando neste tipo"; so sai daqui se ele terminar.</summary>
    internal static void Enter(Mod? mod, string key)
    {
        string folder = FolderOf(mod);
        if (!Enabled || folder.Length == 0) return;
        lock (gate)
        {
            try { File.WriteAllText(Path.Combine(folder, ResumeName), key); }
            catch (Exception e) { Break(e); }
        }
    }

    internal static void Leave(Mod? mod)
    {
        string folder = FolderOf(mod);
        if (!Enabled || folder.Length == 0) return;
        lock (gate)
        {
            try
            {
                string file = Path.Combine(folder, ResumeName);
                if (File.Exists(file)) File.WriteAllText(file, string.Empty);
            }
            catch (Exception e) { Break(e); }
        }
    }

    static string FolderOf(Mod? mod)
    {
        string folder = mod?.Folder ?? "";
        return folder.Length == 0 ? "" : folder;
    }

    static string Name(string folder)
    {
        try { return Path.GetFileName(folder.TrimEnd('/', '\\')); }
        catch { return folder; }
    }

    static void Break(Exception e)
    {
        if (broken) return;
        broken = true;
        Log.Warn($"[resume] desligado: {e.GetType().Name}: {e.Message}");
    }
}
