using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace BunnyLoader;

/// <summary>
/// Faz o <c>BunnyLoader.dll</c> responder pelos assemblies que um <c>.tmod</c>
/// referencia.
///
/// O ponto de partida é o próprio tModLoader 1.4.5:
/// <c>patches/tModLoader/Terraria/release_extras/tMLMod.targets</c> compila
/// todo mod contra <c>$(tMLPath) = tModLoader.dll</c> mais
/// <c>Libraries/**/*.dll</c> (FNA.dll, ReLogic.dll, Steamworks.NET.dll,
/// Newtonsoft.Json.dll, log4net.dll...). O assembly do jogo se chama
/// <b>tModLoader</b>, versão 1.4.4.9 na linha estável — por isso o primeiro
/// <c>Assembly.GetTypes()</c> de um mod pede exatamente
///
/// <code>
/// tModLoader, Version=1.4.4.9, Culture=neutral, PublicKeyToken=null
/// </code>
///
/// Aqui não existe nenhum desses assemblies: o Bunny Loader é UM só, com a API
/// do ModLoader e as proxies do jogo dentro. A versão pedida não importa (o
/// assembly não tem nome forte), o que importa é devolver o shim para que os
/// tipos <c>Terraria.ModLoader.Mod</c>, <c>Terraria.Item</c>... sejam achados.
///
/// Há DOIS caminhos de resolução, porque o runtime aqui é Mono embutido:
///  1. <c>AppDomain.AssemblyResolve</c>, instalado ANTES dos outros handlers
///     (padrão de <c>Terraria/ModLoader/Core/AssemblyResolving.cs</c> do tML);
///  2. hook nativo <c>mono_install_assembly_preload_hook</c> (DotnetHost.cpp),
///     que é o único garantido quando o Mono resolve um TypeRef de dentro de
///     <c>RuntimeModule.GetTypes()</c> — o evento gerenciado nem sempre dispara
///     nesse caminho.
/// </summary>
internal static class ShimAssembly
{
    /// <summary>
    /// Nomes simples que o shim representa. Vêm de <c>tMLMod.targets</c> e de
    /// <c>tModLoader.targets</c> (1.4.5). Não inclui System.*: esses existem de
    /// verdade no runtime e devem continuar indo pelo carregador normal.
    /// </summary>
    static readonly string[] Identities =
    {
        // O jogo + a API do ModLoader, num assembly só no PC.
        "tModLoader", "Terraria", "TerrariaServer", "Terraria.ModLoader",
        // Libraries/ do tModLoader 1.4.5. NÃO inclui "Mono.Cecil": o Cecil é
        // uma dependência real do IlPatcher e vai empacotado como DLL — se
        // esta lista respondesse por ele, o Mono receberia o shim com outro
        // nome (FileLoadException) e a reescrita de IL nunca rodaria.
        "ReLogic", "FNA", "Microsoft.Xna.Framework",
        "log4net", "Newtonsoft.Json", "Steamworks.NET",
        // MonoMod: o tML 1.4.5 embarca `Libraries/MonoMod.Utils.dll` e
        // `MonoMod.RuntimeDetour.dll`. Os dois entram como identidade porque
        // o mod referencia esses assemblies por nome — `List<ILHook>`,
        // `new ILCursor(il)` — e sem resolvê-los o tipo que faz o patch nem
        // carrega (foi o "VTable setup of ScreenShaderFixer failed").
        // Rede de segurança: quem manda são as fachadas `MonoMod.*.dll`
        // (HasFacade devolve null e o Mono carrega a fachada).
        "MonoMod.Utils", "MonoMod.RuntimeDetour",
    };

    static bool installed;

    internal static void Install()
    {
        if (installed) return;

        // REGRA: isto nunca pode lançar. Já esteve no .cctor de Host e, por
        // causa de um Log antes de a API nativa existir, derrubou o runtime
        // inteiro (TypeInitializationException -> "the .NET runtime did not
        // start" para TODO mod, inclusive os .bl). Cada passo é isolado.
        try
        {
            // tML 1.4.5 (Core/AssemblyResolving.cs): o handler nosso tem de
            // rodar ANTES dos demais. O evento é apoiado num campo privado do
            // AppDomain; zerar e readicionar o delegate anterior inverte a
            // ordem sem perder ninguém.
            try
            {
                FieldInfo field = typeof(AppDomain).GetFields((BindingFlags)(-1))
                    .First(f => f.Name.EndsWith("AssemblyResolve", StringComparison.Ordinal) &&
                                !f.Name.Contains("ReflectionOnly", StringComparison.Ordinal) &&
                                f.FieldType == typeof(ResolveEventHandler));
                var previous = (ResolveEventHandler?)field.GetValue(AppDomain.CurrentDomain);
                field.SetValue(AppDomain.CurrentDomain, null);
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(args.Name);
                if (previous != null) AppDomain.CurrentDomain.AssemblyResolve += previous;
            }
            catch (Exception e)
            {
                Log.Info($"assembly resolve: sem campo de evento ({e.GetType().Name}); ordem padrão");
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(args.Name);
            }

            // O caminho "moderno": LoadFromAssemblyPath passa pelo ALC.Default.
            try
            {
                AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(name.FullName ?? name.Name);
            }
            catch (Exception e)
            {
                Log.Info($"assembly resolve: sem AssemblyLoadContext ({e.GetType().Name}); sigo com o AppDomain");
            }
            Log.Info("assembly resolve: shim responde por tModLoader/Terraria (hook nativo + eventos)");
            installed = true;
        }
        catch (Exception e)
        {
            try { Log.Warn($"assembly resolve: instalação falhou, seguindo sem redirecionamento: {e}"); }
            catch { /* sem API nativa: não há onde logar, e não vamos derrubar o boot por isso */ }
        }
    }

    internal static bool IsShimName(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return false;
        foreach (string candidate in Identities)
            if (string.Equals(candidate, simpleName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static Assembly? Resolve(string? requested)
    {
        if (string.IsNullOrEmpty(requested)) return null;
        string simple;
        try
        {
            simple = new AssemblyName(requested).Name ?? "";
        }
        catch
        {
            // Nome malformado: não é nosso, deixa o runtime seguir o caminho dele.
            return null;
        }
        if (!IsShimName(simple)) return null;
        // Fachada com o nome certo (tModLoader.dll) tem prioridade: devolver o
        // shim aqui seria recusado por ValidateAssemblyNameWithSimpleName.
        // O app copia o runtime para filesDir/dotnet, que NÃO é necessariamente
        // AppContext.BaseDirectory; por isso também procuramos na lista TPA que
        // o monovm_initialize recebeu (é onde o Mono vai achar o arquivo).
        if (HasFacade(simple)) return null;
        Log.Info($"assembly resolve: {requested} -> {typeof(ShimAssembly).Assembly.GetName().Name} (sem fachada)");
        return typeof(ShimAssembly).Assembly;
    }

    static bool HasFacade(string simple)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, simple + ".dll"))) return true;
        try
        {
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (string path in tpa.Split(Path.PathSeparator))
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(path), simple,
                            StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch { /* sem TPA: cai para o shim */ }
        return false;
    }
}

/// <summary>
/// <c>Assembly.GetTypes()</c> é tudo ou nada. No CoreCLR um tipo que não
/// carrega vira <see cref="ReflectionTypeLoadException"/> (com a lista dos que
/// carregaram); no Mono embutido o mesmo problema costuma subir direto como
/// <see cref="FileNotFoundException"/> do assembly ausente — foi exatamente o
/// que apareceu no relatório.
///
/// A ideia vem de <c>Core/AssemblyManager.GetLoadableTypes</c> do tML 1.4.5:
/// separar os tipos carregáveis dos que não são, em vez de derrubar o mod
/// inteiro. Lá isso é feito com MetadataLoadContext; aqui, sem esse pacote,
/// cada TypeDef é resolvido pelo token com <c>Module.ResolveType</c>, e a
/// exceção de cada um vira uma linha de log — a lista exata do que falta
/// portar da API do tModLoader.
/// </summary>
internal static class ModTypes
{
    /// <summary>Quantas falhas cabem no log. O resto vira uma linha de total.</summary>
    const int MaxReported = 20;

    internal static List<Type> Of(Assembly assembly, string uid)
    {
        try
        {
            return new List<Type>(assembly.GetTypes());
        }
        catch (ReflectionTypeLoadException e)
        {
            return Partial(e.Types, e.LoaderExceptions, uid);
        }
        catch (Exception e) when (e is FileNotFoundException or TypeLoadException or BadImageFormatException)
        {
            // Mono: quando o resolver não cobre um assembly, GetTypes lança a
            // FileNotFoundException crua, sem ReflectionTypeLoadException.
            Log.Error($"mod {uid}: GetTypes falhou direto ({e.GetType().Name}: {e.Message})");
            Log.Error($"mod {uid}: tentando tipo por token para mostrar exatamente o que falta");
            return OneByOne(assembly, uid);
        }
    }

    static List<Type> Partial(Type?[] types, Exception?[] errors, string uid)
    {
        var loaded = new List<Type>(types.Length);
        int failed = 0;
        foreach (Type? type in types)
        {
            if (type == null) failed++;
            else loaded.Add(type);
        }
        Log.Error($"mod {uid}: {failed} tipo(s) não carregaram; seguindo com {loaded.Count}");
        Report(errors, failed, uid);
        return loaded;
    }

    static List<Type> OneByOne(Assembly assembly, string uid)
    {
        var loaded = new List<Type>();
        var errors = new List<Exception>();
        foreach (Module module in assembly.GetModules())
        {
            // TypeDef é a tabela 0x02; os tokens são contíguos a partir de 1.
            // Um ArgumentException marca o fim da tabela, não um tipo ruim.
            for (int token = 0x02000001; token <= 0x0201_0000; token++)
            {
                try
                {
                    Type? type = module.ResolveType(token);
                    if (type != null) loaded.Add(type);
                }
                catch (ArgumentException)
                {
                    break;
                }
                catch (Exception e)
                {
                    errors.Add(e);
                }
            }
        }
        Log.Error($"mod {uid}: {loaded.Count} tipo(s) carregaram, {errors.Count} falharam");
        Report(errors.ToArray(), errors.Count, uid);
        return loaded;
    }

    static void Report(Exception?[] errors, int failed, string uid)
    {
        int shown = 0;
        foreach (Exception? inner in errors)
        {
            if (inner == null) continue;
            if (shown == MaxReported)
            {
                Log.Error($"mod {uid}: ... e mais {failed - shown}; o resto está no log detalhado");
                break;
            }
            Log.Error($"mod {uid}:   [{shown + 1}] {inner.GetType().Name}: {inner.Message}");
            shown++;
        }
    }
}
