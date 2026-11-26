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

    // ------------------------------------------------------------------
    // Segunda leva (FargowiltasSouls). Todos estes aparecem em
    // `out/tmp/fs-missing.txt` como ASSIN: o gerado os declara
    // `public const int`, o mod faz `ldsflda` e não JITa.
    // `static readonly SoundStyle` + `new(<id legado>)` dá endereço e
    // já resolve o LegacySoundStyle nativo pelo mesmo id do dump.
    // Cada um tem `!Terraria.ID.SoundID.X` em ProxyGen/types.txt.
    // ------------------------------------------------------------------

    /// <summary>Som de cavar — id legado 0.</summary>
    public static readonly SoundStyle Dig = new(0);

    /// <summary>Afogando — id legado 23.</summary>
    public static readonly SoundStyle Drown = new(23);

    /// <summary>Rugido forçado — id legado 36.</summary>
    public static readonly SoundStyle ForceRoar = new(36);

    /// <summary>Agarrar — id legado 7.</summary>
    public static readonly SoundStyle Grab = new(7);

    /// <summary>Grama — id legado 6.</summary>
    public static readonly SoundStyle Grass = new(6);

    /// <summary>Mana cheia — id legado 25.</summary>
    public static readonly SoundStyle MaxMana = new(25);

    /// <summary>Tick de menu — id legado 12.</summary>
    public static readonly SoundStyle MenuTick = new(12);

    /// <summary>Meowmere — id legado 37.</summary>
    public static readonly SoundStyle Meowmere = new(37);

    /// <summary>Fada — id legado 27.</summary>
    public static readonly SoundStyle Pixie = new(27);

    /// <summary>Correndo — id legado 17.</summary>
    public static readonly SoundStyle Run = new(17);

    /// <summary>Trovão — id legado 43.</summary>
    public static readonly SoundStyle Thunder = new(43);

    /// <summary>Tink — id legado 21.</summary>
    public static readonly SoundStyle Tink = new(21);

    /// <summary>Destrancando — id legado 22.</summary>
    public static readonly SoundStyle Unlock = new(22);

    // ------------------------------------------------------------------
    // Nomes que só existem no tML (o dump 1.4.5.8 não os tem). O mod
    // referencia pelo nome, então falta o CAMPO (SEM). Não há recurso
    // equivalente no APK, então caem num id legado próximo — o objetivo
    // aqui é JITar; o timbre pode não ser idêntico ao do PC.
    // ------------------------------------------------------------------

    /// <summary>Grito assustador (tML: Roar_2). Aproximado com o id 15.</summary>
    public static readonly SoundStyle ScaryScream = new(15);

    /// <summary>Rugido forçado com pitch fixo — mesma receita do contrato.</summary>
    public static readonly SoundStyle ForceRoarPitched = ForceRoar with { Pitch = 0.6f };

    /// <summary>Zumbi 20 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie20 = new(29);

    /// <summary>Zumbi 21 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie21 = new(29);

    /// <summary>Zumbi 82 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie82 = new(29);

    /// <summary>Zumbi 102 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie102 = new(29);

    /// <summary>Zumbi 103 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie103 = new(29);

    /// <summary>Zumbi 104 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie104 = new(29);

    /// <summary>Zumbi 125 (tML). Aproximado com o zumbi genérico 29.</summary>
    public static readonly SoundStyle Zombie125 = new(29);
}
