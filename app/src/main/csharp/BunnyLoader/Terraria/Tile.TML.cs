using System;

namespace Terraria;

public enum SlopeType : byte
{
    Solid,
    SlopeDownLeft,
    SlopeDownRight,
    SlopeUpLeft,
    SlopeUpRight
}

public enum BlockType
{
    Solid,
    HalfBlock,
    SlopeDownLeft,
    SlopeDownRight,
    SlopeUpLeft,
    SlopeUpRight
}

public unsafe partial struct Tile
{
    public SlopeType Slope
    {
        get => (SlopeType)slope();
        set => slope((byte)value);
    }

    public BlockType BlockType
    {
        get => IsHalfBlock ? BlockType.HalfBlock : Slope == SlopeType.Solid ? BlockType.Solid : (BlockType)((int)Slope + 1);
        set
        {
            IsHalfBlock = value == BlockType.HalfBlock;
            Slope = value > BlockType.HalfBlock ? (SlopeType)((int)value - 1) : SlopeType.Solid;
        }
    }

    public bool IsHalfBlock
    {
        get => halfBrick();
        set => halfBrick(value);
    }

    public byte TileColor
    {
        get => color();
        set => color(value);
    }

    public byte WallColor
    {
        get => wallColor();
        set => wallColor(value);
    }

    public int TileFrameNumber
    {
        get => frameNumber();
        set => frameNumber((byte)value);
    }

    public bool SkipLiquid
    {
        get => skipLiquid();
        set => skipLiquid(value);
    }

    public bool CheckingLiquid
    {
        get => checkingLiquid();
        set => checkingLiquid(value);
    }

    public bool RedWire
    {
        get => wire();
        set => wire(value);
    }

    public bool BlueWire
    {
        get => wire2();
        set => wire2(value);
    }

    public bool GreenWire
    {
        get => wire3();
        set => wire3(value);
    }

    public bool YellowWire
    {
        get => wire4();
        set => wire4(value);
    }

    public bool HasWire => anyWire();

    public bool IsTileInvisible
    {
        get => invisibleBlock();
        set => invisibleBlock(value);
    }

    public bool IsWallInvisible
    {
        get => invisibleWall();
        set => invisibleWall(value);
    }

    public bool IsTileFullbright
    {
        get => fullbrightBlock();
        set => fullbrightBlock(value);
    }

    public bool IsWallFullbright
    {
        get => fullbrightWall();
        set => fullbrightWall(value);
    }

    public ref byte LiquidAmount
    {
        get
        {
            __liquidAmount = liquid;
            return ref __liquidAmount;
        }
    }

    public int LiquidType
    {
        get => liquidType();
        set => liquidType(value);
    }

    // No tML estes três são `ref` (o Tile de lá é uma CLASSE com campos de
    // verdade). Aqui o Tile é um handle de 4 bytes, então não existe endereço
    // para devolver: o `ref` aponta para um buffer por thread, preenchido no
    // getter. Leitura fica correta (o mod faz `ldind` logo depois), escrita
    // continua sem efeito — o que já era o caso, porque o `tile` que o mod
    // recebe é uma CÓPIA.
    [ThreadStatic] static ushort __tileType;
    [ThreadStatic] static short __tileFrameX;
    [ThreadStatic] static short __tileFrameY;
    [ThreadStatic] static ushort __tileWallType;
    [ThreadStatic] static byte __liquidAmount;

    // ---- tML 1.4.5 patches/Terraria/Tile.TML.cs:311-320 -------------------
    // `Tile` virou `readonly struct` no tML; estes operadores existem para
    // que `tile == null` continue COMPILANDO (o segundo argumento é um
    // "espantalho" do tipo ArgumentException, sempre false/true). Copiados
    // do源码 em vez de adivinhados pelo IL do mod.
    public ushort TileId => type;

    public static bool operator ==(Tile tile, Tile tile2) => tile.TileId == tile2.TileId;
    public static bool operator !=(Tile tile, Tile tile2) => tile.TileId != tile2.TileId;
    public static bool operator ==(Tile tile, ArgumentException justSoYouCanCompareWithNull) => false;
    public static bool operator !=(Tile tile, ArgumentException justSoYouCanCompareWithNull) => true;

    public override bool Equals(object? obj) => obj is Tile other && this == other;
    public override int GetHashCode() => TileId;

    public ref ushort TileType
    {
        get
        {
            __tileType = type;
            return ref __tileType;
        }
    }

    public ref short TileFrameX
    {
        get
        {
            __tileFrameX = frameX;
            return ref __tileFrameX;
        }
    }

    public ref short TileFrameY
    {
        get
        {
            __tileFrameY = frameY;
            return ref __tileFrameY;
        }
    }

    public ref ushort WallType
    {
        get
        {
            __tileWallType = wall;
            return ref __tileWallType;
        }
    }

    public int WallFrameX
    {
        get => wallFrameX();
        set => wallFrameX(value);
    }

    public int WallFrameY
    {
        get => wallFrameY();
        set => wallFrameY(value);
    }

    public int WallFrameNumber
    {
        get => wallFrameNumber();
        set => wallFrameNumber((byte)value);
    }

    public bool HasTile
    {
        get => active();
        set => active(value);
    }

    public bool TopSlope => topSlope();

    public bool BottomSlope => bottomSlope();

    public bool LeftSlope => leftSlope();

    public bool RightSlope => rightSlope();

    public bool HasUnactuatedTile => nactive();

    public bool HasActuator
    {
        get => actuator();
        set => actuator(value);
    }

    public bool IsActuated
    {
        get => inActive();
        set => inActive(value);
    }
}
