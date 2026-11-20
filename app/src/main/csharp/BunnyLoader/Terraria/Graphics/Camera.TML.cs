using System;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Terraria.Graphics;

/// <summary>
/// FNA/Terraria: <c>Camera.ScaledPosition</c> e <c>ScaledSize</c>. O tipo esta
/// no dump (TypeDefIndex 2215, getters 0x9C51B4 / 0x9C5204) mas nao em
/// types.txt, entao o ProxyGen gerou a classe vazia e o Luminance
/// (<c>Main.Camera.ScaledPosition</c>, para o shader de pixelizacao) nao
/// achava os getters.
/// </summary>
public sealed partial class Camera
{
    public Vector2 ScaledPosition => GetVector2("get_ScaledPosition");

    public Vector2 ScaledSize => GetVector2("get_ScaledSize");

    /// <summary>
    /// Getter sem parametros que devolve <c>Vector2</c> (dois floats em
    /// registradores, <see cref="RetKind.H2F"/>). Qualquer falha degrada para
    /// <c>default</c>: uma camera sem posicao e melhor que um desenho abortado.
    /// </summary>
    unsafe Vector2 GetVector2(string name)
    {
        if (Pointer == 0 || GraphicsObjectCalls.IsBroken("Camera." + name)) return default;
        try
        {
            (nint info, nint fn) = GraphicsObjectCalls.Resolve(*(nint*)Pointer, name, 0);
            CallFrame frame = default;
            frame.X[0] = (long)Pointer;
            frame.X[1] = (long)info;
            frame.Ret = RetKind.H2F;
            GameInterop.Call(fn, &frame);
            return *(Vector2*)frame.Out;
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("Camera." + name, e);
            return default;
        }
    }
}
