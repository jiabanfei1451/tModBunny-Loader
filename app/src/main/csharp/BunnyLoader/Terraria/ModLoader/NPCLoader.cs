using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Terraria.GameContent;
using Terraria.ModLoader.Core;

namespace Terraria.ModLoader;

public static unsafe partial class NPCLoader
{
    internal const int VanillaCount = 697;

    static readonly object Gate = new();
    static readonly EntityInstances<ModNPC> Instances = new();
    static readonly List<GameHandle> TemplatePins = new();
    static ModNPC?[] npcs = Array.Empty<ModNPC?>();
    static bool hooked, cloneHooked;

    internal static readonly GlobalList<NPC, GlobalNPC> Globals = new();

    public static int NPCCount => Host.Native()->NPCCount();

    public static ModNPC? GetNPC(int type)
    {
        ModNPC?[] all = Volatile.Read(ref npcs);
        uint index = (uint)(type - VanillaCount);
        return index < (uint)all.Length ? all[index] : null;
    }

    internal static ModNPC? InstanceOf(NPC npc)
    {
        if (npc.IsNull) return null;
        if (NPCEventHooks.Borrowed(npc) is ModNPC borrowed) return borrowed;
        ModNPC? template = GetNPC(npc.type);
        if (template == null) return null;
        ModNPC? current = Instances.Get(npc.Pointer);
        return current != null && current.RegisteredType == template.RegisteredType ? current : Bind(template, npc);
    }

    static ModNPC Bind(ModNPC template, NPC npc)
    {
        ModNPC instance = template.Entity == npc ? template : template.NewInstance(npc);
        instance.Type = template.Type;
        instance.Hooks = template.Hooks;
        instance.Extra = template.Extra;
        Instances.Set(npc.Pointer, instance);
        return instance;
    }

    /// <summary>
    /// Alinha o template com o tipo registrado, como o tML faz em
    /// <c>ModNPC.Register</c> (<c>NPC.type = NPCLoader.Register(this)</c>) e em
    /// <c>NPCLoader.SetDefaults(npc, createModNPC: false)</c> — que so
    /// redimensiona <c>buffImmune</c> e deixa o resto para o mod.
    ///
    /// O template nasce em <c>InitTemplateInstance</c> (antes do Register), com
    /// <c>type</c> zerado. No aparelho isso deixava <c>ModNPC.NPC.type == 0</c>,
    /// <c>NPC.ModNPC == null</c> e <c>NPCID.Sets.SpecificDebuffImmunity[0]</c>
    /// como destino de todo <c>AddDebuffImmunities</c>.
    /// </summary>
    internal static void PrepareTemplate(ModNPC npc)
    {
        NPC template = npc.Entity;
        if (template == null || template.IsNull) return;
        if (template.type != npc.Type) template.type = npc.Type;
        // tML: CreateTemplateEntity() => new NPC() { ModNPC = this }
        Instances.Set(template.Pointer, npc);
        // tML cresce a tabela do template so ate BuffCount (NPCLoader.cs:208-221,
        // ramo createModNPC:false). `Length` le o cabecalho do array nativo e
        // estoura com ponteiro nulo, e `Resized` devolve nulo se o array de
        // origem tambem for: nos dois casos o certo e deixar como esta.
        int count = BuffLoader.BuffCount;
        if (count > 0 && !template.buffImmune.IsNull && template.buffImmune.Length < count)
        {
            GameArray<bool> grown = template.buffImmune.Resized(count);
            if (!grown.IsNull) template.buffImmune = grown;
        }
    }

    internal static void Register(ModNPC npc)
    {
        string texture = ItemLoader.ResolveTexture(npc, npc.Texture, true);
        string displayName = DisplayNames(npc);
        int type;
        byte* mod = Host.Utf8(npc.Mod.Uid), name = Host.Utf8(npc.Name), tex = Host.Utf8(texture), dn = Host.Utf8(displayName);
        try { type = Host.Native()->RegisterNPC(mod, name, tex, dn); }
        finally
        {
            Host.FreeUtf8(mod);
            Host.FreeUtf8(name);
            Host.FreeUtf8(tex);
            Host.FreeUtf8(dn);
        }
        if (type == -1) throw new InvalidOperationException($"{npc.FullName}: the mod already has an NPC named '{npc.Name}'");
        if (type < 0) throw new InvalidOperationException($"{npc.FullName}: registration failed ({type})");
        npc.Type = type;
        // tML grava o id registrado no PROPRIO template: `NPC.type =
        // NPCLoader.Register(this)` (ModNPC.cs:124). Sem isso todo mod lia
        // `NPC.type == 0` dentro de SetStaticDefaults e escrevia as tabelas
        // (Main.npcFrameCount, NPCID.Sets.*, NPCBestiaryDrawOffset) na linha 0
        // — era dai que vinha o "same key has already been added. Key: 0".
        PrepareTemplate(npc);
        npc.Hooks = NPCEventHooks.Install(npc.GetType(), typeof(ModNPC), type);
        npc.Extra = NPCExtraHooks.Install(npc.GetType(), typeof(ModNPC));
        NPCHappinessHooks.Want(npc.GetType(), typeof(ModNPC));
        if (npc.DrawOffsetY != 0) NPCDrawOffsetHooks.Want(type);
        ReserveBossHead(npc);
        NPCHeadLoader.Register(npc);
        lock (Gate)
        {
            int index = type - VanillaCount;
            ModNPC?[] all = npcs;
            if (index >= all.Length)
            {
                var grown = new ModNPC?[index + 1];
                Array.Copy(all, grown, all.Length);
                all = grown;
            }
            all[index] = npc;
            Volatile.Write(ref npcs, all);
            EnsureHooked();
        }
    }

    internal static void RegisterGlobal(GlobalNPC global)
    {
        Globals.Add(global);
        global.Hooks = NPCEventHooks.Install(global.GetType(), typeof(GlobalNPC), -1);
        global.Events = NPCEventHooks.Events(global.Hooks) & NPCEventHooks.PerType;
        global.Extra = NPCExtraHooks.Install(global.GetType(), typeof(GlobalNPC));
        NPCHappinessHooks.Want(global.GetType(), typeof(GlobalNPC));
        NPCEventHooks.Want(-1, 64);
        ItemHooks.Once("NPCDefaultsScaling", () => On_NPC.ScaleStats += NPCDefaultsHooks.BeforeScaling);
        EnsureHooked();
    }

    internal static void WantDefaults() => EnsureHooked(false);

    static void EnsureHooked(bool clones = true)
    {
        lock (Gate)
        {
            if (!hooked)
            {
                On_NPC.SetDefaults += NPCDefaultsHooks.SetDefaults;
                Host.Native()->OnNPCsInstalled(&Installed);
                hooked = true;
            }
            if (clones && !cloneHooked)
            {
                On_NPC.Clone += Clone;
                cloneHooked = true;
            }
        }
    }

    static string DisplayNames(ModNPC npc)
    {
        if (npc.GetType().GetProperty(nameof(ModNPC.DisplayName))!.DeclaringType == typeof(ModNPC))
        {
            var texts = LocalizationLoader.Texts(npc.Mod, npc.GetLocalizationKey(nameof(ModNPC.DisplayName)));
            if (texts.Count > 0) return LocalizationLoader.Pack(texts);
        }
        return npc.DisplayName.IsNull ? "" : npc.DisplayName.Value ?? "";
    }

    internal static void SetDefaults(NPC npc, ModNPC? template)
    {
        ModNPC? instance = null;
        if (template != null) instance = Bind(template, npc);
        else if (Instances.Count > 0) Instances.Set(npc.Pointer, null);
        BossBarLoader.Forget(npc);
        if (npc.type <= 0)
        {
            Globals.Clear(npc);
            return;
        }
        NPCImmunity.Apply(npc);
        GlobalNPC?[]? slots = Globals.Begin(npc);
        bool[] early = slots == null ? Array.Empty<bool>() : new bool[slots.Length];
        for (int i = 0; i < early.Length; i++)
        {
            early[i] = slots![i] != null;
            if (slots[i] is GlobalNPC global)
            {
                global.Hooks = Globals.Templates[i].Hooks;
                global.Extra = Globals.Templates[i].Extra;
            }
        }
        if (instance != null)
        {
            try { instance.SetDefaults(); }
            catch (Exception e) { ItemHooks.Report($"{instance.FullName}.SetDefaults", e); }
            if (instance.Music >= 0) MusicHooks.Install();
        }
        for (int i = 0; i < early.Length; i++)
        {
            if (early[i]) SetGlobalDefaults(npc, slots![i]!);
        }
        Globals.Finish(npc, slots);
        for (int i = 0; i < early.Length; i++)
        {
            if (!early[i] && slots![i] is GlobalNPC global)
            {
                global.Hooks = Globals.Templates[i].Hooks;
                global.Extra = Globals.Templates[i].Extra;
                SetGlobalDefaults(npc, global);
            }
        }
        WantEvents(npc, slots);
        if (instance?.AIType is > 0) NPCEventHooks.Want(npc.type, 1);
        if (instance?.AnimationType is > 0) NPCEventHooks.Want(npc.type, 2);
        NPCUIHooks.Spawned(npc);
    }

    static void WantEvents(NPC npc, GlobalNPC?[]? slots)
    {
        if (slots == null) return;
        GlobalNPC[] templates = Globals.Templates;
        uint events = 0;
        for (int i = 0; i < slots.Length && i < templates.Length; i++)
            if (slots[i] != null) events |= templates[i].Events;
        if (events == 0) return;
        try { NPCEventHooks.Want(npc.type, events); }
        catch (Exception e) { ItemHooks.Report("NPC events", e); }
    }

    static void SetGlobalDefaults(NPC npc, GlobalNPC global)
    {
        try { global.SetDefaults(npc); }
        catch (Exception e) { ItemHooks.Report($"{global.FullName}.SetDefaults", e); }
    }

    internal static void Restore(NPC npc, ModNPC? instance, GlobalNPC?[]? globals)
    {
        Instances.Set(npc.Pointer, instance);
        Globals.Restore(npc, globals);
    }

    static GameObject Clone(On_NPC.orig_Clone orig, NPC self)
    {
        GameObject result = orig(self);
        var copy = new NPC(result.Pointer);
        if (copy.IsNull || copy == self) return result;
        ModNPC? source = Instances.Get(self.Pointer);
        if (source != null && source.RegisteredType == copy.type)
        {
            try
            {
                ModNPC cloned = source.Clone(copy);
                cloned.Type = source.Type;
                Instances.Set(copy.Pointer, cloned);
            }
            catch (Exception e) { ItemHooks.Report($"{source.FullName}.Clone", e); }
        }
        Globals.Clone(self, copy);
        return result;
    }

    [UnmanagedCallersOnly]
    static void Installed(int first, int last)
    {
        NPCImmunity.Resize();
        Log.Info($"NPCs de mod: gancho gerenciado comecou ({first}..{last})");
        for (int type = first; type <= last; type++)
        {
            ModNPC? npc = GetNPC(type);
            if (npc == null) continue;
            // O SetupContent do mod pode derrubar o processo (AV); o proxy de
            // textura, o `NPC.frame` e o `SetDefaults` vem depois, cada um com
            // a sua linha, para a ultima do arquivo apontar o passo exato.
            // E, se ele derrubou numa carga anterior, o SetupResume pula este
            // tipo para o proximo poder rodar (ver Interop/SetupResume.cs).
            SetupResume.Prime(npc.Mod);
            string resumeKey = "NPC:" + npc.FullName;
            bool resumeSkip = SetupResume.Skip(npc.Mod, resumeKey);
            Log.Info($"NPCs de mod: {type} {npc.FullName}: SetStaticDefaults" + (resumeSkip ? " (PULADO: crash anterior)" : ""));
            try
            {
                if (npc.Entity.IsNull)
                {
                    NPC template = NPC.New();
                    TemplatePins.Add(new GameHandle(template.Pointer));
                    npc.Entity = template;
                    template.SetDefaults(type, default);
                }
                PrepareTemplate(npc);
                if (resumeSkip) Log.Warn($"NPCs de mod: {type} {resumeKey}: SetStaticDefaults pulado");
                else
                {
                    SetupResume.Enter(npc.Mod, resumeKey);
                    npc.SetupContent();
                    SetupResume.Leave(npc.Mod);
                }
                int frames = Math.Max(1, Main.npcFrameCount[type]);
                Host.Native()->SetNPCFrames(type, frames);
                var texture = TextureAssets.Npc[type].Value;
                npc.NPC.frame = new Rectangle(0, 0, texture.Width, texture.Height / frames);
            }
            catch (Exception e) { Log.Error($"NPC {npc.FullName}: SetStaticDefaults threw: {e}"); }
            Log.Info($"NPCs de mod: {type} {npc.FullName}: ok");
        }
        Log.Info("NPCs de mod: gancho gerenciado terminou");
    }
}
