using System;
using System.Collections.Generic;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Dyes;
using Terraria.ModLoader.Core;

namespace Terraria.Graphics.Shaders
{
    internal static class ShaderHandles
    {
        static readonly List<GameHandle> handles = new();

        internal static void Keep(nint pointer) => handles.Add(new GameHandle(pointer));
    }

    public unsafe sealed partial class ArmorShaderData
    {
        public ArmorShaderData(ReLogic.Content.Asset<Effect> shader, string passName) : this(New(shader, passName).Pointer)
        {
        }

        /// <summary>
        /// O 1.4.4.9 aceitava <c>Ref&lt;Effect&gt;</c> e o mod compilado ainda usa
        /// esse construtor (o contrato so' o marcou <c>[Obsolete]</c>). No Android
        /// nao existe <c>Asset&lt;Effect&gt;</c> por tras do <c>Ref</c> -- o .fx nunca
        /// foi carregado pelo pipeline do XNA -- entao o objeto nasce com o asset
        /// nulo. Os setters (<c>UseColor</c> etc.) continuam gravando nos campos
        /// nativos; so' o desenho do efeito nao acontece.
        /// </summary>
        public ArmorShaderData(Terraria.Ref<Effect> shader, string passName) : this(New(null!, passName).Pointer) => LegacyShader = shader;

        internal Terraria.Ref<Effect>? LegacyShader { get; }

        /// <summary>
        /// O PC declara <c>Apply(Entity entity, DrawData? drawData)</c>. O proxy
        /// usa <c>GameNullable&lt;DrawData&gt;</c> na assinatura, o que vira
        /// ASSIN para o IlPatcher e o mod morre em
        /// <c>MissingMethodException</c>. A conversao implicita entre
        /// <c>T?</c> e <c>GameNullable&lt;T&gt;</c> ja existe em
        /// <see cref="BunnyLoader.Interop.GameNullable{T}"/>.
        /// </summary>
        public void Apply(Terraria.Entity entity, Terraria.DataStructures.DrawData? drawData) =>
            Apply(entity, (GameNullable<Terraria.DataStructures.DrawData>)drawData);
    }

    public unsafe sealed partial class ArmorShaderDataSet
    {
        public T BindShader<T>(int itemId, T shaderData) where T : class, IGameObject
        {
            ShaderHandles.Keep(shaderData.Pointer);
            int id = ++_shaderDataCount;
            new GameList(_shaderData).Add(shaderData.Pointer);
            new GameDictionary(_shaderLookupDictionary).Set(itemId, id);
            return shaderData;
        }

        public void Apply(int shaderId, Terraria.Entity entity, Terraria.DataStructures.DrawData? drawData) =>
            Apply(shaderId, entity, (GameNullable<Terraria.DataStructures.DrawData>)drawData);

        public void ApplySecondary(int shaderId, Terraria.Entity entity, Terraria.DataStructures.DrawData? drawData) =>
            ApplySecondary(shaderId, entity, (GameNullable<Terraria.DataStructures.DrawData>)drawData);
    }

    public unsafe sealed partial class HairShaderDataSet
    {
        public T BindShader<T>(int itemId, T shaderData) where T : class, IGameObject
        {
            ShaderHandles.Keep(shaderData.Pointer);
            byte id = ++_shaderDataCount;
            new GameList(_shaderData).Add(shaderData.Pointer);
            new GameDictionary(_shaderLookupDictionary).Set(itemId, (short)id);
            return shaderData;
        }
    }
}

namespace Terraria.GameContent.Dyes
{
    public delegate Color HairColorMethod(Player player, Color color, ref bool lighting);

    public unsafe sealed partial class LegacyHairShaderData
    {
        static readonly Dictionary<nint, HairColorMethod> methods = new();

        public LegacyHairShaderData() : this(New().Pointer)
        {
        }

        public LegacyHairShaderData UseLegacyMethod(HairColorMethod colorProcessor)
        {
            methods[Pointer] = colorProcessor;
            ItemHooks.Once("LegacyHairColor", () => On_LegacyHairShaderData.GetColor += GetColor);
            return this;
        }

        static Color GetColor(On_LegacyHairShaderData.orig_GetColor orig, LegacyHairShaderData self, Player player, Color lightColor)
        {
            if (!methods.TryGetValue(self.Pointer, out HairColorMethod? method))
                return orig(self, player, lightColor);
            bool lighting = true;
            Color color = method(player, player.hairColor, ref lighting);
            return lighting ? new Color((int)(color.R * lightColor.R / 255f), (int)(color.G * lightColor.G / 255f), (int)(color.B * lightColor.B / 255f), color.A) : color;
        }
    }
}
