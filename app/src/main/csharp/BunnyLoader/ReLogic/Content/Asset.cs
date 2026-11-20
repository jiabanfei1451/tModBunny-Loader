using System;
using System.Runtime.CompilerServices;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;

namespace ReLogic.Content;

/// <summary>
/// Um asset do jogo (textura, fonte, som).
///
/// A restrição genérica é <c>where T : class</c> como no tModLoader 1.4.4.9:
/// um mod compilado no PC referencia <c>ReLogic.Content.Asset`1&lt;Texture2D&gt;</c>
/// e o runtime valida a restrição ao vincular a chamada. Se aqui fosse
/// <c>where T : struct, IGameObject</c>, o JIT do mod lançaria
/// <c>TypeLoadException</c> ao instanciar <c>Asset`1&lt;Texture2D&gt;</c>.
///
/// A restrição do shim <c>IGameObject</c> foi removida (era
/// <c>where T : class, IGameObject</c>): o Luminance pede
/// <c>Request&lt;Effect&gt;</c> e <c>Effect</c> vem do FNA, sem a interface.
/// Com a restrição extra o JIT do mod lançaria <c>TypeLoadException</c> só
/// por instanciar <c>Asset`1&lt;Effect&gt;</c>. Quem precisa do ponteiro nativo
/// é <see cref="Value"/>, que delega ao
/// <see cref="BunnyLoader.Interop.GameObjectFactory"/> (e falha com
/// <c>NotSupportedException</c> para tipos que não são proxies do jogo).
/// </summary>
public sealed unsafe class Asset<T> where T : class
{
    readonly string? file;

    IntPtr asset;

    internal Asset(string name, string? file)
    {
        Name = name;
        this.file = file;
    }

    internal Asset(string name, IntPtr gameAsset)
    {
        Name = name;
        asset = gameAsset;
    }

    internal IntPtr GameAsset => asset;

    internal IntPtr Pointer => directTexture != IntPtr.Zero ? IntPtr.Zero : Volatile.Read(ref asset);

    IntPtr directTexture;

    internal bool IsDirect => directTexture != IntPtr.Zero;

    IntPtr Resolved()
    {
        if (directTexture != IntPtr.Zero) return directTexture;
        if (Volatile.Read(ref asset) == IntPtr.Zero && Host.Native()->IsGameThread() != 0) Wait();
        return asset;
    }

    public static bool operator ==(Asset<T>? left, Asset<T>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        IntPtr a = left.Resolved(), b = right.Resolved();
        if (a == IntPtr.Zero || b == IntPtr.Zero) return false;
        if (left.IsDirect == right.IsDirect) return a == b;
        // `T` e uma classe: a comparação e pelo endereco NATIVO do valor, nao
        // pela referencia gerenciada (cada `Value` cria um proxy novo).
        return left.ValuePointer() == right.ValuePointer();
    }

    public static bool operator !=(Asset<T>? left, Asset<T>? right) => !(left == right);

    public override bool Equals(object? obj) => obj is Asset<T> other && this == other;

    public override int GetHashCode() => Name.GetHashCode();

    static readonly System.Collections.Generic.Dictionary<IntPtr, Asset<T>> ByGameAsset = new();

    internal static Asset<T> ForGameAsset(IntPtr gameAsset, string name)
    {
        lock (ByGameAsset)
        {
            if (ByGameAsset.TryGetValue(gameAsset, out Asset<T>? known)) return known;
            var wrapped = new Asset<T>(name, gameAsset);
            ByGameAsset[gameAsset] = wrapped;
            return wrapped;
        }
    }

    public static Asset<T> FromGame(IntPtr gameAsset) => gameAsset == IntPtr.Zero ? null! : ForGameAsset(gameAsset, NameOf(gameAsset));

    public static IntPtr ToGame(Asset<T>? asset)
    {
        if (asset is null) return IntPtr.Zero;
        if (asset.IsDirect) throw new InvalidOperationException($"asset {asset.Name}: this asset is a bare texture, not a game asset");
        asset.Wait();
        return asset.GameAsset;
    }

    internal static Asset<T> ForTexture(IntPtr texture)
    {
        IntPtr gameAsset = Host.Native()->AssetOfTexture(texture);
        if (gameAsset != IntPtr.Zero)
        {
            lock (ByGameAsset)
                if (ByGameAsset.TryGetValue(gameAsset, out Asset<T>? known)) return known;
            return ForGameAsset(gameAsset, NameOf(gameAsset));
        }
        return new Asset<T>("?", IntPtr.Zero) { directTexture = texture, asset = (IntPtr)1 };
    }

    static string NameOf(IntPtr gameAsset)
    {
        byte* field = Host.Utf8("<Name>k__BackingField");
        int offset;
        try { offset = Host.Native()->FieldOffset(*(IntPtr*)gameAsset, field); }
        finally { Host.FreeUtf8(field); }
        return offset >= 0 ? GameString.Read(*(IntPtr*)(gameAsset + offset)) ?? "?" : "?";
    }

    public string Name { get; }

    public AssetState State => Volatile.Read(ref asset) != IntPtr.Zero ? AssetState.Loaded : AssetState.NotLoaded;

    public bool IsLoaded => State == AssetState.Loaded;

    public bool IsDisposed => false;

    static int valueOffset = -1;

    public T Value
    {
        get
        {
            // Effect (shader) nao existe neste port: o jogo desenha com o
            // Unity por baixo e o dump nao tem compilador de HLSL nenhum.
            // Devolver um stub gerenciado mantem o mod vivo (ele so escreve
            // parametros e chama Apply) em vez de estourar em
            // GameObjectFactory. Ver Effect.TML.cs.
            if (IsEffectType(typeof(T))) return (T)(object)Effect.CreateManaged();
            Wait();
            return (T)GameObjectFactory.Create(typeof(T), ValuePointer());
        }
    }

    /// <summary>
    /// O tML permite <c>(Texture2D)ModContent.Request&lt;Texture2D&gt;(...)</c>.
    /// O mod compilado no PC chama <c>op_Explicit</c> em
    /// <c>Asset`1&lt;!0&gt;</c>; sem o operador o método inteiro não JITa.
    /// </summary>
    public static explicit operator T(Asset<T> asset) => asset.Value;

    /// <summary>
    /// O ponteiro nativo do objeto que o asset carrega (a textura, a fonte).
    /// Separate de <see cref="Value"/> porque a comparacao de assets precisa do
    /// endereco, nao de um proxy novo a cada chamada.
    /// </summary>
    IntPtr ValuePointer()
    {
        if (directTexture != IntPtr.Zero) return directTexture;
        return typeof(T) == typeof(Texture2D) ? Host.Native()->TextureAssetValue(asset) : *(IntPtr*)(asset + ValueOffset());
    }

    int ValueOffset()
    {
        if (valueOffset >= 0) return valueOffset;
        byte* name = Host.Utf8("Value");
        try { valueOffset = Host.Native()->FieldOffset(*(IntPtr*)asset, name); }
        finally { Host.FreeUtf8(name); }
        if (valueOffset < 0) throw new MissingFieldException($"asset {Name}: the game's Asset has no Value field");
        return valueOffset;
    }

    public void Wait()
    {
        if (Volatile.Read(ref asset) != IntPtr.Zero) return;
        // Effect: nao ha arquivo .xnb para carregar (o port nao tem runtime de
        // shader). Marca como carregado e sai; quem le o valor recebe o stub.
        if (IsEffectType(typeof(T)))
        {
            Volatile.Write(ref asset, (IntPtr)1);
            return;
        }
        NativeApi* api = Host.Native();
        if (api->IsGameThread() == 0)
            throw new InvalidOperationException($"asset {Name}: textures only load on the game thread (SetStaticDefaults, hooks, drawing), not while the mod loads");
        IntPtr loaded;
        // As fontes do 1.4 sao ReLogic.Graphics.DynamicSpriteFont; o SpriteFont
        // continua aceito porque o Asset<SpriteFont> antigo ainda existe no shim.
        if (IsFontType(typeof(T)))
        {
            if (file == null) throw new InvalidOperationException($"asset {Name}: no game font with this name");
            byte* f = Host.Utf8(file);
            try { loaded = api->RequestModFont(f); }
            finally { Host.FreeUtf8(f); }
            if (loaded == IntPtr.Zero) throw new InvalidOperationException($"asset {Name}: {file} is not a font the game can load (blfont/1, after the content is ready)");
            directTexture = loaded;
            Volatile.Write(ref asset, (IntPtr)1);
            return;
        }
        if (file != null)
        {
            byte* f = Host.Utf8(file), n = Host.Utf8(Name);
            try { loaded = api->RequestModTexture(f, n); }
            finally
            {
                Host.FreeUtf8(f);
                Host.FreeUtf8(n);
            }
            if (loaded == IntPtr.Zero) throw new InvalidOperationException($"asset {Name}: {file} is not an image the game can load");
        }
        else
        {
            byte* n = Host.Utf8(Name);
            int missing = 0;
            try { loaded = api->RequestVanillaTexture(n, &missing); }
            finally { Host.FreeUtf8(n); }
            if (missing != 0) throw new InvalidOperationException($"asset {Name}: the game image does not exist on mobile");
            if (loaded == IntPtr.Zero) throw new InvalidOperationException($"asset {Name}: no game texture with this name");
        }
        Volatile.Write(ref asset, loaded);
    }

    /// <summary>
    /// <c>Asset&lt;DynamicSpriteFont&gt;</c> (tML 1.4.4.9) e
    /// <c>Asset&lt;SpriteFont&gt;</c> (resto do shim) carregam fonte.
    /// </summary>
    internal static bool IsFontType(Type type) =>
        type == typeof(SpriteFont) || type == typeof(ReLogic.Graphics.DynamicSpriteFont);

    /// <summary>
    /// <c>Asset&lt;Effect&gt;</c> e o que o Luminance pede em
    /// <c>ShaderManager.LoadShaders</c>. O tipo existe no shim (stub), entao o
    /// pedido passa a ser aceito e o valor sai de <see cref="Effect.CreateManaged"/>.
    /// </summary>
    internal static bool IsEffectType(Type type) => type == typeof(Effect);

    internal static void CheckSupported()
    {
        if (typeof(T) != typeof(Texture2D) && !IsFontType(typeof(T)) && !IsEffectType(typeof(T)))
            throw new NotSupportedException($"Asset<{typeof(T).Name}>: only Texture2D, a font (DynamicSpriteFont/SpriteFont) and Effect can be requested");
    }
}
