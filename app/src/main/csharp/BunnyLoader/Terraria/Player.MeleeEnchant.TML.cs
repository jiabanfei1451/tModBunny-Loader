namespace Terraria;

public sealed partial class Player
{
    public bool MeleeEnchantActive
    {
        get => meleeEnchant > 0;
        set => meleeEnchant = value ? (byte)255 : (byte)0;
    }
}
