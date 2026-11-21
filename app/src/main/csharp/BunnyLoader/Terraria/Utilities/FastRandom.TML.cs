using System;

namespace Terraria.Utilities;

/// <summary>
/// FastRandom e um struct de 8 bytes (so a semente ulong). O ProxyGen gera apenas o
/// layout (Terraria.Utilities.FastRandom.g.cs: __field0 + GameClass), sem nenhum membro,
/// porque o tipo nao esta na lista de proxy completa. Mods que fazem `new FastRandom(seed)`
/// dentro do desenho de tiles estouravam MissingMethodException e o tile ficava sem
/// textura (log v156: "TileLoader.Draw threw: Method not found: void
/// Terraria.Utilities.FastRandom..ctor(ulong)").
///
/// A conta do jogo e o LCG do java.util.Random (seed * 25214903917 + 11 mascarado em 48
/// bits). Ela e aritmetica pura, entao a implementacao em C# da exatamente o mesmo
/// resultado sem passar pelo nativo - e sem depender do ProxyGen resolver struct.
/// </summary>
public unsafe partial struct FastRandom
{
    public const ulong RANDOM_MULTIPLIER = 25214903917UL;
    public const ulong RANDOM_ADD = 11UL;
    public const ulong RANDOM_MASK = 281474976710655UL; // (1 << 48) - 1

    public ulong Seed
    {
        get => __field0;
        set => __field0 = value;
    }

    public FastRandom(ulong seed) : this()
    {
        __field0 = seed;
    }

    public FastRandom(int seed) : this()
    {
        __field0 = (ulong)(uint)seed;
    }

    public static FastRandom CreateWithRandomSeed() => new((ulong)DateTime.UtcNow.Ticks);

    public FastRandom WithModifier(ulong modifier) => new(NextSeed(__field0 + modifier));

    public FastRandom WithModifier(int x, int y) => new(NextSeed(__field0 + (ulong)(x + y * 65536)));

    public void NextSeed()
    {
        __field0 = NextSeed(__field0);
    }

    /// <summary>
    /// No jogo e privado; aqui fica publico para o IL dos mods (que nao checa acesso em
    /// tempo de JIT no Mono, mas o CLR pode) nunca falhar ao resolver o MemberRef.
    /// </summary>
    public int NextBits(int bits)
    {
        NextSeed();
        return (int)(__field0 >> (48 - bits));
    }

    public float NextFloat() => NextBits(24) / 16777216f;

    public double NextDouble() => (((ulong)NextBits(26) << 27) + (ulong)NextBits(27)) / (double)(1UL << 53);

    public int Next(int max)
    {
        if (max <= 0)
            return 0;
        NextSeed();
        return (int)((__field0 >> 17) % (ulong)max);
    }

    public int Next(int min, int max) => max <= min ? min : min + Next(max - min);

    static ulong NextSeed(ulong seed) => (seed * RANDOM_MULTIPLIER + RANDOM_ADD) & RANDOM_MASK;
}
