using System;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Construtor do RenderTarget2D. O ProxyGen nunca emite construtor de instancia
/// (so o <c>(nint pointer)</c>), entao o <c>newobj RenderTarget2D::.ctor(GraphicsDevice,int,int)</c>
/// do Luminance nao resolvia e o JIT do Mono estourava com MissingMethodException
/// no meio de um Begin/End — o lote ficava aberto e a tela parava de desenhar (v115).
///
/// O alvo e procurado no il2cpp por nome + numero de argumentos: o dump de
/// referencia nao traz nomes de parametro confiaveis, e o
/// <see cref="GameInterop.Method(ref MethodRef, GameClass, string, string)"/> exige
/// que todos batam. Falha aqui nao pode estourar dentro do mod: devolve pointer 0
/// (o render target vira um objeto vazio) e registra uma vez.
/// </summary>
public sealed partial class RenderTarget2D
{
    static int warned;

    public RenderTarget2D(GraphicsDevice graphicsDevice, int width, int height)
        : this(Create(graphicsDevice, width, height))
    {
    }

    static nint Create(GraphicsDevice graphicsDevice, int width, int height)
    {
        if (graphicsDevice is null || graphicsDevice.Pointer == 0)
        {
            Warn("sem GraphicsDevice nativo");
            return 0;
        }

        if (width <= 0 || height <= 0)
        {
            Warn($"tamanho invalido {width}x{height}");
            return 0;
        }

        try
        {
            // argc=3: (GraphicsDevice, int, int). O dump tem tambem os de 5, 6,
            // 8, 9 e 10 argumentos; so o de 3 e usado pelos mods (ildump --refs).
            var (info, fn) = GraphicsObjectCalls.Resolve(Class.Handle, ".ctor", 3);
            nint obj = GameInterop.NewObject(Class);
            if (obj == 0)
            {
                Warn("o jogo nao alocou o objeto");
                return 0;
            }

            var call = GameCall.Returning(RetKind.Int);
            call.Arg(obj);
            call.Arg(graphicsDevice.Pointer);
            call.Arg((long)width);
            call.Arg((long)height);
            call.Arg(info);
            call.Invoke(fn);
            return obj;
        }
        catch (Exception e)
        {
            GraphicsObjectCalls.Break("RenderTarget2D..ctor(GraphicsDevice,int,int)", e);
            return 0;
        }
    }

    static void Warn(string why)
    {
        if (Interlocked.Exchange(ref warned, 1) == 0)
            Log.Warn($"render target: {why}; o mod recebe um RenderTarget2D vazio e o desenho dele sai em branco");
    }
}