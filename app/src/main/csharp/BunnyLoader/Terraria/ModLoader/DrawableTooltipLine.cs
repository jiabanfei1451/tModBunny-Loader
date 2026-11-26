using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Terraria.ModLoader;

public class DrawableTooltipLine : TooltipLine
{
    public readonly int Index;

    public readonly int OriginalX;

    public readonly int OriginalY;

    public int X;

    public int Y;

    public Color Color;

    public GameObject Font;

    public float Rotation;

    public Vector2 Origin;

    public Vector2 BaseScale = new(1f);

    public float MaxWidth = -1;

    public float Spread = 2;

    /// <summary>
    /// O PC redeclara <c>Text</c> com <c>new</c> nesta classe e o IL do mod
    /// acessa <c>DrawableTooltipLine::Text</c> direto (6
    /// <c>MissingFieldException</c> no sandbox: BuildModifyTooltips e afins).
    /// </summary>
    public new readonly string Text;

    public DrawableTooltipLine(TooltipLine parent, int index, int x, int y, Color color) : base(parent.Mod, parent.Name, parent.Text)
    {
        IsModifier = parent.IsModifier;
        IsModifierBad = parent.IsModifierBad;
        OverrideColor = parent.OverrideColor;
        OneDropLogo = parent.OneDropLogo;
        Role = parent.Role;
        Index = index;
        OriginalX = X = x;
        OriginalY = Y = y;
        Color = color;
        Text = parent.Text;
    }
}
