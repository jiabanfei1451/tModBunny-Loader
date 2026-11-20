using Microsoft.Xna.Framework;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace Terraria.ModLoader.Core;

internal static partial class TileHooks
{
    static void InstallResting()
    {
        On_PlayerSittingHelper.GetSittingTargetInfo += Sitting;
        On_PlayerSleepingHelper.GetSleepingTargetInfo += Sleeping;
        On_Player.CheckSpawn += CheckSpawn;
    }

    static Vector2 RestingPosition(in TileRestingInfo info, int extraRow) =>
        new(info.AnchorTilePosition.X * 16 + 8 + info.TargetDirection * info.DirectionOffset + info.FinalOffset.X,
            (info.AnchorTilePosition.Y + extraRow) * 16 + 16 + info.FinalOffset.Y);

    static bool Sitting(On_PlayerSittingHelper.orig_GetSittingTargetInfo orig, Player player, int x, int y, out int direction, out Vector2 position, out Vector2 seatOffset, out ExtraSeatInfo extra)
    {
        if (!orig(player, x, y, out direction, out position, out seatOffset, out extra))
            return false;
        Tile tile = Main.tile[x, y];
        if (!tile.active() || TileLoader.GetTile(tile.type) is not ModTile mod)
            return true;
        var info = new TileRestingInfo(player, new Point(x, y), seatOffset, direction, 6, Vector2.Zero, extra);
        try
        {
            mod.ModifySittingTargetInfo(x, y, ref info);
        }
        catch (System.Exception e)
        {
            ItemHooks.Report($"{mod.FullName}.ModifySittingTargetInfo", e);
        }

        direction = info.TargetDirection;
        seatOffset = info.VisualOffset;
        extra = info.ExtraInfo;
        position = RestingPosition(info, 0);
        return true;
    }

    static bool Sleeping(On_PlayerSleepingHelper.orig_GetSleepingTargetInfo orig, int x, int y, out int direction, out Vector2 anchor, out Vector2 visualOffset)
    {
        if (!orig(x, y, out direction, out anchor, out visualOffset))
            return false;
        Tile tile = Main.tile[x, y];
        if (!tile.active() || TileLoader.GetTile(tile.type) is not ModTile mod)
            return true;
        var info = new TileRestingInfo(default, new Point((int)System.MathF.Round((anchor.X - 8) / 16), (int)System.MathF.Round((anchor.Y - 16) / 16) - 1), visualOffset, direction);
        try
        {
            mod.ModifySleepingTargetInfo(x, y, ref info);
        }
        catch (System.Exception e)
        {
            ItemHooks.Report($"{mod.FullName}.ModifySleepingTargetInfo", e);
        }

        direction = info.TargetDirection;
        visualOffset = info.VisualOffset;
        anchor = RestingPosition(info, 1);
        return true;
    }

    static bool CheckSpawn(On_Player.orig_CheckSpawn orig, int x, int y)
    {
        // O jogo chama CheckSpawn enquanto o player ainda esta sendo posicionado e
        // passa coordenadas fora do mundo (o log da v115 mostrou o Tilemap
        // estourando em (-1,-2) e o gancho derrubando o spawn). Em vez de deixar o
        // indexador do shim lancar, sai pelo original: sem mundo valido nao ha
        // rede para transformar nesta altura.
        int ty = y - 1;
        if (Main.maxTilesX <= 0 || Main.maxTilesY <= 0 || x < 0 || ty < 0 || x >= Main.maxTilesX || ty >= Main.maxTilesY)
            return orig(x, y);

        Tile tile = Main.tile[x, ty];
        int type = tile.type;
        if (!tile.active() || TileLoader.GetTile(type) == null)
            return orig(x, y);

        bool[] valid = TileID.Sets.IsValidSpawnPoint;
        if (type < 0 || type >= valid.Length || !valid[type])
            return orig(x, y);
        tile.type = TileID.Beds;
        try
        {
            return orig(x, y);
        }
        finally
        {
            Tile restore = Main.tile[x, ty];
            restore.type = (ushort)type;
        }
    }
}
