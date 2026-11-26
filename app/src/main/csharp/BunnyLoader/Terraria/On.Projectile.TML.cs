using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BunnyLoader.Hooks;
using BunnyLoader.Interop;

namespace Terraria;

/// <summary>
/// Parte escrita à mão de <see cref="On_Projectile"/> para
/// <c>Projectile.GetFairyQueenWeaponsColor</c>.
///
/// O contrato do 1.4.4.9 declara o terceiro parâmetro como
/// <c>float?</c>. No dump do Android o parâmetro é um
/// <c>Nullable&lt;float&gt;</c> nativo, que no shim só existe como
/// <c>GameNullable&lt;float&gt;</c>; se o ProxyGen gerasse a dupla, ela sairia com
/// <c>GameNullable&lt;float&gt;</c> e o mod continuaria com
/// <c>MissingMethodException: orig_GetFairyQueenWeaponsColor.Invoke(Projectile, Single, Single, Nullable&lt;Single&gt;)</c>
/// em <c>DarkFairyQueenMagicItemShot.Detour_GetFairyQueenWeaponsColor</c>.
/// Por isso a geração é excluída em tipos.txt
/// (<c>!!Terraria.Projectile.GetFairyQueenWeaponsColor</c>) e refeita aqui com a
/// forma do contrato; a conversão para <c>GameNullable&lt;float&gt;</c> acontece
/// na chamada do nativo.
///
/// Como no <c>On_WorldGen.CountTileTypesInArea</c>, o <c>orig</c> entregue ao mod
/// executa o nativo de verdade. O parâmetro é um struct por valor, então não há o
/// que espelhar de volta: basta converter e chamar.
/// </summary>
public static partial class On_Projectile
{
    public delegate Color orig_GetFairyQueenWeaponsColor(Projectile self, float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride);

    public delegate Color hook_GetFairyQueenWeaponsColor(orig_GetFairyQueenWeaponsColor orig, Projectile self, float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride);

    public static event hook_GetFairyQueenWeaponsColor GetFairyQueenWeaponsColor
    {
        add => __HGetFairyQueenWeaponsColor.__h.Add(value);
        remove => __HGetFairyQueenWeaponsColor.__h.Remove(value);
    }

    internal static unsafe class __HGetFairyQueenWeaponsColor
    {
        const string Signature = "Color GetFairyQueenWeaponsColor(float alphaChannelMultiplier, float lerpToWhite, Nullable<float> rawHueOverride)";

        // AlphaChannelMultiplier (float), LerpToWhite (float), RawHueOverride
        // (Nullable<float>, 8 bytes alinhado a 8) e o retorno (Color, 4 bytes).
        const string Layout = "\u000a\u000a\u0006\u000a";

        static MethodRef __m;
        static nint* __s;

        [ThreadStatic] static int __runs;

        internal static readonly HookChain<hook_GetFairyQueenWeaponsColor, orig_GetFairyQueenWeaponsColor> __h = new(
            (hk, nx) => (Projectile self, float alpha, float lerp, float? hue) => hk(nx, self, alpha, lerp, hue),
            __o, __i);

        static ref readonly MethodRef Resolve()
        {
            return ref GameInterop.Method(ref __m, Projectile.Class, Signature, Layout);
        }

        static void __i()
        {
            ref readonly MethodRef m = ref Resolve();
            __s = (nint*)global::BunnyLoader.Hook.Install(m.Info,
                (nint)(delegate* unmanaged<long, float, float, long, long, RetS16>)&__n).OriginalSlot;
        }

        /// <summary><c>orig</c> do mod: chama o nativo de verdade.</summary>
        static Color __o(Projectile self, float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride)
        {
            __runs++;
            ref readonly MethodRef m = ref Resolve();
            CallFrame f;
            Unsafe.SkipInit(out f);
            f.X[0] = (long)(self == null ? 0 : self.Pointer);
            f.D[0] = Abi.F32(alphaChannelMultiplier);
            f.D[1] = Abi.F32(lerpToWhite);
            *(GameNullable<float>*)&f.X[1] = rawHueOverride;
            f.X[2] = (long)m.Info;
            f.Ret = RetKind.S16;
            GameInterop.Call(*__s, &f);
            return *(Color*)f.Out;
        }

        static void Dispatch(Projectile self, float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride)
        {
            var chain = __h.Chain;
            if (chain == null) { __o(self, alphaChannelMultiplier, lerpToWhite, rawHueOverride); return; }
            int runs = __runs;
            try { chain(self, alphaChannelMultiplier, lerpToWhite, rawHueOverride); }
            catch (Exception e)
            {
                HookErrors.Report("Projectile.GetFairyQueenWeaponsColor", e);
                if (__runs == runs) __o(self, alphaChannelMultiplier, lerpToWhite, rawHueOverride);
            }
        }

        [UnmanagedCallersOnly]
        static RetS16 __n(long r0, float r1, float r2, long r3, long r4)
        {
            try
            {
                Dispatch(new Projectile((nint)r0), r1, r2, Abi.FromX<GameNullable<float>>(r3));
            }
            catch (Exception e)
            {
                HookErrors.Report("Projectile.GetFairyQueenWeaponsColor (native)", e);
            }
            return default;
        }

        /// <summary>
        /// Chamada direta do mod (<c>Projectile.GetFairyQueenWeaponsColor(float, float, float?)</c>).
        /// </summary>
        internal static Color Call(Projectile self, float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride)
        {
            ref readonly MethodRef m = ref Resolve();
            CallFrame f;
            Unsafe.SkipInit(out f);
            f.X[0] = (long)(self == null ? 0 : self.Pointer);
            f.D[0] = Abi.F32(alphaChannelMultiplier);
            f.D[1] = Abi.F32(lerpToWhite);
            *(GameNullable<float>*)&f.X[1] = rawHueOverride;
            f.X[2] = (long)m.Info;
            f.Ret = RetKind.S16;
            GameInterop.Call(m.Fn, &f);
            return *(Color*)f.Out;
        }
    }
}

public sealed partial class Projectile
{
    /// <summary>
    /// Forma do contrato 1.4.4.9 (<c>float?</c>) — a geração do ProxyGen entrega
    /// <c>GameNullable&lt;float&gt;</c>, ver o comentário de
    /// <see cref="On_Projectile"/>.
    /// </summary>
    public Color GetFairyQueenWeaponsColor(float alphaChannelMultiplier, float lerpToWhite, float? rawHueOverride)
    {
        if (Pointer == 0) throw new NullReferenceException("Projectile.GetFairyQueenWeaponsColor: game object is null");
        return On_Projectile.__HGetFairyQueenWeaponsColor.Call(this, alphaChannelMultiplier, lerpToWhite, rawHueOverride);
    }
}
