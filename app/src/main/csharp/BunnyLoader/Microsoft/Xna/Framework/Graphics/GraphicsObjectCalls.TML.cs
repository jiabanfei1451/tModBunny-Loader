using System;
using System.Collections.Concurrent;
using System.Threading;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Chamadas nativas dos tipos do FNA que o dump de referencia do tModLoader traz
/// sem membros (GraphicsDevice, GraphicsResource, Game, TextureCollection). Como
/// o dump nao tem os membros, o ProxyGen gera classes vazias; o IL dos mods, no
/// entanto, faz callvirt get_Textures()/get_GraphicsDevice()/set_Item(). Estes
/// metodos sao resolvidos pelo nome + numero de argumentos (o dump nao serve para
/// conferir a assinatura) e o resultado vai por <see cref="GameCall"/>.
/// Se o build do jogo nao tiver o metodo, a falha e registada uma vez e o shim
/// passa a usar o stub gerenciado, em vez de estourar dentro do mod.
/// </summary>
internal static unsafe class GraphicsObjectCalls
{
    static readonly ConcurrentDictionary<(nint Klass, string Name, int Argc), (nint Info, nint Fn)> Methods = new();
    static readonly ConcurrentDictionary<string, bool> Broken = new();
    static int reported;

    internal static bool IsBroken(string what) => Broken.ContainsKey(what);

    internal static void Break(string what, Exception error)
    {
        Broken.TryAdd(what, true);
        if (Interlocked.Exchange(ref reported, 1) == 0)
            Log.Error($"{what}: este build do jogo nao tem esse metodo do FNA ({error.GetType().Name}: {error.Message}); a partir daqui uso o stub gerenciado");
    }

    /// <summary>MethodInfo + ponteiro do corpo, procurados uma vez por classe/nome/argc.</summary>
    internal static (nint Info, nint Fn) Resolve(nint klass, string name, int argc)
    {
        return Methods.GetOrAdd((klass, name, argc), key =>
        {
            if (key.Klass == 0) throw new TypeLoadException($"GraphicsObjectCalls: class of {key.Name} not resolved");
            byte* n = Host.Utf8(key.Name);
            try
            {
                nint info = Host.Native()->FindMethod(key.Klass, n, key.Argc);
                if (info == 0) throw new MissingMethodException($"GraphicsObjectCalls: no {key.Name} with {key.Argc} argument(s)");
                nint fn = Host.Native()->MethodPointer(info);
                if (fn == 0) throw new MissingMethodException($"GraphicsObjectCalls: {key.Name} has no executable body");
                return (info, fn);
            }
            finally { Host.FreeUtf8(n); }
        });
    }

    /// <summary>Chamada sem parametros que devolve uma referencia (get_Textures, get_GraphicsDevice).</summary>
    internal static nint GetZero(nint self, GameClass cls, string name)
    {
        var (info, fn) = Resolve(cls.Handle, name, 0);
        var call = GameCall.Returning(RetKind.Int);
        call.Arg(self);
        call.Arg(info);
        return (nint)call.Invoke(fn);
    }
}
