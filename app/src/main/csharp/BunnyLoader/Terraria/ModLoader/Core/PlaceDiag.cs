using System;
using BunnyLoader;

namespace Terraria.ModLoader.Core;

/**
 * DIAGNOSTICO TEMPORARIO.
 *
 * Sintoma: o item (wood/cobweb) sai do inventario sem nada aparecer no mundo e
 * SEM nenhuma chamada a WorldGen.PlaceTile. Aqui ficam quatro sondas:
 *   1. [diag] consumiu   -> stack do HeldItem caiu durante Player.ItemCheck
 *                           (com createTile/createWall + pilha gerenciada);
 *   2. [diag] PlaceThing -> o jogo entrou no caminho de colocacao? quanto custou?
 *   3. [diag] CanPlace   -> a validacao liberou ou barrou, e para qual tipo;
 *   4. [diag] PlaceTile  -> o bloco entrou mesmo no mundo?
 *
 * Se "consumiu" aparece sem PlaceThing/PlaceTile, quem come o item nao e o
 * caminho de colocacao (craft, venda, ammo ou codigo do mod).
 */
internal static class PlaceDiag
{
    internal static void Install()
    {
        ItemHooks.Once("DiagItemCheck", () => On_Player.ItemCheck += ItemCheck);
        ItemHooks.Once("DiagPlaceTile", () => On_WorldGen.PlaceTile += PlaceTile);
        ItemHooks.Once("DiagPlaceThingTiles", () => On_Player.PlaceThing_Tiles += PlaceThingTiles);
        ItemHooks.Once("DiagCanPlace", () => On_Player.PlaceThing_Tiles_BlockPlacementForAssortedThings += CanPlace);
        ItemHooks.Once("DiagTryPlacing", () => On_Player.PlaceThing_Tiles_TryPlacing += TryPlacing);
        ItemHooks.Once("DiagPlaceIt", () => On_Player.PlaceThing_Tiles_PlaceIt += PlaceIt);
        ItemHooks.Once("DiagStartUse", () => On_Player.ItemCheck_StartActualUse += StartUse);
        ItemHooks.Once("DiagFigureOut", () => On_Player.FigureOutWhatToPlace += FigureOut);
    }

    static void FigureOut(On_Player.orig_FigureOutWhatToPlace orig, Player self, Tile targetTile, Item sItem,
        out int tileToCreate, out int previewPlaceStyle, out BunnyLoader.Interop.GameNullable<bool> overrideCanPlace,
        out BunnyLoader.Interop.GameNullable<int> forcedRandom)
    {
        orig(self, targetTile, sItem, out tileToCreate, out previewPlaceStyle, out overrideCanPlace, out forcedRandom);
        if (tileToCreate != lastTileToCreate || Player.tileTargetX != lastTargetX || Player.tileTargetY != lastTargetY)
        {
            lastTileToCreate = tileToCreate;
            lastTargetX = Player.tileTargetX;
            lastTargetY = Player.tileTargetY;
            Log.Info($"[diag] FigureOut item={sItem?.type} alvo=({Player.tileTargetX},{Player.tileTargetY}) "
                     + $"tileToCreate={tileToCreate} style={previewPlaceStyle}");
        }
    }

    static string Estado(Player p)
    {
        try { return $"usa={p.controlUseItem} anim={p.itemAnimation} time={p.itemTime} selecionado={p.selectedItem}"; }
        catch (Exception e) { return $"estado=erro({e.GetType().Name})"; }
    }

    static void TryPlacing(On_Player.orig_PlaceThing_Tiles_TryPlacing orig, Player self, int tileToCreate,
        BunnyLoader.Interop.GameNullable<bool> overrideCanPlace, BunnyLoader.Interop.GameNullable<int> forcedRandom, TileObject data, int placeStyle)
    {
        Log.Info($"[diag] TryPlacing tipo={tileToCreate} alvo=({Player.tileTargetX},{Player.tileTargetY}) {Estado(self)}");
        orig(self, tileToCreate, overrideCanPlace, forcedRandom, data, placeStyle);
    }

    static TileObject PlaceIt(On_Player.orig_PlaceThing_Tiles_PlaceIt orig, Player self, bool newObjectType, TileObject data, int tileToCreate)
    {
        Log.Info($"[diag] PlaceIt tipo={tileToCreate} novoObjeto={newObjectType} alvo=({Player.tileTargetX},{Player.tileTargetY}) {Estado(self)}");
        return orig(self, newObjectType, data, tileToCreate);
    }

    static void StartUse(On_Player.orig_ItemCheck_StartActualUse orig, Player self, Item sItem)
    {
        string desc = Describe(sItem);
        int antes = -1;
        try { antes = sItem.stack; } catch { }
        orig(self, sItem);
        try
        {
            /** Item de mod que nao some ao usar: mostra consumivel/bait/jarra. */
            if (sItem.type >= ID.ItemID.Count)
                Log.Info($"[itemdiag] usou tipo={sItem.type} bait={sItem.bait} consumivel={sItem.consumable} "
                         + $"stack {antes}->{sItem.stack} {Estado(self)}");
        }
        catch { }
    }

    static string Describe(Item item)
    {
        try
        {
            if (item == null || item.IsNull) return "item=vazio";
            return $"item={item.type} createTile={item.createTile} createWall={item.createWall} useStyle={item.useStyle} ammo={item.ammo} consumivel={item.consumable}";
        }
        catch (Exception e) { return $"item=erro({e.GetType().Name})"; }
    }

    static void ItemCheck(On_Player.orig_ItemCheck orig, Player self)
    {
        if (self.whoAmI != Main.myPlayer)
        {
            orig(self);
            return;
        }
        int before = -1;
        string desc = "";
        try
        {
            Item held = self.HeldItem;
            if (held != null && !held.IsNull)
            {
                before = held.stack;
                desc = Describe(held);
            }
        }
        catch { }

        orig(self);

        try
        {
            Item held = self.HeldItem;
            int after = held == null || held.IsNull ? 0 : held.stack;
            if (before > 0 && after < before)
            {
                Log.Info($"[diag] consumiu {desc} stack {before}->{after} alvo=({Player.tileTargetX},{Player.tileTargetY}) {Estado(self)}");
            }
        }
        catch { }
    }

    static int mouseXLast = int.MinValue, mouseYLast = int.MinValue, chamadas;
    static int lastTileToCreate = int.MinValue, lastTargetX = int.MinValue, lastTargetY = int.MinValue;

    static void PlaceThingTiles(On_Player.orig_PlaceThing_Tiles orig, Player self, bool action)
    {
        int before = -1;
        string desc = "";
        try
        {
            Item held = self.HeldItem;
            if (held != null && !held.IsNull)
            {
                before = held.stack;
                desc = Describe(held);
            }
        }
        catch { }
        int mx = -1, my = -1;
        bool ml = false, smart = false;
        try { mx = Main.mouseX; my = Main.mouseY; ml = Main.mouseLeft; smart = Main.SmartCursorIsUsed; } catch { }
        try { orig(self, action); }
        finally
        {
            chamadas++;
            /** Só loga quando o "mouse" muda de posição (ou 1x por segundo) para
             *  descobrir se o dedo chega em Main.mouseX nesta build. */
            bool mudou = mx != mouseXLast || my != mouseYLast;
            if (mudou || chamadas % 60 == 0)
            {
                mouseXLast = mx;
                mouseYLast = my;
                try
                {
                    Item held = self.HeldItem;
                    int after = held == null || held.IsNull ? 0 : held.stack;
                    Log.Info($"[diag] PlaceThing {desc} acao={action} alvo=({Player.tileTargetX},{Player.tileTargetY}) "
                             + $"mouse=({mx},{my}) mouseLeft={ml} smart={smart} stack {before}->{after}");
                }
                catch { }
            }
        }
    }

    static bool CanPlace(On_Player.orig_PlaceThing_Tiles_BlockPlacementForAssortedThings orig, Player player, bool canPlace)
    {
        bool result = orig(player, canPlace);
        try
        {
            Log.Info($"[diag] CanPlace {Describe(player.HeldItem)} entra={canPlace} sai={result} alvo=({Player.tileTargetX},{Player.tileTargetY})");
        }
        catch { }
        return result;
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
