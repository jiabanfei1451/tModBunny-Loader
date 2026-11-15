using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BunnyLoader.Compiler;
using BunnyLoader.Interop;
using BunnyLoader.Pipelines;
using Terraria.ModLoader;

namespace BunnyLoader;

internal static unsafe class Host
{
    internal static NativeApi* Api;

    // SEM construtor estático aqui. O resolver é instalado explicitamente em
    // Initialize (depois de `Api = api`) e em LoadMod. Instalá-lo no .cctor
    // fazia o Install() logar antes de a API nativa existir e derrubava a
    // inicialização inteira com TypeInitializationException.

    static readonly List<Mod> LoadedMods = new();

    static readonly List<Mod> Created = new();

    internal static Mod[] CreatedMods
    {
        get { lock (Created) return Created.ToArray(); }
    }

    internal static Mod? FindMod(string name)
    {
        lock (Created) return Created.Find(m => m.Name == name);
    }

    internal static Mod[] Mods
    {
        get { lock (LoadedMods) return LoadedMods.ToArray(); }
    }

    [UnmanagedCallersOnly]
    static int Initialize(NativeApi* api)
    {
        if (api == null || api->Size < (uint)sizeof(NativeApi)) return 0;
        Api = api;
        Log.Flush();
        // Idempotente e explícito: um [UnmanagedCallersOnly] chamado pelo Mono
        // nem sempre dispara o construtor estático da classe (e agora não há
        // um). Só aqui, com a API nativa pronta, o resolver pode ser instalado.
        ShimAssembly.Install();
        api->OnContentReady(&ModContent.ContentReady);
        ModNet.Install();
        try
        {
            List<string> bad = Layouts.Verify();
            foreach (string line in bad) Log.Error($"struct layout differs from the game: {line}");
        }
        catch (Exception e)
        {
            Log.Error($"struct layout check failed: {e}");
        }
        return 1;
    }

    [UnmanagedCallersOnly]
    static int LoadMod(byte* id, byte* path) =>
        LoadModCore(Marshal.PtrToStringUTF8((IntPtr)id) ?? "", Marshal.PtrToStringUTF8((IntPtr)path) ?? "");

    /// <summary>
    /// O corpo de <see cref="LoadMod"/> como método MANAGED normal.
    ///
    /// <c>LoadMod</c> é <c>[UnmanagedCallersOnly]</c>: só pode ser chamado pelo
    /// código nativo. O harness de PC entra por aqui (por reflexão) em vez de
    /// pelo ponteiro de função — assim, quando o JIT derruba o processo no meio
    /// da carga, o rastro mostra a cadeia de chamadas inteira em vez de parar
    /// na borda nativa.
    /// </summary>
    internal static int LoadModCore(string uid, string path)
    {
        var mods = new List<Mod>();
        ModPipeline? pipeline = null;
        // O painel entra antes do primeiro trabalho: a compilacao de um mod
        // grande leva dezenas de segundos e e justamente onde o jogador acha
        // que o jogo travou.
        LoadingWindow.Begin();
        Hooks.HookGate.Loading = true;
        try
        {
            ShimAssembly.Install();
            string file = path;
            string folder = Path.GetDirectoryName(file)!;
            AddModFolder(folder);
            // Um mod pode referenciar o ASSEMBLY de outro que ainda não chegou
            // à sua vez (o FargowiltasSouls herda tipos do Luminance, que é só
            // `weakReference` e por isso não entra na ordenação do manifesto).
            // Antes de qualquer carga, todos os vizinhos da pasta de mods ficam
            // registrados: a resolução acha o DLL quando o tipo precisar dele.
            AddModRoot(Path.GetDirectoryName(folder));
            string[] dependencies = ManifestField(uid, 3).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Nome para a faixa de progresso: o UID é feio e indistinguível.
            string label = ManifestField(uid, 1);
            if (label.Length == 0) label = ManifestField(uid, 0);
            if (label.Length == 0) label = uid;
            // `.bl` (nativo) e `.tmod` (PC) são DUAS pipelines distintas;
            // a escolha acontece uma vez e não volta a se misturar.
            pipeline = ModPipeline.For(folder);
            if (ModBuild.IsSourceEntry(file))
            {
                string? built = FromSources(uid, file, dependencies);
                if (built == null) return 0;
                file = built;
            }
            string internalName = ManifestField(uid, 0);
            if (internalName.Length == 0) internalName = uid;
            var request = new ModLoadRequest(uid, file, folder, label, internalName, dependencies);
            Progress.BeginMod();
            Progress.ModStep(pipeline.LoadingText(request), 0.05);
            pipeline.Prepare();
            Assembly assembly = pipeline.LoadAssembly(request);

            void Register(Mod mod)
            {
                mod.Uid = uid;
                mod.Folder = folder;
                mod.Logger = new ModLogger(mod.Name);
                mod.ManifestId = internalName;
                mod.DisplayName = ManifestField(uid, 1) is { Length: > 0 } display ? display : mod.Name;
                mod.Version = Version.TryParse(ManifestField(uid, 2), out Version? version) ? version : new Version(0, 0);
                mod.Dependencies = dependencies;
                mods.Add(mod);
                ModContent.Add(mod);
                lock (Created) Created.Add(mod);
            }

            int skippedTypes = 0;
            foreach (Type type in pipeline.DiscoverTypes(assembly, uid))
            {
                // `IsAssignableFrom` RESOLVE a cadeia de bases, então um tipo
                // que herda de um assembly ausente estoura TypeLoadException
                // aqui — e antes isso derrubava o mod INTEIRO (o catch lá fora
                // devolvia 0 e o FargowiltasSouls sumia do jogo por causa de um
                // único tipo). Um tipo que não dá para inspecionar não é um
                // `Mod` que dê para instanciar: pula e segue.
                try
                {
                    if (type.IsAbstract || !typeof(Mod).IsAssignableFrom(type)) continue;
                    Register((Mod)Activator.CreateInstance(type)!);
                }
                catch (Exception e)
                {
                    skippedTypes++;
                    Log.Warn($"mod {uid}: tipo {type.FullName} ignorado ({e.GetType().Name}: {e.Message})");
                }
            }
            if (skippedTypes > 0) Log.Warn($"mod {uid}: {skippedTypes} tipo(s) ignorado(s) por falha de carga");
            if (mods.Count == 0)
            {
                // tML 1.4.5, Core/AssemblyManager.cs: um mod sem classe `Mod`
                // ainda carrega — o loader inventa uma. Sem isto um mod que só
                // tem ModItem (caso comum em mods pequenos) seria recusado.
                Log.Info($"mod {uid}: sem classe Mod; usando substituto '{internalName}' (comportamento do tML 1.4.5)");
                Register(new SyntheticMod(internalName));
            }
            Progress.ModStep($"正在特殊处理：{label}…", 0.45);
            foreach (Mod mod in mods) LocalizationLoader.Load(mod);
            foreach (Mod mod in mods) mod.loading = true;
            try
            {
                Progress.ModStep($"正在自动加载：{label}…", 0.70);
                mods[0].Autoload(assembly);
                foreach (Mod mod in mods) mod.Load();
                foreach (Mod mod in mods) SystemLoader.OnModLoad(mod);
            }
            finally
            {
                foreach (Mod mod in mods) mod.loading = false;
            }
            lock (LoadedMods) LoadedMods.AddRange(mods);
            return 1;
        }
        catch (Exception e)
        {
            Log.Error($"mod {uid}: {e}");
            // Onde procurar a assinatura esperada e a implementação equivalente.
            pipeline?.ReportContract();
            lock (Created)
            {
                foreach (Mod mod in mods) Created.Remove(mod);
            }
            return 0;
        }
        finally
        {
            Hooks.HookGate.Loading = false;
            // Nao fecha o painel: o conteudo (que e a parte longa) vem depois.
            // Clear() so apagava o arquivo — a janela do jogo fica ate os 100%.
            Progress.ModStep($"已完成：{uid}…", 1.0);
        }
    }

    [UnmanagedCallersOnly]
    static void ConsoleSubmit(byte* code)
    {
        try
        {
            DevConsole.Submit(Marshal.PtrToStringUTF8((IntPtr)code) ?? "");
        }
        catch (Exception e)
        {
            Log.Error($"console: {e}");
        }
    }

    [UnmanagedCallersOnly]
    static void ConsoleTick()
    {
        try
        {
            DevConsole.Tick();
        }
        catch (Exception e)
        {
            Log.Error($"console: {e}");
        }
    }

    static string? FromSources(string uid, string entry, string[] dependencies)
    {
        string project = Path.GetDirectoryName(entry)!;
        string name = Path.GetFileName(entry);
        List<string> references = ModBuild.References(AppContext.BaseDirectory);
        List<string> modReferences = DependencyAssemblies(dependencies);
        string api = ModBuild.ApiVersion(typeof(Host).Assembly.GetName().Version?.ToString() ?? "0.0.0.0", modReferences);
        if (ModBuild.IsCurrent(project, name, api, out string reason)) return ModBuild.DllPath(project, name);
        Log.Info($"mod {uid}: compiling {name} ({reason})");
        return Compile(uid, project, name, references, modReferences);
    }

    static List<string> DependencyAssemblies(string[] dependencies)
    {
        var found = new List<string>();
        foreach (Mod mod in CreatedMods)
        {
            if (Array.IndexOf(dependencies, mod.Uid) < 0 && Array.IndexOf(dependencies, mod.ManifestId) < 0) continue;
            string location = mod.GetType().Assembly.Location;
            if (location.Length > 0 && !found.Contains(location)) found.Add(location);
        }
        return found;
    }

    static string ManifestField(string uid, int field)
    {
        byte* id = Utf8(uid);
        try
        {
            int size = 256;
            while (true)
            {
                byte[] managed = new byte[size];
                fixed (byte* p = managed)
                {
                    int length = Native()->ModManifestField(id, field, p, size);
                    if (length < 0) return "";
                    if (length < size) return Marshal.PtrToStringUTF8((IntPtr)p, length);
                    size = length + 1;
                }
            }
        }
        finally { FreeUtf8(id); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string? Compile(string uid, string project, string name, List<string> references, List<string> modReferences)
    {
        BuildResult r = ModCompiler.Build(project, name, references, force: true, modReferences: modReferences);
        foreach (string w in r.Warnings) Log.Warn($"mod {uid}: {w}");
        if (!r.Success)
        {
            foreach (string e in r.Errors) Log.Error($"mod {uid}: {e}");
            Log.Error($"mod {uid}: {name} did not compile ({r.Errors.Count} error(s))");
            return null;
        }
        Log.Info($"mod {uid}: compiled {r.Files} file(s), {r.Lines} lines in {r.Milliseconds} ms");
        return r.DllPath;
    }

    static readonly List<string> ModFolders = new();

    static void AddModFolder(string folder)
    {
        lock (ModFolders)
        {
            if (ModFolders.Count == 0)
            {
                // Os DOIS canais: o Mono embutido às vezes resolve um TypeRef de
                // dentro de `RuntimeModule.GetTypes()` sem passar pelo
                // `AssemblyLoadContext` — é o mesmo motivo pelo qual o
                // ShimAssembly instala evento de AppDomain E hook nativo.
                AssemblyLoadContext.Default.Resolving += ResolveFromMods;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAppDomain;
            }
            if (!ModFolders.Contains(folder)) ModFolders.Add(folder);
        }
    }

    static Assembly? ResolveFromAppDomain(object? sender, ResolveEventArgs args)
    {
        try
        {
            return FindModAssembly(new AssemblyName(args.Name).Name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Nomes em resolução AGORA nesta thread: evita reentrar em si mesmo.</summary>
    [ThreadStatic] static HashSet<string>? resolving;

    /// <summary>
    /// Procura <c>&lt;pasta&gt;/&lt;Nome&gt;.dll</c> em toda pasta de mod conhecida.
    /// Devolve o assembly já reescrito (mesma rota do <see cref="IlPatcher"/>).
    /// </summary>
    static Assembly? FindModAssembly(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return null;
        // `IlPatcher.Load` dispara resolução das dependências do próprio arquivo;
        // sem esta trava o mesmo nome voltaria aqui e entraria em laço.
        resolving ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!resolving.Add(simpleName)) return null;
        try
        {
            string[] folders;
            lock (ModFolders) folders = ModFolders.ToArray();
            foreach (string folder in folders)
            {
                string candidate = Path.Combine(folder, simpleName + ".dll");
                if (!File.Exists(candidate)) continue;
                try
                {
                    Log.Info($"assembly resolve: {simpleName} -> {candidate}");
                    return IlPatcher.Load(candidate);
                }
                catch (Exception e)
                {
                    Log.Warn($"assembly resolve: {simpleName} em {candidate} falhou ({e.GetType().Name}: {e.Message})");
                    return null;
                }
            }
            return null;
        }
        finally
        {
            resolving.Remove(simpleName);
        }
    }

    /// <summary>
    /// Registra todas as pastas de mod vizinhas de uma vez.
    ///
    /// O resolvedor só procura em pastas já vistas, e as pastas entram uma a
    /// uma conforme cada mod começa a carregar. A ordem do manifesto é
    /// alfabética (mais as dependências declaradas), então um mod que usa
    /// <c>weakReference</c> carrega ANTES do referenciado: foi assim que o
    /// FargowiltasSouls falhou com
    /// <c>TypeLoadException: Could not load file or assembly 'Luminance'</c> e
    /// ficou de fora do jogo, sem nenhuma mensagem na tela.
    /// </summary>
    static void AddModRoot(string? root)
    {
        if (string.IsNullOrEmpty(root)) return;
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.GetDirectories(root)) AddModFolder(dir);
            AddModFolder(root);
        }
        catch (Exception e)
        {
            Log.Warn($"resolver: não consegui listar {root} ({e.GetType().Name}: {e.Message})");
        }
    }

    static Assembly? ResolveFromMods(AssemblyLoadContext context, AssemblyName name) =>
        FindModAssembly(name.Name);

    internal static byte* Utf8(string text) => (byte*)Marshal.StringToCoTaskMemUTF8(text);

    internal static void FreeUtf8(byte* text) => Marshal.FreeCoTaskMem((IntPtr)text);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static NativeApi* Native()
    {
        if (Api == null) throw new InvalidOperationException("BunnyLoader native API is not initialized");
        return Api;
    }
}
