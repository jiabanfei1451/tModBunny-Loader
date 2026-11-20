namespace Microsoft.Xna.Framework;

/// <summary>
/// FNA: <c>Quaternion(float,float,float,float)</c>, <c>op_Addition</c> e
/// <c>op_Multiply(Quaternion,float)</c>. O dump tem <c>Add</c>/<c>Multiply</c>
/// estaticos mas nenhum operador nem ctor de 4 floats; o Luminance usa os tres
/// ao girar particulas/quadrados (<c>PrimitiveRenderer</c>).
/// </summary>
public partial struct Quaternion
{
    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static Quaternion operator +(Quaternion a, Quaternion b) => Add(a, b);

    public static Quaternion operator *(Quaternion a, float scale) => Multiply(a, scale);
}
