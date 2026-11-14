using Terraria.ModLoader.Core;

namespace Terraria;

public sealed partial class Player
{
    public void SetCCed() => PlayerCrowdControlHooks.Set(this);
}
