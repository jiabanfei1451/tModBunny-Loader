using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Terraria.GameContent;
using Terraria.GameContent.Personalities;

namespace Terraria.ModLoader;

public readonly struct NPCHappiness
{
    public readonly int NpcType;
    NPCHappiness(int npcType) => NpcType = npcType;
    public static NPCHappiness Get(int npcType) => new(npcType);

    public NPCHappiness SetNPCAffection<T>(AffectionLevel affectionLevel) where T : ModNPC =>
        SetNPCAffection(ModContent.GetInstance<T>().Type, affectionLevel);

    public NPCHappiness SetNPCAffection(int npcId, AffectionLevel affectionLevel)
    {
        // v69: no Android o ShopHelper so e criado quando se entra num mundo; o
        // SetStaticDefaults dos NPCs roda bem antes (NPCLoader.Installed) e o
        // getter devolve null — o acesso a _database estourava NRE (era o erro
        // do LumberJack/Squirrel). Sem mundo ainda nao ha perfil de felicidade
        // para gravar: registramos de volta quando o helper existir (tentativa
        // na proxima chamada).
        ShopHelper helper = Main.ShopHelper;
        if (helper is null || helper.IsNull)
        {
            Log.Warn($"SetNPCAffection({npcId}): ShopHelper indisponivel durante SetupContent; ignorado");
            return this;
        }
        var database = helper._database;
        if (database is null || database.IsNull)
        {
            Log.Warn($"SetNPCAffection({npcId}): banco de felicidade ainda nao criado; ignorado");
            return this;
        }
        var profile = database.GetByNPCID(NpcType);
        if (!profile.IsNull && profile != database._trashEntry)
        {
            var list = new GameList(profile.ShopModifiers);
            for (int i = 0; i < list.Count; i++)
            {
                var value = new GameObject(list.Get<IntPtr>(i));
                if (!NPCPreferenceTrait.Is(value)) continue;
                var entry = new NPCPreferenceTrait(value.Pointer);
                if (entry.NpcId != npcId) continue;
                if (affectionLevel == 0) list.RemoveAt(i);
                else entry.Level = affectionLevel;
                return this;
            }
        }
        if (affectionLevel == 0) return this;
        var trait = NPCPreferenceTrait.New();
        trait.NpcId = npcId;
        trait.Level = affectionLevel;
        database.Register(NpcType, trait);
        return this;
    }

    public NPCHappiness SetBiomeAffection<T>(AffectionLevel affectionLevel) where T : notnull
    {
        if (typeof(ModBiome).IsAssignableFrom(typeof(T)))
        {
            ModBiome? modBiome = BiomeLoader.ByType(typeof(T));
            if (modBiome == null)
            {
                Log.Warn($"SetBiomeAffection<{typeof(T).Name}>: ModBiome nao registrado; ignorado");
                return this;
            }
            return SetBiomeAffection(modBiome, affectionLevel);
        }
        var factory = typeof(T).GetMethod("New", Type.EmptyTypes);
        if (factory == null)
        {
            // v69: bioma do contrato tML 1.4.4.9 que nao existe no Android 1.4.5.8.6
            // (ex.: SkyBiome). Antes isto estourava ArgumentException e derrubava o
            // SetStaticDefaults inteiro do NPC ("SkyBiome is not a native...") .
            Log.Warn($"SetBiomeAffection<{typeof(T).Name}>: bioma nao existe neste jogo; ignorado");
            return this;
        }
        if (factory.Invoke(null, null) is not IGameObject biome) throw new InvalidOperationException("Shopping biome construction failed");
        if (!AShoppingBiome.Is(new GameObject(biome.Pointer))) throw new ArgumentException($"{typeof(T).Name} is not a native shopping biome");
        return SetBiomeAffection(new AShoppingBiome(biome.Pointer), affectionLevel);
    }

    public NPCHappiness SetBiomeAffection(ModBiome biome, AffectionLevel affectionLevel) =>
        SetBiomeAffection(BiomeLoader.ShoppingOf(biome), affectionLevel);

    /// <summary>NPCs que ja tiveram a falha de afinidade reportada (ver o catch).</summary>
    static readonly System.Collections.Generic.HashSet<int> warned = new();
    static int silenced;

    public unsafe NPCHappiness SetBiomeAffection(AShoppingBiome biome, AffectionLevel affectionLevel)
    {
        // v75: o Mono entrega UM quadro de pilha, entao "NullReferenceException em
        // SetBiomeAffection" nao dizia onde. Cada passo grava o seu nome e o catch
        // devolve qual deles estourou. E nao relanca: essa chamada acontece dentro
        // do SetStaticDefaults de um NPC de cidade, e a excecao levava junto o
        // resto da inicializacao dele (loja, dialogo, arquetipo).
        string stage = "inicio";
        try
        {
            stage = "biome.IsNull";
            if (biome.IsNull) throw new ArgumentException("Shopping biome cannot be null", nameof(biome));
            stage = "Main.ShopHelper";
            ShopHelper helper = Main.ShopHelper;
            if (helper is null || helper.IsNull)
            {
                Log.Warn($"SetBiomeAffection({NpcType}): ShopHelper indisponivel durante SetupContent; ignorado");
                return this;
            }
            stage = "helper._database";
            var database = helper._database;
            if (database is null || database.IsNull)
            {
                Log.Warn($"SetBiomeAffection({NpcType}): banco de felicidade ainda nao criado; ignorado");
                return this;
            }
            stage = "GetByNPCID";
            var profile = database.GetByNPCID(NpcType);
            BiomePreferenceListTrait trait = default;
            if (!profile.IsNull && profile != database._trashEntry)
            {
                stage = "perfil: ShopModifiers";
                var list = new GameList(profile.ShopModifiers);
                for (int i = 0; i < list.Count; i++)
                {
                    var value = new GameObject(list.Get<IntPtr>(i));
                    if (!BiomePreferenceListTrait.Is(value)) continue;
                    stage = "perfil: _preferences";
                    var candidate = new BiomePreferenceListTrait(value.Pointer);
                    var preferences = new GameList(candidate._preferences);
                    for (int j = 0; j < preferences.Count; j++)
                    {
                        stage = "perfil: comparar bioma";
                        var entry = new BiomePreferenceListTrait.BiomePreference(preferences.Get<IntPtr>(j));
                        if (entry.Biome.IsNull) continue;
                        bool mod = BiomeLoader.OfShopping(entry.Biome.Pointer) != null || BiomeLoader.OfShopping(biome.Pointer) != null;
                        if (mod ? entry.Biome.Pointer != biome.Pointer : *(IntPtr*)entry.Biome.Pointer != *(IntPtr*)biome.Pointer) continue;
                        stage = "perfil: gravar";
                        if (affectionLevel == 0) preferences.RemoveAt(j);
                        else entry.Affection = affectionLevel;
                        return this;
                    }
                    if (trait.IsNull) trait = candidate;
                }
            }
            if (affectionLevel == 0) return this;
            if (trait.IsNull)
            {
                stage = "criar trait";
                trait = BiomePreferenceListTrait.New();
                stage = "registrar trait";
                database.Register(NpcType, trait);
            }
            stage = "trait.Add";
            trait.Add(affectionLevel, biome);
            return this;
        }
        catch (Exception e)
        {
            // v113: cada NPC que pedia afinidade de bioma gravava a mesma falha
            // varias vezes (1600 linhas de "NullReferenceException @GetByNPCID"
            // na carga do v112, num log de 4.7 MB). O primeiro aviso de cada
            // NPC basta: a repeticao nao traz informacao e o custo da excecao
            // aparece na hora do carregamento.
            if (warned.Add(NpcType))
            {
                Log.Warn($"SetBiomeAffection({NpcType}) @{stage}: {e.GetType().Name}: {e.Message}; ignorado (repeticoes deste NPC ficam em silencio)");
            }
            else if (++silenced % 200 == 0)
            {
                Log.Info($"SetBiomeAffection: {silenced} repeticao(oes) silenciada(s); {warned.Count} NPC(s) afetado(s)");
            }
            return this;
        }
    }
}
