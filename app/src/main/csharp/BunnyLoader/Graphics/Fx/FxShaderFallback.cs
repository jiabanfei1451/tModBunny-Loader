using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;

namespace BunnyLoader.Graphics;

/// <summary>
/// v60: registra os shaders autoloaded de um mod NO registro do próprio mod,
/// sem passar por <c>mod.Assets.Request&lt;Effect&gt;</code>.
///
/// Motivo: se a reescrita de IL (<see cref="FxRequest"/>) não pegar todas as
/// chamadas — ou se o mod pedir os shaders por outro caminho — o
/// <c>ShaderManager.LoadShaders</c> do Luminance aborta e o dicionário
/// <c>shaders</c> dele fica vazio. Aí todo <c>GetShader(name)</c> no draw
/// estoura <c>KeyNotFoundException</c>: o Luminance não desenha nada e sobram
/// os quads brancos de 1x1. Aqui a gente monta o mesmo registro por fora,
/// com a mesma chave (<c>Mod.NomeDoArquivo</c>) e o mesmo <c>Ref&lt;Effect&gt;</code>
/// que o <c>SetShader</code> do mod criaria — só pulando o <c>Request</c>.
///
/// Só inscreve o que ainda NÃO está no dicionário do mod: se a carga normal
/// funcionou, este caminho não toca em nada.
/// </summary>
internal static class FxShaderFallback
{
    const string ShaderDir = "AutoloadedEffects/Shaders";

    const string FilterDir = "AutoloadedEffects/Filters";

    static bool done;

    internal static void RegisterAll()
    {
        if (done) return;
        done = true;

        Mod[] mods;
        try { mods = Terraria.ModLoader.ModLoader.Mods; }
        catch (Exception) { return; }

        int total = 0;
        foreach (Mod mod in mods)
        {
            if (mod == null || string.IsNullOrEmpty(mod.Folder)) continue;
            try { total += Run(mod); }
            catch (Exception e) { Log.Warn($"[fx] fallback {mod.Name}: {e.GetType().Name}: {e.Message}"); }
        }

        if (total > 0)
            Log.Info($"[fx] fallback: {total} entrada(s) inscrita(s) por fora do Assets.Request<Effect>");
    }

    static int Run(Mod mod)
    {
        Registry? registry = Find(mod);
        if (registry == null) return 0;

        int count = RegisterFamilia(registry!, mod, ShaderDir, registry!.SetShader, "shader");
        count += RegisterFamilia(registry!, mod, FilterDir, registry!.SetFilter, "filtro");

        // v61: o PostSetupContent do Luminance marca HasFinishedLoading no fim.
        // Se ele abortou, a flag fica FALSA para sempre e o resto da biblioteca
        // (consumidores dos shaders) acha que ainda nao pode desenhar. Como
        // aqui ja' inscrevemos o registro inteiro, a flag pode ir para true.
        if (count > 0) registry!.FinishLoading();

        return count;
    }

    static int RegisterFamilia(Registry registry, Mod mod, string dir, MethodInfo? setter, string kind)
    {
        if (setter == null) return 0;

        var files = new List<string>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(mod.Folder, "*.fx", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(mod.Folder, path).Replace('\\', '/');
                if (!relative.Contains(dir + "/", StringComparison.Ordinal)) continue;
                if (relative.Contains("/Compiler/", StringComparison.Ordinal)) continue;
                files.Add(relative);
            }
        }
        catch (Exception) { return 0; }

        files.Sort(StringComparer.Ordinal);
        int count = 0;
        foreach (string relative in files)
        {
            string semExt = relative[..^3];
            string key = mod.Name + "." + Path.GetFileName(semExt);
            string asset = mod.Name + "/" + semExt;

            // Já inscrito pela carga normal do mod: não mexe.
            if (registry.Has(key)) continue;

            try
            {
                Effect effect = Effect.CachedOrCreate(asset);
                var boxed = new global::Terraria.Ref<Effect>(effect);
                setter.Invoke(null, new object?[] { key, boxed });
                count++;
                Log.Info($"[fx] fallback {kind} {key} <- {asset}.fx");
            }
            catch (Exception e)
            {
                Log.Warn($"[fx] fallback {kind} {key}: {e.GetType().Name}: {e.Message}");
            }
        }

        return count;
    }

    /// <summary>Os métodos estáticos de registro que o mod expõe.</summary>
    sealed class Registry
    {
        internal MethodInfo? SetShader;

        internal MethodInfo? SetFilter;

        internal FieldInfo? Shaders;

        internal Type? Type;

        /// <summary>Liga a flag "já terminei de carregar" do mod (ver Run).</summary>
        internal void FinishLoading()
        {
            if (Type == null) return;
            try
            {
                PropertyInfo? prop = Type.GetProperty("HasFinishedLoading",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo? setter = prop?.GetSetMethod(true);
                if (setter == null) return;
                setter.Invoke(null, new object?[] { true });
                Log.Info($"[fx] fallback: HasFinishedLoading de {Type.FullName} forçado para true");
            }
            catch (Exception e)
            {
                Log.Warn($"[fx] fallback HasFinishedLoading: {e.GetType().Name}: {e.Message}");
            }
        }

        internal bool Has(string key)
        {
            object? dict;
            try { dict = Shaders?.GetValue(null); }
            catch (Exception) { return false; }
            if (dict == null) return false;

            try
            {
                MethodInfo? contains = dict.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
                return contains != null && (bool)contains.Invoke(dict, new object?[] { key })!;
            }
            catch (Exception) { return false; }
        }
    }

    static Registry? Find(Mod mod)
    {
        Assembly[] assemblies;
        try { assemblies = AssemblyLoadContext.Default.Assemblies.ToArray(); }
        catch (Exception) { return null; }

        string prefixo = mod.Name + ".";
        foreach (Assembly assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            catch (Exception) { continue; }

            foreach (Type type in types)
            {
                string? full;
                try { full = type.FullName; }
                catch (Exception) { continue; }
                if (full == null || !full.StartsWith(prefixo, StringComparison.Ordinal)) continue;

                MethodInfo? setShader = Exact(type, "SetShader");
                if (setShader == null) continue;

                FieldInfo? shaders = null;
                try
                {
                    shaders = type.GetField("shaders", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch (Exception) { }

                return new Registry
                {
                    SetShader = setShader,
                    SetFilter = Exact(type, "SetFilter"),
                    Shaders = shaders,
                    Type = type,
                };
            }
        }

        return null;
    }

    static MethodInfo? Exact(Type type, string name)
    {
        try
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(global::Terraria.Ref<Effect>) }, null);
        }
        catch (Exception) { return null; }
    }
}
