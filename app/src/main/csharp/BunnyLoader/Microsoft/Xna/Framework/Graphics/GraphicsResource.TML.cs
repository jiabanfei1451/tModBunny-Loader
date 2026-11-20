using System;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

public sealed partial class GraphicsResource
{
    /// <summary>
    /// O IL dos mods chega ao device tambem por aqui (ex.: Main.spriteBatch.
    /// GraphicsDevice, que o compilador resolve como GraphicsResource::get_GraphicsDevice).
    /// </summary>
    public GraphicsDevice GraphicsDevice
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsResource.get_GraphicsDevice"))
                return Game.ManagedDevice;
            try
            {
                return new GraphicsDevice(GraphicsObjectCalls.GetZero(Pointer, Class, "get_GraphicsDevice"));
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsResource.get_GraphicsDevice", e);
                return Game.ManagedDevice;
            }
        }
    }

    /// <summary>
    /// FNA: <c>Dispose()</c> (argc=0; a versao <c>Dispose(bool)</c> e protegida e
    /// nenhum mod chama). O <c>ManagedRenderTarget.Dispose</c> do Luminance faz
    /// <c>target?.Dispose()</c> e o compilador resolve a chamada em GraphicsResource.
    /// Sem isto o JIT estoura MissingMethod na primeira limpeza de cache.
    /// </summary>
    public void Dispose()
    {
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsResource.Dispose")) return;
        try
        {
            var (info, fn) = GraphicsObjectCalls.Resolve(Class.Handle, "Dispose", 0);
            var call = GameCall.Returning(RetKind.Int);
            call.Arg(Pointer);
            call.Arg(info);
            call.Invoke(fn);
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("GraphicsResource.Dispose", e);
        }
    }

    /// <summary>FNA: foi descartado?</summary>
    public bool IsDisposed
    {
        get
        {
            if (Pointer == 0 || GraphicsObjectCalls.IsBroken("GraphicsResource.get_IsDisposed")) return true;
            try
            {
                return GraphicsObjectCalls.GetZero(Pointer, Class, "get_IsDisposed") != 0;
            }
            catch (Exception e)
            {
                GraphicsObjectCalls.Break("GraphicsResource.get_IsDisposed", e);
                return true;
            }
        }
    }
}
