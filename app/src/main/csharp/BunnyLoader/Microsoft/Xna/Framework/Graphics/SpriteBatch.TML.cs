using System;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Assinaturas do FNA/tModLoader que o ProxyGen NAO reproduz: no dump do jogo
/// o parametro <c>Nullable&lt;Rectangle&gt;</c> vira
/// <c>BunnyLoader.Interop.GameNullable&lt;Rectangle&gt;</c>. Os mods foram
/// compilados contra <c>System.Nullable</c>, entao o
/// <c>callvirt Draw(..., Nullable&lt;Rectangle&gt;, ...)</c> nao encontra
/// metodo e a camada de desenho inteira aborta
/// (<c>Insta Item Visual threw: MissingMethodException</c>).
///
/// O <c>GameNullable&lt;T&gt;</c> ja tem conversao implicita de <c>T?</c>,
/// entao estes wrappers so repassam a chamada para o metodo gerado.
/// </summary>
public sealed partial class SpriteBatch
{
    public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
        Color color, float rotation, Vector2 origin, Vector2 scale,
        SpriteEffects effects, float layerDepth)
        => Draw(texture, position, (GameNullable<Rectangle>)sourceRectangle,
            color, rotation, origin, scale, effects, layerDepth);

    public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
        Color color, float rotation, Vector2 origin, float scale,
        SpriteEffects effects, float layerDepth)
        => Draw(texture, position, (GameNullable<Rectangle>)sourceRectangle,
            color, rotation, origin, scale, effects, layerDepth);

    // Sem estes dois, qualquer chamada INTERNA do shim que passe um Rectangle
    // (nao null) fica ambigua entre `Rectangle?` e `GameNullable<Rectangle>`:
    // Rectangle converte para os dois por conversao implicita. Com a assinatura
    // exata, o compilador escolhe esta e o wrapper repassa ao metodo gerado.
    public void Draw(Texture2D texture, Vector2 position, Rectangle sourceRectangle,
        Color color, float rotation, Vector2 origin, Vector2 scale,
        SpriteEffects effects, float layerDepth)
        => Draw(texture, position, (GameNullable<Rectangle>)sourceRectangle,
            color, rotation, origin, scale, effects, layerDepth);

    public void Draw(Texture2D texture, Vector2 position, Rectangle sourceRectangle,
        Color color, float rotation, Vector2 origin, float scale,
        SpriteEffects effects, float layerDepth)
        => Draw(texture, position, (GameNullable<Rectangle>)sourceRectangle,
            color, rotation, origin, scale, effects, layerDepth);

    // FNA tem Begin de 6 e 7 argumentos (o ultimo é `Matrix`, NAO `Matrix?`);
    // o proxy só emitiu o de 8 (com GameNullable<Matrix> + bool). Sem estes,
    // Luminance/FargoSouls perdiam 112 métodos — inclusive os que montam o
    // sprite batch dos shaders.
    public void Begin(SpriteSortMode sortMode, BlendState blendState, SamplerState samplerState,
        DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect,
        Matrix transformMatrix)
        => Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect,
            new GameNullable<Matrix>(transformMatrix), false);

    public void Begin(SpriteSortMode sortMode, BlendState blendState, SamplerState samplerState,
        DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect)
        => Begin(sortMode, blendState, samplerState, depthStencilState, rasterizerState, effect,
            default(GameNullable<Matrix>), false);

    // FNA/tML tem `Begin(sortMode, blendState)` de DOIS argumentos (o resto
    // vai default). O proxy só emitiu o de 8, entao `fs-missing.txt:98` lista
    // esta assinatura como ASSIN. Repassa explicito para o de 8 — chamar
    // `Begin(sortMode, blendState)` aqui seria recursao infinita.
    public void Begin(SpriteSortMode sortMode, BlendState blendState)
        => Begin(sortMode, blendState, default(SamplerState), default(DepthStencilState),
            default(RasterizerState), default(Effect), default(GameNullable<Matrix>), false);

    // `fs-missing.txt:526` — `Draw(Texture2D, Rectangle, Rectangle?, Color,
    // float, Vector2, SpriteEffects, float)`. O gerado (linha 966) tem esta
    // MESMA aridade, mas com `GameNullable<Rectangle>` no sourceRectangle:
    // o mod compilou contra `System.Nullable<Rectangle>` e o `callvirt` nao
    // encontra o metodo (18 chamadas: AbomBoss/AbomRitual/LifeRitual/
    // SpiritChampion/WillRitual/MutantBoss/EaterofWorldsHead/Retinazer/
    // WallofFlesh/EmpressRitual/MoltenAuraProj/AbomSky...).
    public void Draw(Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle,
        Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
        => Draw(texture, destinationRectangle, (GameNullable<Rectangle>)sourceRectangle,
            color, rotation, origin, effects, layerDepth);
}
