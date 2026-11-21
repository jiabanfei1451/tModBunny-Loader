using System;
using BunnyLoader;
using Microsoft.Xna.Framework;

namespace Terraria;

// v100: tML troca "TileData Main.tile" por "Tilemap Main.tile", onde Tilemap e'
// um struct (ldsflda -> call get_Item(int,int)). Os mods (FargowiltasSouls,
// Luminance, Fargowiltas) so' chamam este indexer, sempre por valor.
//
// Nao guardamos Width/Height como campos: Main.tile e' um campo estatico zerado
// e nao existe ponto de inicializacao confiavel (a tela de mundo nem sempre
// passa por LoadWorld). Lendo Main.maxTilesX/Y a cada acesso o struct funciona
// sem ninguem precisar preenche-lo.
public readonly struct Tilemap
{
    public ushort Width => (ushort)Main.maxTilesX;

    public ushort Height => (ushort)Main.maxTilesY;

    public Tile this[int x, int y]
    {
        get
        {
            int width = Main.maxTilesX, height = Main.maxTilesY;
            if (width <= 0 || height <= 0)
                return Tile.s_Null;
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                throw new IndexOutOfRangeException($"Tilemap index ({x}, {y}) is out of bounds ({width}x{height})");
            AssetTrace.Tile(0, 5);
            Tile tile = TileData.Scratch(width * y + x);
            AssetTrace.Tile(0, 6);
            return tile;
        }
    }

    public Tile this[Point position] => this[position.X, position.Y];

    public Tile this[DataStructures.Point16 position] => this[position.X, position.Y];
}
