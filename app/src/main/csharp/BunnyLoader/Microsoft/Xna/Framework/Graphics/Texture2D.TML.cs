namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// No FNA <c>Texture2D.Width/Height</c> são propriedades; no dump deste port a
/// textura vem de fora (Unity) e o tamanho ficou em <c>UnityWidth</c>/
/// <c>UnityHeight</c>. Mods do PC leem <c>Width/Height</c> direto — e como o
/// proxy gerado os expõe como <c>ref int</c>, a assinatura não casava.
///
/// As duas propriedades estão marcadas com <c>!</c> no types.txt, então o
/// ProxyGen não emite a versão <c>ref</c> e esta aqui vale.
/// </summary>
public sealed partial class Texture2D
{
    public int Width
    {
        get => UnityWidth;
        set => UnityWidth = value;
    }

    public int Height
    {
        get => UnityHeight;
        set => UnityHeight = value;
    }
}
