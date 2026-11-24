using System;
using System.Collections.Generic;
using Terraria.ID;

namespace Terraria.ModLoader.Core;

/**
 * NPC de cidade nao bate em NPC de cidade.
 *
 * O vanilla ja evita isso por conta propria: o laco de alvo em AI_007 filtra
 * "!Main.npc[m].friendly", o golpe corpo a corpo em AI_007 filtra
 * "!nPC2.friendly", e CanBeChasedBy exige "!friendly". O problema no Android
 * e' que o friendly de um NPC de cidade as vezes chega falso — ou porque o mod
 * nao o ligou (so' ligou townNPC), ou porque algum gancho (EnergizedGlobalNPC
 * .PreAI, DoALittleTrolling) o desligou no meio da partida. Nesse momento o
 * vanilla passa a enxergar o cidadao como inimigo e os cidadaos comecam a se
 * bater.
 *
 * Quem apaga o friendly (v24, log 23:35:29.351) e' o proprio vanilla. O
 * Fargowiltas, com a opcao "CatchNPCs" ligada, marca o NPC de cidade em
 * Main.npcCatchable; a partir dai' SubAI_HandleTemporaryCatchableNPCPlayer
 * Invulnerability escreve "friendly = false" A CADA FRAME, porque a condicao
 * externa do vanilla e' Main.npcCatchable[type], nao o contador de imunidade.
 * O tML conserta isso no patch (Fix #3299) restaurando
 * catchableNPCOriginallyFriendly; esse campo nao existe no dump do jogo
 * Android, entao a restauracao e' feita aqui por fora (porta 4).
 *
 * As portas 1-3 NAO mexem no friendly (v75 tentou restaurar e voltou atras):
 * recusam o golpe so' quando atacante E alvo sao do time do jogador. Invasao,
 * goblin, zumbi e chefe continuam machucando NPC de cidade normalmente, que e'
 * comportamento de vanilla. A porta 4 religa o friendly do NPC de cidade.
 *
 * Quatro portas, porque o caminho de dano NPC->NPC se ramifica:
 *  1. NPCLoader.CanHitNPC      — laco de alvo (NPC.cs.patch:1330) e o scan de
 *                                corpo a corpo (:3055). E' a porta principal e
 *                                nao depende de nenhum gancho estar instalado.
 *  2. NPCContactHooks          — mod que chama BeHurtByOtherNPC direto.
 *  3. StrikeNPCNoInteraction   — o golpe do cidadao (:1572) NAO passa por
 *                                CanHitNPC, so' pelo filtro "!friendly". Como e'
 *                                justamente esse filtro que falha, precisa de
 *                                uma porta propria. O atacante vem do contexto
 *                                de AI_007 (o cidadao que esta' no laco).
 *  4. SubAI_HandleTemporary... — devolve o friendly que o vanilla apaga por
 *                                causa de Main.npcCatchable (Fix #3299).
 */
internal static class NPCFriendlyGate
{
    /** "Do time do jogador": cidade, friendly, ou modNPC que se declara cidade. */
    internal static bool Friendly(NPC npc)
    {
        if (npc.IsNull) return false;
        // Os dois campos nativos sao os mais baratos e cobrem 99% dos casos.
        if (npc.townNPC || npc.friendly) return true;
        return ActsLikeTown(npc.type);
    }

    static bool ActsLikeTown(int type)
    {
        if (type < NPCID.Count) return false;
        bool[] set = NPCID.Sets.ActsLikeTownNPC;
        return (uint)type < (uint)set.Length && set[type];
    }

    /** true = descarta o golpe. Atacante e alvo precisam ser os dois do time. */
    internal static bool Blocks(NPC attacker, NPC victim, string origem)
    {
        try
        {
            if (!Friendly(attacker) || !Friendly(victim)) return false;
            Note(attacker, victim, origem);
            return true;
        }
        catch (Exception e)
        {
            ItemHooks.Report("NPCFriendlyGate.Blocks", e);
            return false;
        }
    }

    /// Um aviso por par (atacante, alvo): o mesmo par batendo todo frame nao vira spam.
    static readonly HashSet<long> seen = new();

    static void Note(NPC attacker, NPC victim, string origem)
    {
        Diagnose(attacker);
        Diagnose(victim);
        long key = ((long)attacker.type << 32) | (uint)victim.type;
        if (!seen.Add(key)) return;
        BunnyLoader.Log.Info($"[hit] NPC de cidade nao bate em NPC de cidade atacante={attacker.type}#{attacker.whoAmI} alvo={victim.type}#{victim.whoAmI} por={origem}");
    }

    /**
     * townNPC ligado com friendly desligado e' o estado que faz o vanilla tratar
     * o cidadao como inimigo. Uma linha por tipo basta para saber se o friendly
     * sumiu (gancho do mod) ou nunca chegou (escrita perdida no IlPatcher).
     */
    static readonly HashSet<int> lone = new();

    static void Diagnose(NPC npc)
    {
        if (!npc.townNPC || npc.friendly) return;
        if (!lone.Add(npc.type)) return;
        BunnyLoader.Log.Info($"[hit] NPC de cidade com friendly=False npc={npc.type}#{npc.whoAmI} town=True like={ActsLikeTown(npc.type)} (algum gancho desligou o friendly)");
    }

    // ------------------------------------------------------------------
    // Contexto do golpe do cidadao (porta 3).
    // ------------------------------------------------------------------

    [ThreadStatic] static NPC swinger;
    [ThreadStatic] static bool swinging;

    internal static void Want()
    {
        ItemHooks.Once("NPCFriendlyGate", () =>
        {
            On_NPC.AI_007_TownEntities += SwingContext;
            On_NPC.StrikeNPCNoInteraction += StrikeGate;
            On_NPC.SubAI_HandleTemporaryCatchableNPCPlayerInvulnerability += CatchableFriendly;
        });
    }

    // ------------------------------------------------------------------
    // Porta 4: devolve o friendly que o vanilla apaga por npcCatchable.
    // ------------------------------------------------------------------

    /// O jogo so' chama este SubAI para NPC alcancavel (bug net). O tML muda a
    /// condicao externa para "catchableNPCTempImmunityCounter > 0" e restaura
    /// catchableNPCOriginallyFriendly; esse campo nao existe no dump Android,
    /// entao o valor "certo" e' deduzido aqui: NPC de cidade sempre volta true.
    static readonly HashSet<int> restored = new();

    static void CatchableFriendly(On_NPC.orig_SubAI_HandleTemporaryCatchableNPCPlayerInvulnerability orig, NPC self)
    {
        orig(self);
        try { Repair(self); }
        catch (Exception e) { ItemHooks.Report("NPCFriendlyGate.CatchableFriendly", e); }
    }

    /** Devolve o friendly do NPC de cidade que o vanilla apagou por npcCatchable. */
    static void Repair(NPC self)
    {
        if (self.IsNull || self.friendly) return;
        if (!self.townNPC && !ActsLikeTown(self.type)) return;
        if (!Catchable(self.type)) return;
        self.friendly = true;
        if (!restored.Add(self.type)) return;
        BunnyLoader.Log.Info($"[hit] friendly restaurado (catchable) npc={self.type}#{self.whoAmI} town=True — o vanilla apagava por Main.npcCatchable");
    }

    /** Main.npcCatchable[type] sem explodir em array nulo ou type fora da faixa. */
    static bool Catchable(int type)
    {
        var set = Main.npcCatchable;
        if (set.IsNull || type < 0) return false;
        return (uint)type < (uint)set.Length && set[type];
    }

    /** Guarda quem esta' no laco de golpe enquanto AI_007 roda. */
    static void SwingContext(On_NPC.orig_AI_007_TownEntities orig, NPC self)
    {
        // O SubAI acima ja' devolve o friendly, mas se ele rodar DEPOIS do laco
        // de alvo nesta mesma passada o cidadao ainda seria visto como inimigo
        // por um frame. Religar aqui, antes do laco, fecha essa janela.
        Repair(self);
        NPC outer = swinger;
        bool outerSwinging = swinging;
        swinger = self;
        swinging = true;
        try { orig(self); }
        finally
        {
            swinger = outer;
            swinging = outerSwinging;
        }
    }

    static int StrikeGate(On_NPC.orig_StrikeNPCNoInteraction orig, NPC self, int Damage, float knockBack, int hitDirection)
    {
        if (!swinging || !Friendly(swinger) || !Friendly(self))
            return orig(self, Damage, knockBack, hitDirection);
        Blocks(swinger, self, "StrikeNPCNoInteraction");
        return 0;
    }
}
