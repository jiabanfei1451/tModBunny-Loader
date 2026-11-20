using Microsoft.Xna.Framework;

namespace Terraria.Utilities;

/// <summary>
/// <c>TerrariaHooks</c> do 1.4.4.9 publica <c>NPCUtils.TargetClosestBetsy</c>
/// (o FargowiltasSouls reimplementa o alvo da Betsy em
/// <c>FargowiltasSouls.FargowiltasSouls.TargetClosestBetsy</c> e assina o evento
/// em <c>LoadDetours</c>/<c>UnloadDetours</c>). Sem o tipo, os dois métodos não
/// JITavam (<c>-> Terraria.Utilities.On_NPCUtils</c> no modsandbox).
///
/// Assinaturas copiadas do IL do mod: <c>orig_TargetClosestBetsy.Invoke(NPC,
/// Boolean, Nullable&lt;Vector2&gt;)</c> devolve <c>void</c> — no 1.4.4 o método
/// só ajusta <c>npc.target</c>. O evento é vazio de propósito: a lógica da Betsy
/// fica com o alvo padrão da vanilla.
/// </summary>
public static partial class On_NPCUtils
{
    public delegate void orig_TargetClosestBetsy(NPC npc, bool ignoreTiles, Vector2? targetCenter);

    public delegate void hook_TargetClosestBetsy(orig_TargetClosestBetsy orig, NPC npc, bool ignoreTiles, Vector2? targetCenter);

    // evento nunca disparado (proposital)
    public static event hook_TargetClosestBetsy? TargetClosestBetsy
    {
        add { }
        remove { }
    }
}
