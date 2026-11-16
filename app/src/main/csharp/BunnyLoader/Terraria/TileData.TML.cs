namespace Terraria;

public unsafe sealed partial class TileData
{
    static readonly Tile[] Slots = new Tile[32];

    static int nextSlot;

    // v100: o anel de 32 slots agora tambem serve o struct Tilemap (Main.tile).
    internal static Tile Scratch(int offset)
    {
        ref Tile tile = ref Slots[System.Threading.Interlocked.Increment(ref nextSlot) & 31];
        tile._tileOffset = offset;
        return tile;
    }

    public ref Tile this[Microsoft.Xna.Framework.Point position] => ref this[position.X, position.Y];

    public ref Tile this[int x, int y]
    {
        get
        {
            ref Tile tile = ref Slots[System.Threading.Interlocked.Increment(ref nextSlot) & 31];
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                tile = Tile.s_Null;
            else
                tile._tileOffset = _width * y + x;
            return ref tile;
        }
    }
}
