using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace BunnyLoader;

/// <summary>
/// Registro em disco dos patches de IL que os mods pedem.
///
/// Por que existe: no il2cpp não há IL do jogo para editar, então hoje o
/// <c>ILHook</c> roda o manipulador contra um esqueleto e não aplica nada. Sem
/// este arquivo o patch simplesmente desaparece — e a próxima etapa ("fazer o
/// efeito do Luminance valer") começaria às cegas. Com ele, cada alvo fica
/// gravado com a assinatura, o motivo da falha e o IL resultante, que é
/// exatamente o que uma implementação de verdade precisa consumir.
///
/// Formato (append-only):
/// <code>
/// ok    Luminance  ILHook  Terraria.Main.DrawTiles(...)   esqueleto; 3 instrução(ões)
/// falha Luminance  ILHook  Terraria.Main.DrawTiles(...)   GotoNext: padrão não encontrado
/// </code>
///
/// Nada aqui pode lançar: um erro de I/O dentro do <c>Apply()</c> derrubaria o
/// construtor estático do mod, que é o problema que este trabalho resolve.
/// </summary>
internal static class IlPatchLedger
{
    /// <summary><c>&lt;TML&gt;/ilpatch</c>. Preenchido pelo TmlPipeline.</summary>
    internal static string? Dir;

    static readonly object Gate = new();
    static int seq;

    internal static void Record(string source, string kind, MethodBase method, MonoMod.Cil.ILContext? context, string note, Exception? error, bool applied)
    {
        string signature = Signature(method);
        int instructions = context?.Body.Instructions.Count ?? 0;
        Log.Info($"ilpatch: {source} {kind} {signature} -> {(error == null ? (applied ? "aplicado" : "registrado") : "falhou")} ({note}; {instructions} instrução(ões))");
        if (error != null) Log.Info($"ilpatch: {source} {kind} motivo: {error.GetType().Name}: {error.Message}");

        lock (Gate)
        {
            try
            {
                if (string.IsNullOrEmpty(Dir)) return;
                Directory.CreateDirectory(Dir);
                seq++;

                var line = new StringBuilder();
                line.Append(error == null ? (applied ? "aplic " : "ok    ") : "falha ");
                line.Append(source).Append("  ").Append(kind).Append("  ").Append(signature);
                line.Append("  ").Append(note);
                if (error != null) line.Append(": ").Append(error.GetType().Name).Append(": ").Append(error.Message);
                line.Append('\n');
                File.AppendAllText(Path.Combine(Dir, "ledger.txt"), line.ToString(), Encoding.UTF8);

                // Corpo por alvo: é o material que uma aplicação de verdade vai
                // consumir (âncoras que casaram, instruções emitidas).
                var dump = new StringBuilder();
                dump.Append("# ").Append(source).Append(' ').Append(kind).Append('\n');
                dump.Append("# alvo: ").Append(signature).Append('\n');
                dump.Append("# nota: ").Append(note).Append('\n');
                if (error != null) dump.Append("# erro: ").Append(error.GetType().Name).Append(": ").Append(error.Message).Append('\n');
                dump.Append(context?.Dump() ?? "# (sem corpo)\n");
                File.WriteAllText(Path.Combine(Dir, $"{seq:D3}-{FileName(method)}.il"), dump.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                // Não relança: o ledger é diagnóstico, não parte do contrato.
                try { Log.Warn($"ilpatch: ledger indisponível ({e.GetType().Name}: {e.Message})"); } catch { }
            }
        }
    }

    internal static string Signature(MethodBase method)
    {
        try
        {
            var sb = new StringBuilder();
            if (method.DeclaringType != null) sb.Append(method.DeclaringType.FullName).Append('.');
            sb.Append(method.Name).Append('(');
            ParameterInfo[] ps = method.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ps[i].ParameterType.Name);
            }
            sb.Append(')');
            return sb.ToString();
        }
        catch { return method.Name; }
    }

    static string FileName(MethodBase method)
    {
        try
        {
            string type = method.DeclaringType?.Name ?? "?";
            var sb = new StringBuilder();
            foreach (char c in type + "-" + method.Name)
                sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
            return sb.ToString();
        }
        catch { return "il"; }
    }
}
