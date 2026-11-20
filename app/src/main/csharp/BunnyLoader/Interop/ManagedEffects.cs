using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace BunnyLoader.Interop;

/// <summary>
/// Instancias que existem SO no lado managed: mods derivam de alguns tipos do
/// jogo (os @open de types.txt, hoje CustomSky e DrawAnimation) e o proxy nao
/// tem objeto nativo. `Pointer` passa a apontar para um bloco zerado alugado
/// aqui, porque todo acessador de campo do proxy faz `Pointer + offset`
/// (Opacity, _isLoaded, _priority) e estouraria em endereco nulo/baixo.
/// `IsManaged` permite que o EffectManager nunca mande um ponteiro falso para
/// o dicionario nativo (Dictionary de CustomSky) nem para o JIT do jogo.
/// </summary>
public static unsafe class ManagedEffects
{
    const int BlockSize = 512;

    static readonly ConcurrentDictionary<nint, byte> Blocks = new();

    public static nint New()
    {
        nint pointer = Marshal.AllocHGlobal(BlockSize);
        new Span<byte>((void*)pointer, BlockSize).Clear();
        Blocks[pointer] = 0;
        return pointer;
    }

    public static bool IsManaged(nint pointer) => pointer != 0 && Blocks.ContainsKey(pointer);
}
