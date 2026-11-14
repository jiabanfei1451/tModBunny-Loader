using Terraria.Audio;

namespace Terraria.ID;

/// <summary>
/// Sons que o mod acessa pelo ENDEREÇO.
///
/// O tML expõe todo <c>SoundID</c> como <c>SoundStyle</c> (struct), e o mod
/// escreve <c>SoundEngine.PlaySound(in SoundID.Roar, position)</c>. Aquilo
/// compila para <c>ldsflda</c> — pega o <b>endereço</b> do campo estático.
///
/// No gerado esses membros saíam como <c>const int</c> (o dump os tem como
/// <c>Int32</c>) ou como propriedade (o dump os tem como
/// <c>LegacySoundStyle</c>). Const e propriedade não têm endereço, então
/// <c>ldsflda</c> não tem para onde apontar e o método não JITa
/// (<c>MissingFieldException: Field not found: 'Terraria.ID.SoundID.Roar'</c>
/// — 46 ocorrências só no Fargowiltas).
///
/// <c>static readonly</c> resolve: existe o campo, e o
/// <c>SoundStyle(int)</c> já cria o <c>LegacySoundStyle</c> nativo
/// correspondente (é o mesmo id legado que o dump usa).
///
/// A declaração do gerado é suprimida com <c>!</c> em
/// <c>ProxyGen/types.txt</c>.
/// </summary>
public sealed partial class SoundID
{
    /// <summary>Rugido (Kraken, Plantera…) — id legado 15 no dump.</summary>
    public static readonly SoundStyle Roar = new(15);

    /// <summary>Explosão dos itens — id legado 14.</summary>
    public static readonly SoundStyle Item14 = new(14);

    /// <summary>Item genérico — id legado 15, volume menor no original.</summary>
    public static readonly SoundStyle Item15 = new(15);

    /// <summary>Vidro quebrando — id legado 13 no dump.</summary>
    public static readonly SoundStyle Shatter = new(13);

    /// <summary>Fecha de menu — id legado 11 no dump.</summary>
    public static readonly SoundStyle MenuClose = new(11);

    /// <summary>Abre de menu — id legado 10 no dump.</summary>
    public static readonly SoundStyle MenuOpen = new(10);
}
