using System;
using BunnyLoader;

namespace Terraria;

public unsafe sealed partial class Chest
{
    public Item GetItem(int slot)
    {
        if (IsNull || slot < 0 || slot >= item.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        return new Item(Host.Native()->ExpandChestItem(index, slot));
    }


    /// <summary>
    /// O tModLoader de PC declara <c>Chest.item</c> como <c>Item[]</c>; neste
    /// port <c>Player.bank</c>/<c>bank2</c>/<c>bank3</c>/<c>bank4</c> sao
    /// <c>InventoryStorage</c> (com <c>GameRefArray&lt;Item&gt; item</c>) e so o
    /// <c>Chest</c> de verdade guarda <c>ChestItem[]</c>. O IlPatcher troca o
    /// <c>ldfld Chest::item</c> do mod por esta funcao: o recebedor pode ser
    /// qualquer um dos dois e o mod recebe um <c>Item[]</c> de verdade
    /// (FargoPlayer.PostUpdateBuffs lia o ChestItem de 6 bytes como se fosse
    /// uma referencia e estourava NullReferenceException em Item.get_IsAir).
    /// </summary>
    public static Item[] Items(BunnyLoader.Interop.IGameObject self)
    {
        if (self is null || self.Pointer == 0) return Array.Empty<Item>();
        if (BunnyLoader.Interop.GameInterop.IsInstance(self.Pointer, InventoryStorage.Class))
        {
            BunnyLoader.Interop.GameRefArray<Item> storage = InventoryStorage.__ref_item(self);
            return storage;
        }
        var chest = new Chest(self.Pointer);
        int length = chest.item.Length;
        var result = new Item[length];
        for (int i = 0; i < length; i++) result[i] = chest.GetItem(i);
        return result;
    }

    public void SetItem(int slot, Item value)
    {
        if (IsNull || slot < 0 || slot >= item.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        if (value.IsNull) throw new ArgumentException("A chest item cannot be null.", nameof(value));
        item[slot].SetToItem(value);
        if (Host.Native()->StoreChestItem(index, slot, value.Pointer) == 0)
            throw new InvalidOperationException("The chest item data could not be stored.");
        InventoryStorage.SyncChestItemUpdateToStorage(index, slot);
    }
}
