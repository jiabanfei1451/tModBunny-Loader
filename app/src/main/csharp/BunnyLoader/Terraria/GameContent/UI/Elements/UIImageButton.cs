using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.UI;

namespace Terraria.GameContent.UI.Elements;

public class UIImageButton : UIElement
{
    Asset<Texture2D>? texture;
    protected float _visibilityActive = 1f;
    protected float _visibilityInactive = 0.4f;
    Asset<Texture2D>? borderTexture;
    readonly Rectangle? frame;
    Rectangle? borderFrame;
    bool measured;
    public Color Color = new(255, 255, 255, 255);
    public Color BorderColor = new(255, 255, 255, 255);

    // O tML 1.4.4.9 (contra o qual os mods compilam) tem o ctor de UM
    // parametro. Aqui o `frame` e opcional, entao o C# gera so a assinatura de
    // DOIS parametros — e o `newobj UIImageButton(asset)` do mod vira
    // MissingMethodException. O IlPatcher entao stubava o ctor INTEIRO do
    // subclasse, o que apaga tambem o `base..ctor()` e o inicializador de
    // campo do UIElement: `Elements` ficava null e o Load estourava em
    // `UIElement.RecalculateChildren()`.
    public UIImageButton(Asset<Texture2D> texture) : this(texture, null) { }

    public UIImageButton(Asset<Texture2D> texture, Rectangle? frame = null)
    {
        this.texture = texture;
        this.frame = frame;
        Measure();
    }

    bool Measure()
    {
        if (measured) return false;
        if (frame is Rectangle f)
        {
            Width.Set(f.Width, 0f);
            Height.Set(f.Height, 0f);
            measured = true;
            return true;
        }
        if (texture is null) return false;
        try
        {
            Width.Set(texture.Width(), 0f);
            Height.Set(texture.Height(), 0f);
        }
        catch (InvalidOperationException)
        {
            // Fora da thread do jogo / textura ainda nao carregada: isso e
            // layout, nao pode derrubar o Load do mod.
            return false;
        }
        measured = true;
        return true;
    }

    public void SetHoverImage(Asset<Texture2D> texture, Rectangle? frame = null)
    {
        borderTexture = texture;
        borderFrame = frame;
    }

    public void SetImage(Asset<Texture2D> texture, Rectangle? frame = null)
    {
        this.texture = texture;
        measured = false;
        Measure();
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (Measure()) Recalculate();
        if (texture is null || texture.Value is null) return;
        CalculatedStyle d = GetDimensions();
        spriteBatch.Draw(texture.Value, d.Position(), frame, Color * (IsMouseHovering ? _visibilityActive : _visibilityInactive));
        if (borderTexture != null && IsMouseHovering) spriteBatch.Draw(borderTexture.Value, d.Position(), borderFrame, BorderColor);
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        SoundEngine.PlaySound(12);
    }

    public void SetVisibility(float whenActive, float whenInactive)
    {
        _visibilityActive = Math.Clamp(whenActive, 0f, 1f);
        _visibilityInactive = Math.Clamp(whenInactive, 0f, 1f);
    }
}
