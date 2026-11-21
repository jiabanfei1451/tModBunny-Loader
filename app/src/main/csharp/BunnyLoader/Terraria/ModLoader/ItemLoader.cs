using Terraria.DataStructures;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System;
using BunnyLoader.Interop;
using BunnyLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Creative;
using Terraria.GameContent.Items;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.Core;
using Terraria.ModLoader.IO;

namespace Terraria.ModLoader;

public static unsafe partial class ItemLoader
{
    static readonly object Gate = new();

    /// <summary>Tipos de item cuja linha de diagnóstico já foi escrita.</summary>
    static readonly HashSet<int> LoggedItems = new();

    /**
     * DIAGNOSTICO: os campos do Item parecem trocados (bait saindo igual a
     * makeNPC, consumivel ficando false). Compara os offsets que o runtime
     * resolve com os do dump (bait 0x3C, makeNPC 0x40, consumable 0x95, ammo 0x100).
     */
    static void LogOffsets()
    {
        try
        {
            static int Off(string campo)
            {
                int slot = -1;
                return BunnyLoader.Interop.GameInterop.Offset(ref slot, Item.Class, campo);
            }
            Log.Info($"[itemdiag] offsets: bait={Off("bait")} makeNPC={Off("makeNPC")} consumable={Off("consumable")} "
                     + $"ammo={Off("ammo")} createTile={Off("createTile")} stack={Off("stack")} type={Off("type")}");
        }
        catch (Exception e) { Log.Warn($"[itemdiag] offsets falharam: {e.GetType().Name}"); }
    }

    static ModItem?[] items = Array.Empty<ModItem?>();

    static int itemCount = ItemID.Count;

    public static int ItemCount => Volatile.Read(ref itemCount);

    static bool hooked;

    static readonly EntityInstances<ModItem> Instances = new();

    internal static readonly GlobalList<Item, GlobalItem> Globals = new();

    internal static void RegisterGlobal(GlobalItem global)
    {
        Globals.Add(global);
        EnsureHooked();
        ItemHooks.Install(global.GetType(), typeof(GlobalItem));
    }

    static void EnsureHooked()
    {
        lock (Gate)
        {
            if (hooked) return;
            On_Item.SetDefaults += SetDefaults;
            On_Item.Clone += Clone;
            On_Item.DeepClone += DeepClone;
            On_Item.clientClone += ClientClone;
            Host.Native()->OnItemsInstalled(&ItemsInstalled);
            Host.Native()->OnItemData(&SaveItemData, &LoadItemData);
            hooked = true;
        }
    }

    internal static ModItem? InstanceOf(Item item)
    {
        if (item.IsNull) return null;
        int type = item.type;
        ModItem? template = Get(type);
        if (template == null) return null;
        ModItem? current = Instances.Get(item.Pointer);
        return current != null && current.Type == type ? current : Bind(template, item);
    }

    static ModItem Bind(ModItem template, Item item)
    {
        ModItem instance = template.NewInstance(item);
        instance.Type = template.Type;
        // NewInstance já faz `Entity = item`; repetir aqui é barato e cobre o
        // caminho `CloneNewInstances` (MemberwiseClone pode trazer o Entity do
        // template, que é um rascunho nativo, não o item do jogo).
        instance.Entity = item;
        Instances.Set(item.Pointer, instance);
        return instance;
    }

    internal static void OnCreated(Item item, ItemCreationContext context)
    {
        if (item.IsNull) return;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modItem.OnCreated(context); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.OnCreated", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.OnCreated(item, context); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.OnCreated", e); }
        }
    }

    public static bool ReforgePrice(Item item, ref long reforgePrice, ref bool canApplyDiscount)
    {
        bool apply = true;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { apply &= modItem.ReforgePrice(ref reforgePrice, ref canApplyDiscount); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.ReforgePrice", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { apply &= global.ReforgePrice(item, ref reforgePrice, ref canApplyDiscount); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.ReforgePrice", e); }
        }
        return apply;
    }

    public static bool CanReforge(Item item)
    {
        bool can = true;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { can &= modItem.CanReforge(); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.CanReforge", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { can &= global.CanReforge(item); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.CanReforge", e); }
        }
        return can;
    }

    public static void PreReforge(Item item)
    {
        if (item.IsNull) return;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modItem.PreReforge(); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.PreReforge", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.PreReforge(item); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.PreReforge", e); }
        }
    }

    public static void PostReforge(Item item)
    {
        if (item.IsNull) return;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modItem.PostReforge(); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.PostReforge", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.PostReforge(item); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.PostReforge", e); }
        }
    }

    public static void ApplyPrefix(Item item, int pre)
    {
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modItem.ApplyPrefix(pre); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.ApplyPrefix", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.ApplyPrefix(item, pre); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.ApplyPrefix", e); }
        }
    }

    public static void HoldoutOrigin(Player player, ref Vector2 origin) => HoldoutOrigin(player.HeldItem, player, ref origin);

    internal static void HoldoutOrigin(Item item, Player player, ref Vector2 origin)
    {
        Vector2 modOrigin = default;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modOrigin = modItem.HoldoutOrigin() ?? modOrigin; }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.HoldoutOrigin", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { modOrigin = global.HoldoutOrigin(item.type) ?? modOrigin; }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.HoldoutOrigin", e); }
        }
        modOrigin.X *= player.direction;
        modOrigin.Y *= -player.gravDir;
        origin += modOrigin;
    }

    public static void CaughtFishStack(Item item)
    {
        int stack = item.stack;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { modItem.CaughtFishStack(ref stack); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.CaughtFishStack", e); }
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.CaughtFishStack(item.type, ref stack); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.CaughtFishStack", e); }
        }
        item.stack = stack;
    }

    public static void ModifyItemDraw(Item item, PlayerDrawSet drawInfo, DrawData drawData, DrawData? coloredDrawData, DrawData? glowmaskDrawData)
    {
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.PreModifyItemDraw(item, ref drawInfo, ref drawData, ref coloredDrawData, ref glowmaskDrawData); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.PreModifyItemDraw", e); }
        }
        bool draw = true;
        if (ItemHooks.ModItemOf(item) is ModItem modItem)
        {
            try { draw = modItem.ModifyItemDraw(ref drawInfo, ref drawData, ref coloredDrawData, ref glowmaskDrawData); }
            catch (Exception e) { ItemHooks.Report($"{modItem.FullName}.ModifyItemDraw", e); }
        }
        if (draw)
        {
            drawInfo.DrawDataCache.Add(drawData);
            if (coloredDrawData.HasValue)
                drawInfo.DrawDataCache.Add(coloredDrawData.Value);
            if (glowmaskDrawData.HasValue)
                drawInfo.DrawDataCache.Add(glowmaskDrawData.Value);
        }
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(item))
        {
            if (global == null) continue;
            try { global.PostModifyItemDraw(item, ref drawInfo, drawData, coloredDrawData, glowmaskDrawData); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.PostModifyItemDraw", e); }
        }
    }

    public static ModItem? GetItem(int type) => Get(type);

    internal static ModItem? Get(int type)
    {
        ModItem?[] all = Volatile.Read(ref items);
        uint index = (uint)(type - ItemID.Count);
        return index < (uint)all.Length ? all[index] : null;
    }

    /// <summary>
    /// tML grava o tipo e o `ModItem` no proprio template em
    /// <c>ModItem.Register</c> (<c>Item.ResetStats(ItemLoader.Register(this));
    /// Item.ModItem = this;</c>, ModItem.cs:66-71). Aqui o template nasce em
    /// <c>InitTemplateInstance</c> e ninguem escrevia o `type`: dentro do
    /// SetStaticDefaults o mod lia <c>Item.type == 0</c> e indexava
    /// `ItemID.Sets.*`/`ItemID.Search` na posicao 0.
    /// </summary>
    internal static void PrepareTemplate(ModItem item)
    {
        Item template = item.Entity;
        if (template == null || template.IsNull) return;
        if (template.type != item.Type) template.type = item.Type;
        Instances.Set(template.Pointer, item);
    }

    internal static void Register(ModItem item)
    {
        string texture = ResolveTextureOrMissing(item, item.Texture);
        int type = Native(item, texture, DisplayNames(item), Tooltips(item));
        if (type == -1) throw new InvalidOperationException($"{item.FullName}: the mod already has an item named '{item.Name}'");
        if (type < 0) throw new InvalidOperationException($"{item.FullName}: registration failed ({type})");
        item.Type = type;
        PrepareTemplate(item);
        if (type + 1 > Volatile.Read(ref itemCount)) Volatile.Write(ref itemCount, type + 1);

        EnsureHooked();
        EquipLoader.Autoload(item);
        ItemHooks.Install(item.GetType(), typeof(ModItem));
        PrefixLoader.WantRollable();
        lock (Gate)
        {
            int index = type - ItemID.Count;
            // Index negativo = o type veio antes do comeco dos ids de mod. No
            // aparelho o native sempre numera a partir de ItemID.Count, mas
            // o sandbox (e um RegisterItem com stub) pode devolver outro valor;
            // antes isso dava um IndexOutOfRangeException sem dizer o que era.
            if (index < 0)
                throw new InvalidOperationException(
                    $"{item.FullName}: item type {type} is below ItemID.Count ({ItemID.Count})");
            ModItem?[] all = items;
            if (index >= all.Length)
            {
                var grown = new ModItem?[Math.Max(index + 1, all.Length * 2)];
                Array.Copy(all, grown, all.Length);
                all = grown;
            }
            all[index] = item;
            Volatile.Write(ref items, all);
        }
    }

    static string DisplayNames(ModItem item) => Localized(item, item.DisplayName, nameof(ModItem.DisplayName));

    static string Tooltips(ModItem item) => Localized(item, item.Tooltip, nameof(ModItem.Tooltip));

    static string Localized(ModItem item, LocalizedText text, string property)
    {
        if (item.GetType().GetProperty(property)!.DeclaringType == typeof(ModItem))
        {
            var texts = LocalizationLoader.Texts(item.Mod, item.GetLocalizationKey(property));
            if (texts.Count > 0) return LocalizationLoader.Pack(texts);
        }
        return text.IsNull ? "" : text.Value ?? "";
    }

    static int Native(ModItem item, string texture, string displayName, string tooltip)
    {
        byte* mod = Host.Utf8(item.Mod.Uid), name = Host.Utf8(item.Name), tex = Host.Utf8(texture);
        byte* dn = Host.Utf8(displayName), tip = Host.Utf8(tooltip);
        try { return Host.Native()->RegisterItem(mod, name, tex, dn, tip); }
        finally
        {
            Host.FreeUtf8(mod);
            Host.FreeUtf8(name);
            Host.FreeUtf8(tex);
            Host.FreeUtf8(dn);
            Host.FreeUtf8(tip);
        }
    }

    internal static string ResolveTextureOrMissing(ModType content, string path)
    {
        try { return ResolveTexture(content, path, true); }
        catch (FileNotFoundException e)
        {
            Log.Error($"{e.Message}; drawing a placeholder instead");
            return "";
        }
    }

    internal static string ResolveTexture(ModType content, string path, bool vanilla = false)
    {
        string texture = path.Replace('\\', '/');
        if (vanilla && texture.StartsWith("Terraria/", StringComparison.Ordinal)) return texture;
        if (!texture.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) texture += ".png";
        if (Path.IsPathRooted(texture))
        {
            if (File.Exists(texture)) return texture;
        }
        else
        {
            string direct = Path.Combine(content.Mod.Folder, texture);
            if (File.Exists(direct)) return direct;
            int slash = texture.IndexOf('/');
            if (slash > 0)
            {
                string withoutMod = Path.Combine(content.Mod.Folder, texture.Substring(slash + 1));
                if (File.Exists(withoutMod)) return withoutMod;
            }
        }
        throw new FileNotFoundException($"{content.FullName}: texture '{path}' not found in {content.Mod.Folder}");
    }

    static void SetDefaults(On_Item.orig_SetDefaults orig, Item self, int type, ItemVariant variant)
    {
        ModItem? template = Get(type);
        if (template == null)
        {
            orig(self, type, variant);
            if (Instances.Count > 0) Instances.Set(self.Pointer, null);
            if (Globals.Count == 0) return;
            Globals.Finish(self, Globals.Begin(self));
            IntPtr previous = DamageClassLoader.Defaulting;
            DamageClassLoader.Defaulting = self.Pointer;
            try { GlobalSetDefaults(self); }
            finally { DamageClassLoader.Defaulting = previous; }
            return;
        }
        NativeApi* api = Host.Native();
        api->PrepareItem(self.Pointer, type);
        // O `Item.ResetStats` nativo nao zera tudo quando o tipo e de mod (ele
        // indexa tabelas do jogo com um id fora da faixa): o `bait` ficava com
        // lixo e o tooltip de TODO consumivel de mod mostrava "forca de isca".
        // Zera aqui, antes do SetDefaults do mod — quem e isca de verdade
        // escreve bait depois (ou via CloneDefaults).
        self.bait = 0;
        // `Item.ResetStats` (o reset do tModLoader) nem sempre deixa os campos
        // de uso preenchidos quando o tipo e de mod — e um mod que nao escreve
        // `useTime` (caso do mod de teste) ficava com 0 e a arma NAO ATACAVA:
        // useTime=0 zera o intervalo de uso. Estes sao os padroes do jogo.
        if (self.useTime <= 0) self.useTime = 100;
        if (self.useAnimation <= 0) self.useAnimation = 100;
        self.maxStack = 1;
        EquipLoader.Install();
        EquipLoader.Apply(self, type);
        ModItem instance = Bind(template, self);
        GlobalItem?[]? slots = Globals.Begin(self);
        IntPtr outer = DamageClassLoader.Defaulting;
        DamageClassLoader.Defaulting = self.Pointer;
        try
        {
            try
            {
                self.dye = (byte)Graphics.Shaders.GameShaders.Armor.GetShaderIdFromItemId(type);
                self.hairDye = Graphics.Shaders.GameShaders.Hair.GetShaderIdFromItemId(type);
                instance.AutoDefaults();
                instance.SetDefaults();
            }
            catch (Exception e)
            {
                Log.Error($"item {template.FullName}: SetDefaults threw: {e}");
                ExplainPcModMismatch(e, template);
            }
            Globals.Finish(self, slots);
            api->FinishItem(self.Pointer, type);
            GlobalSetDefaults(self);
            // Autodiagnostico: sem isto, "a arma nao ataca / a classe esta
            // errada" vira adivinhacao. UMA linha por item de mod no log —
            // o SetDefaults de um mesmo item roda varias vezes (prefixo,
            // receita, inventario) e cada linha custa ~8 ms de logcat: no
            // Fargowiltas eram 3 589 linhas, 29 s de carga e 0 informacao nova.
            if (LoggedItems.Add(type))
            {
                if (LoggedItems.Count == 1) LogOffsets();
                Log.Info($"item {template.FullName}: bait={self.bait} makeNPC={self.makeNPC} consumivel={self.consumable} potion={self.potion} healLife={self.healLife} healMana={self.healMana} ammo={self.ammo} createTile={self.createTile} createWall={self.createWall} stack={self.stack} useStyle={self.useStyle} useTime={self.useTime} useAnimation={self.useAnimation} damage={self.damage} knockBack={self.knockBack} crit={self.crit} rare={self.rare} value={self.value} shoot={self.shoot} mana={self.mana} autoReuse={self.autoReuse} noMelee={self.noMelee} maxStack={self.maxStack} class={DamageClassLoader.ItemClass(self).Name} w={self.width} h={self.height}");
            }
        }
        finally { DamageClassLoader.Defaulting = outer; }
    }

    /**
     * Um `.tmod` compilado no PC espera `Terraria.Item` como CLASSE: o IL do
     * `SetDefaults` faz `callvirt get_Item()` e depois `ldfld/stfld` nessa
     * referência. Aqui `Item` é um proxy struct (e `ModItem.Item` devolve
     * `ref Item`), então o JIT do Mono recusa o corpo do método com
     * "Expected reference type but got type kind 17" — os valores do
     * SetDefaults simplesmente não são aplicados, embora o item seja
     * registrado e o desenho apareça.
     *
     * Isto é uma incompatibilidade de ARQUITETURA entre as duas rotas, não um
     * arquivo faltando. Dizer isso no relatório evita procurar a causa em
     * texturas/manifesto e deixa claro por que as propriedades ficaram vazias.
     */
    static void ExplainPcModMismatch(Exception e, ModItem template)
    {
        if (e is not TypeLoadException) return;
        string message = e.Message ?? "";
        if (!message.Contains("reference type", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("type kind", StringComparison.OrdinalIgnoreCase)) return;
        Log.Error(
            $"item {template.FullName}: PC .tmod incompatibility — the mod was compiled against " +
            "tModLoader (1.4.4.9/1.4.5) where Terraria.Item is a class; here it is a struct proxy. " +
            "Its SetDefaults IL cannot run, so damage/size/stack/etc. stay at shim defaults. " +
            "This is not a missing texture or a bad manifest."
        );
    }

    static void GlobalSetDefaults(Item item)
    {
        GlobalItem?[]? slots = Globals.Slots(item);
        if (slots == null) return;
        foreach (GlobalItem? global in slots)
        {
            if (global == null) continue;
            try
            {
                global.SetDefaults(item);
            }
            catch (Exception e)
            {
                Log.Error($"{global.FullName}: SetDefaults threw: {e}");
            }
        }
    }

    static readonly System.Collections.Generic.Queue<(long Time, GameHandle Pin)> Kept = new();

    internal static void KeepAlive(Item item)
    {
        long now = Environment.TickCount64;
        lock (Kept)
        {
            while (Kept.Count > 0 && now - Kept.Peek().Time > 10000) Kept.Dequeue().Pin.Dispose();
            Kept.Enqueue((now, new GameHandle(item.Pointer)));
        }
    }

    internal static void CloneDefaults(Item item, int typeToClone)
    {
        int type = item.type;
        bool material = item.material;
        ModItem? instance = Instances.Get(item.Pointer);
        GlobalItem?[]? slots = Globals.Slots(item);
        item.SetDefaults(typeToClone, default);
        item.type = type;
        item.material = material;
        Instances.Set(item.Pointer, instance);
        Globals.Restore(item, slots);
    }

    static readonly Dictionary<int, int> Research = new();

    static IntPtr Sacrifices() => (IntPtr)CreativeItemSacrificesCatalog.Instance.SacrificeTablePointer();

    static IntPtr DictionaryMethod(IntPtr dictionary, string signature, out IntPtr info)
    {
        IntPtr klass = *(IntPtr*)dictionary;
        byte* s = Host.Utf8(signature);
        try { info = Host.Native()->FindMethodBySignature(klass, s); }
        finally { Host.FreeUtf8(s); }
        if (info == IntPtr.Zero) throw new MissingMethodException($"Dictionary<int, int>: '{signature}' not found");
        return Host.Native()->MethodPointer(info);
    }

    internal static int ResearchUnlockCount(int type)
    {
        IntPtr table = Sacrifices();
        if (table == IntPtr.Zero) return 0;
        IntPtr fn = DictionaryMethod(table, "bool TryGetValue(int key, out int value)", out IntPtr info);
        int value = 0;
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(table);
        call.Arg(type);
        call.Arg((IntPtr)(&value));
        call.Arg(info);
        return (call.Invoke(fn) & 0xFF) != 0 ? value : 0;
    }

    internal static void SetResearchUnlockCount(int type, int count)
    {
        lock (Research) Research[type] = count;
        ApplyResearch(type, count);
        ItemHooks.Once("Research", () => On_CreativeItemSacrificesCatalog.Initialize += SacrificesInitialize);
    }

    static void SacrificesInitialize(On_CreativeItemSacrificesCatalog.orig_Initialize orig, CreativeItemSacrificesCatalog self)
    {
        orig(self);
        KeyValuePair<int, int>[] all;
        lock (Research) all = Research.ToArray();
        foreach ((int type, int count) in all)
        {
            try { ApplyResearch(type, count); }
            catch (Exception e) { Log.Error($"ResearchUnlockCount {type}: {e.Message}"); }
        }
    }

    static void ApplyResearch(int type, int count)
    {
        IntPtr table = Sacrifices();
        if (table == IntPtr.Zero) return;
        IntPtr fn = DictionaryMethod(table, count < 1 ? "bool Remove(int key)" : "void set_Item(int key, int value)", out IntPtr info);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(table);
        call.Arg(type);
        if (count >= 1) call.Arg(count);
        call.Arg(info);
        call.Invoke(fn);
    }

    static Item Clone(On_Item.orig_Clone orig, Item self) => CopyInstances(self, orig(self));

    static Item DeepClone(On_Item.orig_DeepClone orig, Item self) => CopyInstances(self, orig(self));

    static Item ClientClone(On_Item.orig_clientClone orig, Item self, Item cloneDestination) => CopyInstances(self, orig(self, cloneDestination));

    internal static void KeepInstances(Item original, Item copy) => CopyInstances(original, copy);

    static Item CopyInstances(Item self, Item copy)
    {
        if (copy.IsNull || copy == self) return copy;
        ModItem? source = Instances.Get(self.Pointer);
        if (source != null && source.Type == copy.type)
        {
            try
            {
                ModItem cloned = source.Clone(copy);
                cloned.Type = source.Type;
                Instances.Set(copy.Pointer, cloned);
            }
            catch (Exception e)
            {
                Log.Error($"item {source.FullName}: Clone threw: {e}");
            }
        }
        Globals.Clone(self, copy);
        return copy;
    }

    [UnmanagedCallersOnly]
    static int SaveItemData(IntPtr itemPointer, byte* current, IntPtr sink)
    {
        ModItem? instance = null;
        try
        {
            instance = InstanceOf(new Item(itemPointer));
            if (instance == null) return 1;
            string text = SavedText(instance) ?? Marshal.PtrToStringUTF8((IntPtr)current) ?? "";
            byte* utf8 = Host.Utf8(text);
            try { Host.Native()->WriteString(sink, utf8); }
            finally { Host.FreeUtf8(utf8); }
            return 1;
        }
        catch (Exception e)
        {
            Log.Error($"item {instance?.FullName ?? "?"}: SaveData threw: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static int LoadItemData(IntPtr itemPointer, byte* data)
    {
        ModItem? instance = null;
        try
        {
            instance = InstanceOf(new Item(itemPointer));
            if (instance == null) return 1;
            LoadText(instance, Marshal.PtrToStringUTF8((IntPtr)data) ?? "");
            return 1;
        }
        catch (Exception e)
        {
            Log.Error($"item {instance?.FullName ?? "?"}: LoadData threw (its saved data is kept as it was): {e}");
            return 0;
        }
    }

    internal static string? SavedText(ModItem instance)
    {
        if (instance.LoadFailed) return instance.SavedData;
        var tag = new TagCompound();
        if (Overrides(instance, nameof(ModItem.SaveData))) instance.SaveData(tag);
        var globalData = new TagCompound();
        foreach (GlobalItem? global in ItemHooks.GlobalsOf(instance.Item))
        {
            if (global == null || !ItemHooks.Wants(global, nameof(GlobalItem.SaveData))) continue;
            var saved = new TagCompound();
            global.SaveData(instance.Item, saved);
            if (saved.Count > 0) globalData[global.FullName] = saved;
        }
        if (globalData.Count > 0) tag["BunnyLoader:GlobalItems"] = globalData;
        string text = tag.Count == 0 ? "" : TagJson.Write(tag);
        instance.SavedData = text;
        return text;
    }

    internal static void LoadText(ModItem instance, string text)
    {
        instance.SavedData = text;
        instance.LoadFailed = true;
        TagCompound tag = text.Length == 0 ? new TagCompound() : TagJson.Read(text);
        if (Overrides(instance, nameof(ModItem.LoadData))) instance.LoadData(tag);
        if (tag.TryGet<TagCompound>("BunnyLoader:GlobalItems", out var globalData))
        {
            foreach (GlobalItem? global in ItemHooks.GlobalsOf(instance.Item))
            {
                if (global == null || !ItemHooks.Wants(global, nameof(GlobalItem.LoadData))) continue;
                global.LoadData(instance.Item, globalData.Get<TagCompound>(global.FullName) ?? new TagCompound());
            }
        }
        instance.LoadFailed = false;
    }

    internal static bool Overrides(ModItem item, string method) =>
        item.GetType().GetMethod(method, new[] { typeof(TagCompound) })!.DeclaringType != typeof(ModItem);

    static readonly System.Collections.Generic.List<GameHandle> TemplatePins = new();

    [UnmanagedCallersOnly]
    static void ItemsInstalled(int first, int last)
    {
        EquipLoader.Install();
        for (int type = first; type <= last; type++)
        {
            ModItem? item = Get(type);
            if (item == null) continue;
            SetupResume.Prime(item.Mod);
            string resumeKey = "ITEM:" + item.FullName;
            bool resumeSkip = SetupResume.Skip(item.Mod, resumeKey);
            try
            {
                if (item.Entity.IsNull)
                {
                    Item template = Item.New();
                    TemplatePins.Add(new GameHandle(template.Pointer));
                    template.SetDefaults(type, default);
                    item.Entity = template;
                }
                PrepareTemplate(item);
                if (resumeSkip) Log.Warn($"item {type} {resumeKey}: SetStaticDefaults pulado (crash anterior)");
                else
                {
                    SetupResume.Enter(item.Mod, resumeKey);
                    item.SetupContent();
                    SetupResume.Leave(item.Mod);
                }
            }
            catch (Exception e)
            {
                Log.Error($"item {item.FullName}: SetStaticDefaults threw: {e}");
            }
        }
    }

    public static void IsAnglerQuestAvailable(int itemID, ref bool notAvailable)
    {
        if (Get(itemID) is ModItem modItem)
            notAvailable |= !modItem.IsAnglerQuestAvailable();
        foreach (GlobalItem g in Globals.Templates)
            notAvailable |= !g.IsAnglerQuestAvailable(itemID);
    }

    public static string? AnglerChat(int type)
    {
        string chat = "";
        string catchLocation = "";
        Get(type)?.AnglerQuestChat(ref chat, ref catchLocation);
        foreach (GlobalItem g in Globals.Templates)
            g.AnglerChat(type, ref chat, ref catchLocation);
        if (string.IsNullOrEmpty(chat) || string.IsNullOrEmpty(catchLocation))
            return null;
        return chat + "\n\n(" + catchLocation + ")";
    }

    internal static void ModifyFishingLine(Projectile projectile, Player player, ref float polePosX, ref float polePosY, ref Color lineColor)
    {
        Item item = player.inventory[player.selectedItem];
        if (ItemHooks.ModItemOf(item) is not ModItem m) return;
        Vector2 lineOriginOffset = Vector2.Zero;
        try { m.ModifyFishingLine(projectile, ref lineOriginOffset, ref lineColor); }
        catch (Exception e) { ItemHooks.Report($"{m.FullName}.ModifyFishingLine", e); }
        polePosX += lineOriginOffset.X * player.direction;
        if (player.direction < 0) polePosX -= 13f;
        polePosY += lineOriginOffset.Y * player.gravDir;
    }
}
