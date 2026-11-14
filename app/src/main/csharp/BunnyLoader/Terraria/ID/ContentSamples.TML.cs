using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Terraria.ModLoader;

namespace Terraria.ID;

public static partial class ContentSamples
{
    public static BunnyLoader.Interop.GameString BestiaryCreditId(int npcNetId)
    {
        new BunnyLoader.Interop.GameDictionary(NpcBestiaryCreditIdsByNpcNetIds).TryGetValue(npcNetId, out BunnyLoader.Interop.GameString id);
        return id;
    }

    public static BestiaryRarityStars NpcBestiaryRarityStars { get; } = new();

    public static ProjectileSamples ProjectilesByType { get; } = new();

    /// <summary>
    /// CONTRATO 1.4.4.9 (<c>patches/tModLoader/Terraria/ID/ContentSamples.cs.patch:95</c>):
    /// <c>public static Dictionary&lt;int, Item&gt; ItemsByType</c> — um CAMPO cujo
    /// tipo é o <c>Dictionary</c> gerenciado.
    ///
    /// Antes disto a propriedade escrita à mão devolvia <c>ItemSamples</c>, uma
    /// classe SOLTA com um indexador. O IlPatcher reescreve o <c>ldsfld</c> do
    /// mod para <c>call get_ItemsByType()</c>, mas o <c>callvirt</c> seguinte
    /// continua sendo <c>Dictionary&lt;int,Item&gt;::get_Item</c>. Mono não
    /// verifica o tipo do <c>this</c>: ele executava o código do Dictionary
    /// sobre um objeto que só tem UM campo (<c>samples</c>), lia esse campo
    /// como se fosse <c>_buckets</c> e estourava
    /// <c>IndexOutOfRangeException</c> em <c>Dictionary.FindValue</c>.
    /// Resultado no aparelho: <c>Fargowiltas.Load()</c> morre em IL_0022,
    /// <c>ModLoaded</c> / keybinds / configs / hooks nunca são registrados e
    /// depois o jogo cai com SIGSEGV nativo.
    ///
    /// A correção tem duas metades: (1) devolver o tipo exato do contrato,
    /// (2) fazer <c>ItemSamples</c> HERDAR de <c>Dictionary&lt;int,Item&gt;</c>
    /// e o getter preencher a tabela antes de devolvê-la, para que
    /// <c>get_Item</c> e <c>GetEnumerator</c> (não virtuais, chamados
    /// diretamente pelo mod) encontrem entradas de verdade.
    /// </summary>
    static readonly ItemSamples itemSamples = new();

    public static Dictionary<int, Item> ItemsByType
    {
        get
        {
            itemSamples.Refresh();
            return itemSamples;
        }
    }

    /// <summary>
    /// Tabela viva de amostras de item. Herda de <c>Dictionary</c> porque o
    /// mod chama os membros NÃO VIRTUAIS do Dictionary diretamente
    /// (<c>get_Item</c> em <c>Fargowiltas.Load</c>, <c>GetEnumerator</c> em
    /// <c>Squirrel.GetSellableItems</c>). Um indexador <c>new</c> não serviria:
    /// o token do <c>callvirt</c> aponta para o método da classe base.
    ///
    /// <c>Refresh()</c> copia os ponteiros dos <c>Item</c> que o jogo já criou
    /// em <c>ContentSamples.ItemsByType</c> (campo estático nativo exposto como
    /// <c>ItemsByType_</c>). Não aloca 6 mil objetos nativos: só cria amostra
    /// nova para IDs de mod, que a tabela nativa não conhece.
    /// </summary>
    public sealed class ItemSamples : Dictionary<int, Item>
    {
        readonly object gate = new();
        int filled;
        bool warnedNativeMissing;

        internal void Refresh()
        {
            int count = ItemLoader.ItemCount;
            if (count <= filled) return;
            lock (gate)
            {
                if (count <= filled) return;
                GameDictionary native = default;
                bool useNative = false;
                try
                {
                    native = new GameDictionary(ItemsByType_);
                    useNative = !native.IsNull && native.TryGetReference(0, out _);
                }
                catch (Exception e)
                {
                    Log.Warn($"ContentSamples.ItemsByType: tabela nativa indisponivel ({e.GetType().Name}); criando amostras gerenciadas");
                }
                if (!useNative && !warnedNativeMissing)
                {
                    warnedNativeMissing = true;
                    Log.Warn("ContentSamples.ItemsByType: ItemsByType_ vazio; amostras serao criadas sob demanda");
                }

                for (int type = filled; type < count; type++)
                {
                    Item item;
                    if (useNative && native.TryGetReference(type, out IntPtr ptr) && ptr != IntPtr.Zero)
                        item = new Item(ptr);
                    else
                        item = new Item(type);
                    base[type] = item;
                }
                filled = count;
            }
        }
    }

    public sealed class ProjectileSamples
    {
        readonly Dictionary<int, Projectile> samples = new();
        readonly List<GameHandle> kept = new();

        public Projectile this[int type] => TryGetValue(type, out Projectile sample) ? sample : throw new KeyNotFoundException($"ProjectilesByType has no projectile {type}");

        public bool ContainsKey(int type) => type > 0 && type < ProjectileLoader.ProjectileCount;

        public bool TryGetValue(int type, out Projectile sample)
        {
            sample = default;
            if (!ContainsKey(type)) return false;
            lock (samples)
            {
                if (samples.TryGetValue(type, out sample)) return true;
                sample = Projectile.New();
                kept.Add(new GameHandle(sample.Pointer));
                sample.SetDefaults(type);
                samples[type] = sample;
                return true;
            }
        }
    }

    public sealed class BestiaryRarityStars
    {
        readonly Dictionary<int, int> assigned = new();

        static GameDictionary Table => new(NpcBestiaryRarityStars_);

        public int this[int npcNetId]
        {
            get => Table.TryGetValue(npcNetId, out int stars) ? stars : throw new KeyNotFoundException($"NpcBestiaryRarityStars has no entry for NPC {npcNetId}");
            set
            {
                lock (assigned) assigned[npcNetId] = value;
                Table.Set(npcNetId, value);
            }
        }

        public bool ContainsKey(int npcNetId) => Table.ContainsKey(npcNetId);

        public bool TryGetValue(int npcNetId, out int stars) => Table.TryGetValue(npcNetId, out stars);

        internal void Reapply(int npcNetId)
        {
            int stars;
            lock (assigned)
                if (!assigned.TryGetValue(npcNetId, out stars)) return;
            Table.Set(npcNetId, stars);
        }
    }
}
