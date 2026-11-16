using System;

namespace Terraria.ModLoader;

/// <summary>
/// v101: copia de <c>Terraria.ModLoader.RefReadOnlyArray&lt;T&gt;</c> (tML 1.4.4).
/// Usado nos ganchos de conteudo (o mod recebe um array readonly sem copiar).
/// </summary>
public readonly ref struct RefReadOnlyArray<T>
{
    internal readonly T[] array;

    public RefReadOnlyArray(T[] array) => this.array = array;

    public int Length => array.Length;

    public T this[int index] => array[index];

    public ReadOnlySpan<T>.Enumerator GetEnumerator() => ((ReadOnlySpan<T>)array).GetEnumerator();

    public static implicit operator RefReadOnlyArray<T>(T[] array) => new(array);

    public static implicit operator ReadOnlySpan<T>(RefReadOnlyArray<T> array) => array.array;
}
