using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace Terraria.GameContent;

public static partial class TextureAssets
{
    static readonly TextureAssetArray __Tile = new(7, "Images/Tiles_", static () => Tile_, static i => Main.instance.LoadTiles(i));
    public static TextureAssetSlots<Asset<Texture2D>> Tile { get; } = new(static i => __Tile[i], static (i, v) => __Tile[i] = v, static () => __Tile.Length);

    static readonly TextureAssetArray __Item = new(0, "Images/Item_", static () => Item_, static i => Main.instance.LoadItem(i));
    public static TextureAssetSlots<Asset<Texture2D>> Item { get; } = new(static i => __Item[i], static (i, v) => __Item[i] = v, static () => __Item.Length);

    static readonly TextureAssetArray __Projectile = new(3, "Images/Projectile_", static () => Projectile_, static i => Main.instance.LoadProjectile(i));
    public static TextureAssetSlots<Asset<Texture2D>> Projectile { get; } = new(static i => __Projectile[i], static (i, v) => __Projectile[i] = v, static () => __Projectile.Length);

    static readonly TextureAssetArray __Npc = new(4, "Images/NPC_", static () => Npc_, static i => Main.instance.LoadNPC(i));
    public static TextureAssetSlots<Asset<Texture2D>> Npc { get; } = new(static i => __Npc[i], static (i, v) => __Npc[i] = v, static () => __Npc.Length);

    static readonly TextureAssetArray __Gore = new(5, "Images/Gore_", static () => Gore_, static i => Main.instance.LoadGore(i));
    public static TextureAssetSlots<Asset<Texture2D>> Gore { get; } = new(static i => __Gore[i], static (i, v) => __Gore[i] = v, static () => __Gore.Length);

    static readonly TextureAssetArray __NpcHead = new(6, "Images/NPC_Head_", static () => NpcHead_, static i => { });
    public static TextureAssetSlots<Asset<Texture2D>> NpcHead { get; } = new(static i => __NpcHead[i], static (i, v) => __NpcHead[i] = v, static () => __NpcHead.Length);

    static readonly TextureAssetArray __PlayerHair = new(1, "Images/Player_Hair_", static () => PlayerHair_, static i => Main.instance.LoadHair(i));
    public static TextureAssetSlots<Asset<Texture2D>> PlayerHair { get; } = new(static i => __PlayerHair[i], static (i, v) => __PlayerHair[i] = v, static () => __PlayerHair.Length);

    static readonly TextureAssetArray __PlayerHairAlt = new(2, "Images/Player_HairAlt_", static () => PlayerHairAlt_, static i => Main.instance.LoadHair(i));
    public static TextureAssetSlots<Asset<Texture2D>> PlayerHairAlt { get; } = new(static i => __PlayerHairAlt[i], static (i, v) => __PlayerHairAlt[i] = v, static () => __PlayerHairAlt.Length);

    static readonly TextureAssetArray __Background = new(8, "Images/Background_", static () => Background_, static i => Main.instance.LoadBackground(i));
    public static TextureAssetSlots<Asset<Texture2D>> Background { get; } = new(static i => __Background[i], static (i, v) => __Background[i] = v, static () => __Background.Length);

    static readonly TextureAssetArray __Cloud = new(9, "Images/Cloud_", static () => Cloud_, static i => { });
    public static TextureAssetSlots<Asset<Texture2D>> Cloud { get; } = new(static i => __Cloud[i], static (i, v) => __Cloud[i] = v, static () => __Cloud.Length);

    internal static void ResizeGore(int length)
    {
        if (Gore_.Length < length) Gore_ = Gore_.Resized(length);
    }

    internal static void ResizePlayerHair(int length)
    {
        if (PlayerHair_.Length < length) PlayerHair_ = PlayerHair_.Resized(length);
        if (PlayerHairAlt_.Length < length) PlayerHairAlt_ = PlayerHairAlt_.Resized(length);
    }
}

/// <summary>
/// Espelho de <c>Asset&lt;Texture2D&gt;[]</c> do PC. O nome e a forma sao de
/// proposito: o IlPatcher reconhece <c>AssetArray`1</c> como array do jogo,
/// entao o <c>ldelem.ref</c> do mod (<c>TextureAssets.Tile[type]</c>) e trocado
/// pelo acessador em vez de ler a memoria do objeto como se fosse array - era
/// isso que devolvia um Asset "de mentira" (campos lidos do layout nativo,
/// nome vazio) e estourava dentro de Asset&lt;T&gt;.Value.
/// </summary>
public sealed unsafe class TextureAssetSlots<T>
{
    readonly Func<int, T> get;
    readonly Action<int, T> set;
    readonly Func<int> length;

    internal TextureAssetSlots(Func<int, T> get, Action<int, T> set, Func<int> length)
    {
        this.get = get;
        this.set = set;
        this.length = length;
    }

    public int Length => length();

    public T this[int index]
    {
        get => get(index);
        set => set(index, value);
    }

    public static T Get(TextureAssetSlots<T> array, int index) => array.get(index);

    public static void Set(TextureAssetSlots<T> array, int index, T value) =>
        throw new NotSupportedException("TextureAssets is read-only");

    public static int GetLength(TextureAssetSlots<T> array) => array.length();
}

public sealed unsafe class TextureAssetArray
{
    static readonly Dictionary<(int, int), Asset<Texture2D>> Cache = new();

    readonly int id;
    readonly string prefix;
    readonly Func<GameArray<GameObject>> array;
    readonly Action<int> load;

    internal TextureAssetArray(int id, string prefix, Func<GameArray<GameObject>> array, Action<int> load)
    {
        this.id = id;
        this.prefix = prefix;
        this.array = array;
        this.load = load;
    }

    public int Length => array().Length;

    public Asset<Texture2D> this[int index]
    {
        get
        {
            GameArray<GameObject> items = array();
            if ((uint)index >= (uint)items.Length) throw new IndexOutOfRangeException($"{prefix}{index}: there are {items.Length} textures");
            IntPtr asset = items[index].Pointer;
            if (asset == IntPtr.Zero || Host.Native()->TextureAssetValue(asset) == IntPtr.Zero)
            {
                load(index);
                asset = array()[index].Pointer;
            }
            lock (Cache)
            {
                if (Cache.TryGetValue((id, index), out Asset<Texture2D>? cached) && cached.GameAsset == asset) return cached;
                Asset<Texture2D> wrapped = Asset<Texture2D>.ForGameAsset(asset, prefix + index);
                Cache[(id, index)] = wrapped;
                return wrapped;
            }
        }
        set
        {
            GameArray<GameObject> items = array();
            if ((uint)index >= (uint)items.Length) throw new IndexOutOfRangeException($"{prefix}{index}: there are {items.Length} textures");
            if (value.IsDirect) throw new InvalidOperationException($"{prefix}{index}: this asset is a bare texture, not a game asset");
            value.Wait();
            items[index] = new GameObject(value.GameAsset);
            lock (Cache) Cache[(id, index)] = value;
        }
    }
}
