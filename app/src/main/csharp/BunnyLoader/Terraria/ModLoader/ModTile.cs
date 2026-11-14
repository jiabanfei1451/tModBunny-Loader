using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.ObjectInteractions;
using Terraria.Localization;

namespace Terraria.ModLoader;

public abstract class ModBlockType : ModTexturedType, ILocalizedModType
{
    public ushort Type { get; internal set; }

    // Contrato é PROPRIEDADE (`public SoundStyle? HitSound { get; set; }`).
    public SoundStyle? HitSound { get; set; } = new SoundStyle(ID.SoundID.Dig);

    // Contrato é PROPRIEDADE (`{ get; set; } = 0`), campo não expõe setter.
    public int DustType { get; set; }

    public ushort VanillaFallbackOnModDeletion { get; set; } = 0;

    public abstract string LocalizationCategory { get; }

    public LocalizedText CreateMapEntryName() => this.GetLocalization("MapEntry", PrettyPrintName);

    public virtual ushort GetMapOption(int i, int j) => 0;

    public virtual bool KillSound(int i, int j, bool fail) => true;

    public virtual void NumDust(int i, int j, bool fail, ref int num)
    {
    }

    public virtual bool CreateDust(int i, int j, ref int type) => true;

    public virtual bool CanPlace(int i, int j) => true;

    public virtual bool CanExplode(int i, int j) => true;

    public virtual bool PreDraw(int i, int j, SpriteBatch spriteBatch) => true;

    public virtual void PostDraw(int i, int j, SpriteBatch spriteBatch)
    {
    }

    public virtual void RandomUpdate(int i, int j, bool underground)
    {
    }

    public virtual void PlaceInWorld(int i, int j, Item item)
    {
    }

    public virtual void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
    }

    public virtual void Convert(int i, int j, int conversionType)
    {
    }
}

public abstract class ModTile : ModBlockType
{
    public float MineResist = 1f;

    public int MinPick;

    // Contrato 1.4.4.9 (`ModTile.cs:40`): `public int[] AdjTiles { get; set; }`.
    // Como CAMPO só existe `stfld AdjTiles`; um `.tmod` compilado no PC chama
    // `callvirt set_AdjTiles(int[])`. `ModTile` é abstrato e por isso FICA FORA
    // do ShimMap (BuildMap ignora abstract não-sealed), então o IlPatcher não
    // converte esse call em stfld — vira MissingMethodException nos 3 altares
    // do Fargowiltas. Virar propriedade expõe o setter de verdade.
    public int[] AdjTiles { get; set; } = Array.Empty<int>();

    // Contrato é PROPRIEDADE (`public int AnimationFrameHeight { get; set; }`),
    // campo não expõe `set_AnimationFrameHeight`. Mesmo caso do DustType.
    public int AnimationFrameHeight { get; set; }

    public virtual string HighlightTexture => Texture + "_Highlight";

    public bool IsDoor => ID.TileID.Sets.OpenDoorID[Type] != -1 || ID.TileID.Sets.CloseDoorID[Type] != -1;

    public override string LocalizationCategory => "Tiles";

    protected sealed override void Register() => TileLoader.Register(this);

    public sealed override void SetupContent() => SetStaticDefaults();

    // Contrato 1.4.4.9 tem DOIS overloads: `(Color, LocalizedText name = null)` e
    // `(Color, LocalizedText name, Func<...> nameFunc)`. O mod chamando
    // `AddMapEntry(color, nome)` compila para o overload de DOIS parâmetros; o
    // shim só tinha o de três (com default), então o `callvirt` de 2 parâmetros
    // não ligava -> MissingMethodException (4 tiles do Fargowiltas).
    public void AddMapEntry(Color color, LocalizedText name = null)
    {
        TileLoader.AddMapEntry(this, color, name ?? CreateMapEntryName(), null);
    }

    public void AddMapEntry(Color color, LocalizedText name, Func<string, int, int, string> nameFunc)
    {
        TileLoader.AddMapEntry(this, color, name ?? CreateMapEntryName(), nameFunc);
    }

    public void AddToArray(ref int[] array)
    {
        Array.Resize(ref array, array.Length + 1);
        array[^1] = Type;
    }

    public void RegisterItemDrop(int itemType, params int[] tileStyles)
    {
        TileLoader.RegisterItemDrop(Type, itemType, tileStyles);
    }

    public virtual bool CanDrop(int i, int j) => true;

    public virtual IEnumerable<Item> GetItemDrops(int i, int j) => TileLoader.DefaultDrops(this, i, j);

    public virtual bool CanKillTile(int i, int j, ref bool blockDamaged) => true;

    public virtual void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
    }

    public virtual void KillMultiTile(int i, int j, int frameX, int frameY)
    {
    }

    public virtual void ReplaceTile(int i, int j, int targetType, int targetStyle)
    {
    }

    public virtual void NearbyEffects(int i, int j, bool closer)
    {
    }

    public virtual bool IsTileDangerous(int i, int j, Player player) => false;

    public virtual bool IsTileSpelunkable(int i, int j) => false;

    public virtual void SetSpriteEffects(int i, int j, ref SpriteEffects spriteEffects)
    {
    }

    public virtual void SetDrawPositions(int i, int j, ref int width, ref int offsetY, ref int height, ref short tileFrameX, ref short tileFrameY)
    {
    }

    public virtual void AnimateTile(ref int frame, ref int frameCounter)
    {
    }

    public virtual void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset)
    {
    }

    public virtual void DrawEffects(int i, int j, SpriteBatch spriteBatch, ref TileDrawInfo drawData)
    {
    }

    public virtual void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
    {
    }

    public virtual bool RightClick(int i, int j) => false;

    public virtual bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => TileLoader.Has(this, 14);

    public virtual void ModifySmartInteractCoords(ref int width, ref int height, ref int frameWidth, ref int frameHeight, ref int extraY)
    {
    }

    public virtual bool IsLockedChest(int i, int j) => false;

    public virtual bool UnlockChest(int i, int j, ref short frameXAdjustment, ref int dustType, ref bool manual) => false;

    public virtual bool LockChest(int i, int j, ref short frameXAdjustment, ref bool manual) => false;

    public virtual LocalizedText DefaultContainerName(int frameX, int frameY) => default;

    public virtual void MouseOver(int i, int j)
    {
    }

    public virtual void MouseOverFar(int i, int j)
    {
    }

    public virtual void HitWire(int i, int j)
    {
    }

    public virtual bool Slope(int i, int j) => true;

    public virtual bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak) => true;

    public virtual bool CanReplace(int i, int j, int tileTypeBeingPlaced) => true;

    public virtual void ModifySittingTargetInfo(int i, int j, ref TileRestingInfo info)
    {
    }

    public virtual void ModifySleepingTargetInfo(int i, int j, ref TileRestingInfo info)
    {
    }

    public virtual bool GrowSapling(int i, int j, bool underground, int treeHeightAddon, bool ignoreWalls) => false;

    public virtual void PostSetDefaults()
    {
    }

    public virtual void DropCritterChance(int i, int j, ref int wormChance, ref int grassHopperChance, ref int jungleGrubChance)
    {
    }

    public virtual void HitSwitch(int i, int j)
    {
    }

    public virtual bool SwitchTiles(int i, int j, Entity entity, Vector2 position, int width, int height, Vector2 oldPosition, int objType) => false;

    public virtual void FloorVisuals(Player player)
    {
    }

    public virtual bool HasWalkDust() => false;

    public virtual void WalkDust(ref int dustType, ref bool makeDust, ref Color color)
    {
    }

    public virtual float GetTorchLuck(Player player) => 0f;

    public virtual void EmitParticles(int i, int j, Tile tile, short tileFrameX, short tileFrameY, Color tileLight, bool visible)
    {
    }

    public virtual void GetTileFlameData(int i, int j, ref TileDrawing.TileFlameData tileFlameData)
    {
    }

    public virtual void AdjustMultiTileVineParameters(int i, int j, ref float? overrideWindCycle, ref float windPushPowerX, ref float windPushPowerY, ref bool dontRotateTopTiles, ref float totalWindMultiplier, ref Texture2D glowTexture, ref Color glowColor)
    {
    }

    /// <summary>tML: o mod decide se o tile é escolhido ao "auto selecionar".</summary>
    public virtual bool AutoSelect(int i, int j, Item item) => false;
}
