using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace BunnyLoader.Interop;

/// <summary>
/// Cria proxies <b>class</b> a partir de um ponteiro nativo.
///
/// No tModLoader 1.4 e no IL2CPP os tipos de referencia do jogo
/// (<c>Terraria.Item</c>, <c>Terraria.Player</c>, <c>Texture2D</c>...) sao
/// <b>classes</b>: o proprio jogo tem <c>public class Item</c>. Um proxy
/// <b>struct</b> quebraria o IL do mod — o <c>ldfld/stfld</c> do mod espera
/// uma referencia gerenciada e o runtime lancaria <c>TypeLoadException:
/// Expected reference type but got type kind 17</c>.
///
/// O proxy class guarda o ponteiro nativo e expoe os membros do jogo por
/// offset (<c>Pointer + offset</c>), entao dois proxies do mesmo objeto
/// nativo sao equivalentes — por isso a criacao e feita por ponteiro, e nao
/// por construtor gerenciado.
///
/// Cada proxy class gerado pelo ProxyGen tem o construtor
/// <c>public X(nint pointer)</c>. Este cache evita procurar o construtor a
/// cada acesso.
/// </summary>
public static class GameObjectFactory
{
    static readonly ConcurrentDictionary<Type, Func<IntPtr, object>> Constructors = new();

    /// <summary>
    /// Cria um proxy do tipo indicado apontando para o objeto nativo.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// O tipo nao e um proxy de jogo (nao tem construtor <c>(nint)</c>).
    /// </exception>
    public static object Create(Type type, IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) throw new ArgumentNullException(nameof(pointer));
        return Constructors.GetOrAdd(type, Build)(pointer);
    }

    /// <summary>
    /// Cria um proxy do tipo indicado, ou <c>null</c> se o ponteiro for nulo.
    /// </summary>
    public static object? CreateOrNull(Type type, IntPtr pointer) =>
        pointer == IntPtr.Zero ? null : Create(type, pointer);

    static Func<IntPtr, object> Build(Type type)
    {
        ConstructorInfo? ctor = type.GetConstructor(new[] { typeof(IntPtr) });
        if (ctor == null)
            throw new NotSupportedException($"{type.FullName}: not a game proxy (no (IntPtr) constructor)");

        // O construtor e publico e gerado pelo ProxyGen, entao o linker do
        // Android nao o descarta. Invoke e reflection: so acontece na
        // criacao do proxy, nao no acesso a cada membro.
        return pointer => ctor.Invoke(new object[] { pointer });
    }
}
