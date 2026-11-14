using System.IO;
using BunnyLoader.Interop;
using Terraria.IO;

namespace Terraria.Map;

public sealed partial class WorldMap
{
    // v65: tModLoader expoe TryGetMapPath(PlayerFileData, WorldFileData, out string).
    // O jogo Android tem o metodo, o ProxyGen nao gerou (assinatura com out string).
    // Resolvemos no lado gerenciado: mapa do subworld fica em
    // <WorldPath>/<UniqueId>/<nome>.map, o do mundo principal em <WorldPath>/<nome>.map.
    public static bool TryGetMapPath(PlayerFileData player, WorldFileData world, out GameString path)
    {
        path = default;
        try
        {
            if (world.IsNull) return false;
            string root = Main.WorldPath ?? "";
            if (root.Length == 0) return false;
            string name = world.Name + ".map";
            string sub = Path.Combine(root, world.UniqueId.ToString("N"), name);
            if (File.Exists(sub)) { path = new GameString(GameString.New(sub)); return true; }
            string plain = Path.Combine(root, name);
            if (File.Exists(plain)) { path = new GameString(GameString.New(plain)); return true; }
            return false;
        }
        catch { return false; }
    }
}
