using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Terraria.ID;
using Terraria.ModLoader.Core;

namespace Terraria.ModLoader;

public static class NPCShopDatabase
{
    static readonly Dictionary<string, AbstractNPCShop> Shops = new(StringComparer.Ordinal);
    internal static readonly Dictionary<int, AbstractNPCShop> ByIndex = new();
    static int nextIndex = 99;
    /** Ligado enquanto o AddShops de um NPC de mod roda (ver AbstractNPCShop.AutoRegister). */
    internal static bool RegisteringShops;
    public static IEnumerable<AbstractNPCShop> AllShops => Shops.Values;
    public static readonly ISet<string> NoPylons = new HashSet<string>
    {
        GetShopName(ID.NPCID.TravellingMerchant), GetShopName(ID.NPCID.SkeletonMerchant), GetShopName(ID.NPCID.DD2Bartender), GetShopName(ID.NPCID.SantaClaus)
    };
    public static string GetShopName(int npcType, string shopName = "Shop") =>
        $"{(NPCLoader.GetNPC(npcType) is ModNPC own ? own.FullName : "Terraria/" + NPCID.Search.GetName(npcType))}/{shopName}";

    public static AbstractNPCShop GetShop(string fullName) => Shops[fullName];
    public static bool TryGetNPCShop(string fullName, out NPCShop shop)
    {
        shop = Shops.TryGetValue(fullName, out AbstractNPCShop? found) ? found as NPCShop ?? null! : null!;
        return shop != null;
    }

    public static void AddShop(AbstractNPCShop shop)
    {
        if (shop == null) throw new ArgumentNullException(nameof(shop));
        Log.Info($"[shop] AddShop entrou: {shop.GetType().Name} npc={shop.NpcType} nome='{shop.Name}' idx={nextIndex}");
        if (Shops.ContainsKey(shop.FullName))
        {
            // O registro automatico pode ja ter feito isso; o Register() que o mod
            // chama depois nao pode derrubar o AddShops inteiro.
            Log.Info($"[shop] {shop.FullName} ja registrada, ignorando");
            return;
        }
        if (nextIndex <= 25) throw new InvalidOperationException("Mobile NPC shop slots are exhausted");
        shop.Index = nextIndex--;
        Shops.Add(shop.FullName, shop);
        ByIndex.Add(shop.Index, shop);
        TownShopHooks.Install();
    }

    internal static void Initialize()
    {
        Core.PlaceDiag.Install();
        Core.PlacementFallback.Install();
        if (ByIndex.ContainsKey(1)) return;
        int[] types = { 17, 19, 20, 38, 54, 107, 108, 124, 142, 160, 178, 207, 208, 209, 227, 228, 229, 353, 368, 453, 550, 588, 633, 663, 227 };
        for (int i = 0; i < types.Length; i++)
        {
            var shop = new NPCShop(types[i], i == 24 ? "Decor" : "Shop") { Index = i + 1, Vanilla = true };
            Shops.Add(shop.FullName, shop);
            ByIndex.Add(shop.Index, shop);
        }
        Log.Info($"[shop] init: NPCCount={NPCLoader.NPCCount} vanilla={Shops.Count}");
        for (int type = 697; type < NPCLoader.NPCCount; type++) NPCLoader.AddShops(type);
        List<string> names = new();
        foreach (AbstractNPCShop known in Shops.Values) names.Add($"{known.Index}:{known.FullName}");
        Log.Info($"[shop] registradas={names.Count} -> {string.Join(" | ", names)}");
        foreach (AbstractNPCShop custom in ByIndex.Values)
        {
            if (custom is not NPCShop shop)
            {
                custom.FinishSetup();
                continue;
            }
            if (shop.Vanilla) continue;
            NPCLoader.ModifyShop(shop);
            Core.PylonHooks.AddShopEntries(shop);
            shop.Modified = true;
            shop.FinishSetup();
        }
    }

    public static void OpenShop(string fullName)
    {
        Log.Info($"[shop] OpenShop('{fullName}') registradas={Shops.Count}");
        AbstractNPCShop? shop = Find(fullName);
        if (shop == null || shop.Index < 0)
        {
            Log.Error($"[shop] '{fullName}' nao registrada. Registradas com esse NPC: {Candidates(fullName)}");
            return;
        }
        Log.Info($"[shop] abrir {shop.FullName} idx={shop.Index} (pedido '{fullName}')");
        Prepare(shop);
        try { Main.instance.OpenShop(shop.Index); }
        catch (Exception e) { Log.Error($"[shop] Main.OpenShop({shop.Index}) falhou: {e.GetType().Name}: {e.Message}"); }
        // No celular a pagina de loja nao le o que passamos para o OpenShop: ela
        // olha Main.npcShop (static). Se o OpenShop nao escreveu nele, escreve.
        int current = -2;
        try { current = Main.npcShop; } catch { }
        if (current != shop.Index)
        {
            try
            {
                Main.SetNPCShopIndex(shop.Index);
                current = Main.npcShop;
            }
            catch (Exception e) { Log.Error($"[shop] SetNPCShopIndex({shop.Index}) falhou: {e.GetType().Name}: {e.Message}"); }
        }
        Log.Info($"[shop] npcShop={current} esperado={shop.Index}");
        if (!GUIInstance.Active.IsNull) GUIInstance.Active.GUIPageIcons.OpenUI((GUIPageIcons.Category)2, (GUIPageIcons.Category)4);
        else Log.Warn("[shop] GUIInstance.Active nulo: pagina de loja nao aberta");
    }

    /**
     * O nome que o mod usa no OnChatButtonClicked nem sempre bate com o nome com
     * que a loja foi registrada (o tML aceita "Shop" implicito, o mod pode
     * devolver o nome curto, e o nome do NPC pode vir com maiuscula diferente).
     * Procura exato, depois pelo par NPC + nome curto.
     */
    internal static AbstractNPCShop? Find(string fullName)
    {
        if (Shops.TryGetValue(fullName, out AbstractNPCShop? exact)) return exact;
        int slash = fullName.LastIndexOf('/');
        if (slash <= 0) return null;
        string owner = fullName[..slash], shortName = fullName[(slash + 1)..];
        foreach (AbstractNPCShop shop in Shops.Values)
        {
            if (!string.Equals(shop.Name, shortName, StringComparison.OrdinalIgnoreCase)) continue;
            if (shop.FullName.StartsWith(owner + "/", StringComparison.OrdinalIgnoreCase)) return shop;
        }
        return null;
    }

    /** Amostra das lojas do mesmo NPC, para o log dizer o que existe de verdade. */
    static string Candidates(string fullName)
    {
        int slash = fullName.LastIndexOf('/');
        string owner = slash > 0 ? fullName[..slash] : fullName;
        List<string> names = new();
        foreach (AbstractNPCShop shop in Shops.Values)
            if (shop.FullName.StartsWith(owner + "/", StringComparison.OrdinalIgnoreCase)) names.Add(shop.FullName);
        return names.Count == 0 ? $"nenhuma (total {Shops.Count})" : string.Join(" | ", names);
    }

    /**
     * A loja de um NPC de mod mora num indice que o jogo nao conhece (99, 98, ...).
     * No celular a gaveta de cada loja (Main.shop[i]) e a InventoryStorage que o
     * SetupShop preenche, e quem abre pelo caminho normal do jogo passa por um
     * mapa fixo de NPC vanilla -> indice. Um indice de mod nao esta nesse mapa:
     * ninguem cria a gaveta nem chama o SetupShop dela, entao a janela abria
     * vazia (ou nao abria). Aqui a gaveta e criada e preenchida antes de abrir.
     */
    static void Prepare(AbstractNPCShop shop)
    {
        int index = shop.Index;
        try
        {
            GameRefArray<InventoryStorage> stores = Main.instance.shop;
            if (stores.IsNull)
            {
                Log.Warn($"[shop] Main.shop nulo: nao da para abrir {shop.FullName}");
                return;
            }
            Log.Info($"[shop] abrir {shop.FullName} idx={index} gavetas={stores.Length}");
            if (index < 0 || index >= stores.Length)
            {
                Log.Warn($"[shop] idx={index} fora das {stores.Length} gavetas");
                return;
            }
            InventoryStorage storage = stores[index];
            if (storage.IsNull)
            {
                storage = InventoryStorage.New();
                storage.maxItems = 40;
                storage.item = GameRefArray<Item>.New(Item.Class, 40);
                storage.FillWithEmptyInstances();
                stores[index] = storage;
                Log.Info($"[shop] gaveta {index} criada ({Count(storage)} itens)");
            }
            int before = Count(storage);
            // Nao da para contar com o jogo chamar SetupShop(96): no celular o
            // caminho da loja nao conhece indices de mod. Preenche aqui.
            if (before <= 0)
            {
                Core.TownShopHooks.FillStorage(storage, shop);
                Log.Info($"[shop] {shop.FullName} idx={index} entradas={Core.TownShopHooks.EntryCount(shop)} itens {before} -> {Count(storage)}");
            }
            else Log.Info($"[shop] {shop.FullName} idx={index} ja tinha {before} itens");
        }
        catch (Exception e) { ItemHooks.Report($"NPC shop open {shop.FullName}", e); }
    }

    /** Itens com type > 0 na gaveta; -1 quando a gaveta nem existe. */
    internal static int Count(InventoryStorage storage)
    {
        if (storage.IsNull) return -1;
        GameRefArray<Item> items = storage.item;
        if (items.IsNull) return -1;
        int n = 0;
        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item != null && !item.IsNull && item.type > 0) n++;
        }
        return n;
    }
}
