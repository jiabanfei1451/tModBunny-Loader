namespace Terraria;

/// <summary>
/// <c>WorldGen.gen</c> é o sinalizador legado de "geração de mundo em
/// andamento"; nesta versão o campo virou <c>generatingWorld</c>. Apelidar
/// evita que o mod escreva num endereço que não existe.
/// </summary>
public sealed partial class WorldGen
{
    public static ref bool gen => ref generatingWorld;

    public static ref bool __ref_gen() => ref generatingWorld;

    public static void __set_gen(bool value) => generatingWorld = value;
}
