using System;
using System.Runtime.CompilerServices;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

namespace ReLogic.Graphics;

/// <summary>
/// A fonte dinamica do tModLoader 1.4 (<c>ReLogic.Graphics</c>).
///
/// Nao herda de <c>Microsoft.Xna.Framework.Graphics.SpriteFont</c> de
/// proposito: sao dois tipos nativos diferentes, e chamar um metodo de
/// <c>SpriteFont</c> com o ponteiro de um <c>DynamicSpriteFont</c> cairia no
/// vtable/assinatura errada do il2cpp. O que o mod usa deste tipo e so
/// <c>MeasureString(string)</c> (FargowiltasSouls e Luminance); o resto entra
/// quando algum mod pedir.
/// </summary>
public unsafe class DynamicSpriteFont : IGameObject
{
    public static readonly GameClass Class = new("ReLogic.Graphics", "DynamicSpriteFont");

    public readonly nint Pointer;

    public DynamicSpriteFont(nint pointer) => Pointer = pointer;

    nint IGameObject.Pointer => Pointer;

    public bool IsNull => Pointer == 0;

    static MethodRef __measureString;

    static int reported;

    /// <summary>
    /// <c>Vector2 MeasureString(string text)</c> — instancia, retorno
    /// <c>Vector2</c> em dois registradores (H2F).
    /// </summary>
    public Vector2 MeasureString(string? text)
    {
        if (Pointer == 0) return Vector2.Zero;
        try
        {
            ref readonly MethodRef m = ref GameInterop.Method(ref __measureString, Class,
                "Vector2 MeasureString(string text)", "\u0000\u0000\u0000");
            CallFrame frame;
            Unsafe.SkipInit(out frame);
            frame.X[0] = (long)Pointer;
            frame.X[1] = (long)GameString.New(text);
            frame.X[2] = (long)m.Info;
            frame.Ret = RetKind.H2F;
            GameInterop.Call(m.Fn, &frame);
            return *(Vector2*)frame.Out;
        }
        catch (Exception e)
        {
            // Uma fonte que nao mede nao pode derrubar o desenho do mod: o
            // tamanho zero e degradacao aceitavel (texto sem centralizacao).
            if (Interlocked.Exchange(ref reported, 1) == 0)
                Log.Error($"DynamicSpriteFont.MeasureString: {e.Message}");
            return Vector2.Zero;
        }
    }
}
