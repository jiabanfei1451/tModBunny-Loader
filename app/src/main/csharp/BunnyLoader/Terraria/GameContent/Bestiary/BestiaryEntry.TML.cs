using System;
using System.Collections;
using System.Collections.Generic;
using BunnyLoader.Interop;

namespace Terraria.GameContent.Bestiary;

public sealed partial class BestiaryEntry
{
    /// <summary>Wrapper ao vivo da lista nativa (uso interno do shim).</summary>
    public BestiaryInfoList InfoList => new(Info_);

    /// <summary>
    /// O tML declara <c>Info</c> como <c>List&lt;IBestiaryInfoElement&gt;</c>;
    /// aqui a lista nativa é do il2cpp, então o que dá para entregar é um
    /// RETRATO gerenciado. Ler funciona; um <c>Add</c> do mod se perde.
    /// </summary>
    public List<IBestiaryInfoElement> Info
    {
        get
        {
            var result = new List<IBestiaryInfoElement>();
            if (IsNull) return result;
            var native = new GameList(Info_);
            int count = native.Count;
            for (int i = 0; i < count; i++) result.Add(new IBestiaryInfoElement(native[i]));
            return result;
        }
    }
}

public sealed partial class FlavorTextBestiaryInfoElement
{
    public FlavorTextBestiaryInfoElement(string languageKey) : this(New(languageKey).Pointer) { }
}

public readonly struct BestiaryInfoList : IEnumerable<IBestiaryInfoElement>
{
    readonly GameList list;

    internal BestiaryInfoList(GameObject list) => this.list = new GameList(list);

    public int Count => list.Count;

    public IBestiaryInfoElement this[int index] => new(list[index]);

    public void Add(IBestiaryInfoElement element)
    {
        if (element.IsNull) throw new ArgumentNullException(nameof(element));
        list.Add(element.Pointer);
    }

    public void AddRange(IEnumerable<IBestiaryInfoElement> elements)
    {
        foreach (IBestiaryInfoElement element in elements) Add(element);
    }

    public void RemoveAt(int index) => list.RemoveAt(index);

    public int RemoveAll(Predicate<IBestiaryInfoElement> match)
    {
        int removed = 0;
        for (int i = Count - 1; i >= 0; i--)
        {
            if (!match(this[i])) continue;
            RemoveAt(i);
            removed++;
        }
        return removed;
    }

    public IEnumerator<IBestiaryInfoElement> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
