using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace BunnyLoader.Sandbox;

/// <summary>
/// Roda o carregamento de verdade, sem o jogo.
///
/// Diferente do modo --jit (que so compila os metodos), aqui:
///   - descompacta o pacote INTEIRO (inclusive .rawimg -> .png e Info texto);
///   - carrega BunnyLoader.dll + tModLoader.dll no AssemblyLoadContext.Default
///     (o mesmo que Host.LoadMod usa la dentro);
///   - instala o NativeApi falso e chama os proprios `[UnmanagedCallersOnly]`
///     Host.Initialize / Host.LoadMod pelo ponteiro da funcao.
///
/// Ou seja: e o MESMO codigo que roda no aparelho, so que com um "jogo" de
/// faz de conta por baixo. Nenhuma linha do loader foi reescrita.
/// </summary>
internal static unsafe class LoadRunner
{
    internal sealed record Package(string Work, string Dll, string Id, string Name, string Version, string[] Deps);

    internal static Package Prepare(string path, string root)
    {
        string work = Path.Combine(Path.GetTempPath(), "bl-sandbox-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".dll")
        {
            string copy = Path.Combine(work, Path.GetFileName(path));
            File.Copy(path, copy, true);
            return new Package(work, copy, Path.GetFileNameWithoutExtension(path),
                Path.GetFileNameWithoutExtension(path), "0.0", Array.Empty<string>());
        }

        if (ext == ".bl")
        {
            ZipFile.ExtractToDirectory(path, work, true);
            string manifestPath = Path.Combine(work, "manifest.json");
            string id = Path.GetFileNameWithoutExtension(path).Split('-')[0];
            string name = id, version = "0.0";
            string[] deps = Array.Empty<string>();
            if (File.Exists(manifestPath))
            {
                string json = File.ReadAllText(manifestPath);
                id = Program.JsonField(json, "id") ?? id;
                name = Program.JsonField(json, "name") ?? id;
                version = Program.JsonField(json, "version") ?? version;
            }
            string? dll = Directory.GetFiles(work, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dll == null) throw new InvalidOperationException(".bl sem dll na raiz");
            return new Package(work, dll, id, name, version, deps);
        }

        // .tmod: container TMOD ou zip antigo
        //
        // A pasta TEM de conter "/TML/" — é assim que ModPipeline.For() escolhe
        // a rota (ver Pipelines/ModPipeline.cs). Sem isso o sandbox carregava o
        // .tmod pela rota .bl, SEM reescrever a IL, e o relatório enchia de
        // MissingField (Item.damage, Entity.active...) que nao existem no
        // aparelho. Mesmo layout do telefone: TML/mods/<nome>/.
        work = Path.Combine(work, "TML", "mods");
        Directory.CreateDirectory(work);

        byte[] data = File.ReadAllBytes(path);
        bool tmod = data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "TMOD";
        if (!tmod)
        {
            ZipFile.ExtractToDirectory(path, work, true);
            string? dll = Directory.GetFiles(work, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dll == null) throw new InvalidOperationException(".tmod zip sem dll na raiz");
            return new Package(work, dll, Path.GetFileNameWithoutExtension(dll),
                Path.GetFileNameWithoutExtension(dll), "0.0", Array.Empty<string>());
        }
        return UnpackTmodAll(data, work);
    }

    static Package UnpackTmodAll(byte[] data, string work)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        br.ReadBytes(4); // "TMOD"
        string tmlVersion = ReadString(br);
        br.ReadBytes(20 + 256);
        br.ReadInt32();
        string name = ReadString(br);
        string version = ReadString(br);
        int count = br.ReadInt32();
        var entries = new List<(string Name, int Length, int Packed, long Offset)>();
        for (int i = 0; i < count; i++)
            entries.Add((ReadString(br).Replace('\\', '/'), br.ReadInt32(), br.ReadInt32(), 0));
        long cursor = ms.Position;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            entries[i] = (e.Name, e.Length, e.Packed, cursor);
            cursor += e.Packed;
        }

        int converted = 0, written = 0;
        foreach (var e in entries)
        {
            if (e.Name.EndsWith("/", StringComparison.Ordinal) || e.Name.Length == 0) continue;
            var raw = new byte[e.Packed];
            Array.Copy(data, e.Offset, raw, 0, e.Packed);
            byte[] bytes = raw;
            if (e.Length != e.Packed)
            {
                using var input = new MemoryStream(raw);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                bytes = output.ToArray();
            }
            // Mesma protecao contra zip-slip do Kotlin.
            string dest = Path.GetFullPath(Path.Combine(work, e.Name));
            if (!dest.StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (e.Name.EndsWith(".rawimg", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? png = PngWriter.FromRawImg(bytes);
                dest = Path.ChangeExtension(dest, ".png");
                File.WriteAllBytes(dest, png ?? bytes);
                if (png != null) converted++;
            }
            else if (Path.GetFileName(e.Name).Equals("Info", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(dest, Program.ModInfoReader.ToText(bytes, name, version));
            }
            else File.WriteAllBytes(dest, bytes);
            written++;
        }
        Console.WriteLine($"[sandbox] descompactado: {written} arquivo(s), {converted} .rawimg -> .png, tML {tmlVersion}");

        string? dll = Directory.GetFiles(work, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (dll == null) throw new InvalidOperationException(".tmod sem dll na raiz");
        return new Package(work, dll, name, name, version, Array.Empty<string>());
    }

    static string ReadString(BinaryReader br)
    {
        int length = br.Read7BitEncodedInt();
        return length <= 0 || length > 8 * 1024 * 1024 ? "" : Encoding.UTF8.GetString(br.ReadBytes(length));
    }

    /// <summary>Carrega o shim de verdade e chama Initialize/LoadMod pelo fnptr.</summary>
    internal static int Run(Package pkg, string root)
    {
        string shimDir = Path.Combine(root, "app", "build", "csharp", "BunnyLoader");
        string facadeDir = Path.Combine(root, "app", "build", "csharp", "tmlFacade");
        string shimPath = Path.Combine(shimDir, "BunnyLoader.dll");
        string facadePath = Path.Combine(facadeDir, "tModLoader.dll");
        if (!File.Exists(shimPath) || !File.Exists(facadePath))
            throw new FileNotFoundException("rode gradle :app:buildBunnyLoaderCs :app:buildTmlFacade antes");

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            foreach (string dir in new[] { facadeDir, shimDir, pkg.Work })
            {
                string p = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };

        Assembly shim = AssemblyLoadContext.Default.LoadFromAssemblyPath(shimPath);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(facadePath);
        // Fachadas das Libraries (FNA, ...): pre-carregar para o
        // Assembly.Load do mod achar no cache do contexto default, em vez de
        // cair no ShimAssembly.Resolve (que devolve o shim com OUTRO nome e
        // o Mono recusa: FileLoadException 0x80131509).
        foreach (string facadeDll in Directory.GetFiles(facadeDir, "*.dll"))
        {
            if (Path.GetFileName(facadeDll).Equals("tModLoader.dll", StringComparison.OrdinalIgnoreCase)) continue;
            try { AssemblyLoadContext.Default.LoadFromAssemblyPath(facadeDll); }
            catch (Exception e) { Console.WriteLine($"[sandbox] facade {Path.GetFileName(facadeDll)}: {e.Message}"); }
        }
        Console.WriteLine($"[sandbox] shim: {shim.FullName}");
        // A rota (.bl x .tmod) e escolhida por ModPipeline.For(folder), que so
        // olha o caminho. Imprimir aqui evita a duvida "reescreveu ou nao?"
        // quando o relatorio volta cheio de MissingField.
        Console.WriteLine($"[sandbox] dll: {pkg.Dll}");
        Console.WriteLine($"[sandbox] pipeline: {(pkg.Dll.Replace('\\', '/').Contains("/TML/", StringComparison.OrdinalIgnoreCase) ? "TML (com reescrita de IL)" : "BL (sem reescrita)")}");

        StubNativeApi.ConfigureManifest(pkg.Id, pkg.Name, pkg.Version, pkg.Deps);
        StubNativeApi.Install(shim);

        Type host = shim.GetType("BunnyLoader.Host", throwOnError: true)!;
        MethodInfo init = host.GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo load = host.GetMethod("LoadMod", BindingFlags.NonPublic | BindingFlags.Static)!;
        RuntimeHelpers.PrepareMethod(init.MethodHandle);
        RuntimeHelpers.PrepareMethod(load.MethodHandle);

        var initFn = (delegate* unmanaged<void*, int>)init.MethodHandle.GetFunctionPointer();
        int initRc = initFn((void*)StubNativeApi.Api);
        Console.WriteLine($"[sandbox] Host.Initialize -> {initRc}");
        if (initRc == 0) return 3;

        // Chama o corpo MANAGED (LoadModCore), nao o [UnmanagedCallersOnly]
        // LoadMod: entrada nativa corta a pilha e, quando o JIT do PC derruba o
        // processo no meio da carga, nao sobra frame nenhum para dizer onde foi.
        // Por reflexao a cadeia aparece inteira.
        MethodInfo core = host.GetMethod("LoadModCore", BindingFlags.NonPublic | BindingFlags.Static)!;
        int loadRc = (int)core.Invoke(null, new object[] { pkg.Id, pkg.Dll })!;
        Console.WriteLine($"[sandbox] Host.LoadMod -> {loadRc}");
        if (loadRc == 1) TriggerSetDefaults(shim);
        Dump(pkg);
        return loadRc == 1 ? 0 : 4;
    }

    /// <summary>
    /// O jogo chama ItemLoader.SetDefaults quando cria um item. No sandbox nao
    /// ha loop de jogo, entao disparamos uma vez por item registrado — e ai o
    /// SetDefaults do mod roda de verdade e escreve na memoria falsa. E como
    /// se verifica "as propriedades do item ficaram certas?" sem o aparelho.
    /// </summary>
    static void TriggerSetDefaults(Assembly shim)
    {
        Type? itemLoader = shim.GetType("Terraria.ModLoader.ItemLoader");
        MethodInfo? sd = itemLoader?.GetMethod("SetDefaults",
            BindingFlags.NonPublic | BindingFlags.Static);
        Type? itemType = shim.GetType("Terraria.Item");
        Type? variantType = shim.GetType("Terraria.GameContent.Items.ItemVariant");
        Type? origType = shim.GetType("Terraria.On_Item+orig_SetDefaults");
        if (sd == null || itemType == null || variantType == null || origType == null)
        {
            Console.WriteLine("[sandbox] nao achei as assinaturas do SetDefaults");
            return;
        }

        // Delegate `orig` vazio, gerado por emit para nao precisar referenciar
        // os tipos do shim em tempo de compilacao.
        var dm = new System.Reflection.Emit.DynamicMethod(
            "NoOpOrig", typeof(void), new[] { itemType, typeof(int), variantType },
            typeof(LoadRunner).Module, skipVisibility: true);
        dm.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        object orig = dm.CreateDelegate(origType);

        foreach (int type in StubNativeApi.RegisteredItems.Keys)
        {
            Console.WriteLine($"[sandbox] disparando SetDefaults do item [{type}]");
            try
            {
                object self = Activator.CreateInstance(itemType, new object[] { StubNativeApi.FakeObject })!;
                object variant = Activator.CreateInstance(variantType)!;
                sd.Invoke(null, new[] { orig, self, (object)type, variant });
            }
            catch (Exception e)
            {
                Exception real = e.InnerException ?? e;
                Console.WriteLine($"[sandbox] SetDefaults [{type}] FALHOU: {real.GetType().Name}: {real.Message}");
            }
        }
    }

    static void Dump(Package pkg)
    {
        Console.WriteLine("=== resultado do sandbox ===");
        Console.WriteLine($"  pasta de trabalho : {pkg.Work}");
        Console.WriteLine($"  itens registrados : {StubNativeApi.RegisteredItems.Count}");
        foreach (var (type, name) in StubNativeApi.RegisteredItems)
            Console.WriteLine($"    [{type}] {name}");
        Console.WriteLine("  campos de jogo escritos pelo SetDefaults (memoria falsa):");
        if (StubNativeApi.Offsets.Count == 0) Console.WriteLine("    (nenhum campo acessado)");
        foreach (string field in StubNativeApi.Offsets.Keys.OrderBy(x => x))
            Console.WriteLine($"    {field,-24} = {StubNativeApi.ReadInt32(field)}");
        int errors = StubNativeApi.NativeLog.Count(l => l.StartsWith("[mod] E"));
        Console.WriteLine($"  linhas de erro do loader : {errors}");
        Console.WriteLine($"  checks de layout ignorados: {StubNativeApi.LayoutNoise} (sem IL2CPP, nao sao erros reais)");
    }

    internal static void Cleanup(Package pkg)
    {
        try { Directory.Delete(pkg.Work, true); } catch { /* temporario */ }
    }
}
