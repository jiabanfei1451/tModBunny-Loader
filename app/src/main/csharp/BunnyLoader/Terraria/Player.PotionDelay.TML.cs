using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace Terraria;

public sealed partial class Player
{
    public ref StatModifier PotionDelayModifier => ref PlayerPotionDelayHooks.Get(this);
}
