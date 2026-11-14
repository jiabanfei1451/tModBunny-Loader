using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BunnyLoader.Hooks;
using BunnyLoader.Interop;

namespace Terraria;

/// <summary>
/// Parte escrita à mão de <see cref="On_WorldGen"/> para
/// <c>WorldGen.CountTileTypesInArea</c>.
///
/// O contrato do 1.4.4.9 (e o que o HookGen mostrou ao mod) usa <c>int[]</c>.
/// No dump do Android o método recebe o array do próprio jogo, que no shim só
/// existe como <c>GameArray&lt;int&gt;</c> (ponteiro para o Il2CppArray). Se o
/// ProxyGen gerasse a dupla, ela sairia com <c>GameArray&lt;int&gt;</c> e o mod
/// continuaria com <c>MissingMethodException: orig_CountTileTypesInArea.Invoke(Int32[], ...)</c>.
/// Por isso a geração é excluída em tipos.txt
/// (<c>!Terraria.WorldGen.CountTileTypesInArea</c>) e refeita aqui com a forma
/// do contrato, copiando o array na entrada e devolvendo as alterações na saída
/// (o mod lê e escreve posições, ex.: +1800 em sunflower no PurityTotem).
///
/// O <c>orig</c> entregue ao mod executa o nativo de verdade e atualiza o
/// <c>int[]</c>; as mudanças do mod voltam para o array do jogo quando a cadeia
/// termina. Estado é [ThreadStatic]: o gancho roda na thread do jogo e o mod só
/// pode chamar <c>orig</c> de forma síncrona (mesma thread).
/// </summary>
public static partial class On_WorldGen
{
    public delegate void orig_CountTileTypesInArea(int[] tileTypeCounts, int startX, int endX, int startY, int endY);

    public delegate void hook_CountTileTypesInArea(orig_CountTileTypesInArea orig, int[] tileTypeCounts, int startX, int endX, int startY, int endY);

    public static event hook_CountTileTypesInArea CountTileTypesInArea
    {
        add => __HCountTileTypesInArea.__h.Add(value);
        remove => __HCountTileTypesInArea.__h.Remove(value);
    }

    internal static unsafe class __HCountTileTypesInArea
    {
        const string Signature = "void CountTileTypesInArea(int[] tileTypeCounts, int startX, int endX, int startY, int endY)";
        const string Layout = "\0\0\0\0\0\0";

        static MethodRef __m;
        static nint* __s;

        /// <summary>Array nativo da chamada corrente (vem do r0 do gancho).</summary>
        [ThreadStatic] static nint __native;

        /// <summary>Cópia gerenciada passada ao mod na chamada corrente.</summary>
        [ThreadStatic] static int[]? __managed;

        [ThreadStatic] static int __runs;

        internal static readonly HookChain<hook_CountTileTypesInArea, orig_CountTileTypesInArea> __h = new(
            (hk, nx) => (int[] counts, int sx, int ex, int sy, int ey) => hk(nx, counts, sx, ex, sy, ey),
            __o, __i);

        static ref readonly MethodRef Resolve()
        {
            return ref GameInterop.Method(ref __m, WorldGen.Class, Signature, Layout);
        }

        static void __i()
        {
            ref readonly MethodRef m = ref Resolve();
            __s = (nint*)global::BunnyLoader.Hook.Install(m.Info,
                (nint)(delegate* unmanaged<long, long, long, long, long, long, void>)&__n).OriginalSlot;
        }

        static void CopyToManaged(nint pointer, int[] counts)
        {
            var native = new GameArray<int>(pointer);
            if (!native.IsNull && native.Length == counts.Length) native.AsSpan().CopyTo(counts);
        }

        /// <summary>
        /// <c>orig</c> do mod: roda o método nativo e espelha o resultado no
        /// <c>int[]</c> do mod (ele lê as contagens depois de chamar <c>orig</c>).
        /// </summary>
        static void __o(int[] tileTypeCounts, int startX, int endX, int startY, int endY)
        {
            __runs++;
            nint native = __native;
            if (native == 0 || tileTypeCounts == null) return;
            ref readonly MethodRef m = ref Resolve();
            CallFrame f;
            Unsafe.SkipInit(out f);
            f.X[0] = (long)native;
            f.X[1] = (long)startX;
            f.X[2] = (long)endX;
            f.X[3] = (long)startY;
            f.X[4] = (long)endY;
            f.X[5] = (long)m.Info;
            f.Ret = RetKind.Int;
            GameInterop.Call(*__s, &f);
            CopyToManaged(native, tileTypeCounts);
        }

        static void Dispatch(nint pointer, int startX, int endX, int startY, int endY)
        {
            nint previousNative = __native;
            int[]? previousManaged = __managed;
            var native = new GameArray<int>(pointer);
            int[] counts = native.IsNull ? Array.Empty<int>() : native.AsSpan().ToArray();
            __native = pointer;
            __managed = counts;
            try
            {
                var chain = __h.Chain;
                if (chain == null) __o(counts, startX, endX, startY, endY);
                else
                {
                    int runs = __runs;
                    try { chain(counts, startX, endX, startY, endY); }
                    catch (Exception e)
                    {
                        HookErrors.Report("WorldGen.CountTileTypesInArea", e);
                        if (__runs == runs) __o(counts, startX, endX, startY, endY);
                    }
                }
                // O que o mod escreveu no int[] precisa valer no array do jogo.
                if (!native.IsNull && native.Length == counts.Length) counts.AsSpan().CopyTo(native.AsSpan());
            }
            finally
            {
                __native = previousNative;
                __managed = previousManaged;
            }
        }

        [UnmanagedCallersOnly]
        static void __n(long r0, long r1, long r2, long r3, long r4, long r5)
        {
            try { Dispatch((nint)r0, (int)r1, (int)r2, (int)r3, (int)r4); }
            catch (Exception e) { HookErrors.Report("WorldGen.CountTileTypesInArea (native)", e); }
        }

        /// <summary>
        /// Chamada direta do mod (<c>WorldGen.CountTileTypesInArea(int[])</c>):
        /// aloca o array do jogo, chama o método (passa pelo gancho se instalado)
        /// e devolve os valores para o array gerenciado.
        /// </summary>
        internal static void Call(int[] tileTypeCounts, int startX, int endX, int startY, int endY)
        {
            GameArray<int> native = tileTypeCounts;
            ref readonly MethodRef m = ref Resolve();
            CallFrame f;
            Unsafe.SkipInit(out f);
            f.X[0] = (long)native.Pointer;
            f.X[1] = (long)startX;
            f.X[2] = (long)endX;
            f.X[3] = (long)startY;
            f.X[4] = (long)endY;
            f.X[5] = (long)m.Info;
            f.Ret = RetKind.Int;
            GameInterop.Call(m.Fn, &f);
            if (!native.IsNull && native.Length == tileTypeCounts.Length) native.AsSpan().CopyTo(tileTypeCounts);
        }
    }
}

public unsafe sealed partial class WorldGen
{
    /// <summary>
    /// Forma do contrato 1.4.4.9 (<c>int[]</c>). O array do jogo é criado a
    /// partir do array gerenciado, preenchido pelo nativo e copiado de volta.
    /// </summary>
    public static void CountTileTypesInArea(int[] tileTypeCounts, int startX, int endX, int startY, int endY)
    {
        if (tileTypeCounts == null) throw new ArgumentNullException(nameof(tileTypeCounts));
        On_WorldGen.__HCountTileTypesInArea.Call(tileTypeCounts, startX, endX, startY, endY);
    }
}
