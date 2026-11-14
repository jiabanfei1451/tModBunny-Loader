namespace Terraria.GameContent.Personalities;

/// <summary>
/// Interface que o tModLoader ACRESCENTA ao jogo
/// (<c>patches/tModLoader/Terraria/GameContent/Personalities/AShoppingBiome.cs.patch</c>):
/// não existe no dump, e um mod do PC que a referencia não carrega sem ela
/// ("Could not resolve type ... IShoppingBiome").
/// </summary>
public interface IShoppingBiome
{
    string NameKey { get; }

    bool IsInBiome(Player player);
}

/// <summary>
/// O <c>AShoppingBiome</c> do jogo já traz <c>NameKey</c> e <c>IsInBiome</c>
/// (virtual, via proxy); falta só declarar o contrato do tML.
/// </summary>
public sealed partial class AShoppingBiome : IShoppingBiome
{
    string IShoppingBiome.NameKey => NameKey ?? "";
}
