using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

namespace BunnyLoader.Sandbox;

/// <summary>
/// Diagnóstico de carregamento de mods SEM o corpo do jogo.
///
/// O objetivo e produzir ERROS UTEIS para quem esta portando a API: roda no PC
/// (.NET 10), abre .tmod/.bl/.dll, carrega o assembly pelo MESMO caminho do
/// aparelho (tModLoader -> fachada -> BunnyLoader) e JITA cada metodo declarado
/// sem executa-lo. MissingMethod / MissingField / TypeLoad aparecem aqui com
/// nome completo, em vez de uma linha generica no relatorio do celular.
///
///   dotnet run --project tools/modsandbox -- <arquivo> [arquivo...] [--out dir]
///
/// Saida: 0 = tudo JITou / 2 = algum metodo falhou / 1 = erro fatal.
/// Nao referencia BunnyLoader.dll em tempo de compilacao (resolvido pelo ALC).
/// </summary>
internal static class Program
{
    static readonly List<string> Lines = new();
    static readonly List<Failure> Failures = new();
    static int fatal;
    static bool verbose;
    static bool dumpMembers;

    sealed record Failure(string Type, string Method, string Kind, string Message, string Missing);

    /// <summary>
    /// Rastro do ultimo metodo JITado, gravado linha a linha com AutoFlush.
    /// PrepareMethod de IL invalido pode dar AV (0xC0000005) e matar o processo
    /// sem excecao — o console nao mostra nada. O arquivo mostra o culpado.
    /// </summary>
    static StreamWriter? jitTrace;

    static void Say(string text = "")
    {
        Console.WriteLine(text);
        Lines.Add(text);
    }

    static int Main(string[] args)
    {
        // --run: carga REAL com NativeApi falso, num processo filho. O filho
        // pode ser derrubado por um slot nativo nao coberto; o pai sobrevive
        // e entrega o log ate o ponto em que parou.
        if (args.Contains("--run"))
            return args.Contains("--worker") ? RunWorker(args) : SpawnWorker(args);

        string? outputDir = null;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outputDir = args[++i];
            else if (args[i] == "--verbose") verbose = true;
            else if (args[i] == "--members") dumpMembers = true;
            else if (args[i] is "-h" or "--help") { Usage(); return 0; }
            else paths.Add(args[i]);
        }
        if (paths.Count == 0 && !args.Contains("--probe")) { Usage(); return 1; }
        if (args.Contains("--il")) return IlOnly(args, paths);
        if (args.Contains("--probe")) return Probe(args);

        string root = FindRoot();
        string facade = Path.Combine(root, "app", "build", "csharp", "tmlFacade");
        string shim = Path.Combine(root, "app", "build", "csharp", "BunnyLoader");

        Say("=== Bunny Loader / sandbox de carregamento ===");
        Say($"hora     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Say($"raiz     : {root}");
        Say($"fachada  : {Describe(Path.Combine(facade, "tModLoader.dll"))}");
        Say($"shim     : {Describe(Path.Combine(shim, "BunnyLoader.dll"))}");
        if (!File.Exists(Path.Combine(shim, "BunnyLoader.dll")))
            Say("AVISO    : rode `gradle :app:buildBunnyLoaderCs :app:buildTmlFacade` antes.");
        Say();

        foreach (string raw in paths)
        {
            string path = Path.GetFullPath(raw);
            if (!File.Exists(path) && !Directory.Exists(path)) { Say($"AUSENTE: {path}"); fatal++; continue; }
            try { Inspect(path, facade, shim); }
            catch (Exception e) { Say($"ERRO FATAL em {path}: {e}"); fatal++; }
        }

        Summarize();

        if (outputDir != null)
        {
            Directory.CreateDirectory(outputDir);
            string file = Path.Combine(outputDir, $"sandbox-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, string.Join(Environment.NewLine, Lines) + Environment.NewLine,
                new UTF8Encoding(false));
            Console.Error.WriteLine("relatorio: " + file);
        }
        return fatal > 0 ? 1 : Failures.Count > 0 ? 2 : 0;
    }

    /// <summary>
    /// Modo `--il &lt;dir&gt;`: aplica o IlPatcher do shim no .tmod/.bl/.dll e
    /// grava o assembly REESCRITO. Serve para olhar o IL corrigido (ildump)
    /// sem subir no aparelho.
    /// </summary>
    /// <summary>
    /// Modo `--probe &lt;assembly&gt; &lt;tipo&gt;`: responde "o runtime acha esse
    /// tipo?" — que e a pergunta por tras de todo TypeLoadException.
    ///
    /// Carrega o shim e as fachadas exatamente como o LoadRunner faz e resolve
    /// o nome pelo assembly pedido (ex.: <c>--probe TerrariaHooks
    /// "Terraria.On_SceneMetrics+orig_ExportTileCountsToMain"</c>). Aceita
    /// varios pares. Util para separar "o tipo nao existe" de "o tipo existe no
    /// shim mas a fachada nao encaminha".
    /// </summary>
    static int Probe(string[] args)
    {
        string root = FindRoot();
        string shimDir = Path.Combine(root, "app", "build", "csharp", "BunnyLoader");
        string facadeDir = Path.Combine(root, "app", "build", "csharp", "tmlFacade");
        string shimPath = Path.Combine(shimDir, "BunnyLoader.dll");
        if (!File.Exists(shimPath)) { Console.Error.WriteLine("shim ausente: " + shimPath); return 1; }

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            foreach (string dir in new[] { facadeDir, shimDir })
            {
                string p = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };
        AssemblyLoadContext.Default.LoadFromAssemblyPath(shimPath);
        foreach (string f in Directory.GetFiles(facadeDir, "*.dll"))
        {
            try { AssemblyLoadContext.Default.LoadFromAssemblyPath(f); }
            catch (Exception e) { Console.WriteLine($"fachada {Path.GetFileName(f)}: {e.Message}"); }
        }

        string? asmName = null;
        var typeNames = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (asmName == null) asmName = args[i];
            else typeNames.Add(args[i]);
        }
        if (asmName == null) { Console.Error.WriteLine("uso: --probe <assembly> <tipo> [tipo...]"); return 1; }

        int bad = 0;
        foreach (string typeName in typeNames)
        {
            try
            {
                Assembly asm = Assembly.Load(new AssemblyName(asmName));
                Type? t = asm.GetType(typeName, throwOnError: false);
                if (t != null)
                    Console.WriteLine($"OK    [{asm.GetName().Name}] {typeName} -> {t.Assembly.GetName().Name} ({t.FullName})");
                else
                {
                    bad++;
                    // Dica util: se o tipo existe no shim mas nao pela fachada,
                    // o problema e a lista de TypeForwardedTo, nao o tipo.
                    Assembly? shimAsm = AssemblyLoadContext.Default.Assemblies
                        .FirstOrDefault(a => a.GetName().Name == "BunnyLoader");
                    Type? inShim = shimAsm?.GetType(typeName, throwOnError: false);
                    Console.WriteLine($"FALHA [{asmName}] {typeName}" +
                        (inShim != null ? "  (existe em BunnyLoader: fachada sem forwarder)" : "  (nao existe em BunnyLoader)"));
                }
            }
            catch (Exception e)
            {
                bad++;
                Console.WriteLine($"ERRO  [{asmName}] {typeName}: {e.GetType().Name}: {e.Message}");
            }
        }
        return bad == 0 ? 0 : 2;
    }

    static int IlOnly(string[] args, List<string> paths)
    {
        string? outDir = null, input = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--il" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--out" when i + 1 < args.Length: i++; break;
                case "--verbose" or "--members": break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal)) input ??= args[i];
                    break;
            }
        }
        if (input == null || outDir == null)
        {
            Console.Error.WriteLine("uso: modsandbox --il <dir> <arquivo.tmod|.bl|.dll>");
            return 1;
        }

        string root = FindRoot();
        string shimDir = Path.Combine(root, "app", "build", "csharp", "BunnyLoader");
        string work = Path.Combine(Path.GetTempPath(), "bl-il-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            input = Path.GetFullPath(input);
            string dll = Path.GetExtension(input).ToLowerInvariant() switch
            {
                ".dll" => input,
                ".bl" => UnpackBl(input, work),
                _ => UnpackTmod(input, work),
            };
            if (dll.Length == 0) { Console.Error.WriteLine("sem dll de entrada"); return 1; }

            var alc = new AssemblyLoadContext("il-" + Guid.NewGuid().ToString("N"));
            alc.Resolving += (ctx, name) =>
            {
                string probe = Path.Combine(shimDir, name.Name + ".dll");
                return File.Exists(probe) ? ctx.LoadFromAssemblyPath(probe) : null;
            };
            Assembly shim = alc.LoadFromAssemblyPath(Path.Combine(shimDir, "BunnyLoader.dll"));
            Type patcher = shim.GetType("BunnyLoader.IlPatcher", throwOnError: true)!;
            MethodInfo rewrite = patcher.GetMethod("Rewrite", BindingFlags.NonPublic | BindingFlags.Static)!;

            byte[] bytes = File.ReadAllBytes(dll);
            byte[] patched;
            try
            {
                // Rewrite(string path, byte[] bytes, out List<string>? warmKeys):
                // o `out` conta como argumento em Invoke (null = descartado).
                patched = (byte[])rewrite.Invoke(null, new object?[] { dll, bytes, null })!;
            }
            catch (TargetInvocationException e)
            {
                Console.Error.WriteLine("il patch falhou: " + e.InnerException);
                return 1;
            }
            Directory.CreateDirectory(outDir);
            string target = Path.Combine(outDir, Path.GetFileName(dll));
            File.WriteAllBytes(target, patched);
            Console.WriteLine($"il: {Path.GetFileName(dll)} -> {target} ({bytes.Length} -> {patched.Length} bytes)");
            return 0;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temporario */ }
        }
    }

    static int SpawnWorker(string[] original)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        psi.ArgumentList.Add("--worker");
        // Mantem --run: e ele que faz o filho entrar em RunWorker em vez de
        // cair no modo --jit. RunWorker ignora todos os flags.
        foreach (string a in original) psi.ArgumentList.Add(a);
        using var child = System.Diagnostics.Process.Start(psi)!;
        string stdout = child.StandardOutput.ReadToEnd();
        string stderr = child.StandardError.ReadToEnd();
        child.WaitForExit();
        Console.Write(stdout);
        if (stderr.Length > 0) Console.Error.Write(stderr);
        string workerLine = $"[sandbox] worker exit={child.ExitCode}" +
            (child.ExitCode < 0 ? " (processo derrubado por sinal nativo)" : "");
        if (child.ExitCode < 0)
            Console.WriteLine("[sandbox] dica: o CLR do PC proibiu uma chamada que o Mono do" +
                " aparelho permite. Use --jit nesse pacote (so checa IL/API, nao executa).");
        Console.WriteLine(workerLine);

        string? outDir = null;
        for (int i = 0; i + 1 < original.Length; i++)
            if (original[i] == "--out") { outDir = original[i + 1]; break; }
        if (outDir != null)
        {
            Directory.CreateDirectory(outDir);
            string file = Path.Combine(outDir, $"sandbox-run-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, stdout + workerLine + Environment.NewLine, new UTF8Encoding(false));
            Console.Error.WriteLine("relatorio: " + file);
        }
        return child.ExitCode == 0 ? 0 : Math.Max(child.ExitCode, 2);
    }

    static int RunWorker(string[] args)
    {
        string root = FindRoot();
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") { i++; continue; }
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            paths.Add(args[i]);
        }
        int rc = 0;
        foreach (string raw in paths)
        {
            string path = Path.GetFullPath(raw);
            Console.WriteLine($"--- {path} ---");
            LoadRunner.Package? pkg = null;
            try
            {
                pkg = LoadRunner.Prepare(path, root);
                int r = LoadRunner.Run(pkg, root);
                if (r != 0) rc = r;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[sandbox] FALHOU: {e}");
                rc = 1;
            }
            finally { if (pkg != null) LoadRunner.Cleanup(pkg); }
        }
        return rc;
    }

    static void Usage()
    {
        Console.WriteLine("uso: dotnet run --project tools/modsandbox -- <arquivo> [arquivo...] [--out <dir>]");
        Console.WriteLine("  aceita .tmod (container TMOD ou zip), .bl (zip), .dll solto ou diretorio");
        Console.WriteLine("  --out  grava o relatorio em arquivo (alem do stdout)");
        Console.WriteLine("  --verbose  lista tambem cada metodo que JITou (por padrao so falhas)");
        Console.WriteLine("  --run      carga REAL: descompacta tudo, instala um NativeApi falso e chama");
        Console.WriteLine("             os proprios Host.Initialize / Host.LoadMod num processo filho");
        Console.WriteLine("  saida  0 = tudo JITou / 2 = algum metodo falhou / 1 = erro fatal");
    }

    /// <summary>tools/modsandbox/bin/... -> sobe ate achar app/build.gradle.kts.</summary>
    static string FindRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            dir = Path.GetDirectoryName(dir) ?? dir;
            if (File.Exists(Path.Combine(dir, "app", "build.gradle.kts"))) return dir;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    static string Describe(string path) => !File.Exists(path)
        ? $"AUSENTE ({path})"
        : $"{new FileInfo(path).Length} bytes, {new FileInfo(path).LastWriteTime:yyyy-MM-dd HH:mm:ss}";

    static void Inspect(string path, string facade, string shim)
    {
        Say($"--- {path} ---");
        if (Directory.Exists(path))
        {
            string? found = Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (found == null) { Say("diretorio sem .dll na raiz"); fatal++; return; }
            InspectDll(found, "diretorio", facade, shim);
            return;
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        string work = Path.Combine(Path.GetTempPath(), "bl-sandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string dll = ext switch
            {
                ".dll" => InspectDllSource(path),
                ".bl" => UnpackBl(path, work),
                _ => UnpackTmod(path, work),
            };
            if (dll.Length == 0) { Say("sem dll de entrada"); fatal++; return; }
            InspectDll(dll, ext switch { ".dll" => "dll solto", ".bl" => "pacote .bl", _ => "container .tmod" },
                facade, shim);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temporario */ }
        }
        Say();
    }

    static string InspectDllSource(string path)
    {
        Say("origem   : dll solto");
        return path;
    }

    // ---- pacotes ---------------------------------------------------------

    static string UnpackBl(string path, string work)
    {
        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Say($"origem   : pacote .bl (zip)");
        Say($"entradas : {names.Count}");
        ZipArchiveEntry? manifest = zip.GetEntry("manifest.json");
        if (manifest != null)
        {
            using var reader = new StreamReader(manifest.Open());
            string text = reader.ReadToEnd();
            foreach (string key in new[] { "uid", "id", "name", "version", "type" })
            {
                string? value = JsonField(text, key);
                if (value != null) Say($"manifest.{key,-8}: {value}");
            }
        }
        string? dllName = names.FirstOrDefault(n =>
            n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !n.Contains('/'));
        if (dllName == null) return "";
        string target = Path.Combine(work, Path.GetFileName(dllName));
        zip.GetEntry(dllName)!.ExtractToFile(target, true);
        return target;
    }

    internal static string? JsonField(string json, string key)
    {
        string needle = "\"" + key + "\"";
        int at = json.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return null;
        int colon = json.IndexOf(':', at + needle.Length);
        if (colon < 0) return null;
        int start = json.IndexOf('"', colon + 1);
        if (start < 0) return null;
        int end = json.IndexOf('"', start + 1);
        return end < 0 ? null : json.Substring(start + 1, end - start - 1);
    }

    /// <summary>
    /// `.tmod` atual nao e zip: "TMOD" + versao tML + hash20 + assinatura256 +
    /// tamanho + Nome/ Versao + tabela (nome, original, comprimido) + blobs.
    /// Comprime em deflate cru quando os dois tamanhos diferem (ImageIO/ToRaw,
    /// TmodFile.Save). O `.tmod` pre-1.4.4 ainda e zip comum.
    /// </summary>
    static string UnpackTmod(string path, string work)
    {
        byte[] data = File.ReadAllBytes(path);
        bool isTmod = data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "TMOD";
        if (!isTmod)
        {
            Say("origem   : .tmod antigo (zip)");
            using var zip = ZipFile.OpenRead(path);
            string? n = zip.Entries.Select(e => e.FullName).FirstOrDefault(x =>
                x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !x.Contains('/'));
            if (n == null) return "";
            string target = Path.Combine(work, Path.GetFileName(n));
            zip.GetEntry(n)!.ExtractToFile(target, true);
            return target;
        }

        Say("origem   : container TMOD");
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        br.ReadBytes(4); // "TMOD": o MemoryStream comeca no byte zero
        Say($"tML      : {DotnetString(br)}");
        br.ReadBytes(20 + 256);
        br.ReadInt32();
        string name = DotnetString(br);
        string version = DotnetString(br);
        Say($"mod      : {name} v{version}");
        int count = br.ReadInt32();
        var entries = new List<(string Name, int Length, int Packed, long Offset)>();
        for (int i = 0; i < count; i++)
        {
            string entryName = DotnetString(br).Replace('\\', '/');
            entries.Add((entryName, br.ReadInt32(), br.ReadInt32(), 0));
        }
        long cursor = ms.Position;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            entries[i] = (e.Name, e.Length, e.Packed, cursor);
            cursor += e.Packed;
        }
        int rawImgs = entries.Count(e => e.Name.EndsWith(".rawimg", StringComparison.OrdinalIgnoreCase));
        Say($"entradas : {count} ({rawImgs} .rawimg -> PNG no import)");
        foreach (var e in entries.Take(10)) Say($"  {e.Name,-46} {e.Length,9} -> {e.Packed,9}");
        if (entries.Count > 10) Say($"  ... e mais {entries.Count - 10}");

        byte[]? info = Entry(entries, data, "Info") ?? Entry(entries, data, "build.txt");
        if (info != null) DumpInfo(info, name, version);

        string? dllName = entries.Select(e => e.Name).FirstOrDefault(n =>
            n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !n.Contains('/'));
        if (dllName == null) return "";
        byte[] dllBytes = Entry(entries, data, dllName)!;
        string target2 = Path.Combine(work, Path.GetFileName(dllName));
        File.WriteAllBytes(target2, dllBytes);
        return target2;
    }

    static byte[]? Entry(List<(string Name, int Length, int Packed, long Offset)> entries, byte[] data, string name)
    {
        foreach (var e in entries)
        {
            if (!e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = new byte[e.Packed];
            Array.Copy(data, e.Offset, raw, 0, e.Packed);
            if (e.Length == e.Packed) return raw;
            using var input = new MemoryStream(raw);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        return null;
    }

    static void DumpInfo(byte[] bytes, string containerName, string containerVersion)
    {
        var fields = ModInfoReader.Read(bytes);
        if (!fields.ContainsKey("name") && containerName.Length > 0) fields["name"] = containerName;
        if (!fields.ContainsKey("version") && containerVersion.Length > 0) fields["version"] = containerVersion;
        Say("Info     :");
        foreach (string key in new[] { "name", "displayname", "author", "version", "side",
                     "modreferences", "weakreferences", "description", "buildversion" })
            if (fields.TryGetValue(key, out string? value)) Say($"  {key,-16}= {Flatten(value)}");
    }

    static string Flatten(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

    // ---- metadados -------------------------------------------------------

    /// <summary>
    /// Os assemblies que o mod referencia e que NAO estao no TPA do APK.
    ///
    /// Este e o tipo de coisa que so aparece no aparelho: o Mono, ao
    /// instanciar um tipo generico compartilhado, precisa carregar a classe do
    /// ELEMENTO do array; se o assembly faltar, a assert "is_ok(error)" mata o
    /// processo com SIGABRT (status=6) e o tombstone nao diz nada util.
    /// Comparando a lista do mod com o que o APK empacota, sobra isso aqui.
    /// </summary>
    static void ReportMissingAssemblies(List<string> assemblyRefs)
    {
        string assets = Path.Combine(FindRoot(), "app", "build", "generated", "blAssets", "dotnet");
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(assets))
            foreach (string f in Directory.GetFiles(assets, "*.dll"))
                available.Add(Path.GetFileNameWithoutExtension(f));

        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in assemblyRefs)
        {
            if (available.Contains(name)) continue;
            // O runtime resolve estes pelo nome, sem arquivo no TPA.
            if (name is "netstandard" or "mscorlib" or "System" or "System.Core" or "System.Runtime") continue;
            missing.Add(name);
        }
        if (missing.Count == 0)
        {
            Say("  -> todos os assemblies referenciados existem no TPA do APK");
            return;
        }
        Say("  -> FALTANDO no TPA do APK (vai dar SIGABRT no aparelho):");
        foreach (string name in missing)
            Say($"     {name}.dll");
    }

    static void DumpMetadata(string dll)
    {
        using FileStream fs = File.OpenRead(dll);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();
        Say("AssemblyRefs (o que o mod espera resolver):");
        var assemblyRefs = new List<string>();
        foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
        {
            var ar = md.GetAssemblyReference(h);
            string name = md.GetString(ar.Name);
            assemblyRefs.Add(name);
            Say($"  {name}, Version={ar.Version}");
        }
        ReportMissingAssemblies(assemblyRefs);
        var refs = new SortedSet<string>();
        foreach (TypeReferenceHandle h in md.TypeReferences)
        {
            var tr = md.GetTypeReference(h);
            string ns = md.GetString(tr.Namespace);
            string n = md.GetString(tr.Name);
            if (ns.StartsWith("System") || ns.StartsWith("Microsoft") || n.StartsWith("<")) continue;
            refs.Add(string.IsNullOrEmpty(ns) ? n : ns + "." + n);
        }
        Say($"TypeRefs relevantes ({refs.Count}):");
        foreach (string r in refs.Take(25)) Say("  " + r);
        if (refs.Count > 25) Say($"  ... e mais {refs.Count - 25}");
    }

    // ---- carga + JIT -----------------------------------------------------

    /// <summary>
    /// Aplica o IlPatcher do shim no dll e devolve os bytes REESCRITOS, para o
    /// modo JIT analisar exatamente o que o aparelho carrega. Devolve null e
    /// explica em <paramref name="why"/> quando nao deu (aí o relatorio avisa,
    /// em vez de fingir que o IL original e o real).
    /// </summary>
    static byte[]? TryRewrite(string shimDir, string dll, out string? why)
    {
        why = null;
        try
        {
            string shimPath = Path.Combine(shimDir, "BunnyLoader.dll");
            if (!File.Exists(shimPath)) { why = "shim ausente"; return null; }
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string p = Path.Combine(shimDir, name.Name + ".dll");
                return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
            };
            Assembly shim = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == "BunnyLoader")
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(shimPath);
            Type? patcher = shim.GetType("BunnyLoader.IlPatcher");
            MethodInfo? rewrite = patcher?.GetMethod("Rewrite", BindingFlags.NonPublic | BindingFlags.Static);
            if (rewrite == null) { why = "IlPatcher.Rewrite nao encontrado"; return null; }
            return (byte[])rewrite.Invoke(null, new object[] { dll, File.ReadAllBytes(dll) })!;
        }
        catch (Exception e)
        {
            why = (e as TargetInvocationException)?.InnerException?.Message ?? e.Message;
            return null;
        }
    }

    static void InspectDll(string dll, string origin, string facade, string shim)
    {
        Say($"dll      : {Path.GetFileName(dll)} ({new FileInfo(dll).Length} bytes) [{origin}]");
        Say();
        DumpMetadata(dll);
        if (dumpMembers) DumpMembers(dll);
        Say();

        // NAO collectible: com `LoadFromStream` + ALC collectible o CoreCLR
        // derruba o processo (SIGSEGV) ao JITar o assembly reescrito. O aparelho
        // usa o ALC default; aqui um ALC normal e o mais proximo disso.
        var alc = new AssemblyLoadContext("sandbox-" + Guid.NewGuid().ToString("N"), isCollectible: false);
        alc.Resolving += (ctx, name) =>
        {
            foreach (string dir in new[] { facade, shim, Path.GetDirectoryName(dll)! })
            {
                string p = Path.Combine(dir, name.Name + ".dll");
                if (!File.Exists(p)) continue;
                Say($"  resolve {name.Name} -> {Path.GetFileName(Path.GetDirectoryName(p))}/{Path.GetFileName(p)}");
                return ctx.LoadFromAssemblyPath(p);
            }
            Say($"  NAO RESOLVIDO: {name.FullName}");
            return null;
        };

        Say("Carga + JIT (PrepareMethod: compila, NAO executa o mod)");
        try
        {
            // .tmod tem de ser JITado no assembly REESCRITO: no aparelho a
            // rota TML passa pelo IlPatcher antes de carregar (ver
            // Pipelines/TmlPipeline.cs). JITar o IL original reporta centenas de
            // MissingField (Item.type, Main.player...) que NAO existem no
            // aparelho — o campo ja foi trocado por __ref_/__get_.
            Assembly asm;
            bool tml = origin.Contains("tmod", StringComparison.OrdinalIgnoreCase);
            string? why = null;
            byte[]? patched = tml ? TryRewrite(shim, dll, out why) : null;
            if (patched != null)
            {
                Say($"  IL reescrita: {new FileInfo(dll).Length} -> {patched.Length} bytes");
                string tracePath = Path.Combine(Path.GetTempPath(), "bl-jit-trace.txt");
                jitTrace = new StreamWriter(tracePath, false, new UTF8Encoding(false)) { AutoFlush = true };
                Say($"  rastro JIT: {tracePath}");
                asm = alc.LoadFromStream(new MemoryStream(patched));
            }
            else
            {
                if (tml) Say($"  IL NAO reescrita: {why}");
                asm = alc.LoadFromAssemblyPath(dll);
            }
            Say($"  assembly: {asm.FullName}");
            Type[] types;
            try
            {
                types = asm.GetTypes();
                Say($"  GetTypes OK: {types.Length} tipo(s)");
            }
            catch (ReflectionTypeLoadException e)
            {
                Say($"  GetTypes PARCIAL: {e.GetType().Name}");
                int i = 0;
                foreach (Exception? inner in e.LoaderExceptions)
                {
                    if (inner == null) continue;
                    Say($"    [{++i}] {inner.GetType().Name}: {inner.Message}");
                    Failures.Add(new Failure("<GetTypes>", "GetTypes", inner.GetType().Name,
                        inner.Message, ExtractMissing(inner.Message)));
                }
                types = e.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.DeclaredOnly;
            // Padrao: so os tipos COM FALHA aparecem. Um .bl de 2 mil tipos
            // geraria 500 KB de "OK" e afogaria o unico erro que interessa.
            // --verbose volta a listar todos os tipos e metodos.
            int totalTypes = 0, totalOk = 0, totalSkipped = 0, failedTypes = 0;
            var typeReports = new List<string>();
            foreach (Type t in types)
            {
                totalTypes++;
                // PrepareMethod nao aceita tipo generico aberto nem os
                // BeginInvoke/EndInvoke sinteticos de delegate: limites do
                // runtime, NAO APIs faltando no shim.
                if (t.ContainsGenericParameters || typeof(MulticastDelegate).IsAssignableFrom(t))
                {
                    totalSkipped++;
                    if (verbose) Say($"  {t.FullName}  SKIP (generico aberto / delegate)");
                    continue;
                }
                int ok = 0;
                var local = new List<string>();
                foreach (MethodInfo m in t.GetMethods(flags))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) { totalSkipped++; continue; }
                    try
                    {
                        jitTrace?.WriteLine($"{t.FullName}::{m.Name}");
                        RuntimeHelpers.PrepareMethod(m.MethodHandle);
                        ok++;
                        if (verbose) local.Add($"    OK    {m.Name}");
                    }
                    catch (Exception je)
                    {
                        string missing = ExtractMissing(je.Message);
                        local.Add($"    FAIL  {m.Name}: {je.GetType().Name}: {je.Message}");
                        Failures.Add(new Failure(t.FullName ?? "?", m.Name, je.GetType().Name,
                            je.Message, missing));
                    }
                }
                totalOk += ok;
                bool hasFailure = local.Any(l => l.Contains("FAIL"));
                if (hasFailure) failedTypes++;
                if (!verbose && !hasFailure) continue;
                typeReports.Add($"  {t.FullName}  :  {t.BaseType?.FullName ?? "-"}");
                typeReports.AddRange(local);
                typeReports.Add($"    OK x{ok}");
            }
            Say($"  tipos={totalTypes}  metodos OK={totalOk}  ignorados={totalSkipped}  tipos com falha={failedTypes}");
            jitTrace?.Dispose();
            jitTrace = null;
            foreach (string line in typeReports) Say(line);
        }
        catch (Exception e)
        {
            Say($"  CARGA FALHOU: {e.GetType().FullName}: {e.Message}");
            if (e.InnerException != null) Say($"    inner: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
            Failures.Add(new Failure("<Assembly>", "Load", e.GetType().Name, e.Message,
                ExtractMissing(e.Message)));
            fatal += 0; // nao e fatal de processo: ainda podemos resumir
        }
    }

    /// <summary>
    /// Lista os MemberRef que o mod usa, separando CAMPO de METODO.
    ///
    /// Isso decide a estrategia de compatibilidade: se o mod referencia
    /// `Terraria.Item::stack` como CAMPO (ldfld/stfld), o shim precisa expor
    /// o membro como campo de verdade — proxy de propriedade `ref int` nao
    /// serve. Se referencia como METODO (callvirt get_stack), o proxy de
    /// propriedade ja resolve.
    /// </summary>
    static void DumpMembers(string dll)
    {
        using FileStream fs = File.OpenRead(dll);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();

        string Owner(EntityHandle h)
        {
            switch (h.Kind)
            {
                case HandleKind.TypeReference:
                    var tr = md.GetTypeReference((TypeReferenceHandle)h);
                    string ns = md.GetString(tr.Namespace);
                    string n = md.GetString(tr.Name);
                    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
                case HandleKind.TypeDefinition:
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)h);
                    string dns = md.GetString(td.Namespace);
                    string dn = md.GetString(td.Name);
                    return string.IsNullOrEmpty(dns) ? dn : dns + "." + dn;
                case HandleKind.TypeSpecification:
                    return "<spec>";
                default:
                    return h.Kind.ToString();
            }
        }

        var fields = new SortedSet<string>();
        var methods = new SortedSet<string>();
        foreach (MemberReferenceHandle h in md.MemberReferences)
        {
            var mr = md.GetMemberReference(h);
            string owner = Owner(mr.Parent);
            string name = md.GetString(mr.Name);
            if (owner.StartsWith("System") || owner.StartsWith("Microsoft") || owner.StartsWith("<")) continue;
            if (mr.GetKind() == MemberReferenceKind.Field) fields.Add($"{owner}::{name}");
            else methods.Add($"{owner}::{name}");
        }

        Say($"MemberRefs de CAMPO ({fields.Count}) — o mod usa ldfld/stfld neles:");
        foreach (string f in fields.Take(40)) Say("  " + f);
        if (fields.Count > 40) Say($"  ... e mais {fields.Count - 40}");

        Say($"MemberRefs de METODO ({methods.Count}) — o mod usa callvirt/call neles:");
        foreach (string m in methods.Take(40)) Say("  " + m);
        if (methods.Count > 40) Say($"  ... e mais {methods.Count - 40}");

        // O que mais interessa: como o mod toca em Terraria.Item / Item.
        Say("Toques em Item (o tipo que o shim expoe como proxy):");
        foreach (string f in fields.Where(f => f.StartsWith("Terraria.Item::"))) Say("  CAMPO  " + f);
        foreach (string m in methods.Where(m => m.StartsWith("Terraria.Item::"))) Say("  METODO " + m);
    }

    /// <summary>Tira o nome completo do metodo/campo/tipo da mensagem do runtime.</summary>
    static string ExtractMissing(string message)
    {
        Match m = Regex.Match(message, @"'([^']+)'");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(message, @"Method not found:\s*(.+)");
        if (m.Success) return m.Groups[1].Value.Trim();
        return message.Length > 160 ? message[..160] : message;
    }

    static void Summarize()
    {
        Say("=== lista de API ausente / incompativel (copiar para o relatorio) ===");
        if (Failures.Count == 0) { Say("  (nenhuma)"); return; }
        foreach (var group in Failures.GroupBy(f => f.Kind).OrderBy(g => g.Key))
        {
            Say($"  [{group.Key}] {group.Count()} ocorrencia(s)");
            foreach (var f in group)
                Say($"    {f.Type}::{f.Method}  ->  {f.Missing}");
        }
        Say();
        Say("=== diagnosticos ===");
        bool pcMismatch = Failures.Any(f =>
            (f.Kind is "MissingMethodException" or "TypeLoadException" or "MissingFieldException") &&
            (f.Missing.Contains("Terraria.Item") || f.Missing.Contains("get_Item") ||
             f.Message.Contains("reference type", StringComparison.OrdinalIgnoreCase) ||
             f.Message.Contains("type kind", StringComparison.OrdinalIgnoreCase)));
        if (pcMismatch)
        {
            Say("  * PC .tmod x shim: o mod foi compilado contra tModLoader, onde Terraria.Item");
            Say("    e uma CLASSE com campos reais. Aqui Item e um struct proxy e os campos");
            Say("    sao propriedades `ref`. SetDefaults nao JITa -> dano/tamanho/empilhamento");
            Say("    ficam nos defaults do shim. Nao e textura nem manifesto ausente.");
            Say("    Caminho: recompilar o codigo-fonte como .bl, ou reescrever a IL.");
        }
        var missingAssemblies = Failures.Where(f => f.Kind == "FileNotFoundException").ToList();
        if (missingAssemblies.Count > 0)
            Say($"  * {missingAssemblies.Count} assembly(s) nao resolvidos: conferir a fachada/TPA.");
        Say($"  * total de metodos que nao JITaram: {Failures.Count}");
    }

    // ---- leitura de Info (binario BinaryWriter ou texto) -----------------

    internal static class ModInfoReader
    {
        static readonly HashSet<string> ListKeys = new(StringComparer.OrdinalIgnoreCase)
            { "dllReferences", "modReferences", "weakReferences", "sortAfter", "sortBefore" };
        static readonly HashSet<string> ByteKeys = new(StringComparer.OrdinalIgnoreCase) { "side" };
        static readonly HashSet<string> ValueKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "author", "version", "homepage", "description", "eacPath", "modSource",
            "buildVersion", "name", "displayName", "updateUrl", "modSide",
        };
        static readonly HashSet<string> FlagKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "noCompile", "!playableOnPreview", "translationMod", "libraryMod",
            "!hideCode", "!hideResources", "includeSource",
        };

        static bool Known(string tag) => ListKeys.Contains(tag) || ByteKeys.Contains(tag) ||
                                          ValueKeys.Contains(tag) || FlagKeys.Contains(tag) ||
                                          tag.StartsWith("displayName.", StringComparison.OrdinalIgnoreCase);

        public static Dictionary<string, string> Read(byte[] bytes)
        {
            return IsBinary(bytes) ? Binary(bytes) : Text(Encoding.UTF8.GetString(bytes));
        }

        /** O Info em texto, do jeito que o import do APK grava na pasta do mod. */
        public static string ToText(byte[] bytes, string containerName, string containerVersion)
        {
            var fields = Read(bytes);
            if (!fields.ContainsKey("name") && containerName.Length > 0) fields["name"] = containerName;
            if (!fields.ContainsKey("version") && containerVersion.Length > 0) fields["version"] = containerVersion;
            var sb = new StringBuilder();
            foreach (var (key, value) in fields)
                sb.Append(key).Append(" = ").Append(Flatten(value)).Append('\n');
            return sb.ToString();
        }

        // O primeiro byte de um Info binario e o TAMANHO da primeira chave.
        // Pode ser 0x0A (`libraryMod`, so na estavel 1.4.4.9) ou 0x09
        // (`noCompile`): jamais classificar byte de controle como "texto".
        static bool IsBinary(byte[] bytes)
        {
            if (bytes.Length == 0) return false;
            int first = bytes[0];
            if (first == 0xEF) return false;
            if ((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') ||
                first == '#' || first == ' ')
                return Encoding.UTF8.GetString(bytes).Split('\n')[0].IndexOf('=') < 0;
            if (first <= 0 || first > 0x7F || first >= bytes.Length) return false;
            return Known(Encoding.UTF8.GetString(bytes, 1, first));
        }

        static Dictionary<string, string> Binary(byte[] bytes)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            while (ms.Position < ms.Length)
            {
                string key = DotnetString(br);
                if (key.Length == 0) break;
                string lower = key.ToLowerInvariant();
                if (ListKeys.Contains(key))
                {
                    var items = new List<string>();
                    while (ms.Position < ms.Length)
                    {
                        string item = DotnetString(br);
                        if (item.Length == 0) break;
                        items.Add(item);
                    }
                    result[lower] = string.Join(", ", items);
                }
                else if (ByteKeys.Contains(key))
                {
                    int side = ms.Position < ms.Length ? br.ReadByte() : 0;
                    result[lower] = side switch { 1 => "Client", 2 => "Server", 3 => "NoSync", _ => "Both" };
                }
                else if (ValueKeys.Contains(key))
                {
                    string value = DotnetString(br);
                    string k = key.StartsWith("displayName.", StringComparison.OrdinalIgnoreCase)
                        ? "displayname" : lower;
                    if (!result.ContainsKey(k) || result[k].Length == 0) result[k] = value;
                }
                // flag: so a chave, sem valor
            }
            return result;
        }

        static Dictionary<string, string> Text(string raw)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in raw.Split('\n'))
            {
                string clean = line.Trim().TrimStart('\uFEFF');
                if (clean.Length == 0 || clean.StartsWith('#') || clean.StartsWith("//")) continue;
                int eq = clean.IndexOf('=');
                if (eq <= 0) continue;
                string key = clean[..eq].Trim().ToLowerInvariant();
                string value = clean[(eq + 1)..].Trim().Trim('"');
                result[key] = result.TryGetValue(key, out string? old) ? old + ", " + value : value;
            }
            return result;
        }
    }

    /// <summary>BinaryWriter.Write(string): tamanho em 7 bits + UTF-8.</summary>
    static string DotnetString(BinaryReader br)
    {
        int length = br.Read7BitEncodedInt();
        if (length <= 0 || length > 8 * 1024 * 1024) return "";
        return Encoding.UTF8.GetString(br.ReadBytes(length));
    }
}
