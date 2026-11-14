using System;
using System.Collections.Generic;
using System.IO;
using BunnyLoader.Interop;
using ReLogic.Content;
using Terraria.ModLoader;

namespace BunnyLoader;

public sealed class ModAssets
{
    readonly Mod mod;

    readonly Dictionary<string, object> requested = new();

    internal ModAssets(Mod mod) => this.mod = mod;

    public Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad) where T : class, IGameObject
    {
        Asset<T>.CheckSupported();
        string relative = Relative(assetName);
        string extension = Extension<T>();
        lock (requested)
        {
            if (requested.TryGetValue(relative + extension, out object? have)) return (Asset<T>)have;
            string? file = Find(relative, extension);
            if (file == null) throw new FileNotFoundException($"{mod.Name}: asset '{assetName}' not found in {mod.Folder}");
            var asset = new Asset<T>($"{mod.Name}/{relative}", file);
            requested[relative + extension] = asset;
            return asset;
        }
    }

    public bool HasAsset<T>(string assetName) where T : class, IGameObject =>
        (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D) || typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont)) &&
        Find(Relative(assetName), Extension<T>()) != null;

    static string Extension<T>() => typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont) ? ".json" : ".png";

    static string Relative(string assetName)
    {
        string name = assetName.Replace('\\', '/').TrimStart('/');
        foreach (string extension in new[] { ".png", ".json" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        return name;
    }

    string? Find(string relative, string extension)
    {
        string? path = mod.ResolveFile(relative + extension);
        if (path != null && File.Exists(path)) return path;
        // No tModLoader 1.4.5 "NomeDoMod/Item" e "Item" apontam para o mesmo
        // recurso (o nome do mod é só um prefixo lógico do asset). Um `.tmod`
        // pede quase sempre a forma completa; o arquivo extraído fica na raiz
        // da pasta do mod. Tentar as duas formas evita o "asset not found".
        string prefix = mod.Name + "/";
        if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            path = mod.ResolveFile(relative.Substring(prefix.Length) + extension);
            if (path != null && File.Exists(path)) return path;
        }
        return null;
    }
}
