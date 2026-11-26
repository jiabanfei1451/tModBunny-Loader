using System;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Terraria.ModLoader.Core;

internal static class TownShopHooks
{
    internal static void Install()
    {
        PageIconHooks.Install();
        // Upstream 0.5.13: troco/devolucao na loja (On_GUIShop.Draw).
        ShopSavingsHooks.Install();
        ItemHooks.Once("TownShops", () => On_InventoryStorage.SetupShop += SetupShop);
    }
    internal static void InstallTravel() => ItemHooks.Once("TownTravelShop", () => On_InventoryStorage.SetupTravelShop += Travel);

    internal static void ContentReady()
    {
        NPCShopDatabase.Initialize();
    }

    static void Travel(On_InventoryStorage.orig_SetupTravelShop orig)
    {
        orig();
        var native = Main.travelShop;
        int[] items = new int[native.Length];
        int slot = 0;
        for (int i = 0; i < items.Length; i++) items[i] = native[i];
        while (slot < items.Length && items[slot] != 0) slot++;
        NPCLoader.SetupTravelShop(items, ref slot);
        for (int i = 0; i < items.Length; i++) native[i] = items[i];
    }

    static void SetupShop(On_InventoryStorage.orig_SetupShop orig, InventoryStorage storage, int type)
    {
        orig(storage, type);
        if (!NPCShopDatabase.ByIndex.TryGetValue(type, out AbstractNPCShop? shop))
        {
            Log.Info($"[shop] SetupShop tipo={type} (sem loja registrada nesse indice)");
            return;
        }
        try
        {
            FillStorage(storage, shop);
            Log.Info($"[shop] SetupShop tipo={type} loja={shop.FullName} entradas={EntryCount(shop)} itens={NPCShopDatabase.Count(storage)}");
        }
        catch (Exception e) { ItemHooks.Report($"NPC shop {shop.FullName}", e); }
    }

    /**
     * Enche a gaveta da loja com as entradas do mod. Fica exposto porque o
     * caminho do celular nao chama o SetupShop com o indice da loja de mod
     * (o Prepare precisa preencher sozinho, senao a janela abre vazia).
     */
    internal static void FillStorage(InventoryStorage storage, AbstractNPCShop shop)
    {
        if (shop is NPCShop ordinary) Fill(storage, ordinary);
        else FillCustom(storage, shop);
    }

    internal static int EntryCount(AbstractNPCShop shop) => shop is NPCShop ordinary ? ordinary.Entries.Count : -1;

    /**
     * O array do jogo tem 40+ gavetas, a loja do mod quase sempre tem menos
     * entradas: as gavetas que sobram ficam null e `items[i].IsNull` estourava
     * NullReferenceException (que o Report engolia, deixando a loja "meio"
     * preenchida). Enche tudo com Item vazio antes de qualquer coisa.
     */
    static Item[] EmptySlots(int length)
    {
        Item[] items = new Item[length];
        for (int i = 0; i < length; i++) items[i] = NPCShop.Entry.Create(0);
        return items;
    }

    static void FillCustom(InventoryStorage storage, AbstractNPCShop shop)
    {
        var native = storage.item;
        NPC npc = TownNPCHooks.TalkingTo();
        if (npc.IsNull || npc.type != shop.NpcType) npc = default;
        Item[] items = EmptySlots(native.Length);
        shop.FillShop(items, npc, out _);
        for (int i = 0; i < native.Length; i++)
        {
            Item item = items[i];
            if (item == null || item.IsNull)
            {
                item = NPCShop.Entry.Create(0);
            }
            item.isAShopItem = true;
            native[i] = item;
        }
    }

    static void Fill(InventoryStorage storage, NPCShop shop)
    {
        var native = storage.item;
        NPC npc = TownNPCHooks.TalkingTo();
        if (npc.IsNull || npc.type != shop.NpcType) npc = default;
        if (shop.Vanilla && !shop.Modified)
        {
            shop.Modified = true;
            for (int i = 0; i < native.Length; i++)
            {
                Item item = native[i];
                if (item == null || item.IsNull || item.type <= 0) continue;
                shop.Add(new NPCShop.Entry(item.Clone()) { Native = true });
            }
            NPCLoader.ModifyShop(shop);
            PylonHooks.AddShopEntries(shop);
            shop.FinishSetup();
        }
        Item[] items = EmptySlots(native.Length);
        int slot = 0, limit = Math.Max(0, items.Length - (shop.FillLastSlot ? 0 : 1));
        if (shop.Vanilla)
        {
            for (int i = 0; i < native.Length && slot < limit; i++)
            {
                Item item = native[i];
                if (item == null || item.IsNull || item.type <= 0) continue;
                NPCShop.Entry? entry = null;
                foreach (NPCShop.Entry candidate in shop.Entries) if (candidate.Native && candidate.Item.type == item.type) { entry = candidate; break; }
                if (entry != null)
                {
                    if (entry.Disabled || !entry.ConditionsMet()) continue;
                    item = entry.Item.Clone();
                    native[slot] = item;
                    entry.OnShopOpen(item, npc);
                }
                native[slot] = item;
                items[slot++] = item;
            }
        }
        foreach (NPCShop.Entry entry in shop.Entries)
        {
            if (entry.Native || entry.Disabled) continue;
            bool available = entry.ConditionsMet();
            if (!available && !entry.SlotReserved) continue;
            if (slot >= limit) { Log.Warn($"NPC shop {shop.FullName}: item {entry.Item.type} exceeds the mobile inventory capacity"); break; }
            Item item = available ? entry.Item.Clone() : NPCShop.Entry.Create(0);
            native[slot] = item;
            if (available) entry.OnShopOpen(item, npc);
            items[slot++] = item;
        }
        if (!npc.IsNull) NPCLoader.ModifyActiveShop(npc, shop.FullName, items);
        int filled = 0;
        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item == null || item.IsNull) item = NPCShop.Entry.Create(0);
            if (item.type > 0) filled++;
            item.isAShopItem = true;
            native[i] = item;
        }
        Log.Info($"[shop] fill {shop.FullName}: gavetas={native.Length} preenchidas={filled} entradas={EntryCount(shop)}");
    }
}
