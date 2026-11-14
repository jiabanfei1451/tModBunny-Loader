using Terraria.ModLoader;

namespace Terraria;

public sealed partial class Main
{
    public void OpenShop(string shopName) => NPCShopDatabase.OpenShop(shopName);
}
