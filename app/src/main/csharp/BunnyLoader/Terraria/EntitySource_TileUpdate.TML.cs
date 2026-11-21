namespace Terraria.DataStructures;

public readonly struct EntitySource_TileUpdate(int x, int y)
{
    public static implicit operator IEntitySource(EntitySource_TileUpdate source) => EntitySource_TileBreak.New(source.X, source.Y);

    public int X => x;
    public int Y => y;

    /// <summary>
    /// O jogo de PC declara EntitySource_TileUpdate(int tileCoordsX, int tileCoordsY, string context = null)
    /// e os mods chamam a versao de tres argumentos: sem esse ctor o JIT estourava
    /// "Method not found: void Terraria.DataStructures.EntitySource_TileUpdate..ctor(int,int,string)"
    /// dentro de TileLoader.DrawEffects (log v157, tile 773 do Fargowiltas).
    /// O contexto e so descritivo, entao nao guardamos.
    /// </summary>
    public EntitySource_TileUpdate(int x, int y, string? context) : this(x, y)
    {
    }
}
