using System;
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
    readonly Mod mod;

    readonly Dictionary<string, object> requested = new();

    internal ModAssets(Mod mod) => this.mod = mod;

    public override Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad)
        where T : class
    {
        AssetKinds.CheckSupported(typeof(T));
        string relative = Relative(assetName);
        string extension = Extension<T>();
        lock (requested)
        {
            if (requested.TryGetValue(relative + extension, out object? have)) return (Asset<T>)have;
            if (AssetKinds.IsEffectType(typeof(T)))
            {
                // Shader (.xnb / D3D9 bytecode): este port nao tem runtime de
                // HLSL (o jogo desenha pelo Unity). Devolver o stub mantem o
                // mod carregando; sem isso o ShaderManager.LoadShaders do
                // Luminance morria em FileNotFoundException e levava o
                // assembly inteiro junto.
                var stub = new Asset<T>($"{mod.Name}/{relative}", null);
                requested[relative + extension] = stub;
                return stub;
            }
            string? file = Find(relative, extension);
            if (file == null) throw new FileNotFoundException($"{mod.Name}: asset '{assetName}' not found in {mod.Folder}");
            var asset = new Asset<T>($"{mod.Name}/{relative}", file);
            requested[relative + extension] = asset;
            return asset;
        }
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
        foreach (string extension in new[] { ".png", ".json", ".ttf" })
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
