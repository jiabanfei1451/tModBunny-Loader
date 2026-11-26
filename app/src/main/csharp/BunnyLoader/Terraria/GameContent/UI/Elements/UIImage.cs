using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.UI;

namespace Terraria.GameContent.UI.Elements;

public class UIImage : UIElement
{
    Asset<Texture2D>? texture;
    // `Texture2D` e uma CLASSE proxy (nao struct): `default` e `null`. O
    // `CurrentTexture()` original chamava `.IsNull` nela e estourava
    // NullReferenceException na PRIMEIRA imagem de UI construida no Load do
    // mod (Fargowiltas/StatSheetUI.OnInitialize) — o Load inteiro era dado
    // como falho e nada mais era registrado. Nao pode ser non-nullable.
    Texture2D? nonReloadingTexture;
    bool measured;
    // v36: quantas linhas de diagnostico de desenho ainda cabem.
    static int drawProbeLeft = 8;
    public float ImageScale = 1f;
    public float Rotation;
    public bool ScaleToFit;
    public bool AllowResizingDimensions = true;
    public Color Color = new(255, 255, 255, 255);
    public Vector2 NormalizedOrigin = Vector2.Zero;
    public Rectangle? Frame;
    public bool RemoveFloatingPointsFromDrawPosition;
    public bool UseTextureSizeForOrigin = true;

    protected UIImage() { }

    public UIImage(Asset<Texture2D> texture) => SetImage(texture);

    public UIImage(Texture2D nonReloadingTexture) => SetImage(nonReloadingTexture);

    public Asset<Texture2D>? Texture => texture;

    public void SetImage(Asset<Texture2D> value)
    {
        texture = value;
        nonReloadingTexture = null;
        measured = false;
        Measure();
    }

    public void SetImage(Texture2D value)
    {
        texture = null;
        nonReloadingTexture = value;
        measured = false;
        Measure();
    }

    bool Measure()
    {
        if (measured || !AllowResizingDimensions) return false;
        try
        {
            Texture2D? tex = CurrentTexture();
            if (tex is null || tex.IsNull) return false;
            Width.Set(tex.Width, 0f);
            Height.Set(tex.Height, 0f);
            measured = true;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    Texture2D? CurrentTexture()
    {
        if (nonReloadingTexture is not null) return nonReloadingTexture;
        if (texture is null) return null;
        try
        {
            // Fora da thread do jogo o Asset ainda nao pode ser carregado —
            // isso e layout de UI, nao um erro fatal (o Load do mod continua).
            return texture.Value;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (Measure()) Recalculate();
        Texture2D? tex = CurrentTexture();
        if (tex is null || tex.IsNull) return;
        CalculatedStyle d = GetDimensions();
        // v36: a pagina de configuracao desenha o icone (80x80) gigante na tela
        // inteira. Esta linha diz se o retangulo ja' sai grande daqui (erro de
        // layout nosso) ou se sai 80x80 e o desenho e' que estoura depois.
        if (ScaleToFit || ImageScale != 1f)
        {
            if (System.Threading.Interlocked.Decrement(ref drawProbeLeft) >= 0)
                global::BunnyLoader.Log.Info($"[ui] UIImage.DrawSelf tex={tex.Width}x{tex.Height} rect={d.ToRectangle()} " +
                    $"scaleToFit={ScaleToFit} imageScale={ImageScale} frame={Frame?.ToString() ?? "null"}");
        }
        if (ScaleToFit)
        {
            spriteBatch.Draw(tex, d.ToRectangle(), Frame, Color);
            return;
        }
        Vector2 size = new(tex.Width, tex.Height);
        Vector2 box = UseTextureSizeForOrigin ? size : new Vector2(d.Width, d.Height);
        Vector2 position = d.Position() + box * (1f - ImageScale) / 2f + box * NormalizedOrigin;
        if (RemoveFloatingPointsFromDrawPosition) position = new Vector2((int)position.X, (int)position.Y);
        spriteBatch.Draw(tex, position, Frame, Color, Rotation, size * NormalizedOrigin, ImageScale, SpriteEffects.None, 0f);
    }
}
