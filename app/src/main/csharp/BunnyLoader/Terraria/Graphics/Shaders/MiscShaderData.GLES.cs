using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.DataStructures;

namespace Terraria.Graphics.Shaders;

/// <summary>
/// O shader de mod do tModLoader 1.4.4.9 no port Android.
///
/// No PC este tipo e um <c>ShaderData</c> nativo que guarda um <c>Effect</c>
/// (HLSL compilado em D3D9) e, em <c>Apply</c>, copia os campos
/// <c>_uColor/uSecondaryColor/uImage0..2</c> para os parametros do Effect e
/// chama <c>EffectPass.Apply()</c>. Aqui nao existe Effect nativo: o
/// <c>Asset&lt;Effect&gt;</c> vira um stub gerenciado (Effect.TML.cs) e o
/// desenho passa pela pipeline GLES do proprio loader (screenShader +
/// screenSubmit). O que sobra do Effect para baixo e' so' o nome do asset,
/// que e' o que <see cref="GlesShaderBridge"/> usa para saber qual .fx
/// traduzido corresponde a este shader.
///
/// Nada aqui e' um proxy do ProxyGen (o tipo nao esta no types.txt), entao os
/// nomes sao livres. <c>Pointer</c> aponta para um bloco de <see cref="ManagedEffects"/>
/// somente para o base <see cref="ShaderData"/> nao estourar quando algum
/// acesso de campo generico passar por ele.
/// </summary>
public class MiscShaderData
{
    readonly Ref<Effect>? legacyShader;

    readonly Asset<Effect>? shaderAsset;

    readonly Effect stub = Effect.CreateManaged();

    internal readonly Texture2D?[] Images = new Texture2D?[3];

    internal readonly string?[] ImagePaths = new string?[3];

    bool warned;

    /// <summary>
    /// O <see cref="ShaderData"/> do ProxyGen e' <c>sealed</c> (o ProxyGen so'
    /// abre a classe quando o types.txt pede), entao nao da' para derivar. Este
    /// bloco de <see cref="ManagedEffects"/> existe so' para quem fizer cast
    /// para o base nao estourar; o Effect de verdade e' <see cref="stub"/>.
    /// </summary>
    public readonly nint Pointer = ManagedEffects.New();

    /// <summary>
    /// Chave do shader no <see cref="GlesShaderBridge"/> (ex.:
    /// <c>Assets/Effects/Armor/LifeChampionShader</c>). <c>null</c> quando o
    /// asset veio de um <see cref="Ref{T}"/> e nao ha nome nenhum.
    /// </summary>
    public string? ShaderKey { get; private set; }

    /// <summary>O <c>_passName</c> do vanilla (qual technique do .fx).</summary>
    public string? PassName { get; private set; }

    public MiscShaderData(Asset<Effect> shader, string passName)
        : this(shader, null, passName)
    {
    }

    public MiscShaderData(Ref<Effect> shader, string passName)
        : this(null, shader, passName)
    {
    }

    MiscShaderData(Asset<Effect>? asset, Ref<Effect>? legacy, string? passName)
    {
        shaderAsset = asset;
        legacyShader = legacy;
        ShaderKey = asset?.Name;
        PassName = passName;
        if (!string.IsNullOrEmpty(PassName))
            GlesShaderBridge.Register(this, ShaderKey, PassName);
    }

    /// <summary>
    /// O <c>Shader</c> do <see cref="ShaderData"/>. E' um Effect gerenciado
    /// sem codigo: os <c>Use*</c> gravam nele e o backend GLES le' de la'.
    /// </summary>
    public Effect Shader => stub;

    /// <summary>
    /// O tModLoader 1.4.5 chama <c>Apply(DrawData?)</c>; o 1.4.4.9 chama
    /// <c>Apply(Entity, DrawData?)</c>. As duas formas existem aqui porque os
    /// mods compilados trazem as duas (FargowiltasSouls usa a curta).
    ///
    /// A sobrecarga com <c>DrawData?</c> vem literal (e' a que o mod chama);
    /// a com <c>GameNullable&lt;DrawData&gt;</c> e' a mesma coisa com outro
    /// empacotamento, so' existe para as chamadas do proprio codigo BL.
    /// </summary>
    public void Apply(DrawData? drawData)
    {
        if (ShaderKey == null)
        {
            Warn();
            return;
        }
        GlesShaderBridge.Submit(this, ShaderKey, PassName, drawData);
    }

    /// <summary>Forma do 1.4.4.9 (a que o FargowiltasSouls chama).</summary>
    public void Apply(Entity entity, DrawData? drawData) => Apply(drawData);

    public virtual Color GetSecondaryShader(Entity entity) => Color.White;

    public MiscShaderData UseColor(float r, float g, float b) => UseColor(new Vector3(r, g, b));

    public MiscShaderData UseColor(Color color) => UseColor(color.ToVector3());

    public MiscShaderData UseColor(Vector3 color)
    {
        Write("uColor", color);
        return this;
    }

    public MiscShaderData UseSecondaryColor(float r, float g, float b) => UseSecondaryColor(new Vector3(r, g, b));

    public MiscShaderData UseSecondaryColor(Color color) => UseSecondaryColor(color.ToVector3());

    public MiscShaderData UseSecondaryColor(Vector3 color)
    {
        Write("uSecondaryColor", color);
        return this;
    }

    public MiscShaderData UseOpacity(float alpha)
    {
        Write("uOpacity", alpha);
        return this;
    }

    public MiscShaderData UseSaturation(float saturation)
    {
        Write("uSaturation", saturation);
        return this;
    }

    public MiscShaderData UseProjectionMatrix(bool useProjectionMatrix)
    {
        Write("uUseProjectionMatrix", useProjectionMatrix);
        return this;
    }

    public MiscShaderData UseShaderSpecificData(Vector4 shaderSpecificData)
    {
        Write("uShaderSpecificData", shaderSpecificData);
        return this;
    }

    public MiscShaderData UseImage0(string image)
    {
        ImagePaths[0] = image;
        return this;
    }

    public MiscShaderData UseImage1(string image)
    {
        ImagePaths[1] = image;
        return this;
    }

    public MiscShaderData UseImage2(string image)
    {
        ImagePaths[2] = image;
        return this;
    }

    public MiscShaderData UseImage0(Asset<Texture2D>? asset) => UseImage(0, asset?.Value, asset?.Name);

    public MiscShaderData UseImage1(Asset<Texture2D>? asset) => UseImage(1, asset?.Value, asset?.Name);

    public MiscShaderData UseImage2(Asset<Texture2D>? asset) => UseImage(2, asset?.Value, asset?.Name);

    public MiscShaderData UseImage0(Texture2D? texture) => UseImage(0, texture, null);

    public MiscShaderData UseImage1(Texture2D? texture) => UseImage(1, texture, null);

    public MiscShaderData UseImage2(Texture2D? texture) => UseImage(2, texture, null);

    MiscShaderData UseImage(int index, Texture2D? texture, string? name)
    {
        Images[index] = texture;
        ImagePaths[index] = name;
        Write($"uImage{index}", texture);
        return this;
    }

    /// <summary>
    /// Grava nos parametros do Effect stub. O backend GLES le de la'
    /// (<see cref="EffectParameter.Value"/>); nenhum Effect real e' criado.
    /// </summary>
    void Write(string parameter, object? value)
    {
        EffectParameterCollection parameters = Shader.Parameters;
        parameters[parameter].SetValueObject(value);
    }

    void Warn()
    {
        if (warned) return;
        warned = true;
        Log.Warn($"MiscShaderData({PassName}): o mod montou o shader a partir de Ref<Effect>, sem nome de asset — nao ha .fx para desenhar no GLES");
    }

    /// <summary>O asset do Effect, para o bridge deduzir a chave se preciso.</summary>
    internal Asset<Effect>? ShaderAsset => shaderAsset;

    internal Ref<Effect>? LegacyShader => legacyShader;
}
