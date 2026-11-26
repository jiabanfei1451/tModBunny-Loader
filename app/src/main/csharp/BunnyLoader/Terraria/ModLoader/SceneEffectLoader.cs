using System;
using System.Collections.Generic;
using BunnyLoader;
using Terraria.Graphics.Capture;
using Terraria.ModLoader.Core;

namespace Terraria.ModLoader;

public static class SceneEffectLoader
{
    public const int VanillaSceneEffectCount = 0;

    public class SceneEffectInstance
    {
        public struct PrioritizedPair
        {
            public static readonly PrioritizedPair Default = new() { value = -1 };

            public int value;
            public SceneEffectPriority priority;
        }

        public bool anyActive;
        public PrioritizedPair waterStyle;
        public PrioritizedPair undergroundBackground;
        public PrioritizedPair surfaceBackground;
        public PrioritizedPair music;
        public string? mapBackground;
        public ModSceneEffect? mapBackgroundSceneEffect;
        public PrioritizedPair biomeTorchItemType;
        public PrioritizedPair biomeCampfireItemType;
        public CaptureBiome.TileColorStyle tileColorStyle;

        public SceneEffectInstance()
        {
            waterStyle = undergroundBackground = surfaceBackground = music = biomeTorchItemType = biomeCampfireItemType = PrioritizedPair.Default;
        }
    }

    static readonly List<ModSceneEffect> Effects = new();
    static readonly SceneEffectInstance[] Scenes = new SceneEffectInstance[256];
    static readonly SceneEffectInstance?[] Spares = new SceneEffectInstance?[256];
    static readonly SceneEffectInstance Empty = new();
    static readonly List<(float Weight, int Order, ModSceneEffect Effect)> Ranked = new();
    static readonly Comparison<(float Weight, int Order, ModSceneEffect Effect)> ByWeight =
        (a, b) => b.Weight != a.Weight ? b.Weight.CompareTo(a.Weight) : a.Order.CompareTo(b.Order);

    public static int Count => Effects.Count;

    static string lastMusicOwner = "";
    // v30 diagnostic: who currently owns the scene music slot (real-time).
    static string lastMusicDesc = "";

    internal static string MusicOwnerLine()
    {
        var m = Of(Main.LocalPlayer).music;
        return $"[scene] music={m.value} prio={m.priority} owner={lastMusicDesc}";
    }

    internal static IReadOnlyList<ModSceneEffect> All => Effects;

    internal static int Register(ModSceneEffect effect)
    {
        Effects.Add(effect);
        BiomeHooks.Install();
        MusicHooks.Install();
        if (ItemHooks.Overrides(effect.GetType(), typeof(ModSceneEffect), "get_MapBackground")) MapBackgroundHooks.Install();
        return Effects.Count - 1;
    }

    internal static SceneEffectInstance Of(Player player)
    {
        int who = player.whoAmI;
        return (uint)who < (uint)Scenes.Length ? Scenes[who] ?? Empty : Empty;
    }

    internal static void Clear(int who)
    {
        if ((uint)who < (uint)Scenes.Length) Scenes[who] = null!;
    }

    static SceneEffectInstance Fresh(int who)
    {
        if ((uint)who >= (uint)Spares.Length || Spares[who] is not SceneEffectInstance spare) return new SceneEffectInstance();
        spare.anyActive = false;
        spare.waterStyle = spare.undergroundBackground = spare.surfaceBackground = spare.music = SceneEffectInstance.PrioritizedPair.Default;
        spare.biomeTorchItemType = spare.biomeCampfireItemType = SceneEffectInstance.PrioritizedPair.Default;
        spare.mapBackground = null;
        spare.mapBackgroundSceneEffect = null;
        spare.tileColorStyle = default;
        return spare;
    }

    public static void UpdateSceneEffect(Player player)
    {
        var result = Fresh(player.whoAmI);
        Ranked.Clear();
        for (int i = 0; i < Effects.Count; i++)
        {
            ModSceneEffect effect = Effects[i];
            bool active;
            try { active = effect.IsSceneEffectActive(player); }
            catch (Exception e)
            {
                ItemHooks.Report($"{effect.FullName}.IsSceneEffectActive", e);
                active = false;
            }
            try { effect.SpecialVisuals(player, active); }
            catch (Exception e) { ItemHooks.Report($"{effect.FullName}.SpecialVisuals", e); }
            if (!active) continue;
            float weight;
            try { weight = effect.GetCorrWeight(player); }
            catch (Exception e)
            {
                ItemHooks.Report($"{effect.FullName}.GetWeight", e);
                continue;
            }
            Ranked.Add((weight, i, effect));
        }
        if (Ranked.Count > 0)
        {
            result.anyActive = true;
            Ranked.Sort(ByWeight);
            // v26: o contrato do tML preenche no maximo 8 campos e, em cada um,
            // testa "priority == 0" (nao "value == -1"). A diferenca importa: um
            // cenario de ambiencia tem Music != -1 mas Priority = None, entao com
            // o teste antigo ele travava o slot de musica para sempre e o cenario
            // de verdade (Town/Desert) nunca entrava, ou entrava a faixa de
            // ambiencia no lugar da musica.
            int sceneEffectFields = 0;
            for (int i = 0; sceneEffectFields < 8 && i < Ranked.Count; i++)
            {
                ModSceneEffect effect = Ranked[i].Effect;
                try { sceneEffectFields += Fill(result, effect); }
                catch (Exception e) { ItemHooks.Report($"{effect.FullName} (scene)", e); }
            }
        }
        int who = player.whoAmI;
        if ((uint)who >= (uint)Scenes.Length) return;
        if (Scenes[who] is SceneEffectInstance current && Same(current, result))
        {
            Spares[who] = result;
            return;
        }
        Spares[who] = null;
        Scenes[who] = result;
    }

    static bool Same(SceneEffectInstance a, SceneEffectInstance b) =>
        a.anyActive == b.anyActive && Same(a.waterStyle, b.waterStyle) && Same(a.undergroundBackground, b.undergroundBackground) &&
        Same(a.surfaceBackground, b.surfaceBackground) && Same(a.music, b.music) && Same(a.biomeTorchItemType, b.biomeTorchItemType) &&
        Same(a.biomeCampfireItemType, b.biomeCampfireItemType) && a.tileColorStyle == b.tileColorStyle &&
        string.Equals(a.mapBackground, b.mapBackground, StringComparison.Ordinal) && ReferenceEquals(a.mapBackgroundSceneEffect, b.mapBackgroundSceneEffect);

    static bool Same(SceneEffectInstance.PrioritizedPair a, SceneEffectInstance.PrioritizedPair b) => a.value == b.value && a.priority == b.priority;

    static int Fill(SceneEffectInstance result, ModSceneEffect effect)
    {
        SceneEffectPriority priority = effect.Priority;
        int fields = 0;
        if (result.waterStyle.priority == 0 && effect.WaterStyle is { } water)
        {
            result.waterStyle = new SceneEffectInstance.PrioritizedPair { value = water.Slot, priority = priority };
            fields++;
        }
        if (result.undergroundBackground.priority == 0 && effect.UndergroundBackgroundStyle is { } underground)
        {
            result.undergroundBackground = new SceneEffectInstance.PrioritizedPair { value = underground.Slot, priority = priority };
            fields++;
        }
        if (result.surfaceBackground.priority == 0 && effect.SurfaceBackgroundStyle is { } surface)
        {
            result.surfaceBackground = new SceneEffectInstance.PrioritizedPair { value = surface.Slot, priority = priority };
            fields++;
        }
        if (result.music.priority == 0)
        {
            int music = effect.Music;
            if (music != -1)
            {
                result.music = new SceneEffectInstance.PrioritizedPair { value = music, priority = priority };
                fields++;
                // v27 diagnostico: quem ganhou o slot de musica. So' escreve quando
                // o dono muda (dedup por tipo+faixa) para nao inundar o log.
                string owner = effect.GetType().FullName ?? effect.GetType().Name;
                string key = owner + "|" + music;
                if (lastMusicOwner != key)
                {
                    lastMusicOwner = key;
                    lastMusicDesc = effect.GetType().Name + "/" + music;
                    Log.Info($"[scene] musica slot <- {music} por {owner} prioridade={priority} peso={effect.GetWeight(Main.LocalPlayer):F2}");
                }
            }
        }
        if (effect is ModBiome biome)
        {
            if (result.biomeTorchItemType.priority == 0 && biome.BiomeTorchItemType != -1)
            {
                result.biomeTorchItemType = new SceneEffectInstance.PrioritizedPair { value = biome.BiomeTorchItemType, priority = priority };
                fields++;
            }
            if (result.biomeCampfireItemType.priority == 0 && biome.BiomeCampfireItemType != -1)
            {
                result.biomeCampfireItemType = new SceneEffectInstance.PrioritizedPair { value = biome.BiomeCampfireItemType, priority = priority };
                fields++;
            }
        }
        if (result.tileColorStyle == CaptureBiome.TileColorStyle.Normal && effect.TileColorStyle != CaptureBiome.TileColorStyle.Normal)
        {
            result.tileColorStyle = effect.TileColorStyle;
            fields++;
        }
        if (result.mapBackground == null && effect.MapBackground is { Length: > 0 } map)
        {
            result.mapBackground = map;
            result.mapBackgroundSceneEffect = effect;
            fields++;
        }
        return fields;
    }

    public static void UpdateMusic(ref int music, ref SceneEffectPriority priority)
    {
        SceneEffectInstance.PrioritizedPair current = Of(Main.LocalPlayer).music;
        if (current.value > -1 && current.priority > priority)
        {
            music = current.value;
            priority = current.priority;
        }
    }
}
