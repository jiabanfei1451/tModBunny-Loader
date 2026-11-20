using System;
using BunnyLoader.Interop;
using ReLogic.Content;
using ReLogic.Graphics;

namespace Terraria.GameContent;

/// <summary>
/// As fontes do jogo.
///
/// O dump do mobile declara <c>FontAssets.MouseText</c> e companhia como
/// <c>Asset&lt;SpriteFont&gt;</c>, mas o tModLoader 1.4.4.9 usa
/// <c>ReLogic.Graphics.DynamicSpriteFont</c>: o mod faz
/// <c>ldsfld FontAssets::MouseText</c> seguido de
/// <c>get_Value()</c> e <c>DynamicSpriteFont::MeasureString(string)</c>.
/// Um campo com outro tipo de elemento nao e o mesmo campo — o acesso do mod
/// falharia com <c>MissingFieldException</c>. Por isso as tres fontes sao
/// escritas aqui, com o tipo que o mod espera (o ProxyGen nao as gera: ver
/// <c>!Terraria.GameContent.FontAssets.*</c> em types.txt).
///
/// <c>CombatText</c> continua sendo gerado: nenhum mod o referencia.
/// </summary>
public static unsafe partial class FontAssets
{
    static StaticRef __itemStack, __mouseText, __deathText;

    public static Asset<DynamicSpriteFont> ItemStack
    {
        get => Asset<DynamicSpriteFont>.FromGame(GameInterop.ReadStatic<IntPtr>(ref __itemStack, Class, "ItemStack"));
        set => GameInterop.WriteStatic(ref __itemStack, Class, "ItemStack", Asset<DynamicSpriteFont>.ToGame(value), true);
    }

    public static Asset<DynamicSpriteFont> MouseText
    {
        get => Asset<DynamicSpriteFont>.FromGame(GameInterop.ReadStatic<IntPtr>(ref __mouseText, Class, "MouseText"));
        set => GameInterop.WriteStatic(ref __mouseText, Class, "MouseText", Asset<DynamicSpriteFont>.ToGame(value), true);
    }

    public static Asset<DynamicSpriteFont> DeathText
    {
        get => Asset<DynamicSpriteFont>.FromGame(GameInterop.ReadStatic<IntPtr>(ref __deathText, Class, "DeathText"));
        set => GameInterop.WriteStatic(ref __deathText, Class, "DeathText", Asset<DynamicSpriteFont>.ToGame(value), true);
    }
}
