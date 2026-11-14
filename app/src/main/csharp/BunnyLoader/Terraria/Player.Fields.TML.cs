namespace Terraria;

/// <summary>
/// <c>Player.equippedWings</c> é um CAMPO do tML (não existe no jogo): o tML
/// o preenche em <c>UpdateEquips</c> com o acessório que deu as asas.
///
/// Aqui o valor é derivado na hora: o primeiro slot de acessório com
/// <see cref="Item.wingSlot"/> preenchido. Devolver <c>null</c> quando não há
/// asas é o mesmo contrato do tML, e o mod já checa por nulo.
/// </summary>
public sealed partial class Player
{
    public Item? equippedWings
    {
        get
        {
            if (IsNull) return null;
            var slots = armor;
            if (slots.IsNull) return null;
            int length = slots.Length;
            for (int i = 3; i < length; i++)
            {
                // 0-2 são armadura; 3+ são acessórios (e sociais).
                Item? item = slots[i];
                if (item is null || item.IsNull) continue;
                if (item.type <= 0) continue;
                if (item.wingSlot > 0) return item;
            }
            return null;
        }
    }

    /// <summary>
    /// O tML escreve neste campo; aqui ele é derivado, então a escrita é
    /// aceita e descartada — sem isto o IlPatcher marcaria o <c>stfld</c>
    /// como "sem tradução" e o acesso estouraria no Mono.
    /// </summary>
    public static void __set_equippedWings(global::BunnyLoader.Interop.IGameObject self, Item value) { }
}
