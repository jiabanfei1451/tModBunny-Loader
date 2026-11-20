namespace Microsoft.Xna.Framework;

/// <summary>
/// FNA: <c>Color.A</c> e propriedade (<c>get_A</c>/<c>set_A</c>); o dump do
/// Android traz os quatro componentes como CAMPOS, entao o ProxyGen gerou
/// campos e o IL do Luminance (<c>callvirt Color::set_A(byte)</c>) nao achava
/// os acessadores. Sao metodos comuns, nao uma propriedade — nao ha conflito
/// com o campo <c>A</c>.
/// </summary>
public partial struct Color
{
    public byte get_A() => A;

    public void set_A(byte value) => A = value;

    public byte get_R() => R;

    public void set_R(byte value) => R = value;

    public byte get_G() => G;

    public void set_G(byte value) => G = value;

    public byte get_B() => B;

    public void set_B(byte value) => B = value;
}
