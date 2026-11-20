using System;
using System.Runtime.CompilerServices;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ReLogic.Graphics;

/// <summary>
/// <c>ReLogic.Graphics.DynamicSpriteFontExtensionMethods</c> — no tML 1.4 e
/// aqui um <b>metodo de extensao</b>, mas o mod o chama como chamada estatica
/// (<c>call</c> em <c>DynamicSpriteFontExtensionMethods::DrawString</c>), o
/// que a assinatura estatica satisfaz.
///
/// O FargowiltasSouls usa so a sobrecarga de 10 parametros, em
/// <c>DrawInterface_35_YouDied</c> e <c>DrawMenu</c>. Ela e encaminhada para
/// o metodo homonimo nativo; se o jogo nao tiver esse metodo (ainda nao
/// verificado no aparelho) o desenho vira no-op com um aviso no log, em vez
/// de derrubar o hook de desenho do mod.
/// </summary>
public static unsafe class DynamicSpriteFontExtensionMethods
{
    public static readonly GameClass Class = new("ReLogic.Graphics", "DynamicSpriteFontExtensionMethods");

    static MethodRef __drawString;

    static int reported;

    const string Signature =
        "void DrawString(SpriteBatch spriteBatch, DynamicSpriteFont spriteFont, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)";

    const string Layout = "\u0000\u0000\u0000\u0009\u0005\u0005\u0009\u0005\u0005\u0005\u0000";

    public static void DrawString(this SpriteBatch spriteBatch, DynamicSpriteFont spriteFont, string? text, Vector2 position, Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        if (spriteBatch is null || spriteFont is null || spriteFont.Pointer == 0) return;
        try
        {
            ref readonly MethodRef m = ref GameInterop.Method(ref __drawString, Class, Signature, Layout);
            CallFrame frame;
            Unsafe.SkipInit(out frame);
            frame.X[0] = (long)spriteBatch.Pointer;
            frame.X[1] = (long)spriteFont.Pointer;
            frame.X[2] = (long)GameString.New(text);
            frame.D[0] = Abi.F32(((float*)&position)[0]);
            frame.D[1] = Abi.F32(((float*)&position)[1]);
            *(Color*)&frame.X[3] = color;
            frame.D[2] = Abi.F32(rotation);
            frame.D[3] = Abi.F32(((float*)&origin)[0]);
            frame.D[4] = Abi.F32(((float*)&origin)[1]);
            frame.D[5] = Abi.F32(scale);
            frame.X[4] = (long)(int)effects;
            frame.D[6] = Abi.F32(layerDepth);
            frame.X[5] = (long)m.Info;
            frame.Ret = RetKind.Int;
            GameInterop.Call(m.Fn, &frame);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref reported, 1) == 0)
                Log.Error($"DynamicSpriteFontExtensionMethods.DrawString: {e.Message}");
        }
    }
}
