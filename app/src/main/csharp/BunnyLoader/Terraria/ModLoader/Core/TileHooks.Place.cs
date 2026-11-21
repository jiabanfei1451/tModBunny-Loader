namespace Terraria.ModLoader.Core;

internal static partial class TileHooks
{
    /**
     * Regras dos mods para colocar um bloco (as mesmas que o gancho CanPlace
     * aplica no caminho normal do jogo). O remendo de colocacao chama isto,
     * porque no celular o `PlaceThing_Tiles_BlockPlacementForAssortedThings`
     * nunca roda.
     */
    internal static bool ModsAllowPlace(int i, int j, int type)
    {
        if (TileLoader.GetTile(type)?.CanPlace(i, j) == false) return false;
        foreach (GlobalTile g in TileLoader.Hooks.CanPlace)
            if (!g.CanPlace(i, j, type)) return false;
        return true;
    }
}
