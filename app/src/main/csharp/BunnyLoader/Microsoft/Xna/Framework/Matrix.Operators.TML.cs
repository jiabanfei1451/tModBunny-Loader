namespace Microsoft.Xna.Framework;

/// <summary>
/// FNA: <c>Matrix.operator *</c>. O dump do Android so tem
/// <c>Multiply(Matrix, Matrix)</c> (0xF7..., TypeDefIndex 985), entao o
/// ProxyGen gerou a struct sem operador e o Luminance
/// (<c>GameViewMatrix.TransformationMatrix * ...</c>) nao resolvia
/// <c>op_Multiply</c>.
/// </summary>
public partial struct Matrix
{
    public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);
}
