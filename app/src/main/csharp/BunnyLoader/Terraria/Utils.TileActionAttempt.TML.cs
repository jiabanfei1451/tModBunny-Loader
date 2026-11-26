using System;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Terraria;

public static partial class Utils
{
    /// <summary>
    /// No PC <c>Utils.TileActionAttempt</c> e um <c>MulticastDelegate</c>; no
    /// 1.4.5.8 do Android ele continua sendo delegate, mas o ProxyGen so gera o
    /// construtor de ponteiro. O Roslyn compila <c>PlotTileLine(a, b, w, Metodo)</c>
    /// como <c>ldftn + newobj TileActionAttempt(object, IntPtr)</c>, entao sem
    /// este construtor o mod morre em
    /// <c>MissingMethodException: Void TileActionAttempt..ctor(System.Object, System.IntPtr)</c>
    /// (17 ocorrencias no sandbox: NanoBlade, TrueEye*, PlasmaDeathRay,
    /// BaseDeathray, AbomPhoenix, MeteorEnchant, MasoAeolusFlower...).
    ///
    /// O host so sabe fabricar delegate a partir de um thunk gerenciado
    /// (<see cref="GameDelegates.New"/> + <c>NativeApi.NewDelegate</c>), que
    /// exige um alvo gerenciado; o <paramref name="method"/> que chega aqui e
    /// um ponteiro de funcao IL. Tentamos a conversao e, se o host recusar,
    /// deixamos o ponteiro nulo: o jogo pula o callback (o corte de tiles e o
    /// <c>CastLight</c> simplesmente nao acontecem) em vez de derrubar o
    /// metodo inteiro do mod.
    /// </summary>
    public unsafe sealed partial class TileActionAttempt
    {
        static bool warned;

        public TileActionAttempt(object? target, IntPtr method)
        {
            Pointer = Bind(target, method);
        }

        static nint Bind(object? target, IntPtr method)
        {
            if (method == IntPtr.Zero) return 0;
            try
            {
                nint owner;
                nint handle = Host.Native()->NewDelegate(Class.Handle, method, &owner);
                if (handle != 0) return handle;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warn($"TileActionAttempt: delegate do mod nao pode ser criado ({e.GetType().Name}: {e.Message}); o callback sera ignorado");
                }
            }
            if (!warned)
            {
                warned = true;
                Log.Warn("TileActionAttempt: delegate do mod nao pode ser criado; o callback sera ignorado");
            }
            return 0;
        }
    }
}
