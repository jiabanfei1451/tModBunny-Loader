using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System;
using BunnyLoader.Hooks;
using BunnyLoader.Interop;
using BunnyLoader;
using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terraria.ModLoader.Core;

internal static unsafe class ItemHooks
{
    static readonly object Gate = new();
    static readonly HashSet<string> Installed = new();
    static readonly HashSet<string> Reported = new();
    static readonly Dictionary<int, IntPtr> UsedBy = new();

    static Hook? canUse, startUse, ownerOnly, animation, useStyle, holdStyle, updateEquips;
    static readonly int* AirScopes = (int*)NativeMemory.AllocZeroed(sizeof(int));

    [ThreadStatic] static IntPtr equipping;

    public static void Install(Type type, Type baseType)
    {
        bool Has(string name) => Overrides(type, baseType, name);
        if (Has(nameof(ModItem.CanUseItem))) WantCanUse();
        // v68: A/B. Se existir o arquivo <TML>/no-owneronly, NAO instalamos o
        // gancho de Player.ItemCheck_OwnerOnlyCode. Serve para separar duas
        // causas do "NPC de cidade aparece ao usar item":
        //   a) a propria funcao nativa faz o spawn (entao o gancho nao tem culpa);
        //   b) a chamada do Original pelo gancho esta' indo para o lugar errado.
        // Com o arquivo, o jogo roda com a funcao 100% nativa.
        if (Has(nameof(ModItem.UseItem)) && !NoOwnerOnly)
        {
            // v70: antes estes dois ganchos usavam Raw(...) + GameCall — o
            // original era chamado com os argumentos re-empacotados a mao, e
            // essa reconstrucao era a suspeita principal do "NPC de cidade
            // nascia DENTRO de ItemCheck_OwnerOnlyCode ao usar qualquer item"
            // (o A/B mostrou que o spawn nao vem do UseItem de nenhum mod).
            // Agora usa os ganchos TIPADOS gerados pelo ProxyGen
            // (orig_ItemCheck_*): quem chama o original e o proprio trampolim
            // nativo, com a ABI correta, e nos so vemos o par (orig, self).
            Once("OwnerOnly", () => On_Player.ItemCheck_OwnerOnlyCode += OwnerOnlyCode);
            Once("StartUse", () => On_Player.ItemCheck_StartActualUse += StartActualUse);
        }
        if (Has(nameof(ModItem.UseAnimation)))
            Once("Animation", () => animation = Raw("void ApplyItemAnimation(Item sItem)",
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ApplyItemAnimation));
        if (Has(nameof(ModItem.UseStyle)) || Has(nameof(ModItem.HoldItem)) || Has(nameof(ModItem.HoldoutOffset)))
            Once("UseStyle", () => useStyle = Raw("void ItemCheck_ApplyUseStyle(float mountOffset, Item sItem, Rectangle heldItemFrame)",
                (IntPtr)(delegate* unmanaged<IntPtr, float, IntPtr, Rectangle, IntPtr, void>)&ApplyUseStyle));
        if (Has(nameof(ModItem.HoldStyle)) || Has(nameof(ModItem.HoldItem)) || Has(nameof(ModItem.HoldoutOffset)))
            Once("HoldStyle", () => holdStyle = Raw("void ItemCheck_ApplyHoldStyle(float mountOffset, Item sItem, Rectangle heldItemFrame)",
                (IntPtr)(delegate* unmanaged<IntPtr, float, IntPtr, Rectangle, IntPtr, void>)&ApplyHoldStyle));
        if (Has(nameof(ModItem.UpdateInventory)) || Has(nameof(ModItem.UpdateInfoAccessory)))
            Once("Inventory", () => updateEquips = Raw("void UpdateEquips(int i)",
                (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, void>)&UpdateEquips));
        if (Has(nameof(ModItem.UpdateInfoAccessory)))
            Once("InventoryInfo", () => On_Player.RefreshInfoAccsFromTeamPlayers += RefreshInfoAccsFromTeamPlayers);
        ItemExampleHooks.Install(Has);
        ItemVisibilityHooks.Install(Has);
        ItemStackHooks.Install(Has);
        ItemConsumeHooks.Install(Has);
        ItemHealHooks.Install(Has);
        ItemPickupHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemDrawHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemHeldDrawHooks.Install(Has);
        if (Has(nameof(ModItem.OnCraft))) RecipeLoader.WantCraft();
        ItemReforgeHooks.Install(Has);
        if (Has(nameof(ModItem.CaughtFishStack))) PlayerFishingHooks.WantCaught();
        if (Has(nameof(ModItem.ApplyPrefix))) PrefixLoader.WantApplyHook();
        ItemOpenHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemNetHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemTooltipHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemEquipHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemCombatHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemShootHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemHitHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemPvpHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemTimingHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemFrameHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemManaHooks.Install(Has, baseType == typeof(GlobalItem));
        FishingLineHooks.Install(Has);
        ItemResearchHooks.Install(Has);
        ItemWorldHooks.Install(Has, baseType == typeof(GlobalItem));
        if (Has(nameof(ModItem.ModifyEquipTextureDraw))) EquipDrawHooks.Install();
        AnglerQuestHooks.Install(Has, baseType == typeof(GlobalItem));
        ItemLootHooks.Install(Has, baseType == typeof(GlobalItem));
        PrefixLoader.WantItemHooks(Has);
    }

    internal static void WantCanUse() =>
        Once("CanUse", () => canUse = Raw("bool ItemCheck_CheckCanUse_Inner(Item sItem, bool ignoreCursed)",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte, IntPtr, byte>)&CheckCanUse));

    static readonly Dictionary<(Type, string), bool> OverrideCache = new();

    internal static bool Wants(GlobalItem global, string name)
    {
        var key = (global.GetType(), name);
        lock (OverrideCache)
        {
            if (OverrideCache.TryGetValue(key, out bool wants)) return wants;
            return OverrideCache[key] = Overrides(key.Item1, typeof(GlobalItem), name);
        }
    }

    internal static GlobalItem?[] GlobalsOf(Item item) => ItemLoader.Globals.Slots(item) ?? Array.Empty<GlobalItem?>();

    static readonly ConcurrentDictionary<(Type, Type), HashSet<string>> OverrideNames = new();

    internal static bool Overrides(Type type, Type baseType, string name) =>
        OverrideNames.GetOrAdd((type, baseType), static key => FindOverrides(key.Item1, key.Item2)).Contains(name);

    internal static bool ModOverrides(ModItem item, string name) => Overrides(item.GetType(), typeof(ModItem), name);

    internal static ModItem? Overrider(Item item, int type, string name) =>
        ItemLoader.Get(type) is ModItem template && ModOverrides(template, name) ? ItemLoader.InstanceOf(item) : null;

    internal static bool WantsType(int type, string name) =>
        ItemLoader.Globals.Overriding(name).Length > 0 || ItemLoader.Get(type) is ModItem template && ModOverrides(template, name);

    internal static GlobalItem? Applied(GlobalItem?[]? slots, short index) => slots != null && index < slots.Length ? slots[index] : null;

    internal static int Selected(Player player) => player.selectedItemState.selected;

    internal static void TypeGate<THook>(int arg, Func<int, bool> wanted) where THook : Delegate
    {
        HookGates.Table<THook>(() => HookGates.Field(arg, Item.Class, "type"), () => ItemLoader.ItemCount);
        HookGates.Want<THook>(wanted);
    }

    internal static Hook? RawGated<TGate>(string ns, string type, string signature, IntPtr replacement, int arg, Func<int, bool> wanted) where TGate : Delegate
    {
        TypeGate<TGate>(arg, wanted);
        Type? outer = HookGates.Installing;
        HookGates.Installing = typeof(TGate);
        try { return Raw(ns, type, signature, replacement); }
        finally { HookGates.Installing = outer; }
    }

    internal static void WantTurnToAir()
    {
        HookGates.Define<On_Item.hook_TurnToAir>(() => new GateSpec
        {
            Arg = HookGates.AbsoluteArg, PointerOffset = -1, FieldOffset = 0, FieldSize = 4, Address = (IntPtr)AirScopes,
        });
        HookGates.Want<On_Item.hook_TurnToAir>();
    }

    internal static void EnterAirScope() => Interlocked.Increment(ref *AirScopes);

    internal static void ExitAirScope() => Interlocked.Decrement(ref *AirScopes);

    static HashSet<string> FindOverrides(Type type, Type baseType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.DeclaringType != baseType && method.GetBaseDefinition().DeclaringType == baseType) names.Add(method.Name);
        }
        return names;
    }

    internal static void Once(string key, Action install)
    {
        lock (Gate)
        {
            if (!Installed.Add(key)) return;
        }
        Log.Info($"hook: once '{key}'");
        install();
    }

    internal static Hook? Raw(string signature, IntPtr replacement) => Raw("Terraria", "Player", signature, replacement);

    internal static Hook? Raw(string ns, string type, string signature, IntPtr replacement)
    {
        try
        {
            return Hook.Install(ns, type, signature, replacement);
        }
        catch (Exception e)
        {
            Log.Error($"item hooks: {type}.{signature} is not hooked: {e.Message}");
            return null;
        }
    }

    internal static Hook? Raw(GameClass type, string signature, IntPtr replacement)
    {
        try
        {
            return Hook.Install(type, signature, replacement);
        }
        catch (Exception e)
        {
            Log.Error($"item hooks: {type.FullName}.{signature} is not hooked: {e.Message}");
            return null;
        }
    }

    internal static Hook? Gated(GameClass type, string signature, IntPtr replacement, int arg, int pointerOffset, int fieldOffset, int fieldSize)
    {
        try
        {
            var gate = new GateSpec { Arg = arg, PointerOffset = pointerOffset, FieldOffset = fieldOffset, FieldSize = fieldSize };
            return Hook.InstallGated(type, signature, replacement, gate, out _);
        }
        catch (Exception e)
        {
            Log.Error($"item hooks: {type.FullName}.{signature} is not hooked: {e.Message}");
            return null;
        }
    }

    internal static Hook? Gated(GameClass type, string signature, IntPtr replacement, Func<GateSpec> spec)
    {
        GateSpec gate;
        try { gate = spec(); }
        catch (Exception e)
        {
            Log.Warn($"item hooks: {type.FullName}.{signature} is hooked without a gate: {e.Message}");
            return Raw(type, signature, replacement);
        }
        try
        {
            return Hook.InstallGated(type, signature, replacement, gate, out _);
        }
        catch (Exception e)
        {
            Log.Error($"item hooks: {type.FullName}.{signature} is not hooked: {e.Message}");
            return null;
        }
    }

    internal static int FieldOffset(GameClass type, string field)
    {
        int slot = -1;
        return GameInterop.Offset(ref slot, type, field);
    }

    internal static void Report(string who, Exception e)
    {
        bool first;
        lock (Reported) first = Reported.Add(who + "|" + e.GetType().FullName);
        if (first) Log.Error($"{who} threw: {e}");
    }

    internal static long Original(ref GameCall call, Hook hook, string what)
    {
        try { return call.Invoke(hook.Original); }
        catch (GameException e)
        {
            Report(what, e);
            return 0;
        }
    }

    internal static ModItem? ModItemOf(Item item) => ItemLoader.Get(item.type) != null ? ItemLoader.InstanceOf(item) : null;

    [UnmanagedCallersOnly]
    static byte CheckCanUse(IntPtr self, IntPtr itemPointer, byte ignoreCursed, IntPtr method)
    {
        Hook? hook = canUse;
        if (hook == null) return 1;
        bool allowed = true;
        try { allowed = CanUseItem(new Item(itemPointer), new Player(self)); }
        catch (Exception e) { Report("CanUseItem", e); }
        if (!allowed) return 0;
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(itemPointer);
        call.Arg(ignoreCursed);
        call.Arg(method);
        return (byte)(Original(ref call, hook, "Player.ItemCheck_CheckCanUse_Inner") & 0xFF);
    }

    /// <summary>
    /// Dispatch de <c>CanUseItem</c> (ModPlayer + ModItem + GlobalItem).
    /// Interno porque <c>CombinedHooks.CanUseItem</c> é o nome que o tML expõe
    /// aos mods e precisa cair exatamente aqui.
    /// </summary>
    internal static bool CanUseItem(Item item, Player player)
    {
        bool allowed = PlayerLoader.All(player, nameof(ModPlayer.CanUseItem), item, static (m, i) => m.CanUseItem(i));
        if (ModItemOf(item) is ModItem m)
        {
            try { allowed &= m.CanUseItem(player); }
            catch (Exception e) { Report($"{m.FullName}.CanUseItem", e); }
        }
        short[] overriding = ItemLoader.Globals.Overriding(nameof(GlobalItem.CanUseItem));
        if (overriding.Length == 0) return allowed;
        GlobalItem?[]? slots = ItemLoader.Globals.Slots(item);
        foreach (short index in overriding)
        {
            if (Applied(slots, index) is not GlobalItem g) continue;
            try { allowed &= g.CanUseItem(item, player); }
            catch (Exception e) { Report($"{g.FullName}.CanUseItem", e); }
        }
        return allowed;
    }

    static void StartActualUse(On_Player.orig_ItemCheck_StartActualUse orig, Player self, Item sItem)
    {
        orig(self, sItem);
        try
        {
            // v64: so cancela quando o mod devolve false EXPLICITO. Quando
            // nenhum mod sobrescreve UseItem o retorno e null — no tModLoader
            // isso significa "uso normal", e o tempo do item tem de ser
            // aplicado. Antes, null caia no != true e o item NUNCA recebia
            // itemTime: um item com useAnimation=10 voltava a ser usavel a
            // cada 10 quadros (~6x/s) em vez de a cada useTime=100.
            if (UseItem(sItem, self) == false) return;
            lock (UsedBy) UsedBy[self.whoAmI] = (IntPtr)sItem.Pointer;
        }
        catch (Exception e) { Report("UseItem", e); }
    }

    /// v67: >0 enquanto o UseItem de algum mod esta' rodando. O gancho de
    /// NPC.NewNPC imprime isso: distingue "o NPC nasceu DENTRO do UseItem de um
    /// mod" de "nasceu em outro lugar" — sem depender de pilha gerenciada
    /// (que atravessa mal o limite de AssemblyLoadContext do mod).
    internal static int UseDepth;

    /// Pasta TML (definida pelo TmlPipeline) — usada para flags de diagnostico.
    internal static string? DebugDir;

    internal static bool NoOwnerOnly
    {
        get
        {
            try { return DebugDir is { Length: > 0 } d && File.Exists(Path.Combine(d, "no-owneronly")); }
            catch { return false; }
        }
    }

    static bool? UseItem(Item item, Player player)
    {
        // v63: espelho do lado "uso". Comparar esta linha com a de
        // ItemCheck_Shoot diz se o toque chega a usar o item mas nao chega ao
        // Shoot (caminho de entrada diferente) ou se nem chega a usar.
        Diag63.Once("use:" + item.type, 120, () => $"[v63] UseItem rodou {Diag63.Name(item)} pid={player.whoAmI} ctrlUse={player.controlUseItem} ctrlTile={player.controlUseTile}");
        bool? result = null;
        int stackBefore = item.stack;
        UseDepth++;
        foreach (GlobalItem? g in ItemLoader.Globals.Slots(item) ?? Array.Empty<GlobalItem?>())
        {
            if (g == null) continue;
            try
            {
                bool? used = g.UseItem(item, player);
                if (used.HasValue && result != false) result = used.Value;
            }
            catch (Exception e) { Report($"{g.FullName}.UseItem", e); }
        }
        if (ModItemOf(item) is ModItem m)
        {
            try
            {
                bool? used = m.UseItem(player);
                if (used.HasValue && result != false) result = used.Value;
                if (used != false) SwarmSpread(m, item, player, stackBefore);
            }
            catch (Exception e) { Report($"{m.FullName}.UseItem", e); }
        }
        UseDepth--;
        return result;
    }

    /// <summary>
    /// Regra do Fargowiltas para os "Overload" (SwarmSummonBase): com 10 ou
    /// mais no slot sai UM boss sobrecarregado; com menos de 10 saem tantos
    /// bosses normais quanto o slot tinha. O mod so' faz a primeira parte —
    /// `SwarmSummonBase.UseItem` gera um NPC e desconta `Min(stack,10)-1` — e
    /// `FargoGlobalNPC.SpawnBoss(NPC,int)` (que geraria o resto) nao tem
    /// chamador nesta build. Entao completamos aqui, so' para itens daquele
    /// namespace, sem tocar no caminho do boss sobrecarregado.
    /// </summary>
    static void SwarmSpread(ModItem m, Item item, Player player, int stackBefore)
    {
        try
        {
            Type type = m.GetType();
            string? name = type.FullName;
            if (name is null || !name.StartsWith("Fargowiltas.Items.Summons.SwarmSummons.", StringComparison.Ordinal)) return;

            int count = Math.Min(stackBefore, 10);
            if (count < 2 || count > 9) return;

            FieldInfo? npcField = type.GetField("npcType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (npcField is null || npcField.FieldType != typeof(int)) return;
            if (npcField.GetValue(m) is not int npcType || npcType <= 0) return;

            NPC source = new(NPC.GetBossSpawnSource(player.whoAmI).Pointer);
            for (int i = 1; i < count; i++)
            {
                float x = player.Center.X + Main.rand.Next(-700, 700);
                float y = player.Center.Y - Main.rand.Next(500, 900);
                int index = NPC.NewNPC(NPC.GetBossSpawnSource(player.whoAmI), (int)x, (int)y, npcType, 0, 0f, 0f, 0f, 0f, 255);
                if (index >= 0) Log.Info($"[swarm] extra {i + 1}/{count} tipo {npcType} nasceu como NPC #{index}");
            }

            // O mod desconta `count - 1`; a regra e' consumir `count`.
            int left = stackBefore - count;
            if (left < 0) left = 0;
            if (left >= item.stack) return;
            item.stack = left;
            if (left == 0) item.TurnToAir();
        }
        catch (Exception e) { Report("SwarmSpread", e); }
    }

    static void OwnerOnlyCode(On_Player.orig_ItemCheck_OwnerOnlyCode orig, Player self, ref Player.ItemCheckContext context, Item sItem, int weaponDamage, Rectangle heldItemFrame)
    {
        // v79: o vanilla faz `if (sItem.makeNPC > 0) NPC.NewNPC(...)` aqui. Um
        // item de mod sem makeNPC nenhum estava nascendo Dryad (type=20) ao ser
        // usado, e o item em questao tem width=20/height=20 — o mesmo 20. Estes
        // quatro numeros (valor, offsets de width/makeNPC/height) dizem se o
        // makeNPC foi escrito por engano no lugar do width.
        OwnerDiag(sItem);
        // v79: item de mod nao passa pelo ramo `makeNPC` do vanilla. O
        // `mods/HuanChong152` (width=20, height=20, shoot=0, sem UseItem) nascia
        // uma Dryad a cada uso, e Dryad e' NPC 20 — o mesmo 20 do width, sinal
        // de que o makeNPC acabou escrito junto com o tamanho do item. Enquanto
        // a causa exata nao fecha, o item de mod usa o makeNPC zerado: item de
        // tModLoader nao declara makeNPC (no tML isso e' coisa de ItemID fixo,
        // tipo bicho de gaiola), entao zerar aqui nao tira recurso de ninguem.
        short makeNPC = sItem.makeNPC;
        bool mute = makeNPC > 0 && ModItemOf(sItem) != null;
        if (mute) sItem.makeNPC = 0;
        try { orig(self, ref context, sItem, weaponDamage, heldItemFrame); }
        finally { if (mute) sItem.makeNPC = makeNPC; }
        try
        {
            IntPtr used;
            lock (UsedBy)
            {
                if (UsedBy.Count == 0 || !UsedBy.Remove(self.whoAmI, out used)) return;
            }
            if (self.ItemTimeIsZero && self.itemAnimation > 0) self.ApplyItemTime(new Item(used));
        }
        catch (Exception e)
        {
            Report("UseItem (item time)", e);
        }
    }

    static readonly HashSet<int> ownerDiag = new();
    static int ow = -1, om = -1, oh = -1;

    static void OwnerDiag(Item sItem)
    {
        try
        {
            if (!ownerDiag.Add(sItem.type)) return;
            int wo = GameInterop.Offset(ref ow, Item.Class, "width");
            int mo = GameInterop.Offset(ref om, Item.Class, "makeNPC");
            int ho = GameInterop.Offset(ref oh, Item.Class, "height");
            BunnyLoader.Log.Info($"[v79] OwnerOnly item={sItem.type} makeNPC={sItem.makeNPC} w={sItem.width} h={sItem.height} useStyle={sItem.useStyle} | offsets w={wo} makeNPC={mo} h={ho}");
        }
        catch (Exception e) { Report("OwnerOnly (diag)", e); }
    }

    [UnmanagedCallersOnly]
    static void ApplyItemAnimation(IntPtr self, IntPtr itemPointer, IntPtr method)
    {
        Hook? hook = animation;
        if (hook == null) return;
        try
        {
            var item = new Item(itemPointer);
            var player = new Player(self);
            if (ModItemOf(item) is ModItem m)
            {
                try { m.UseAnimation(player); }
                catch (Exception e) { Report($"{m.FullName}.UseAnimation", e); }
            }
            foreach (GlobalItem? g in ItemLoader.Globals.Slots(item) ?? Array.Empty<GlobalItem?>())
            {
                if (g == null) continue;
                try { g.UseAnimation(item, player); }
                catch (Exception e) { Report($"{g.FullName}.UseAnimation", e); }
            }
        }
        catch (Exception e) { Report("UseAnimation", e); }
        Call(hook, self, itemPointer, method, "Player.ApplyItemAnimation");
    }

    [UnmanagedCallersOnly]
    static void ApplyUseStyle(IntPtr self, float mountOffset, IntPtr itemPointer, Rectangle heldItemFrame, IntPtr method)
    {
        Hook? hook = useStyle;
        if (hook == null) return;
        Styled(hook, self, mountOffset, itemPointer, heldItemFrame, method, "Player.ItemCheck_ApplyUseStyle");
        try { Style(new Player(self), new Item(itemPointer), heldItemFrame, use: true); }
        catch (Exception e) { Report("UseStyle", e); }
    }

    [UnmanagedCallersOnly]
    static void ApplyHoldStyle(IntPtr self, float mountOffset, IntPtr itemPointer, Rectangle heldItemFrame, IntPtr method)
    {
        Hook? hook = holdStyle;
        if (hook == null) return;
        Styled(hook, self, mountOffset, itemPointer, heldItemFrame, method, "Player.ItemCheck_ApplyHoldStyle");
        try { Style(new Player(self), new Item(itemPointer), heldItemFrame, use: false); }
        catch (Exception e) { Report("HoldStyle", e); }
    }

    static void Styled(Hook hook, IntPtr self, float mountOffset, IntPtr itemPointer, Rectangle frame, IntPtr method, string what)
    {
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(mountOffset);
        call.Arg(itemPointer);
        call.Arg(frame);
        call.Arg(method);
        Original(ref call, hook, what);
    }

    internal static long Call(Hook hook, IntPtr self, IntPtr argument, IntPtr method, string what)
    {
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(argument);
        call.Arg(method);
        return Original(ref call, hook, what);
    }

    internal static long Call(Hook hook, IntPtr self, IntPtr method, string what)
    {
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(method);
        return Original(ref call, hook, what);
    }

    static void Style(Player player, Item item, Rectangle frame, bool use)
    {
        if (item.IsNull || item.type <= 0) return;
        ModItem? m = ModItemOf(item);
        GlobalItem?[] globals = ItemLoader.Globals.Slots(item) ?? Array.Empty<GlobalItem?>();
        if (m != null)
        {
            try { ApplyHoldout(player, m.HoldoutOffset()); }
            catch (Exception e) { Report($"{m.FullName}.HoldoutOffset", e); }
        }
        foreach (GlobalItem? g in globals)
        {
            if (g == null) continue;
            try { ApplyHoldout(player, g.HoldoutOffset(item.type)); }
            catch (Exception e) { Report($"{g.FullName}.HoldoutOffset", e); }
        }
        if (m != null)
        {
            try
            {
                if (use) m.UseStyle(player, frame);
                else m.HoldStyle(player, frame);
                m.HoldItem(player);
            }
            catch (Exception e) { Report($"{m.FullName}.{(use ? "UseStyle" : "HoldStyle")}", e); }
        }
        foreach (GlobalItem? g in globals)
        {
            if (g == null) continue;
            try
            {
                if (use) g.UseStyle(item, player, frame);
                else g.HoldStyle(item, player, frame);
                g.HoldItem(item, player);
            }
            catch (Exception e) { Report($"{g.FullName}.{(use ? "UseStyle" : "HoldStyle")}", e); }
        }
    }

    static void ApplyHoldout(Player player, Vector2? offset)
    {
        if (offset is not Vector2 o || (o.X == 0 && o.Y == 0)) return;
        float x = o.X * player.direction, y = o.Y * player.gravDir;
        float cos = MathF.Cos(player.itemRotation), sin = MathF.Sin(player.itemRotation);
        Vector2 location = player.itemLocation;
        location.X += x * cos - y * sin;
        location.Y += x * sin + y * cos;
        player.itemLocation = location;
    }

    [UnmanagedCallersOnly]
    static void UpdateEquips(IntPtr self, int i, IntPtr method)
    {
        Hook? hook = updateEquips;
        if (hook == null) return;
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(i);
        call.Arg(method);
        IntPtr outer = equipping;
        equipping = self;
        try { Original(ref call, hook, "Player.UpdateEquips"); }
        finally { equipping = outer; }
        try { CarriedItems(new Player(self), static (item, type, player, overriding) => UpdateInventory(item, type, player, overriding), nameof(GlobalItem.UpdateInventory)); }
        catch (Exception e)
        {
            Report("UpdateInventory", e);
        }
    }

    static void RefreshInfoAccsFromTeamPlayers(On_Player.orig_RefreshInfoAccsFromTeamPlayers orig, Player self)
    {
        if (equipping != IntPtr.Zero && equipping == self.Pointer)
        {
            try { CarriedItems(self, static (item, _, player, overriding) => UpdateInfoAccessory(item, player, overriding), nameof(GlobalItem.UpdateInfoAccessory)); }
            catch (Exception e) { Report("UpdateInfoAccessory", e); }
        }
        orig(self);
    }

    static void CarriedItems(Player player, Action<Item, int, Player, short[]> visit, string globalHook)
    {
        short[] overriding = ItemLoader.Globals.Overriding(globalHook);
        var items = player.inventory;
        int count = Math.Min(items.Length, 58);
        bool voidBag = false;
        for (int slot = 0; slot < count; slot++)
        {
            Item item = items[slot];
            if (item.IsNull) continue;
            int type = item.type;
            if (type == 4131) voidBag = true;
            if (type <= 0 || item.stack <= 0) continue;
            visit(item, type, player, overriding);
        }
        if (!voidBag) return;
        var bank = player.bank4;
        if (bank.IsNull) return;
        var bag = bank.item;
        if (bag.IsNull) return;
        var works = ItemID.Sets.WorksInVoidBag;
        for (int slot = 0; slot < bag.Length; slot++)
        {
            Item item = bag[slot];
            if (item.IsNull) continue;
            int type = item.type;
            if (type <= 0 || item.stack <= 0 || (type <= ItemID.Count && !(!works.IsNull && (uint)type < (uint)works.Length && works[type]))) continue;
            visit(item, type, player, overriding);
        }
    }

    static void UpdateInventory(Item item, int type, Player player, short[] overriding)
    {
        if (Overrider(item, type, nameof(ModItem.UpdateInventory)) is ModItem m)
        {
            try { m.UpdateInventory(player); }
            catch (Exception e) { Report($"{m.FullName}.UpdateInventory", e); }
        }
        if (overriding.Length == 0) return;
        GlobalItem?[]? slots = ItemLoader.Globals.Slots(item);
        foreach (short index in overriding)
        {
            if (Applied(slots, index) is not GlobalItem g) continue;
            try { g.UpdateInventory(item, player); }
            catch (Exception e) { Report($"{g.FullName}.UpdateInventory", e); }
        }
    }

    internal static void UpdateInfoAccessory(Item item, Player player) =>
        UpdateInfoAccessory(item, player, ItemLoader.Globals.Overriding(nameof(GlobalItem.UpdateInfoAccessory)));

    internal static void UpdateInfoAccessory(Item item, Player player, short[] overriding)
    {
        if (item.IsNull) return;
        int type = item.type;
        if (type <= 0 || item.stack <= 0) return;
        if (Overrider(item, type, nameof(ModItem.UpdateInfoAccessory)) is ModItem m)
        {
            try { m.UpdateInfoAccessory(player); }
            catch (Exception e) { Report($"{m.FullName}.UpdateInfoAccessory", e); }
        }
        if (overriding.Length == 0) return;
        GlobalItem?[]? slots = ItemLoader.Globals.Slots(item);
        foreach (short index in overriding)
        {
            if (Applied(slots, index) is not GlobalItem g) continue;
            try { g.UpdateInfoAccessory(item, player); }
            catch (Exception e) { Report($"{g.FullName}.UpdateInfoAccessory", e); }
        }
    }
}
