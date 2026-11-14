using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Bestiary;
using Terraria.Localization;

namespace Terraria.ModLoader;

public abstract class ModBiome : ModSceneEffect, Terraria.GameContent.Personalities.IShoppingBiome, ILocalizedModType
{
    public override SceneEffectPriority Priority => SceneEffectPriority.BiomeLow;

    public override int Music => 0;

    public virtual int BiomeTorchItemType => -1;

    public virtual int BiomeCampfireItemType => -1;

    internal int ZeroIndexType => Type;

    public virtual string LocalizationCategory => "Biomes";

    public virtual LocalizedText DisplayName => this.GetLocalization(nameof(DisplayName), PrettyPrintName);

    public virtual string BestiaryIcon => (GetType().Namespace + "." + Name + "_Icon").Replace('.', '/');

    public virtual string BackgroundPath => (GetType().Namespace + "." + Name + "_Background").Replace('.', '/');

    public virtual Color? BackgroundColor => null;

    public ModBiomeBestiaryInfoElement ModBiomeBestiaryInfoElement { get; internal set; }

    internal string ShoppingNameKey => this.GetLocalizationKey("TownNPCDialogueName");

    protected sealed override void Register()
    {
        Type = BiomeLoader.Register(this);
        SceneEffectLoader.Register(this);
    }

    public sealed override void SetupContent()
    {
        SetStaticDefaults();
        BiomeLoader.SetupContent(this);
    }

    public sealed override bool IsSceneEffectActive(Player player) => BiomeLoader.InBiome(player, this);

    public override void SetStaticDefaults() { }

    public virtual bool IsBiomeActive(Player player) => false;

    // IShoppingBiome: o tML liga o diálogo dos NPCs de cidade ao bioma.
    string Terraria.GameContent.Personalities.IShoppingBiome.NameKey => ShoppingNameKey;

    bool Terraria.GameContent.Personalities.IShoppingBiome.IsInBiome(Player player) => IsBiomeActive(player);

    public virtual void OnEnter(Player player) { }

    public virtual void OnInBiome(Player player) { }

    public virtual void OnLeave(Player player) { }
}
