namespace Terraria.ID;

/// <summary>
/// Parte escrita à mão de <see cref="ItemID"/>.
///
/// <c>Count</c> no tML é <c>static readonly short</c> (ItemLoader.ItemCount),
/// então o mod lê <c>ldsfld ItemID::Count</c>. O gerado saía
/// <c>const short Count = 6196</c> — const não tem armazenamento em runtime, o
/// <c>ldsfld</c> quebra com <c>MissingFieldException</c>. Aqui vira um campo
/// real (valor = total de itens do dump 1.4.5.8.6, 6196).
///
/// A declaração do gerado é suprimida com <c>!</c> em <c>ProxyGen/types.txt</c>.
/// </summary>
public sealed partial class ItemID
{
    public static readonly short Count = 6196;

    /// <summary>
    /// O contrato 1.4.4.9 (<c>ItemID.cs.patch</c>) usa
    /// <c>new BannerEffect(strength)</c>; o ProxyGen só emitiu o factory
    /// estático <c>New(float)</c>, sem o <c>.ctor(float)</c>. O `newobj` do
    /// Fargowiltas dentro de <c>Load()</c> ficava sem alvo ->
    /// MissingMethodException no meio da inicialização.
    /// </summary>
    public unsafe partial struct BannerEffect
    {
        public BannerEffect(float strength) => this = New(strength);
    }
}
