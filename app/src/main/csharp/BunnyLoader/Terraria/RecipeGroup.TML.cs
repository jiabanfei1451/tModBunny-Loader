using System;
using BunnyLoader.Interop;
using Terraria.ModLoader;

namespace Terraria;

public sealed partial class RecipeGroup
{
    /// <summary>Construtor do tML: <c>new RecipeGroup(() => Lang.misc[37].Value, items)</c>.</summary>
    public RecipeGroup(Func<string> getName, params int[] validItems)
        : this(New(getName, ItemArray(validItems)).Pointer) { }

    /// <summary>Idem, com um nome fixo (usado por <c>Register(string, ...)</c>).</summary>
    public RecipeGroup(string name, params int[] validItems)
        : this(New(name, ItemArray(validItems)).Pointer) { }

    public string Key => RecipeLoader.KeyOf(this);

    /// <summary>
    /// Do tML (o jogo deste port não tem <c>RegisterGroup</c>: os grupos
    /// nativos nascem direto de <c>RecipeGroup.Register()</c>). Registra e
    /// guarda a chave para o <c>AddRecipeGroup(string, ...)</c> e para
    /// <see cref="ID.RecipeGroupID"/> acharem o id depois.
    /// </summary>
    public static int RegisterGroup(string key, RecipeGroup group)
    {
        if (RecipeLoader.GroupByKey(key) is RecipeGroup existing) return existing.RegisteredId;
        group.Register();
        int id = group.RegisteredId;
        RecipeLoader.Named(key, group, null);
        return id;
    }

    public RecipeGroup Add(int itemID) => Add(itemID, default(BunnyLoader.Interop.GameObject));

    public static RecipeGroup Register(string key, string groupDescriptorKey, params int[] validItems)
    {
        if (RecipeLoader.GroupByKey(key) is RecipeGroup existing) return RecipeLoader.Merge(existing, validItems);
        RecipeGroup group = New(groupDescriptorKey, ItemArray(validItems)).Register();
        RecipeLoader.Named(key, group, null);
        return group;
    }

    public static RecipeGroup Register(string key, Func<string> getName, params int[] validItems)
    {
        if (RecipeLoader.GroupByKey(key) is RecipeGroup existing) return RecipeLoader.Merge(existing, validItems);
        RecipeGroup group = New(key, ItemArray(validItems)).Register();
        RecipeLoader.Named(key, group, getName);
        return group;
    }

    static GameArray<int> ItemArray(int[] validItems)
    {
        var items = GameArray<int>.New(new GameClass("System", "Int32"), validItems.Length);
        for (int i = 0; i < validItems.Length; i++) items[i] = validItems[i];
        return items;
    }
}