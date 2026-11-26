using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Terraria.Graphics;

/// <summary>
/// O <see cref="VertexStrip"/> do 1.4.4.9 no port Android.
///
/// O ProxyGen gera tres coisas que nao batem com o que o mod compilado chama:
/// o <c>.ctor()</c> sem argumentos, o <c>DrawTrail()</c> sem argumentos e os
/// construtores de delegate <c>(object, IntPtr)</c> dos tipos aninhados. O IL
/// do mod usa <c>newobj</c> em todos eles, e o IlPatcher nao reescreve
/// <c>newobj</c>/<c>ldftn</c> - por isso os macetes ficam aqui.
/// </summary>
public unsafe sealed partial class VertexStrip
{
    public VertexStrip()
        : this(New().Pointer)
    {
    }

    public void DrawTrail() => DrawTrail(false);

    static MethodRef __mPrepare;

    /// <summary>
    /// Versao com arrays gerenciados. O ProxyGen expoe a mesma assinatura nativa
    /// em duas sobrecargas (<c>GameArray&lt;T&gt;</c> e <c>ref Vector2_DynamicArray_120</c>),
    /// e as conversoes implicitas de <c>GameArray&lt;T&gt;</c> para <c>T[]</c>
    /// tornam a chamada ambigua dentro da classe - por isso a invocacao nativa
    /// e' montada a mao aqui, com a mesma assinatura/layout que o proxy usa.
    /// </summary>
    public void PrepareStrip(Vector2[] positions, float[] rotations, StripColorFunction? colorFunction, StripHalfWidthFunction? widthFunction, Vector2 offsetForAllPositions, int? expectedVertexPairsAmount = null, bool includeBacksides = false)
    {
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        if (rotations == null) throw new ArgumentNullException(nameof(rotations));

        GameArray<Vector2> gamePositions = ToGameArray(positions, "Vector2");
        GameArray<float> gameRotations = ToGameArray(rotations, "Single");

        nint color = colorFunction?.ToNative() ?? 0;
        nint width = widthFunction?.ToNative() ?? 0;
        using GameHandle? colorHandle = color != 0 ? new GameHandle(color) : null;
        using GameHandle? widthHandle = width != 0 ? new GameHandle(width) : null;

        GameNullable<int> expected = expectedVertexPairsAmount;

        ref readonly MethodRef m = ref GameInterop.Method(
            ref __mPrepare,
            Class,
            "void PrepareStrip(Vector2[] positions, float[] rotations, VertexStrip.StripColorFunction colorFunction, VertexStrip.StripHalfWidthFunction widthFunction, Vector2 offsetForAllPositions, Nullable<int> expectedVertexPairsAmount, bool includeBacksides)",
            "\u0000\u0000\u0000\u0000\u0009\u0009\u0000\u0000");

        CallFrame f;
        Unsafe.SkipInit(out f);
        f.X[0] = (long)Pointer;
        f.X[1] = (long)gamePositions.Pointer;
        f.X[2] = (long)gameRotations.Pointer;
        f.X[3] = (long)color;
        f.X[4] = (long)width;
        f.X[5] = Unsafe.As<Vector2, long>(ref offsetForAllPositions);
        f.X[6] = Unsafe.As<GameNullable<int>, long>(ref expected);
        f.X[7] = includeBacksides ? 1L : 0L;
        f.X[8] = (long)m.Info;
        f.Ret = RetKind.Int;
        GameInterop.Call(m.Fn, &f);
    }

    static GameArray<T> ToGameArray<T>(T[] values, string elementName) where T : unmanaged
    {
        GameArray<T> array = GameArray<T>.New(new GameClass("Microsoft.Xna.Framework", elementName), values.Length);
        values.AsSpan().CopyTo(array.AsSpan());
        return array;
    }

    /// <summary>Thunks nativos que o jogo chama de volta.</summary>
    internal static class Thunks
    {
        internal static nint ColorFn => (nint)(delegate* unmanaged<long, float, long, void>)&InvokeColor;

        internal static nint WidthFn => (nint)(delegate* unmanaged<long, float, float>)&InvokeWidth;

        [UnmanagedCallersOnly]
        static void InvokeColor(long target, float progressOnStrip, long colorOut)
        {
            try
            {
                Func<float, Color>? callback = GameDelegates.Get<Func<float, Color>>((nint)target);
                if (callback == null || colorOut == 0) return;
                *(Color*)colorOut = callback(progressOnStrip);
            }
            catch (Exception e) { GameDelegates.Report("VertexStrip.StripColorFunction", e); }
        }

        [UnmanagedCallersOnly]
        static float InvokeWidth(long target, float progressOnStrip)
        {
            try
            {
                Func<float, float>? callback = GameDelegates.Get<Func<float, float>>((nint)target);
                if (callback != null) return callback(progressOnStrip);
            }
            catch (Exception e) { GameDelegates.Report("VertexStrip.StripHalfWidthFunction", e); }
            return 0f;
        }
    }

    public unsafe sealed partial class StripColorFunction
    {
        readonly Func<float, Color>? callback;

        nint native;

        /// <summary>O que o jogo de verdade chama (<c>void Invoke(float, out Color)</c>).</summary>
        internal nint ToNative() => native;

        /// <summary>
        /// O construtor de delegate que o IL do mod usa. O metodo do mod
        /// devolve <see cref="Color"/> (nao <c>out Color</c>), entao o tipo
        /// gerenciado aqui e' <see cref="Func{T,TResult}"/>.
        ///
        /// <c>ldftn</c> entrega um ponteiro de metodo gerenciado; nem
        /// <c>Delegate.CreateDelegate</c> (que so' aceita <c>MethodInfo</c> nesta
        /// plataforma) nem <c>Marshal.GetDelegateForFunctionPointer</c> servem,
        /// por isso a chamada sai por <c>calli</c> (<c>delegate* managed</c>).
        /// </summary>
        public StripColorFunction(object target, IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                Log.Warn("VertexStrip.StripColorFunction: ldftn nulo, o traco nao vai aparecer");
                return;
            }
            delegate* managed<object, float, Color> call = (delegate* managed<object, float, Color>)method;
            object owner = target;
            callback = progressOnStrip => call(owner, progressOnStrip);
            native = GameDelegates.Create(callback, Class.Handle, Thunks.ColorFn);
        }
    }

    public unsafe sealed partial class StripHalfWidthFunction
    {
        readonly Func<float, float>? callback;

        nint native;

        internal nint ToNative() => native;

        public StripHalfWidthFunction(object target, IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                Log.Warn("VertexStrip.StripHalfWidthFunction: ldftn nulo, o traco nao vai aparecer");
                return;
            }
            delegate* managed<object, float, float> call = (delegate* managed<object, float, float>)method;
            object owner = target;
            callback = progressOnStrip => call(owner, progressOnStrip);
            native = GameDelegates.Create(callback, Class.Handle, Thunks.WidthFn);
        }
    }
}
