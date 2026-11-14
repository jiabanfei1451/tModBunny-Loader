namespace Terraria.GameContent;

public unsafe sealed partial class FlexibleTileWand
{
    public FlexibleTileWand AddVariations(int itemType, int tileIdToPlace, params int[] stylesToPlace)
    {
        AddVariations(itemType, (ushort)tileIdToPlace, (BunnyLoader.Interop.GameArray<int>)stylesToPlace);
        return this;
    }
}
