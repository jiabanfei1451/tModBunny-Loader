using System;
using BunnyLoader.Graphics;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectPass.Apply()</c>.
///
/// Antes era um no-op: sem hospedeiro de HLSL o <c>Effect</c> so' aceitava
/// parametros. Com o <c>.fx</code> do mod traduzido para GLSL ES, o apply passa
/// a ser o ponto onde o desenho nasce: o mod escreveu os parametros, chamou
/// <c>Apply</c></c> e vai desenhar um quad de tela inteira (filtros do
/// Luminance) ou uma tira de primitivas (trails).
///
/// O que este port entrega hoje e' a parte de tela inteira: um
/// <see cref="ScreenDraw"/> na camada <c>Screen</c>, montado com os parametros
/// que o mod acabou de escrever. A parte de geometria (primitivas) ainda nao
/// tem backend.
/// </summary>
public sealed partial class EffectPass
{
    /// <summary>Efeito dono deste passo (nulo quando veio de um ponteiro do jogo).</summary>
    internal Effect? Owner;

    /// <summary>Nome do passo como esta no <c>.fx</c>.</summary>
    internal string PassName = "";

    public void Apply()
    {
        // Ponteiro do jogo: o apply e' do nativo, nao ha nada para traduzir.
        if (Pointer != 0) return;

        try
        {
            EffectShaderBridge.Apply(Owner, PassName);
        }
        catch (Exception e)
        {
            // Apply roda dentro do desenho do mod: nada aqui pode estourar.
            BunnyLoader.Log.Warn("shader: EffectPass.Apply falhou: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
