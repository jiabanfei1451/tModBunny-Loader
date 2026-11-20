namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectPass.Apply()</c>. Sem hospedeiro de shader nao ha o que
/// aplicar; o metodo existe para o IL do mod (Luminance e FargowiltasSouls
/// chamam <c>callvirt Apply()</c>) resolver e nao estourar no meio de um
/// desenho.
/// </summary>
public sealed partial class EffectPass
{
    public void Apply()
    {
    }
}
