using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Graphics;
using Terraria.Map;

namespace Terraria.ModLoader.Core;

internal static class SystemDrawHooks
{
    static bool preMapIcons, postFullscreenMap;
    static readonly HashSet<nint> HiddenLayers = new();
    static readonly List<IMapLayer> Layers = new();
    static readonly Func<nint, bool> HiddenLayer = static layer => HiddenLayers.Contains(layer);
    static IntPtr[] items = new IntPtr[16];

    internal static void Install(Func<string, bool> has)
    {
        if (has(nameof(ModSystem.ModifyScreenPosition))) PlayerDrawHooks.InstallCamera(false);
        if (has(nameof(ModSystem.ModifySunLightColor)))
            ItemHooks.Once("SystemSunLight", () => On_Main.ApplyColorOfTheSkiesToTiles += ApplyColorOfTheSkiesToTiles);
        if (has(nameof(ModSystem.ModifyLightingBrightness)))
            ItemHooks.Once("SystemBrightness", () => On_Lighting.UpdateGlobalBrightness += UpdateGlobalBrightness);
        if (has(nameof(ModSystem.PostDrawTiles)))
            ItemHooks.Once("SystemPostDrawTiles", () => On_Main.DrawSuperSpecialProjectiles += DrawSuperSpecialProjectiles);
        if (has(nameof(ModSystem.PostDrawInterface)))
            ItemHooks.Once("SystemPostDrawInterface", () => On_Main.DrawInterface_33_MouseText += DrawMouseText);
        if (has(nameof(ModSystem.ModifyInterfaceLayers))) InterfaceLayerHooks.Install();
        if (has(nameof(ModSystem.PreDrawMapIconOverlay))) preMapIcons = true;
        if (has(nameof(ModSystem.PostDrawFullscreenMap))) postFullscreenMap = true;
        if (preMapIcons || postFullscreenMap) WantMapIcons();
    }

    // v29: town NPC debug corner. Reuses the same MouseText interface stage as
    // PostDrawInterface; called from ModContent setup, independent of mod hooks.
    internal static void WantTownDebug() => ItemHooks.Once("TownDebug", () => On_Main.DrawInterface_33_MouseText += DrawTownDebug);

    internal static void WantMapIcons() => ItemHooks.Once("SystemMapIcons", () => On_MapIconOverlay.Draw += DrawMapIcons);

    internal static void ModifyTransformMatrix()
    {
        if (Main.gameMenu) return;
        SpriteViewMatrix transform = Main.GameViewMatrix;
        SystemLoader.ModifyTransformMatrix(ref transform);
        if (!transform.IsNull) Main.GameViewMatrix = transform;
    }

    internal static bool IsHidden(nint layer) => HiddenLayers.Contains(layer);

    internal static void SetHidden(nint layer, bool hidden)
    {
        if (hidden) HiddenLayers.Add(layer);
        else HiddenLayers.Remove(layer);
    }

    static void ApplyColorOfTheSkiesToTiles(On_Main.orig_ApplyColorOfTheSkiesToTiles orig)
    {
        orig();
        if (Main.gameMenu) return;
        Color tile = Main.tileColor, background = Main.ColorOfTheSkies;
        SystemLoader.ModifySunLightColor(ref tile, ref background);
        Main.tileColor = tile;
        Main.ColorOfTheSkies = background;
    }

    static void UpdateGlobalBrightness(On_Lighting.orig_UpdateGlobalBrightness orig)
    {
        orig();
        if (Main.gameMenu) return;
        float scale = 1f;
        SystemLoader.ModifyLightingBrightness(ref scale);
        if (float.IsFinite(scale) && scale >= 0f && scale != 1f) Lighting.GlobalBrightness *= scale;
    }

    static void DrawSuperSpecialProjectiles(On_Main.orig_DrawSuperSpecialProjectiles orig, Main self, GameObject projCache, bool startSpriteBatch)
    {
        if (!Main.gameMenu) SystemLoader.PostDrawTiles();
        orig(self, projCache, startSpriteBatch);
    }

    static void DrawMouseText(On_Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        if (!Main.gameMenu) SystemLoader.PostDrawInterface(Main.spriteBatch);
        orig(self);
    }

    // v29: town NPC debug corner (merged from the temporary TownDebugOverlay).
    const float TownLeft = 20f;
    const float TownTop = 40f;
    const float TownLineHeight = 22f;

    // v30: real-time read of UnCalamityModMusic.Common.MusicFlags. Cached because
    // reflection over every loaded assembly is far too costly per frame.
    static Type? flagsType;
    static bool flagsProbed;
    static readonly string[] FlagNames =
    {
        "Town", "Forest", "Desert", "Surface", "Underground", "Night",
        "NearThreeVillagers", "NotInTownAverseBiome", "NotInTownAverseEvent",
        "WorkshopAnyTier", "Rain", "Sandstorm", "Party", "DayTime"
    };
    static readonly FieldInfo?[] FlagFields = new FieldInfo?[FlagNames.Length];
    static string BadgeState(FieldInfo? f)
    {
        if (f == null) return "?";
        try { return f.GetValue(null) is bool b && b ? "1" : "0"; }
        catch { return "?"; }
    }

    static string MusicFlagsLine()
    {
        if (!flagsProbed)
        {
            flagsProbed = true;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? t = asm.GetType("UnCalamityModMusic.Common.MusicFlags");
                    if (t == null) continue;
                    flagsType = t;
                    for (int i = 0; i < FlagNames.Length; i++)
                        FlagFields[i] = t.GetField(FlagNames[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    break;
                }
            }
            catch { }
        }
        if (flagsType == null) return "[ucmm] MusicFlags not found";
        var sb = new StringBuilder("[ucmm] ");
        for (int i = 0; i < FlagNames.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(FlagNames[i]).Append('=').Append(BadgeState(FlagFields[i]));
        }
        return sb.ToString();
    }

    static void DrawTownDebug(On_Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        try
        {
            BunnyLoader.TownDebugSetting.Restore();
            if (BunnyLoader.TownDebugSetting.Enabled && !Main.gameMenu)
                PaintTownDebug(Main.spriteBatch);
        }
        catch (Exception e) { ItemHooks.Report("TownDebug (draw)", e); }
        orig(self);
    }

    /// <summary>
    /// v39: as mesmas linhas do angulo, montadas uma vez para quem quiser lista-las
    /// (a janela de depuracao desenha isto numa caixa rolavel).
    /// </summary>
    internal static List<string> DebugLines()
    {
        var p = Main.LocalPlayer;
        int townNpcs = p.IsNull ? -1 : p.townNPCs;
        int metrics = Main.SceneMetrics.IsNull ? -1 : Main.SceneMetrics.TownNPCCount;
        string state = townNpcs > 2 ? "TOWN" : "WILD";
        return new List<string>
        {
            $"[town] townNPCs={townNpcs} (need >2) -> {state}",
            $"[town] metricsTownNPCCount={metrics}",
            $"[town] day={Main.dayTime} time={Main.time:0}",
            $"[town] musicBox2={Main.musicBox2}",
            $"[town] boxActive={MusicBoxHooks.Active()} wanted={MusicHooks.Wanted} playing={MusicHooks.Playing}",
            SceneEffectLoader.MusicOwnerLine(),
            MusicFlagsLine(),
        };
    }

    static void PaintTownDebug(SpriteBatch batch)
    {
        if (batch is null || batch.Pointer == 0) return;
        Color color = new(255, 235, 120, 255);
        float y = TownTop;
        foreach (string text in DebugLines())
            TownDebugLine(batch, text, ref y, color);
    }

    static void TownDebugLine(SpriteBatch batch, string text, ref float y, Color color)
    {
        Utils.DrawBorderString(batch, text, new Vector2(TownLeft, y), color, 1f, 0f, 0f, -1);
        y += TownLineHeight;
    }

    static void DrawMapIcons(On_MapIconOverlay.orig_Draw orig, MapIconOverlay self, Vector2 mapPosition, Vector2 mapOffset,
                             GameNullable<Rectangle> clippingRect, float mapScale, float drawScale, int alpha, ref GameString text)
    {
        if (Main.gameMenu)
        {
            orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
            return;
        }
        IntPtr pointer = self._layers.Pointer;
        var list = new GameList(pointer);
        int count = MapLayerLoader.Snapshot(pointer, ref items);
        int firstHidden = -1;
        MapOverlayDrawContext context = default;
        bool built = false;
        if (preMapIcons && !list.IsNull)
        {
            HiddenLayers.Clear();
            Layers.Clear();
            for (int i = 0; i < count; i++) Layers.Add(new IMapLayer(items[i]));
            context = MapOverlayDrawContext.New(mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha / 255f);
            built = true;
            SystemLoader.PreDrawMapIconOverlay(Layers, context);
            if (HiddenLayers.Count > 0)
                for (int i = 0; i < Layers.Count && firstHidden < 0; i++)
                    if (HiddenLayers.Contains(Layers[i].Pointer)) firstHidden = i;
        }
        if (MapLayerLoader.Any && !list.IsNull)
        {
            if (!built) context = MapOverlayDrawContext.New(mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha / 255f);
            if (HiddenLayers.Count == 0 && MapLayerLoader.OnlyAtEnds(items, count))
            {
                MapLayerLoader.DrawSlot(0, ref context, ref text);
                orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
                MapLayerLoader.DrawSlot(count, ref context, ref text);
            }
            else MapLayerLoader.Draw(items, count, ref context, ref text, HiddenLayer);
        }
        else if (firstHidden < 0)
        {
            orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
        }
        else
        {
            for (int i = list.Count - 1; i >= firstHidden; i--) list.RemoveAt(i);
            for (int i = firstHidden; i < Layers.Count; i++)
            {
                if (HiddenLayers.Contains(Layers[i].Pointer)) continue;
                list.Add(Layers[i].Pointer);
            }
            try { orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text); }
            finally
            {
                for (int i = list.Count - 1; i >= firstHidden; i--) list.RemoveAt(i);
                for (int i = firstHidden; i < Layers.Count; i++) list.Add(Layers[i].Pointer);
            }
        }
        if (!postFullscreenMap || !Main.mapFullscreen) return;
        string mouseText = text.ToString() ?? "";
        string before = mouseText;
        SystemLoader.PostDrawFullscreenMap(ref mouseText);
        if (mouseText != before) text = mouseText;
    }
}
