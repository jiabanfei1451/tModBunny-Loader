using System;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectTechnique.Passes</c>. O ProxyGen gera a propriedade, mas ela
/// comeca com <c>if (Pointer == 0) throw new NullReferenceException(...)</c> —
/// e o Effect gerenciado daqui nunca tem ponteiro. A propriedade gerada foi
/// suprimida em types.txt (<c>!...EffectTechnique.Passes</c>) e esta e a
/// versao que funciona nos dois mundos: com ponteiro delega ao nativo, sem
/// ponteiro devolve a colecao gerenciada.
///
/// O <c>Owner</c> existe para o <c>EffectPass.Apply()</c> saber de QUAL efeito
/// ele veio: sem isso o apply nao tem como achar o <c>.fx</c> nem os
/// parametros que o mod escreveu.
/// </summary>
public sealed partial class EffectTechnique
{
    EffectPassCollection? passes;

    /// <summary>Efeito dono desta tecnica (nulo quando veio de um ponteiro do jogo).</summary>
    internal Effect? Owner;

    /// <summary>Nome da tecnica como esta no <c>.fx</c> (o gerado so' le por ponteiro).</summary>
    internal string TechniqueName = "";

    public EffectPassCollection Passes
    {
        get
        {
            if (Pointer == 0)
            {
                EffectPassCollection owned = passes ??= new EffectPassCollection(0);
                owned.Owner ??= Owner;
                return owned;
            }
            try
            {
                nint pointer = Microsoft.Xna.Framework.Graphics.GraphicsObjectCalls.GetZero(Pointer, Class, "get_Passes");
                return pointer == 0 ? (passes ??= new EffectPassCollection(0)) : new EffectPassCollection(pointer);
            }
            catch (Exception e)
            {
                Microsoft.Xna.Framework.Graphics.GraphicsObjectCalls.Break("EffectTechnique.get_Passes", e);
                return passes ??= new EffectPassCollection(0);
            }
        }
        set => passes = value;
    }
}
