namespace Terraria;

public static partial class Utils
{
    public static void Swap<T>(ref T t1, ref T t2) => (t1, t2) = (t2, t1);

    public static T Clamp<T>(T value, T min, T max) where T : System.IComparable<T>
    {
        if (value.CompareTo(max) > 0) return max;
        if (value.CompareTo(min) < 0) return min;
        return value;
    }

    // ---- aleatoriedade (tML Utils.TML.cs) ------------------------------
    // Mods do PC fazem a chamada ESTÁTICA `Terraria.Utils.NextBool(rng, n)`,
    // não a de extensão: as versões daqui são de propósito NÃO-extensão (as
    // de `UtilsExtensions` continuam valendo para `rng.NextBool()`), senão o
    // C# acusa ambiguidade.

    public static bool NextBool(Utilities.UnifiedRandom r, int chanceDenominator = 2)
        => chanceDenominator <= 1 || r.Next(chanceDenominator) == 0;

    public static bool NextBool(Utilities.UnifiedRandom r, int chanceNumerator, int chanceDenominator)
        => NextFloat(r, 0f, chanceDenominator) < chanceNumerator;

    public static float NextFloat(Utilities.UnifiedRandom r, float min, float max)
        => min + (float)r.NextDouble() * (max - min);

    public static T Next<T>(Utilities.UnifiedRandom r, params T[] choices)
        => choices[r.Next(choices.Length)];

    // tML 1.4.4.9 tem TRES sobrecargas: (params T[]), (IList<T>) e
    // (IReadOnlyList<T>). O IL do mod chama direto a de IList — sem ela o
    // `GetChat` de todo NPC de cidade vira MissingMethodException e o JIT
    // recusa o metodo inteiro (Fargo: Abominationn/Mutant/Deviantt/LumberJack).
    public static T Next<T>(Utilities.UnifiedRandom r, System.Collections.Generic.IList<T> choices)
        => choices.Count == 0 ? throw new System.ArgumentOutOfRangeException(nameof(choices)) : choices[r.Next(choices.Count)];

    public static T Next<T>(Utilities.UnifiedRandom r, System.Collections.Generic.IReadOnlyList<T> choices)
        => choices.Count == 0 ? throw new System.ArgumentOutOfRangeException(nameof(choices)) : choices[r.Next(choices.Count)];
}
