using Terraria.ModLoader;

namespace Terraria;

public sealed partial class Player
{
    public SceneEffectLoader.SceneEffectInstance CurrentSceneEffect => SceneEffectLoader.Of(this);

    public bool InModBiome(ModBiome baseInstance) => BiomeLoader.InBiome(this, baseInstance);

    public bool InModBiome<T>() where T : ModBiome => InModBiome(ModContent.GetInstance<T>());
}
