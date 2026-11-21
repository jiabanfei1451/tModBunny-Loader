using System;
using BunnyLoader;
using Terraria.DataStructures;
using Terraria.ObjectData;

namespace Terraria.ModLoader.Core;

/**
 * Remendo do caminho de colocacao de blocos/paredes.
 *
 * Medido no celular (v140-v147): usando um bloco, o jogo chega em
 * `Player.PlaceThing_Tiles` -> `FigureOutWhatToPlace` (que devolve o tipo
 * certo, ex.: 0 = dirt) e para ali: `PlaceThing_Tiles_TryPlacing`, `PlaceIt` e
 * `WorldGen.PlaceTile` nunca sao chamados. Mesmo assim `Player.ItemCheck`
 * consome 1 unidade por ciclo de uso (~233 ms) — o bloco sai do inventario e
 * nada aparece no mundo.
 *
 * O que este arquivo faz: no frame em que o stack caiu e o jogo nao colocou
 * nada, aplica as MESMAS regras do jogo e coloca pelo `WorldGen.PlaceTile` /
 * `PlaceWall`; quando alguma regra barra, devolve a unidade (vanilla tambem
 * nao consumiria).
 *
 * Regras (Player.PlaceThing_Tiles_BlockPlacementForAssortedThings +
 * BlockPlacementIfOverPlayers):
 *   1. dentro do mundo;
 *   2. alcance: |x - centroX| <= Player.tileRangeX + blockRange (idem Y);
 *   3. nao em cima do jogador;
 *   4. apoio: vizinho (4 direcoes) com bloco ou parede;
 *   5. TileLoader/GlobalTile.CanPlace (blocos de mod);
 *   6. quem decide o resto (ocupado, replaceable, estilo) e o WorldGen.
 * Paredes so precisam de 1, 2 e 6.
 */
internal static class PlacementFallback
{
    static bool gamePlaced;
    static bool manual;

    internal static void Install()
    {
        ItemHooks.Once("PlaceFallback", () => On_Player.ItemCheck += ItemCheck);
        ItemHooks.Once("PlaceFallbackTile", () => On_WorldGen.PlaceTile += PlaceTile);
    }

    static bool PlaceTile(On_WorldGen.orig_PlaceTile orig, int i, int j, int type, bool mute, bool force, int player, int style)
    {
        bool result = orig(i, j, type, mute, force, player, style);
        if (result && !manual) gamePlaced = true;
        return result;
    }

    static void ItemCheck(On_Player.orig_ItemCheck orig, Player self)
    {
        if (self.whoAmI != Main.myPlayer)
        {
            orig(self);
            return;
        }
        int before = -1, tipo = -1, tile = -1, parede = -1, estilo = 0;
        try
        {
            Item held = self.HeldItem;
            if (held != null && !held.IsNull)
            {
                before = held.stack;
                tipo = held.type;
                tile = held.createTile;
                parede = held.createWall;
                estilo = held.placeStyle;
            }
        }
        catch
        {
            orig(self);
            return;
        }

        gamePlaced = false;
        orig(self);

        try
        {
            Item agora = self.HeldItem;
            if (agora == null || agora.IsNull) return;
            if (agora.type != tipo || agora.stack >= before) return;
            if (gamePlaced) return;
            if (parede > 0) ColocarParede(self, agora, parede);
            else if (tile >= 0) ColocarTile(self, agora, tile, estilo);
        }
        catch (Exception e) { Log.Warn($"[place] remendo falhou: {e.GetType().Name}: {e.Message}"); }
    }

    static void ColocarTile(Player self, Item item, int tile, int estilo)
    {
        int x = Player.tileTargetX, y = Player.tileTargetY;

        // Objetos multi-tile (a caixa de musica do VCMM e' Style2x2) usam como
        // coordenada o canto de ORIGEM do objeto, nao a celula onde o dedo caiu.
        // Sem esse ajuste `WorldGen.PlaceTile` recusa, o remendo devolve o item
        // e ele so' era consumido depois de varias tentativas — quando o toque
        // caia por acaso na celula de origem. Era o "precisa clicar 3 vezes".
        TileObjectData? data = TileObjectData.GetTileData(tile, estilo);
        if (data is { IsNull: false })
        {
            Point16 origin = data.Origin;
            x -= origin.X;
            y -= origin.Y;
        }

        if (!DentroDoMundo(x, y) || ForaDeAlcance(self, x, y) || EmCimaDoJogador(self, x, y) || !Apoiado(x, y)
            || !TileHooks.ModsAllowPlace(x, y, tile))
        {
            Devolver(item);
            return;
        }

        bool ok = false;
        manual = true;
        try { ok = WorldGen.PlaceTile(x, y, tile, false, false, self.whoAmI, estilo); }
        catch (Exception e) { Log.Warn($"[place] PlaceTile({tile}) falhou: {e.GetType().Name}"); }
        finally { manual = false; }

        if (ok) Log.Info($"[place] shim colocou bloco tipo={tile} em ({x},{y}) estilo={estilo}");
        else Devolver(item);
    }

    static void ColocarParede(Player self, Item item, int parede)
    {
        int x = Player.tileTargetX, y = Player.tileTargetY;
        if (!DentroDoMundo(x, y) || ForaDeAlcance(self, x, y))
        {
            Devolver(item);
            return;
        }
        try { WorldGen.PlaceWall(x, y, parede, false); }
        catch (Exception e) { Log.Warn($"[place] PlaceWall({parede}) falhou: {e.GetType().Name}"); }

        if (Main.tile[x, y].wall == parede) Log.Info($"[place] shim colocou parede tipo={parede} em ({x},{y})");
        else Devolver(item);
    }

    static bool DentroDoMundo(int x, int y) =>
        x >= 1 && y >= 1 && x < Main.maxTilesX - 1 && y < Main.maxTilesY - 1;

    /** Vanilla usa Player.tileRangeX/Y (5/4) a partir do tile central. */
    static bool ForaDeAlcance(Player self, int x, int y)
    {
        try
        {
            int cx = (int)((self.position.X + self.width * 0.5f) / 16f);
            int cy = (int)((self.position.Y + self.height * 0.5f) / 16f);
            int rx = Player.tileRangeX + self.blockRange;
            int ry = Player.tileRangeY + self.blockRange;
            return Math.Abs(x - cx) > rx || Math.Abs(y - cy) > ry;
        }
        catch { return false; }
    }

    /** BlockPlacementIfOverPlayers: o jogo nao deixa fechar o bloco no jogador. */
    static bool EmCimaDoJogador(Player self, int x, int y)
    {
        try
        {
            int px = (int)(self.position.X / 16f), py = (int)(self.position.Y / 16f);
            return x >= px && x <= px + 1 && y >= py && y <= py + 2;
        }
        catch { return false; }
    }

    /** Mesma regra do jogo: vizinho (4 direcoes) com bloco ou parede. */
    static bool Apoiado(int x, int y)
    {
        try
        {
            return Main.tile[x - 1, y].HasTile || Main.tile[x - 1, y].wall > 0
                || Main.tile[x + 1, y].HasTile || Main.tile[x + 1, y].wall > 0
                || Main.tile[x, y - 1].HasTile || Main.tile[x, y - 1].wall > 0
                || Main.tile[x, y + 1].HasTile || Main.tile[x, y + 1].wall > 0;
        }
        catch { return false; }
    }

    static void Devolver(Item item)
    {
        try { item.stack += 1; } catch { }
    }
}
