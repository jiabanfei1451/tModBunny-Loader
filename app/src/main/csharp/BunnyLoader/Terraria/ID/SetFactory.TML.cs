using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BunnyLoader.Interop;
using Terraria.ModLoader;

namespace Terraria.ID;

public sealed partial class SetFactory
{
    public sealed class NamedSetKey
    {
        readonly SetFactory factory;

        internal readonly string fullKey;

        internal string? description;

        internal NamedSetKey(SetFactory factory, string fullKey)
        {
            if (fullKey.Contains(' ')) throw new ArgumentException("Set names may not contain spaces");
            this.factory = factory;
            this.fullKey = fullKey.Contains('/') ? fullKey : $"{reinitializing?.Name ?? "Terraria"}/{fullKey}";
        }

        public NamedSetKey Description(string description)
        {
            this.description = description;
            return this;
        }

        public T[] RegisterCustomSet<T>(T defaultState, params object[] inputs)
        {
            T[] set = factory.NewSet(defaultState);
            for (int i = 0; i + 1 < inputs.Length; i += 2)
                set[Convert.ToInt32(inputs[i])] = (T)inputs[i + 1];
            return factory.RegisterNamed(this, defaultState, set);
        }

        public float[] RegisterFloatSet(float defaultState, params float[] inputs)
        {
            float[] set = factory.NewSet(defaultState);
            for (int i = 0; i + 1 < inputs.Length; i += 2)
                set[(int)inputs[i]] = inputs[i + 1];
            return factory.RegisterNamed(this, defaultState, set);
        }

        public ushort[] RegisterUshortSet(ushort defaultState, params ushort[] inputs)
        {
            ushort[] set = factory.NewSet(defaultState);
            for (int i = 0; i + 1 < inputs.Length; i += 2)
                set[inputs[i]] = inputs[i + 1];
            return factory.RegisterNamed(this, defaultState, set);
        }

        public int[] RegisterIntSet(int defaultState, params int[] inputs)
        {
            int[] set = factory.NewSet(defaultState);
            for (int i = 0; i + 1 < inputs.Length; i += 2)
                set[inputs[i]] = inputs[i + 1];
            return factory.RegisterNamed(this, defaultState, set);
        }

        public int[] RegisterIntSet(params int[] types) => RegisterIntSet(-1, types);

        public bool[] RegisterBoolSet(params int[] types) => RegisterBoolSet(false, types);

        public bool[] RegisterBoolSet(bool defaultState, params int[] types)
        {
            bool[] set = factory.NewSet(defaultState);
            foreach (int type in types)
                set[type] = !defaultState;
            return factory.RegisterNamed(this, defaultState, set);
        }
    }

    static readonly ConcurrentDictionary<(nint Factory, string Key, Type Type), Array> namedSets = new();

    static bool grown;

    static Mod? reinitializing;

    internal static void GrowToContent()
    {
        ItemID.Sets.Factory._size = ItemLoader.ItemCount;
        ProjectileID.Sets.Factory._size = ProjectileLoader.ProjectileCount;
        NPCID.Sets.Factory._size = NPCLoader.NPCCount;
        BuffID.Sets.Factory._size = BuffLoader.BuffCount;
        TileID.Sets.Factory._size = TileLoader.TileCount;
        WallID.Sets.Factory._size = WallLoader.WallCount;
        // As duas tabelas escritas à mão (SortingPriorityBossSpawns /
        // BannerStrength) não são geradas pelo ProxyGen, então o
        // ReinitializeSets não as reconstrói. Precisa crescer aqui, ANTES
        // dos SetStaticDefaults, senão `array[Type]` de item de mod estoura.
        ItemID.Sets.GrowForModContent();
        grown = true;
    }

    internal static void ReinitializeSets(Mod mod)
    {
        reinitializing = mod;
        try
        {
            foreach (Type type in mod.GetType().Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<ReinitializeDuringResizeArraysAttribute>() != null)
                .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase))
                Reinitialize(type);
        }
        finally
        {
            reinitializing = null;
        }
    }

    static void Reinitialize(Type type)
    {
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        type.TypeInitializer?.Invoke(null, null);
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            if (!nested.ContainsGenericParameters)
                Reinitialize(nested);
    }

    T[] NewSet<T>(T defaultState)
    {
        var set = new T[_size];
        if (!EqualityComparer<T>.Default.Equals(defaultState, default!))
            Array.Fill(set, defaultState);
        return set;
    }

    /// <summary>
    /// API pública do contrato 1.4.4.9 (<c>SetFactory.cs.patch</c>): conjuntos
    /// de ID para tipos que as variantes tipadas não cobrem (string, List&lt;T&gt;,
    /// bool?, structs). O Fargowiltas chama isto em <c>Load()</c> para
    /// <c>ItemID.Sets.BannerStrength</c>. Como o shim só tinha o equivalente em
    /// <c>NamedSetKey.RegisterCustomSet</c>, o IlPatcher classificava o `call`
    /// como "método apagado na 1.4.5" e esvaziava o `Load()` INTEIRO — todos
    /// os singletons/config/keybinds do mod ficavam nulos.
    ///
    /// `inputs` vem em pares (índice, valor); índices além do tamanho atual
    /// são ignorados, como no patch do tML (o mod pode registrar conjuntos
    /// antes de todos os IDs de mod existirem).
    /// </summary>
    /// <summary>
    /// O ProxyGen gera `CreateBoolSet` com `GameArray&lt;int&gt;` (array nativo),
    /// mas um `.tmod` compilado no PC chama a assinatura do contrato
    /// <c>bool[] CreateBoolSet(bool, int[])</c>. Sem o overload com `int[]` o
    /// Mono não liga o `call` e `FargoPlayer.Initialize`, `LoadData` e
    /// `FargoSets.PostSetupContent` falham — três inicializações a menos.
    /// </summary>
    public bool[] CreateBoolSet(bool defaultState, params int[] types)
    {
        var set = NewSet(defaultState);
        if (types == null) return set;
        foreach (int type in types)
            if ((uint)type < (uint)set.Length) set[type] = !defaultState;
        return set;
    }

    public bool[] CreateBoolSet(params int[] types) => CreateBoolSet(false, types);

    public T[] CreateCustomSet<T>(T defaultState, params object[] inputs)
    {
        if (inputs.Length % 2 != 0)
            throw new ArgumentException("Inputs must be a list of pairs: id, value, id, value...");
        T[] set = NewSet(defaultState);
        if (inputs == null) return set;
        for (int i = 0; i + 1 < inputs.Length; i += 2)
        {
            int index = Convert.ToInt32(inputs[i]);
            if ((uint)index >= (uint)set.Length) continue;
            object? raw = inputs[i + 1];
            if (raw is null)
            {
                set[index] = default!;
                continue;
            }
            if (typeof(T).IsInstanceOfType(raw)) set[index] = (T)raw;
            else set[index] = (T)Convert.ChangeType(raw, typeof(T));
        }
        return set;
    }

    T[] RegisterNamed<T>(NamedSetKey key, T defaultState, T[] input)
    {
        if (!grown)
            return input;
        var id = (Pointer, key.fullKey, typeof(T));
        if (!namedSets.TryGetValue(id, out Array? existing) || ((T[])existing).Length != input.Length)
        {
            namedSets[id] = input;
            return input;
        }

        var shared = (T[])existing;
        for (int i = 0; i < input.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(input[i], defaultState))
                shared[i] = input[i];
        return shared;
    }

    public NamedSetKey CreateNamedSet(string fullKey) => new(this, fullKey);

    public NamedSetKey CreateNamedSet(string modName, string key) => new(this, $"{modName}/{key}");

    public NamedSetKey CreateNamedSet(Mod mod, string key) => new(this, $"{mod.Name}/{key}");

    /// <summary>
    /// Contrato 1.4.4.9 (<c>SetFactory.cs.patch:47/:50</c>): devolve
    /// <c>int[]</c> GERENCIADO, com pares (índice, valor). A versão gerada
    /// pelo ProxyGen devolve <c>GameArray&lt;int&gt;</c> (struct nativo) e
    /// recebe <c>GameArray&lt;int&gt;</c>; o <c>call</c> do
    /// <c>FargoSets.PostSetupContent</c> espera <c>int[] CreateIntSet(int, int[])</c>
    /// e virava <c>MissingMethodException</c>. Não há sobrecarga por tipo de
    /// retorno em C#, então este overload com <c>int[]</c> é obrigatório.
    /// </summary>
    public int[] CreateIntSet(int defaultState, params int[] inputs)
    {
        if (inputs.Length % 2 != 0)
            throw new ArgumentException("Inputs must be a list of pairs: id, value, id, value...");
        int[] set = NewSet(defaultState);
        for (int i = 0; i + 1 < inputs.Length; i += 2)
            if ((uint)inputs[i] < (uint)set.Length) set[inputs[i]] = inputs[i + 1];
        return set;
    }

    public int[] CreateIntSet(params int[] types) => CreateIntSet(-1, types);
}
