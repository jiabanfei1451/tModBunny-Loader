using System;
using System.IO;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent.Bestiary;
using Terraria.ID;

namespace Terraria.ModLoader.Core;

internal static class BestiaryHooks
{
    internal static void ContentReady()
    {
        BestiaryDatabase database = Main.BestiaryDB;
        if (database.IsNull) return;
        int added = 0;
        for (int type = NPCID.Count; type < NPCLoader.NPCCount; type++)
        {
            if (NPCLoader.GetNPC(type) is not ModNPC own) continue;
            try
            {
                if (Register(database, own)) added++;
            }
            catch (Exception e) { ItemHooks.Report($"{own.FullName} bestiary entry", e); }
        }
        NPCID.Sets.NPCBestiaryDrawOffset.Flush();
        bool globals = NPCLoader.AnyGlobal(NPCMethod.SetBestiary);
        bool loot = NPCLoader.AnyGlobal(NPCMethod.ModifyNPCLoot) || NPCLoader.AnyGlobal(NPCMethod.ModifyGlobalLoot);
        if (globals || loot)
        {
            for (int type = 1; type < NPCID.Count; type++)
            {
                try { Vanilla(database, type, globals, loot); }
                catch (Exception e) { ItemHooks.Report($"bestiary entry of NPC {type}", e); }
            }
        }
        BiomeBestiaryHooks.RegisterFilters(database);
        if (added == 0 && !globals && !loot) return;
        ContentSamples.CreateBestiarySortingIds(database);
        Log.Info($"bestiary: {added} mod NPC entr{(added == 1 ? "y" : "ies")}");
    }

    // v9: `ContentSamples.NpcsByNetId` pode estar VAZIO neste ponto da carga
    // (o dicionario nativo so' e' preenchido depois de ContentSamples.ItemsByType,
    // e `new GameDictionary(pointer)` com ponteiro nulo estoura NRE). A excecao
    // subia ate' o catch externo de ContentReady e virava ~160 linhas
    // "bestiary entry of NPC N threw" por mundo carregado.
    static NPC? Sample(int type)
    {
        try
        {
            GameDictionary samples = new(ContentSamples.NpcsByNetId);
            if (samples.IsNull) return null;
            return samples.TryGetValue(type, out IntPtr npc) && npc != IntPtr.Zero ? new NPC(npc) : null;
        }
        catch { return null; }
    }

    static BestiaryEntry Find(BestiaryDatabase database, int type)
    {
        try
        {
            BestiaryEntry entry = database.FindEntryByNPCID(type);
            if (entry is null || entry.IsNull) return default;
            return IndexOf(database, entry) >= 0 ? entry : default;
        }
        catch { return default; }
    }

    static int IndexOf(BestiaryDatabase database, BestiaryEntry entry)
    {
        var entries = new GameList(database._entries);
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] == entry.Pointer) return i;
        return -1;
    }

    static bool Register(BestiaryDatabase database, ModNPC own)
    {
        int type = own.Type;
        BestiaryEntry generic = Find(database, type);
        // `Find` devolve `default` (referencia nula) quando o NPC nao tem
        // entrada generica; chamar `.IsNull` nesse null estourava NRE e
        // derrubava o registro do NPC do mod inteiro.
        if (generic is not null && !generic.IsNull) new GameList(database._entries).RemoveAt(IndexOf(database, generic));
        if (NPCID.Sets.NPCBestiaryDrawOffset.TryGetValue(type, out var modifiers) && modifiers.Hide) return false;
        ContentSamples.NpcBestiaryRarityStars.Reapply(type);
        NPC? sample = Sample(type);
        BestiaryEntry entry;
        if (sample is { IsNull: false } n && n.townNPC) entry = BestiaryEntry.TownNPC(type);
        else if (NPCID.Sets.CountsAsCritter[type]) entry = BestiaryEntry.Critter(type);
        else entry = BestiaryEntry.Enemy(type);
        BiomeBestiaryHooks.AddTo(entry, (sample is { IsNull: false } live ? NPCLoader.InstanceOf(live) : null) ?? own);
        NPCLoader.SetBestiary(sample, own, database, entry);
        Icon(own, entry);
        database.Register(entry);
        database.ExtractDropsForNPC(Main.ItemDropsDB, type);
        return true;
    }

    static void Icon(ModNPC own, BestiaryEntry entry)
    {
        string? path = NPCID.Sets.NPCBestiaryDrawOffset.ModTexture(own.Type);
        if (path == null) return;
        string file;
        try { file = ItemLoader.ResolveTexture(own, path); }
        catch (FileNotFoundException)
        {
            Log.Warn($"{own.FullName}: CustomTexturePath '{path}' is not in the mod");
            return;
        }
        var asset = new Asset<Texture2D>(path, file);
        _ = asset.Value;
        if (asset.GameAsset == IntPtr.Zero || entry.Icon.IsNull) return;
        new UnlockableNPCEntryIcon(entry.Icon.Pointer)._customTexture = asset;
    }

    static void Vanilla(BestiaryDatabase database, int type, bool globals, bool loot)
    {
        BestiaryEntry entry = Find(database, type);
        if (entry is null || entry.IsNull) return;
        // v71: Sample() devolve null quando o NPC ainda nao esta' na tabela
        // NpcsByNetId (acontece na carga antes do conteudo fechar). Um
        // sample.IsNull em null estourava NRE para TODOS os NPCs vanilla —
        // eram as 159 linhas "bestiary entry of NPC N threw".
        NPC? sample = Sample(type);
        if (sample is null || sample.IsNull) return;
        if (globals)
        {
            try { NPCLoader.SetBestiary(sample, null, database, entry); }
            catch (Exception e) { ItemHooks.Report($"bestiary entry of NPC {type} (SetBestiary)", e); }
        }
        if (!loot) return;
        // `entry.Info` devolve um RETRATO gerenciado: um RemoveAll nele não
        // tira nada da lista nativa (era código morto) e ainda passava um
        // proxy nulo para `IsInstance`. Aqui vai o wrapper ao vivo.
        try { entry.InfoList.RemoveAll(static element => IsItemDrop(element)); }
        catch (Exception e) { ItemHooks.Report($"bestiary entry of NPC {type} (info)", e); }
        try
        {
            if (Main.ItemDropsDB is { IsNull: false } drops) database.ExtractDropsForNPC(drops, type);
        }
        catch (Exception e) { ItemHooks.Report($"bestiary entry of NPC {type} (drops)", e); }
    }

    static bool IsItemDrop(IBestiaryInfoElement element) =>
        element is { IsNull: false } && ItemDropBestiaryInfoElement.Is(element);
}
