using System.Collections.Generic;
using Terraria.Audio;
using Terraria.ModLoader.Core;
using Terraria.ModLoader;

namespace Terraria;

public sealed partial class Item : IEntityWithGlobals<GlobalItem>
{
    public ModItem? ModItem => ItemLoader.InstanceOf(this);

    public IReadOnlyList<GlobalItem> EntityGlobals => ItemLoader.Globals.Of(this);

    public T GetGlobalItem<T>() where T : GlobalItem => ItemLoader.Globals.Get<T>(this);

    public T GetGlobalItem<T>(T baseInstance) where T : GlobalItem => ItemLoader.Globals.Get(this, baseInstance);

    public bool TryGetGlobalItem<T>(out T result) where T : GlobalItem => ItemLoader.Globals.TryGet(this, out result);

    public WorldItem GetWorldItem() => ItemPickupHooks.WorldItemOf(this);

    public DamageClass DamageType
    {
        get => DamageClassLoader.ItemClass(this);
        set => DamageClassLoader.SetItemClass(this, value);
    }

    public bool attackSpeedOnlyAffectsWeaponAnimation
    {
        get => DamageClassLoader.AnimationOnly(type);
        set => DamageClassLoader.SetAnimationOnly(type, value);
    }

    public static int NewItem(Terraria.DataStructures.IEntitySource source, Microsoft.Xna.Framework.Rectangle rectangle, int type, int stack = 1, int prefix = 0, NewItemOwnership ownership = NewItemOwnership.None) =>
        NewItem(source, new Microsoft.Xna.Framework.Vector2(rectangle.X + rectangle.Width / 2f, rectangle.Y + rectangle.Height / 2f), type, stack, prefix, ownership);

    public static int NewItem(Terraria.DataStructures.IEntitySource source, Microsoft.Xna.Framework.Vector2 position, Microsoft.Xna.Framework.Vector2 size, int type, int stack = 1, int prefix = 0, NewItemOwnership ownership = NewItemOwnership.None) =>
        NewItem(source, new Microsoft.Xna.Framework.Vector2(position.X + size.X / 2f, position.Y + size.Y / 2f), type, stack, prefix, ownership);

    public void DefaultToPlaceableWall(int wallToPlace) => DefaultToPlaceableWall_((ushort)wallToPlace);

    public bool CountsAsClass<T>() where T : DamageClass => CountsAsClass(ModContent.GetInstance<T>());

    public bool CountsAsClass(DamageClass damageClass) => DamageType.CountsAsClass(damageClass);

    public void DefaultToTorch(int tileIDToPlace, int tileStyleToPlace, bool allowWaterPlacement = false)
    {
        DefaultToTorch(tileStyleToPlace, allowWaterPlacement);
        createTile = tileIDToPlace;
    }

    public Item(int setDefaultsToType, int stack = 1, int prefix = 0)
    {
        Item item = New();
        ItemLoader.KeepAlive(item);
        item.SetDefaults(setDefaultsToType, default);
        if (item.type > 0)
        {
            item.stack = stack;
            if (prefix > 0) item.Prefix(prefix);
        }
        Pointer = item.Pointer;
    }

    public void CloneDefaults(int TypeToClone) => ItemLoader.CloneDefaults(this, TypeToClone);

    public void DefaultToMusicBox(int tileIDToPlace, int style = 0)
    {
        DefaultToMusicBox(style);
        createTile = tileIDToPlace;
        placeStyle = style;
    }

    public int ResearchUnlockCount
    {
        get => ItemLoader.ResearchUnlockCount(type);
        set => ItemLoader.SetResearchUnlockCount(type, value);
    }

    /// <summary>O jogo declara <c>(short)</c>; o tML expõe <c>(int)</c>.</summary>
    public void DefaultToCapturedCritter(int npcIdToSpawnOnUse) => DefaultToCapturedCritter((short)npcIdToSpawnOnUse);

    static int __useSoundOffset = -1;

    /// <summary>
    /// No tML <c>Item.UseSound</c> é <c>SoundStyle?</c> — e é assim que o mod
    /// usa: <c>item.UseSound = SoundID.Item1;</c> vira
    /// <c>newobj Nullable&lt;SoundStyle&gt;</c> + <c>stfld</c>. No dump o campo
    /// é uma REFERÊNCIA <c>LegacySoundStyle</c> (8 bytes). Ler/escrever o
    /// ponteiro direto mantém o layout e dá a assinatura certa ao mod.
    /// </summary>
    public unsafe SoundStyle? UseSound
    {
        get
        {
            if (Pointer == 0) return null;
            nint pointer = *(nint*)(Pointer + BunnyLoader.Interop.GameInterop.Offset(ref __useSoundOffset, Class, "UseSound"));
            return pointer == 0 ? null : new SoundStyle(pointer);
        }
        set
        {
            if (Pointer == 0) return;
            *(nint*)(Pointer + BunnyLoader.Interop.GameInterop.Offset(ref __useSoundOffset, Class, "UseSound")) =
                value?.Pointer ?? 0;
        }
    }

    /// <summary>
    /// Assinatura de <c>NewItem</c> do tML 1.4.4 (com <c>noBroadcast</c>,
    /// <c>noGrabDelay</c> e <c>reverseLookup</c>). Os sinalizadores extras não
    /// têm equivalente nativo aqui; ficam aceitos e ignorados.
    /// </summary>
    public static int NewItem(Terraria.DataStructures.IEntitySource source, Microsoft.Xna.Framework.Rectangle rectangle,
        int type, int stack, bool noBroadcast, int prefix, bool noGrabDelay, bool reverseLookup)
        => NewItem(source, rectangle, type, stack, prefix);
}
