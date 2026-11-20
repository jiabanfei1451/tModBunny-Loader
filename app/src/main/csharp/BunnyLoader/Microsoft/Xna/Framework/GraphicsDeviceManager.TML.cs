using System;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework;

/// <summary>
/// FNA: <c>GraphicsDeviceManager.GraphicsDevice</c>. O tipo existe no dump
/// (TypeDefIndex 933, <c>get_GraphicsDevice</c> 0xF60588) mas nao esta em
/// types.txt, entao o ProxyGen gerou a classe vazia. O Luminance faz
/// <c>Main.graphics.GraphicsDevice</c> antes de criar os render targets.
/// </summary>
public sealed partial class GraphicsDeviceManager
{
    public GraphicsDevice GraphicsDevice
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsDeviceManager.get_GraphicsDevice"))
                return new GraphicsDevice(0);
            try
            {
                return new GraphicsDevice(GraphicsObjectCalls.GetZero(Pointer, Class, "get_GraphicsDevice"));
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsDeviceManager.get_GraphicsDevice", e);
                return new GraphicsDevice(0);
            }
        }
    }
}
