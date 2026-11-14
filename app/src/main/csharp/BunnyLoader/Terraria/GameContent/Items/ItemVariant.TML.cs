namespace Terraria.GameContent.Items;

// O tML do PC expoe um construtor sem argumentos (variante padrao): mods
// compilados la fazem `newobj Terraria.GameContent.Items.ItemVariant::.ctor()`.
// Aqui a variante "sem objeto" e o ponteiro nulo, que o caminho de
// SetDefaults trata como a variante padrao.
public partial class ItemVariant
{
    public ItemVariant() { }
}
