using System;
using BunnyLoader;

namespace Terraria.ModLoader.Core;

/**
 * DIAGNOSTICO TEMPORARIO.
 *
 * Sintoma: ao tocar num ponto vazio o bloco sai do inventario e nada aparece
 * no mundo. Aqui ficam tres sondas que respondem, em ordem:
 *   1. [diag] consumiu  -> o stack caiu durante o Player.ItemCheck do jogador;
 *   2. [diag] PlaceTile -> o jogo tentou colocar (e o que ele respondeu);
 *   3. depois: tile=.. ativo=.. -> o bloco entrou mesmo no mundo?
 *
 * Se aparecer "consumiu" sem nenhuma linha "PlaceTile" logo antes, quem come o
 * item nao e o caminho de colocacao (provavelmente o mod, ex.: InstaBridge).
 */
internal static class PlaceDiag
{
    internal static void Install()
    {
        ItemHooks.Once("DiagItemCheck", () => On_Player.ItemCheck += ItemCheck);
        ItemHooks.Once("DiagPlaceTile", () => On_WorldGen.PlaceTile += PlaceTile);
    }

    static void ItemCheck(On_Player.orig_ItemCheck orig, Player self)
    {
        if (self.whoAmI != Main.myPlayer)
        {
            orig(self);
            return;
        }
        int before = -1, type = -1;
        try
        {
            Item held = self.HeldItem;
            if (held != null && !held.IsNull)
            {
                before = held.stack;
                type = held.type;
            }
        }
        catch { }

        orig(self);

        try
        {
            Item held = self.HeldItem;
            int after = held == null || held.IsNull ? 0 : held.stack;
            if (before > 0 && after < before)
                Log.Info($"[diag] consumiu tipo={type} stack {before}->{after} alvo=({Player.tileTargetX},{Player.tileTargetY})");
        }
        catch { }
    }

    static bool PlaceTile(On_WorldGen.orig_PlaceTile orig, int i, int j, int type, bool mute, bool force, int player, int style)
    {
        bool result = orig(i, j, type, mute, force, player, style);
        try
        {
            int depois = -1;
            bool ativo = false;
            if (i >= 0 && j >= 0 && i < Main.maxTilesX && j < Main.maxTilesY)
            {
                Tile tile = Main.tile[i, j];
                ativo = tile.HasTile;
                depois = tile.type;
            }
            Log.Info($"[diag] PlaceTile tipo={type} ({i},{j}) force={force} -> {result} | depois ativo={ativo} tipo={depois}");
        }
        catch (Exception e) { Log.Info($"[diag] PlaceTile (log) {e.GetType().Name}"); }
        return result;
    }
}
