using System;
using System.Runtime.InteropServices;
using System.Threading;
using BunnyLoader;
using Terraria.DataStructures;
using Terraria.GameContent;

namespace Terraria.ModLoader.Core;

[Flags]
internal enum NPCMethod : ulong
{
    PreAI = 1, AI = 2, PostAI = 4, FindFrame = 8, ResetEffects = 16, CheckActive = 32,
    CheckDead = 64, HitEffect = 128, UpdateLifeRegen = 256, OnSpawn = 512,
    ApplyDifficultyAndPlayerScaling = 1024, SetDefaultsFromNetId = 2048,
    CanBeHitByItem = 4096, CanCollideWithPlayerMeleeAttack = 8192, ModifyIncomingHit = 16384,
    ModifyHitByItem = 32768, OnHitByItem = 65536, CanBeHitByProjectile = 131072, ModifyHitByProjectile = 262144,
    OnHitByProjectile = 524288, CanHitPlayer = 1048576, ModifyHitPlayer = 2097152, OnHitPlayer = 4194304,
    CanHitNPC = 8388608, CanBeHitByNPC = 16777216, ModifyHitNPC = 33554432, OnHitNPC = 67108864,
    GetAlpha = 1UL << 40, DrawEffects = 1UL << 41, PreDraw = 1UL << 42, PostDraw = 1UL << 43, DrawBehind = 1UL << 44,
    BossHeadSlot = 1UL << 45, BossHeadRotation = 1UL << 46, BossHeadSpriteEffects = 1UL << 47,
    SendExtraAI = 1UL << 48, ReceiveExtraAI = 1UL << 49, SetBestiary = 1UL << 60,
    PickEmote = 1UL << 61, EmoteBubblePosition = 1UL << 62,
    SpawnChance = 1UL << 37, SpawnNPC = 1UL << 38, EditSpawnRate = 1UL << 39,
    EditSpawnRange = 1UL << 50, EditSpawnPool = 1UL << 51,
    TownSpawn = 1UL << 52, TownRoom = 1UL << 53, TownNames = 1UL << 54, TownChat = 1UL << 55,
    TownButtons = 1UL << 56, TownShops = 1UL << 57, TownAttack = 1UL << 58, TownAttackDraw = 1UL << 59,
    PreKill = 1UL << 32, OnKill = 1UL << 33, BossLoot = 1UL << 34,
    ModifyNPCLoot = 1UL << 35, ModifyGlobalLoot = 1UL << 36,
}

internal static unsafe class NPCEventHooks
{
    static readonly object Gate = new();
    static uint[] wanted = Array.Empty<uint>();
    static uint all;
    internal struct BorrowContext
    {
        public IntPtr Pointer;
        public ModNPC? Instance;
    }

    [ThreadStatic] internal static BorrowContext Borrowing;
    [ThreadStatic] static (IntPtr Pointer, NPC.HitInfo Hit)? hitting;

    internal static ModNPC? Borrowed(NPC npc) => Borrowing.Pointer == npc.Pointer ? Borrowing.Instance : null;

    internal static NPCMethod Install(Type type, Type baseType, int npcType)
    {
        NPCMethod flags = 0;
        foreach (NPCMethod method in Enum.GetValues<NPCMethod>())
            if (ItemHooks.Overrides(type, baseType, method.ToString())) flags |= method;
        // AI/PreAI/PostAI e FindFrame sao virtuais na base (PreAI => true, os
        // outros vazios), e o dispatcher agora chama os tres sem consultar o
        // mapa de overrides. O bit do evento, porem, continua vindo daquele
        // mapa: se ele falha, o nativo nunca chama o callback e o NPC fica
        // parado sem gravidade. Como a chamada e inofensiva, o evento entra
        // sempre para NPC de mod e para GlobalNPC.
        if (baseType == typeof(ModNPC) || baseType == typeof(GlobalNPC))
            flags |= NPCMethod.PreAI | NPCMethod.AI | NPCMethod.PostAI;
        if (baseType == typeof(ModNPC)) flags |= NPCMethod.FindFrame;
        uint events = 0;
        if ((flags & (NPCMethod.PreAI | NPCMethod.AI | NPCMethod.PostAI)) != 0) events |= 1;
        if ((flags & NPCMethod.FindFrame) != 0) events |= 2;
        if ((flags & NPCMethod.ResetEffects) != 0) events |= 4;
        if ((flags & NPCMethod.CheckActive) != 0) events |= 8;
        if ((flags & NPCMethod.UpdateLifeRegen) != 0) events |= 16;
        if ((flags & NPCMethod.ApplyDifficultyAndPlayerScaling) != 0) events |= 32;
        if ((flags & NPCMethod.CheckDead) != 0) events |= 128;
        if (events != 0) Want(npcType, events);
        if ((flags & NPCMethod.HitEffect) != 0) ItemHooks.Once("NPCHitEffect", () => On_NPC.HitEffect += HitEffect);
        if ((flags & NPCMethod.OnSpawn) != 0) ItemHooks.Once("NPCSpawn", () => On_NPC.NewNPC += NewNPC);
        if ((flags & NPCMethod.SetDefaultsFromNetId) != 0) ItemHooks.Once("NPCVariantDefaults", () => On_NPC.SetDefaultsFromNetId += VariantDefaults);
        ItemHitHooks.InstallNPC(flags);
        ProjectileHitHooks.InstallNPC(flags);
        NPCContactHooks.Install(flags);
        NPCDrawHooks.Install(flags);
        NPCNetHooks.Install(flags);
        EmoteBubbleHooks.InstallNPC(flags);
        NPCLootHooks.Install(flags);
        NPCSpawnHooks.Install(type, baseType, npcType, ref flags);
        return flags;
    }

    /// <summary>
    /// Eventos que o binário do jogo não permite instalar (o ponto de gancho
    /// não foi encontrado). Ficam marcados para não tentar de novo a cada NPC.
    /// </summary>
    static uint unavailable;

    internal static void Want(int type, uint events)
    {
        uint[] masks = Volatile.Read(ref wanted);
        uint have = type < 0 ? Volatile.Read(ref all) : (uint)type < (uint)masks.Length ? masks[type] | Volatile.Read(ref all) : Volatile.Read(ref all);
        if ((have & events) == events) return;
        lock (Gate)
        {
            // Já sabemos que este binário não tem o ponto de gancho: seguir SEM
            // o evento. Antes isto lançava e derrubava o mod inteiro — o
            // Fargowiltas não chegava nem a registrar o conteúdo por causa de
            // um único `bl` que mudou de deslocamento.
            if ((unavailable & events) == events) return;
            if (Host.Native()->NPCEvents(type, events, &Event) == 0)
            {
                unavailable |= events;
                Log.Warn($"NPC events: sem ponto de gancho para 0x{events:X} neste binário; seguindo sem ele");
                return;
            }
            if (type < 0) Volatile.Write(ref all, all | events);
            else
            {
                if (type >= wanted.Length)
                {
                    var grown = new uint[Math.Max(type + 1, wanted.Length * 2)];
                    Array.Copy(wanted, grown, wanted.Length);
                    Volatile.Write(ref wanted, grown);
                }
                wanted[type] |= events;
            }
            // Sem isto, "NPC parado" nao distingue evento nao registrado de
            // gancho nativo que nunca chama de volta.
            Log.Info($"NPC events: type={type} 0x{events:X} registrado");
        }
    }

    [UnmanagedCallersOnly]
    static int Event(int kind, IntPtr pointer, void* args)
    {
        NPC npc = new(pointer);
        try
        {
            switch (kind)
            {
                case 0: NPCLoader.NPCAI(npc); break;
                case 1:
                    int frames = Math.Max(1, Main.npcFrameCount[npc.type]);
                    NPCLoader.FindFrame(npc, Main.dedServ ? 1 : TextureAssets.Npc[npc.type].Value.Height / frames);
                    break;
                case 2: NPCLoader.ResetEffects(npc); break;
                case 3: return NPCLoader.CheckActive(npc) ? 1 : 0;
                case 4: NPCLoader.UpdateLifeRegen(npc, ref *(int*)args); break;
                case 5:
                    NPCLoader.ApplyDifficultyAndPlayerScaling(npc, ((int*)args)[0], ((float*)args)[1], ((float*)args)[2]);
                    break;
                case 6: NPCDefaultsHooks.BeforeVanillaTail(npc); break;
                case 7: return NPCLoader.CheckDead(npc) ? 1 : 0;
            }
        }
        catch (Exception e) { ItemHooks.Report("NPC lifecycle", e); }
        return 1;
    }

    static void HitEffect(On_NPC.orig_HitEffect orig, NPC npc, int hitDirection, double dmg)
    {
        NPC.HitInfo hit = hitting is { } context && context.Pointer == npc.Pointer ? context.Hit : LegacyHit(hitDirection, dmg, null);
        orig(npc, hitDirection, dmg);
        NPCLoader.HitEffect(npc, in hit);
    }

    internal static NPC.HitInfo LegacyHit(int direction, double damage, bool? instantKill)
    {
        bool instant = instantKill ?? damage == 9999;
        int value = instant ? 1 : (int)damage;
        return new NPC.HitInfo { DamageType = DamageClass.Default, SourceDamage = value, Damage = value, HitDirection = direction, InstantKill = instant };
    }

    internal readonly struct HitContext : IDisposable
    {
        readonly (IntPtr Pointer, NPC.HitInfo Hit)? previous;

        internal HitContext(NPC npc, NPC.HitInfo hit)
        {
            previous = hitting;
            hitting = (npc.Pointer, hit);
        }

        public void Dispose() => hitting = previous;
    }

    internal static void Hit(NPC npc, NPC.HitInfo hit)
    {
        using var context = new HitContext(npc, hit);
        npc.HitEffect_(hit.HitDirection, hit.InstantKill ? 9999 : hit.Damage);
    }

    static int NewNPC(On_NPC.orig_NewNPC orig, IEntitySource source, int X, int Y, int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target)
    {
        // Um item de template (so SetDefaults, sem UseItem) nao tem como pedir NPC.
        // Se um NPC aparece ao brandir, o pedido veio de outro lugar: esta linha
        // diz o tipo e as duas primeiras molduras do chamador.
        Log.Info($"[mod] NewNPC type={Type} pos={X},{Y} src={source?.GetType().Name ?? "null"}{Held()} por {Caller()}");
        int index = orig(source, X, Y, Type, Start, ai0, ai1, ai2, ai3, Target);
        if ((uint)index < 200u && Main.npc[index].active)
        {
            // v75: faccao do NPC recem-criado. "A Dryad virou hostil" so' da' para
            // separar em duas causas olhando isto: se friendly ja' nasce false o
            // problema esta' no SetDefaults/NewNPC; se nasce true e some depois, e'
            // algum gancho (hit/hurt) devolvendo errado. Antes nao havia como saber.
            NPC fresh = Main.npc[index];
            Log.Info($"[mod] NewNPC -> #{index} type={fresh.type} friendly={fresh.friendly} town={fresh.townNPC} target={fresh.target} vida={fresh.lifeMax} mao={Held()}");
            NPCLoader.OnSpawn(fresh, source);
            Log.Info($"[mod] NewNPC pos-ganchos #{index} type={fresh.type} friendly={fresh.friendly} town={fresh.townNPC} target={fresh.target}");
        }
        return index;
    }

    // v66: o que o jogador tem na mao NESTE instante. Se o item segurado nao
    // for o mesmo que o log de UseItem diz, o mapeamento tipo->ModItem do shim
    // esta' errado (era a suspeita de "contrato da camada de compatibilidade").
    static string Held()
    {
        try
        {
            Player me = Main.player[Main.myPlayer];
            if (me.IsNull) return " mao=nulo";
            Item it = me.inventory[me.selectedItem];
            string name = it.IsNull ? "nulo" : (ItemHooks.ModItemOf(it)?.FullName ?? ("item#" + it.type));
            return $" mao={name} it={me.itemTime}/{me.itemAnimation} u={ItemHooks.UseDepth} c={me.Center.X:0},{me.Center.Y:0}";
        }
        catch (Exception e) { return " mao=?" + e.GetType().Name; }
    }

    static string Caller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(3, false);
            string text = "";
            for (int i = 0; i < trace.FrameCount && i < 12; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method == null) continue;
                if (text.Length > 0) text += " < ";
                text += method.DeclaringType?.Name + "." + method.Name;
            }
            return text.Length == 0 ? "?" : text;
        }
        catch { return "?"; }
    }

    static void VariantDefaults(On_NPC.orig_SetDefaultsFromNetId orig, NPC npc, int id, NPCSpawnParams spawnparams)
    {
        orig(npc, id, spawnparams);
        foreach (GlobalNPC? global in NPCLoader.Slots(npc))
        {
            if (global == null || (global.Hooks & NPCMethod.SetDefaultsFromNetId) == 0) continue;
            try { global.SetDefaultsFromNetId(npc); }
            catch (Exception e) { ItemHooks.Report($"{global.FullName}.SetDefaultsFromNetId", e); }
        }
    }
}
