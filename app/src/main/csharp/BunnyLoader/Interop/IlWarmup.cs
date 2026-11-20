using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace BunnyLoader.Interop;

/// <summary>
/// Compila (JIT) todos os metodos de um assembly de mod assim que ele entra
/// no jogo, ANTES de instalar o conteudo.
///
/// Motivo: o Mono do aparelho so compila um metodo quando ele e chamado pela
/// primeira vez. Se aquele corpo tem IL que o Mono nao aceita, a falha aparece
/// no meio do jogo, longe de onde foi gerada (o crash de 2026-11-16 morreu em
/// libmonosgen-2.0.so +0xC8758, com pilha ilegivel, logo depois de instalar
/// ganchos - sem nenhuma pista de QUAL metodo). Compilando tudo aqui:
///  - o metodo problematico vira uma excecao capturavel (InvalidProgram,
///    TypeLoad, Missing*) em vez de derrubar o processo;
///  - cada metodo e anunciado no log antes de ser compilado, entao o ultimo
///    nome escrito e o culpado, mesmo se o Mono morrer de verdade;
///  - os recusados ficam gravados em `.{mod}.ilbad.txt` ao lado do DLL e o
///    IlPatcher esvazia esses corpos na proxima carga (o mod continua
///    carregando; so aqueles metodos deixam de fazer efeito).
/// Desligar: variavel de ambiente BL_IL_WARM=0. Silenciar o anuncio por
/// metodo: BL_IL_WARM_V=0.
/// </summary>
internal static class IlWarmup
{
    const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Public
        | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static readonly HashSet<string> bad = new(StringComparer.Ordinal);
    static string? badFile;

    public static bool Enabled => Environment.GetEnvironmentVariable("BL_IL_WARM") != "0";

    static bool Verbose => Environment.GetEnvironmentVariable("BL_IL_WARM_V") != "0";

    static string Key(string typeName, string methodName) => typeName.Replace('+', '/') + "::" + methodName;

    /// <summary>Le a lista de metodos que o Mono recusou na execucao anterior.</summary>
    public static void Read(string modPath)
    {
        bad.Clear();
        badFile = null;
        try
        {
            string? folder = Path.GetDirectoryName(modPath);
            if (folder == null) return;
            badFile = Path.Combine(folder, "." + Path.GetFileNameWithoutExtension(modPath) + ".ilbad.txt");
            if (!File.Exists(badFile)) return;
            foreach (string raw in File.ReadAllLines(badFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                bad.Add(line);
            }
            if (bad.Count > 0)
                Log.Info($"il patch: {Path.GetFileName(modPath)}: {bad.Count} metodo(s) na lista de IL invalido; corpo esvaziado nesta carga");
        }
        catch (Exception e)
        {
            Log.Warn($"il patch: {Path.GetFileName(modPath)}: lista de IL invalido ilegivel ({e.GetType().Name}: {e.Message})");
        }
    }

    public static bool IsBad(string typeName, string methodName) =>
        bad.Count > 0 && bad.Contains(Key(typeName, methodName));

    /// <summary>
    /// Compila tudo o que o Cecil marcou como compilavel e devolve o que o
    /// runtime recusou. Sem a lista do Cecil (carga ja cacheada) compila o que
    /// a reflexao listar.
    /// </summary>
    public static void Run(Assembly assembly, string modPath) => Run(assembly, modPath, null);

    /// <param name="candidates">
    /// Metodos que valem a pena compilar, no formato `tipo::metodo` (tipos
    /// aninhados ja com '/' — ver <see cref="Key"/>). Vem da reescrita do Cecil,
    /// que ja tem os flags na mao: assim aqui NAO se consulta `IsAbstract` nem
    /// `ContainsGenericParameters`. Foram exactamente essas duas consultas que
    /// derrubaram o Mono do aparelho em 2026-11-17 (libmonosgen-2.0.so
    /// +0x2274A0, dentro de `MonoMethodInfo.get_method_attributes`, si_addr=0xc):
    /// o ultimo metodo anunciado foi `HookHelper::LoadHookInterfaces` e a queda
    /// veio ao consultar o metodo SEGUINTE. Nulo/vazio = sem Cecil, modo
    /// reflexao (nada de flags; so registra recusa quando o erro e' de IL).
    /// </param>
    public static void Run(Assembly assembly, string modPath, HashSet<string>? candidates)
    {
        if (!Enabled) return;
        string mod = Path.GetFileName(modPath);
        HashSet<string>? want = candidates != null && candidates.Count > 0 ? candidates : null;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            var loaded = new List<Type>();
            foreach (Type? t in e.Types) if (t != null) loaded.Add(t);
            types = loaded.ToArray();
        }
        catch (Exception e)
        {
            Log.Warn($"il warm: {mod}: nao consegui listar os tipos ({e.GetType().Name}: {e.Message})");
            return;
        }
        string modo = want != null ? $"lista do Cecil com {want.Count} metodo(s)" : "reflexao";
        Log.Info($"il warm: {mod}: compilando os metodos de {types.Length} tipo(s) antes de instalar o conteudo ({modo})");
        int ok = 0, refused = 0, seen = 0, skipped = 0;
        var fresh = new List<string>();
        foreach (Type type in types)
        {
            string tname = type.FullName ?? type.Name;
            MethodBase[] methods;
            try
            {
                var list = new List<MethodBase>();
                list.AddRange(type.GetMethods(All));
                list.AddRange(type.GetConstructors(All));
                methods = list.ToArray();
            }
            catch (Exception e)
            {
                Log.Warn($"il warm: {mod}: {tname}: metodos ilegiveis ({e.GetType().Name}: {e.Message})");
                continue;
            }
            int typeOk = 0, typeBad = 0;
            foreach (MethodBase method in methods)
            {
                seen++;
                // A chave usa so o nome, que ja veio no array do GetMethods.
                string key = Key(tname, method.Name);
                if (want != null && !want.Contains(key)) continue;
                // A linha do metodo VEM ANTES de qualquer outra leitura: se o
                // Mono morrer, o ultimo nome no log e o culpado.
                if (Verbose) Log.Info($"[ilwarm] {mod}: {key}");
                try
                {
                    MethodBody? body = method.GetMethodBody();
                    if (body == null) { skipped++; continue; }
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    ok++; typeOk++;
                }
                catch (Exception e)
                {
                    // Corpo vazio (abstract) e metodo generico tambem caem aqui
                    // no modo reflexao: so conta como recusa o erro de IL, para
                    // nao esvaziar um corpo que estava bom.
                    if (!Malformed(e)) { skipped++; continue; }
                    refused++; typeBad++;
                    fresh.Add(key);
                    string msg = e.Message.Length > 220 ? e.Message.Substring(0, 220) : e.Message;
                    Log.Warn($"[ilwarm] {mod}: IL recusado em {key} ({e.GetType().Name}: {msg})");
                }
            }
            // Fecha o tipo mesmo quando nenhum metodo foi compilado: e' esta
            // linha que separa "morreu dentro do tipo X" de "morreu ao listar o
            // proximo tipo".
            if (Verbose && (typeOk > 0 || typeBad > 0))
                Log.Info($"[ilwarm+] {mod}: {tname}: {typeOk} compilado(s), {typeBad} recusado(s)");
        }
        SaveList(mod, fresh);
        Log.Info($"il warm: {mod}: {seen} metodo(s) visto(s), {ok} compilado(s), {refused} recusado(s), {skipped} pulado(s)");
    }

    /// <summary>Erro que denuncia IL/metadados ruins (e nao um caso previsto).</summary>
    static bool Malformed(Exception e) =>
        e is InvalidProgramException || e is BadImageFormatException || e is TypeLoadException
        || e is MissingMemberException || e is MemberAccessException;

    static void SaveList(string mod, List<string> fresh)
    {
        if (fresh.Count == 0 || badFile == null) return;
        try
        {
            var all = new SortedSet<string>(bad, StringComparer.Ordinal);
            foreach (string key in fresh) all.Add(key);
            var sb = new StringBuilder();
            sb.AppendLine("# metodos cujo IL o runtime recusou; o IlPatcher esvazia o corpo na proxima carga");
            foreach (string key in all) sb.AppendLine(key);
            File.WriteAllText(badFile, sb.ToString());
            Log.Info($"il warm: {mod}: {all.Count} metodo(s) gravado(s) em {Path.GetFileName(badFile)}");
        }
        catch (Exception e)
        {
            Log.Warn($"il warm: {mod}: nao consegui gravar a lista ({e.GetType().Name}: {e.Message})");
        }
    }
}
