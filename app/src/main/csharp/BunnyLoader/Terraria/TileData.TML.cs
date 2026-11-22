namespace Terraria;

public unsafe sealed partial class TileData
{
    static readonly Tile[] Slots = new Tile[32];

    static int nextSlot;

    // Os arrays nativos de tile sao campos estaticos ponteiro (dump.cs:
    // TileLookup 0x68, TileType 0xA8, TileSHeader 0xB0, TileFrameX 0xB8,
    // TileFrameY 0xC0, TileWall 0xC8). O ProxyGen nao gera esses campos, entao
    // lemos o endereco do campo estatico e desreferenciamos uma vez.
    static global::BunnyLoader.Interop.StaticRef lookupField, typeField, headerField, frameXField, frameYField, wallField;

    static uint* Lookup => (uint*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref lookupField, Class, "TileLookup");
    static ushort* Types => (ushort*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref typeField, Class, "TileType");
    static short* Headers => (short*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref headerField, Class, "TileSHeader");
    static short* FrameXs => (short*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref frameXField, Class, "TileFrameX");
    static short* FrameYs => (short*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref frameYField, Class, "TileFrameY");
    static ushort* Walls => (ushort*)*(nint*)global::BunnyLoader.Interop.GameInterop.StaticAddress(ref wallField, Class, "TileWall");

    internal static int RawOffset(int x, int y)
    {
        int width = Main.maxTilesX, height = Main.maxTilesY;
        if (width <= 0 || height <= 0 || x < 0 || y < 0) return -1;
        return x >= width || y >= height ? -1 : width * y + x;
    }

    // Bit 0x20 do tileSHeader = tile ativo (o resto do header tem os bits de
    // liquid/slope que o jogo usa no desenho).
    internal static bool RawActive(int offset) => offset >= 0 && ((Headers[Lookup[offset]] & 0x20) != 0);
    internal static ushort RawType(int offset) => offset < 0 ? (ushort)0 : Types[Lookup[offset]];
    internal static short RawFrameX(int offset) => offset < 0 ? (short)0 : FrameXs[Lookup[offset]];
    internal static short RawFrameY(int offset) => offset < 0 ? (short)0 : FrameYs[Lookup[offset]];
    internal static ushort RawWall(int offset) => offset < 0 ? (ushort)0 : Walls[offset];

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
