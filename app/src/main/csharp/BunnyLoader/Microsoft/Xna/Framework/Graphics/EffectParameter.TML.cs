using System;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectParameter</c>. O dump do Android tem so 6 <c>SetValue</c>
/// (Matrix/bool/float/Vector2/Vector3/Vector4); o Luminance chama 15, incluindo
/// <c>Texture</c> e as versoes de array. Como o Effect e gerenciado (sem
/// hospedeiro de shader), o valor e so guardado: nenhum <c>Apply</c> o le.
/// </summary>
public sealed partial class EffectParameter
{
    object? stored;
    string? name;

    internal EffectParameter(string? name)
        : this(0)
        => this.name = name;

    /// <summary>
    /// O ultimo valor escrito por <c>SetValue</c>. O MiscShaderData le daqui
    /// para saber o que mandar ao backend GLES (uColor, uImage0...).
    /// </summary>
    internal object? Value => stored;

    public string? Name
    {
        get => name;
        set => name = value;
    }

    public void SetValue(Texture? value) => stored = value;

    public void SetValue(Matrix value) => stored = value;

    public void SetValue(Matrix[]? value) => stored = value;

    public void SetValue(Vector2 value) => stored = value;

    public void SetValue(Vector2[]? value) => stored = value;

    public void SetValue(Vector3 value) => stored = value;

    public void SetValue(Vector3[]? value) => stored = value;

    public void SetValue(Vector4 value) => stored = value;

    public void SetValue(Vector4[]? value) => stored = value;

    public void SetValue(bool value) => stored = value;

    public void SetValue(bool[]? value) => stored = value;

    public void SetValue(int value) => stored = value;

    public void SetValue(int[]? value) => stored = value;

    public void SetValue(float value) => stored = value;

    public void SetValue(float[]? value) => stored = value;

    /// <summary>
    /// O formato do PC e' fixo por parametro, mas o shim nao tem uma colecao
    /// de tipos: o MiscShaderData escreve <c>Color</c>, <c>Texture2D</c> e
    /// <c>object</c> pelo mesmo caminho e resolve o tipo na hora de mandar
    /// para o GLES.
    /// </summary>
    internal void SetValueObject(object? value) => stored = value;
}
