namespace Terraria.ID;

// v100: o FargowiltasSouls referencia Terraria.ID.SlopeType (namespace do
// contrato 1.4.4.9), mas o dump nao traz o tipo. Os valores seguem a ordem
// do enum nativo (Solid=0 .. SlopeUpRight=4); o shim tambem tem
// Terraria.SlopeType em Terraria/Tile.TML.cs, usado pelo proxy de Tile.
public enum SlopeType : byte
{
    Solid,
    SlopeDownLeft,
    SlopeDownRight,
    SlopeUpLeft,
    SlopeUpRight
}
