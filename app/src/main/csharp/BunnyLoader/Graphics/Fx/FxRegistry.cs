using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;

namespace BunnyLoader.Graphics;

// One translated .fx pass: the GLSL program registered in the native shader
// registry plus the metadata the draw path needs (samplers, uniform names).
internal sealed class FxShaderBinding
{
    internal string Name = "";

    internal string Pass = "";

    internal string? File;

    internal FxProgram Program = null!;

    internal ScreenGraphics.Shader? Shader;

    internal int Id = -1;

    internal string Error = "";

    internal bool Ready => Shader != null && Id > 0;
}

// Resolves "ModName/Assets/.../Foo" + pass name to the mod's Foo.fx, translates
// it and registers the resulting GLSL program. Results (success or failure) are
// cached, so a missing .fx costs one warning per asset.
internal static class FxRegistry
{
    static readonly object Gate = new();

    static readonly Dictionary<string, FxShaderBinding> ByName = new(StringComparer.Ordinal);

    static readonly Dictionary<string, int> TextureIds = new(StringComparer.Ordinal);

    static readonly HashSet<string> ReportedTextures = new(StringComparer.Ordinal);

    internal static int Count
    {
        get
        {
            lock (Gate) return ByName.Count;
        }
    }

    internal static FxShaderBinding? Get(string key)
    {
        lock (Gate) return ByName.TryGetValue(key, out FxShaderBinding? binding) ? binding : null;
    }

    static string Key(string assetName, string? pass) => assetName + "|" + (pass ?? "");

    /// <summary>Translates (once) the .fx behind an asset name, for one pass.</summary>
    internal static FxShaderBinding? Find(string assetName, string? pass = null)
    {
        string key = Key(assetName, pass);
        lock (Gate)
        {
            if (ByName.TryGetValue(key, out FxShaderBinding? cached)) return cached;
        }

        FxShaderBinding binding = Compile(assetName, pass ?? "");
        lock (Gate) ByName[key] = binding;
        return binding;
    }

    static FxShaderBinding Compile(string assetName, string pass)
    {
        var binding = new FxShaderBinding { Name = assetName, Pass = pass };

        Mod? mod = SplitMod(assetName, out string relative);
        string full = "";
        string found = "";

        if (mod != null)
        {
            string? resolved = ResolveSource(mod, relative, out found);
            if (resolved != null) full = resolved;
        }

        if (full.Length == 0)
        {
            // Nome sem prefixo de mod (ou com prefixo que nao existe): procura em
            // todos os mods carregados. O Asset.Name do tML normalmente traz o
            // prefixo, mas nao vale quebrar o efeito inteiro por causa disso.
            string probe = mod == null ? assetName : relative;
            foreach (Mod candidate in LoadedMods())
            {
                string? resolved = ResolveSource(candidate, probe, out string where);
                if (resolved == null) continue;
                if (mod != null && !ReferenceEquals(candidate, mod)) continue;
                mod = candidate;
                full = resolved;
                found = where;
                Log.Info($"shader {assetName}: resolvido em {candidate.Name}/{where} (asset sem prefixo de mod)");
                break;
            }
        }

        if (mod == null || full.Length == 0)
        {
            binding.Error = $"no .fx found for {assetName}";
            Log.Warn($"shader {assetName}: nenhum .fx encontrado ({assetName}.fx); o efeito nao vai desenhar");
            return binding;
        }

        binding.File = mod.Name + "/" + found;
        FxProgram program;
        try
        {
            program = FxTranslator.Translate(File.ReadAllText(full), string.IsNullOrEmpty(pass) ? null : pass);
        }
        catch (Exception e)
        {
            binding.Error = e.GetType().Name + ": " + e.Message;
            Log.Warn($"shader {assetName}: leitura de {found} falhou: {binding.Error}");
            return binding;
        }

        binding.Program = program;
        if (!program.Ok)
        {
            binding.Error = program.Error ?? "translation failed";
            Log.Warn($"shader {assetName}: HLSL->GLSL falhou em {found}: {binding.Error}");
            return binding;
        }

        try
        {
            binding.Shader = ScreenGraphics.Shader.FromSource(program.Glsl, assetName + (pass.Length > 0 ? "#" + pass : ""));
            binding.Id = binding.Shader.Id;
        }
        catch (Exception e)
        {
            binding.Error = e.GetType().Name + ": " + e.Message;
            Log.Warn($"shader {assetName}: GLSL de {found} nao compilou: {binding.Error}");
            return binding;
        }

        Log.Info($"shader {assetName}: .fx traduzido ({found}; pass '{program.PixelEntry}'; "
            + $"{program.Samplers.Count} sampler(s), {program.Uniforms.Count} uniform(s), "
            + $"{program.Booleans.Count} bool(s))");
        foreach (string note in program.Notes) Log.Warn($"shader {assetName}: {note}");
        return binding;
    }

    static Mod? SplitMod(string assetName, out string relative)
    {
        relative = assetName;
        int slash = assetName.IndexOf('/');
        if (slash <= 0) return null;
        relative = assetName[(slash + 1)..];
        string modName = assetName[..slash];
        try { return Terraria.ModLoader.ModLoader.GetMod(modName); }
        catch (Exception) { return null; }
    }

    static Mod[] LoadedMods()
    {
        try { return Terraria.ModLoader.ModLoader.Mods; }
        catch (Exception) { return Array.Empty<Mod>(); }
    }

    static string? ResolveSource(Mod mod, string relative, out string found)
    {
        found = "";
        string trimmed = relative.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
            || relative.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)
            ? relative
            : relative + ".fx";

        foreach (string candidate in new[] { trimmed, relative })
        {
            try
            {
                string? full = mod.ResolveFile(candidate);
                if (full != null && File.Exists(full))
                {
                    found = candidate;
                    return full;
                }
            }
            catch (Exception)
            {
                // ResolveFile throws for paths escaping the mod root; ignore.
            }
        }

        return null;
    }

    /// <summary>
    /// Loads (once) a mod texture into the native texture registry so the draw
    /// can bind it to a sampler. Returns 0 when it cannot be loaded.
    /// </summary>
    internal static int Texture(string assetName)
    {
        lock (Gate)
        {
            if (TextureIds.TryGetValue(assetName, out int cached)) return cached;
        }

        int id = 0;
        try
        {
            Mod? mod = SplitMod(assetName, out string relative);
            if (mod != null)
            {
                try { id = ScreenGraphics.Texture.Load(mod, relative, nearest: false, repeat: true).Texture; }
                catch (Exception) { id = 0; }
            }

            if (id == 0)
            {
                string probe = mod == null ? assetName : relative;
                foreach (Mod candidate in LoadedMods())
                {
                    try
                    {
                        id = ScreenGraphics.Texture.Load(candidate, probe, nearest: false, repeat: true).Texture;
                        if (id != 0) break;
                    }
                    catch (Exception)
                    {
                        // candidate has no such texture; keep looking.
                    }
                }
            }

            if (id == 0)
            {
                lock (Gate)
                {
                    if (ReportedTextures.Add(assetName))
                        Log.Warn($"shader texture {assetName}: nao carregou; usando a tela");
                }
            }
        }
        catch (Exception e)
        {
            lock (Gate)
            {
                if (ReportedTextures.Add(assetName))
                    Log.Warn($"shader texture {assetName}: nao carregou ({e.GetType().Name}: {e.Message}); usando a tela");
            }
        }

        lock (Gate) TextureIds[assetName] = id;
        return id;
    }
}
