using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BunnyLoader.Interop;
using ReLogic.Content;
using Terraria.ModLoader;

namespace BunnyLoader;

/// <summary>
/// <c>Mod.Assets</c> do tML e <see cref="AssetRepository"/>; este e o
/// repositorio concreto do shim, que resolve os assets no disco do mod.
/// Ver <see cref="AssetRepository"/> para o porque de derivar (assinatura de
/// <c>Mod::get_Assets</c> no IL do mod).
/// </summary>
public sealed class ModAssets : AssetRepository
{
    static readonly string[] Extensions = { ".png", ".json" };

    readonly Mod mod;

    readonly Dictionary<string, object> requested = new();

    readonly Dictionary<(string Name, string Extension), object> byName = new();

    readonly ConcurrentDictionary<string, string?> located = new(StringComparer.Ordinal);

    internal ModAssets(Mod mod) => this.mod = mod;

    public override Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad)
        where T : class
    {
        Asset<T>.CheckSupported();
        string extension = Extension<T>();
        lock (requested)
        {
            if (byName.TryGetValue((assetName, extension), out object? seen)) return ((Asset<T>)seen).OnRequest(mode, false);
        }
        string relative = Relative(assetName);
        string key = relative + extension;
        Asset<T> result;
        bool created = false;
        lock (requested)
        {
            if (!requested.TryGetValue(key, out object? have))
            {
                if (AssetKinds.IsEffectType(typeof(T)))
                {
                    // Shader (.xnb / bytecode D3D9): este port nao tem runtime de
                    // HLSL (o jogo desenha pelo Unity). Devolver o stub mantem o
                    // mod carregando; sem isso o ShaderManager.LoadShaders do
                    // Luminance morria em FileNotFoundException e levava o
                    // assembly inteiro junto.
                    have = new Asset<T>($"{mod.Name}/{relative}", null);
                }
                else
                {
                    string? file = Find(relative, extension);
                    if (file == null) throw new FileNotFoundException($"{mod.Name}: asset '{assetName}' not found in {mod.Folder}");
                    have = new Asset<T>($"{mod.Name}/{relative}", file);
                }
                requested[key] = have;
                created = true;
            }
            byName[(assetName, extension)] = have;
            result = (Asset<T>)have;
        }
        return result.OnRequest(mode, created);
    }

    public override bool HasAsset<T>(string assetName) where T : class =>
        AssetKinds.IsEffectType(typeof(T)) ||
        ((typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D) || AssetKinds.IsFontType(typeof(T))) &&
         Find(Relative(assetName), Extension<T>()) != null);

    static string Extension<T>() where T : class =>
        typeof(T) == typeof(ReLogic.Graphics.DynamicSpriteFont) ? ".ttf" :
        typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont) ? ".json" :
        AssetKinds.IsEffectType(typeof(T)) ? ".xnb" : ".png";

    static string Relative(string assetName)
    {
        string name = assetName.Replace('\\', '/').TrimStart('/');
        foreach (string extension in Extensions)
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        return name;
    }

    string? Find(string relative, string extension)
    {
        string key = relative + extension;
        if (located.TryGetValue(key, out string? known)) return known;
        string? path = mod.ResolveFile(key);
        string? found = path != null && File.Exists(path) ? path : null;

        // No tModLoader 1.4.5 "NomeDoMod/Item" e "Item" apontam para o mesmo
        // recurso (o nome do mod e' so um prefixo logico do asset). Um .tmod
        // pede quase sempre a forma completa; o arquivo extraido fica na raiz
        // da pasta do mod. Tentar as duas formas evita o "asset not found".
        if (found == null)
        {
            string prefix = mod.Name + "/";
            if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = mod.ResolveFile(relative.Substring(prefix.Length) + extension);
                found = path != null && File.Exists(path) ? path : null;
            }
        }

        located[key] = found;
        return found;
    }
}
