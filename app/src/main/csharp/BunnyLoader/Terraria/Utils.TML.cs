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

    // tML 1.4.5 expõe `Utils.NextBool(rng)` como método de UM parâmetro;
    // o shim só tinha a versão com parâmetro opcional `= 2`, que em IL é
    // SEMPRE de dois parâmetros. O mod compila `call bool
    // Terraria.Utils::NextBool(UnifiedRandom)`, o runtime não acha e o
    // IlPatcher esvaziava o método inteiro (293 ocorrências no FargoSouls).
    public static bool NextBool(Utilities.UnifiedRandom r) => r.Next(2) == 0;

    public static bool NextBool(Utilities.UnifiedRandom r, int chanceDenominator = 2)
        => chanceDenominator <= 1 || r.Next(chanceDenominator) == 0;

    public static bool NextBool(Utilities.UnifiedRandom r, int chanceNumerator, int chanceDenominator)
        => NextFloat(r, 0f, chanceDenominator) < chanceNumerator;

    // Mesmo caso: `Utils.NextFloat(rng, max)` (dois parâmetros) não existe em
    // IL quando só há `NextFloat(rng, min, max)` e `NextFloat(rng)`.
    public static float NextFloat(Utilities.UnifiedRandom r, float max)
        => (float)r.NextDouble() * max;

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

    // ---- mesmos helpers, mas sob os NOMES do vanilla 1.4.5.8 -----------
    // `out/tmp/fs-missing.txt` lista estes como SEM (o dump os tem, o shim
    // não). Implementação idêntica à de `Next`, só muda o nome.
    public static T SelectRandom<T>(Utilities.UnifiedRandom r, T[] choices)
        => choices[r.Next(choices.Length)];

    public static T NextFromList<T>(Utilities.UnifiedRandom r, T[] objs)
        => objs[r.Next(objs.Length)];

    public static T NextFromCollection<T>(Utilities.UnifiedRandom r, System.Collections.Generic.List<T> objs)
        => objs[r.Next(objs.Count)];

    // `Utils.Max(T[])` (refs/dump.cs:72823). `Comparer<T>.Default` cobre
    // int/float/Point/… sem precisar da constraint `IComparable<T>` do
    // original (constraint não entra na assinatura, o JIT resolve igual).
    public static T Max<T>(T[] args)
    {
        if (args == null || args.Length == 0) return default!;
        var cmp = System.Collections.Generic.Comparer<T>.Default;
        T best = args[0];
        for (int i = 1; i < args.Length; i++)
        {
            if (cmp.Compare(args[i], best) > 0) best = args[i];
        }
        return best;
    }

    // ---- Vector2 em BinaryWriter/BinaryReader --------------------------
    // O gerado declara estes com o parâmetro rebaixado para
    // `BunnyLoader.Interop.GameObject`, mas o mod passa o `BinaryWriter` /
    // `BinaryReader` de verdade (tipo da BCL, não tem proxy aqui) -> ASSIN
    // e o método inteiro não JITa (SendExtraAI de uns 15 projéteis).
    // `refs/dump.cs:73046/73058` são as implementações do vanilla.
    //
    // `Packed` é formato próprio do vanilla e o dump só tem a RVA (sem IL).
    // Aqui a codificação é a mesma nos dois lados do BL (short com 1/16 de
    // precisão), então o round-trip fecha; se um dia precisar de
    // compatibilidade com o PC, é só trocar a escala nos dois métodos.
    public static void WriteVector2(System.IO.BinaryWriter bb, Microsoft.Xna.Framework.Vector2 v)
    {
        bb.Write(v.X);
        bb.Write(v.Y);
    }

    public static Microsoft.Xna.Framework.Vector2 ReadVector2(System.IO.BinaryReader bb)
        => new(bb.ReadSingle(), bb.ReadSingle());

    public static void WritePackedVector2(System.IO.BinaryWriter bb, Microsoft.Xna.Framework.Vector2 v)
    {
        bb.Write((short)(v.X * 16f));
        bb.Write((short)(v.Y * 16f));
    }

    public static Microsoft.Xna.Framework.Vector2 ReadPackedVector2(System.IO.BinaryReader bb)
        => new(bb.ReadInt16() * (1f / 16f), bb.ReadInt16() * (1f / 16f));
}
