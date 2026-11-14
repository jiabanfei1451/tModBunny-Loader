using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

namespace Terraria.GameContent;

public unsafe sealed partial class PressurePlateHelper
{
    public static bool IsPressed(Point location) => new GameDictionary(PressurePlatesPressed_).ContainsKey(location);
}
