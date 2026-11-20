using System;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework;

public sealed partial class Game
{
    /// <summary>Device usado quando o build nao expoe o getter nativo.</summary>
    internal static readonly GraphicsDevice ManagedDevice = new(0);

    /// <summary>
    /// O IL dos mods faz Main.instance.GraphicsDevice (callvirt em Game, nao em
    /// Terraria.Main), e no shim Terraria.Main nao herda de Game: quem atende e
    /// este membro. O layout das duas classes geradas e igual (so o campo Pointer),
    /// entao o receiver do mod e lido corretamente.
    /// </summary>
    public GraphicsDevice GraphicsDevice
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("Game.get_GraphicsDevice"))
                return ManagedDevice;
            try
            {
                return new GraphicsDevice(GraphicsObjectCalls.GetZero(Pointer, Class, "get_GraphicsDevice"));
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("Game.get_GraphicsDevice", e);
                return ManagedDevice;
            }
        }
    }
}
