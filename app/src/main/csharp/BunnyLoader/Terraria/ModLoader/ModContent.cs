using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System;
using BunnyLoader.Interop;
using BunnyLoader;
using ReLogic.Content;
using Terraria.ModLoader.Core;

namespace Terraria.ModLoader;

public static class ModContent
{
    public static int GetModHeadSlot(string texture) => NPCHeadLoader.GetHeadSlot(texture);

    static readonly Dictionary<Type, object> Instances = new();

    static readonly Dictionary<string, List<IModType>> ByName = new(StringComparer.Ordinal);

    public static T GetInstance<T>() where T : class
    {
        lock (Instances)
        {
            return Instances.TryGetValue(typeof(T), out object? instance) ? (T)instance : null!;
        }
    }

    public static int TileType<T>() where T : ModTile => GetInstance<T>()?.Type ?? 0;

    public static int WallType<T>() where T : ModWall => GetInstance<T>()?.Type ?? 0;

    public static ModTile? GetModTile(int type) => TileLoader.GetTile(type);

    public static int ItemType<T>() where T : ModItem => GetInstance<T>()?.Type ?? 0;

    public static int RarityType<T>() where T : ModRarity => GetInstance<T>()?.Type ?? 0;

    public static int BuffType<T>() where T : ModBuff => GetInstance<T>()?.Type ?? 0;

    public static int MountType<T>() where T : ModMount => GetInstance<T>()?.Type ?? 0;

    public static int ProjectileType<T>() where T : ModProjectile => GetInstance<T>()?.Type ?? 0;

    public static int NPCType<T>() where T : ModNPC => GetInstance<T>()?.Type ?? 0;

    public static ModNPC? GetModNPC(int type) => NPCLoader.GetNPC(type);

    public static int GetModBossHeadSlot(string texture) => NPCLoader.BossHeadSlot(texture);

    public static int EmoteBubbleType<T>() where T : ModEmoteBubble => GetInstance<T>()?.Type ?? 0;

    public static ModEmoteBubble? GetModEmoteBubble(int type) => EmoteBubbleLoader.GetEmoteBubble(type);

    public static ModBuff? GetModBuff(int type) => BuffLoader.GetBuff(type);

    public static int DustType<T>() where T : ModDust => GetInstance<T>()?.Type ?? 0;

    public static int GoreType<T>() where T : ModGore => GetInstance<T>()?.Type ?? 0;

    public static ModDust? GetModDust(int type) => DustLoader.GetDust(type);

    public static ModItem? GetModItem(int type) => ItemLoader.Get(type);

    public static T Find<T>(string fullname) where T : IModType =>
        TryFind(fullname, out T value) ? value : throw new KeyNotFoundException($"{typeof(T).Name} '{fullname}' not found");

    public static T Find<T>(string modName, string name) where T : IModType => Find<T>(modName + "/" + name);

    public static bool TryFind<T>(string fullname, out T value) where T : IModType
    {
        lock (Instances)
        {
            if (ByName.TryGetValue(fullname, out List<IModType>? found))
            {
                foreach (IModType content in found)
                {
                    if (content is T match)
                    {
                        value = match;
                        return true;
                    }
                }
            }
        }
        value = default!;
        return false;
    }

    public static Terraria.GameContent.TeleportPylonType PylonType<T>() where T : ModPylon => GetInstance<T>().PylonType;

    public static bool TryFind<T>(string modName, string name, out T value) where T : IModType =>
        TryFind(modName + "/" + name, out value);

    public static Asset<T> Request<T>(string name, AssetRequestMode mode = AssetRequestMode.AsyncLoad) where T : class
    {
        AssetKinds.CheckSupported(typeof(T));
        (string modName, string path) = SplitName(name);
        if (modName == "Terraria")
        {
            // As fontes do jogo sao DynamicSpriteFont no 1.4; um Request<SpriteFont>
            // de "Terraria/Fonts/..." nao tem equivalente nativo aqui.
            if (typeof(T) == typeof(ReLogic.Graphics.DynamicSpriteFont)) return (Asset<T>)(object)VanillaFont(path);
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
                throw new NotSupportedException($"ModContent.Request: on mobile the game fonts are ReLogic.Graphics.DynamicSpriteFont, not SpriteFont ('{name}')");
            return VanillaAsset<T>.Get(path);
        }
        return ModOf(modName, name).Assets.Request<T>(path, mode);
    }

    static Asset<ReLogic.Graphics.DynamicSpriteFont> VanillaFont(string path)
    {
        string font = path.Replace('\\', '/').TrimStart('/');
        if (font.StartsWith("Images/", StringComparison.Ordinal)) font = font["Images/".Length..];
        return font switch
        {
            "Fonts/Mouse_Text" => Terraria.GameContent.FontAssets.MouseText,
            "Fonts/Item_Stack" => Terraria.GameContent.FontAssets.ItemStack,
            "Fonts/Death_Text" => Terraria.GameContent.FontAssets.DeathText,
            _ => throw new NotSupportedException($"ModContent.Request: no game font '{path}' on mobile (Fonts/Mouse_Text, Fonts/Item_Stack and Fonts/Death_Text are)"),
        };
    }

    public static bool RequestIfExists<T>(string name, out Asset<T> asset, AssetRequestMode mode = AssetRequestMode.AsyncLoad) where T : class
    {
        // Requested<T>.ByName era do motor antigo (IlInterop); o upstream resolve
        // tudo por byName dentro do ModAssets, entao aqui basta a checagem.
        if (!HasAsset(name!))
        {
            asset = null!;
            return false;
        }
        asset = Request<T>(name!, mode);
        return true;
    }

    static readonly ConcurrentDictionary<string, bool> AssetExists = new(StringComparer.Ordinal);

    public static bool HasAsset(string assetName)
    {
        if (AssetExists.TryGetValue(assetName, out bool known)) return known;
        (string modName, string path) = SplitName(assetName);
        bool exists;
        if (modName != "Terraria")
            return Host.FindMod(modName) is Mod mod &&
                (mod.Assets.HasAsset<Microsoft.Xna.Framework.Graphics.Texture2D>(path) ||
                 mod.Assets.HasAsset<ReLogic.Graphics.DynamicSpriteFont>(path) ||
                 mod.Assets.HasAsset<Microsoft.Xna.Framework.Graphics.SpriteFont>(path));
        try
        {
            VanillaAsset<Microsoft.Xna.Framework.Graphics.Texture2D>.Get(path).Wait();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool FileExists(string name)
    {
        (string modName, string path) = SplitName(name);
        return Host.FindMod(modName) is Mod mod && mod.FileExists(path);
    }

    public static byte[] GetFileBytes(string name)
    {
        (string modName, string path) = SplitName(name);
        return ModOf(modName, name).GetFileBytes(path);
    }

    public static (string modName, string subName) SplitName(string name)
    {
        string clean = name.Replace('\\', '/');
        int slash = clean.IndexOf('/');
        if (slash <= 0 || slash == clean.Length - 1) throw new ArgumentException($"'{name}' is not 'ModName/path'", nameof(name));
        return (clean[..slash], clean[(slash + 1)..]);
    }

    static Mod ModOf(string modName, string name) =>
        Host.FindMod(modName) ?? throw new KeyNotFoundException($"'{name}': no C# mod named '{modName}' is loaded");

    internal static void Add(object instance)
    {
        lock (Instances)
        {
            Instances[instance.GetType()] = instance;
            if (instance is IModType content)
            {
                if (!ByName.TryGetValue(content.FullName, out List<IModType>? list)) ByName[content.FullName] = list = new List<IModType>();
                list.Add(content);
            }
        }
    }

    const int SliceMilliseconds = 16;

    static IEnumerator<int>? setup;

    [UnmanagedCallersOnly]
    internal static int ContentReady()
    {
        try
        {
            setup ??= SetupSteps().GetEnumerator();
            long end = Stopwatch.GetTimestamp() + Stopwatch.Frequency * SliceMilliseconds / 1000;
            do
            {
                if (!setup.MoveNext()) return 0;
            }
            while (Stopwatch.GetTimestamp() < end);
            return 1;
        }
        catch (Exception e)
        {
            Log.Error($"content setup failed: {e}");
            return 0;
        }
    }

    static IEnumerable<int> SetupSteps()
    {
        Mod[] mods = Host.Mods;
        // Os mods já carregaram (0..55%); daqui para a frente é a instalação
        // do conteúdo, que é a parte longa da abertura.
        Progress.Write("正在安装内容…", 55);
        // v48: criar aqui as instanciacoes Asset<Effect>/Request<Effect> (ver
        // ModAssets.PrewarmEffect). Sem isso o callvirt do Luminance cai num slot
        // nulo e vira NRE, sem nunca entrar no nosso override.
        BunnyLoader.ModAssets.PrewarmEffect();
        ServerSyncHooks.Install();
        yield return 0;
        ServerSyncHooks.InstallMenus();
        yield return 0;
        BunnyLoader.ScreenEffectsSetting.Install();
        BunnyLoader.ScreenFxCommand.Install();
        // v47: o MiscShaderFrame era instalado so' no primeiro desenho de shader
        // (GlesShaderBridge.EnsureFrame), e o v46 provou que isso nunca acontece
        // quando o registro de shaders do mod fica vazio: sem instalacao o
        // On_Main.DoDraw nem e' assinado, e todo o caminho por quadro (submit +
        // rastro GL) fica morto. Instalar aqui custa um hook e sempre roda.
        BunnyLoader.MiscShaderFrame.Install();
        // Upstream 0.5.13: linha "Frame Skip" nas configuracoes. Precisa do
        // On_Game que agora o ProxyGen gera (types.txt).
        FrameSkipSetting.Install();
        BunnyLoader.TownDebugSetting.Install();
        Core.SystemDrawHooks.WantTownDebug();
        TouchControlSizes.Install();
        yield return 0;
        Terraria.ID.SetFactory.GrowToContent();
        yield return 0;
        foreach (Mod mod in mods)
            Run(mod, "ReinitializeDuringResizeArrays", () => Terraria.ID.SetFactory.ReinitializeSets(mod));
        yield return 0;
        SystemLoader.ResizeArrays();
        SystemLoader.OnLocalizationsLoaded();
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (GlobalType global in mod.GetContent<GlobalType>())
            {
                Run(mod, $"{global.Name}.SetStaticDefaults", global.SetupContent);
                yield return 0;
            }
            foreach (GlobalBuff global in mod.GetContent<GlobalBuff>())
            {
                Run(mod, $"{global.Name}.SetStaticDefaults", global.SetupContent);
                yield return 0;
            }
            foreach (ModPlayer player in mod.GetContent<ModPlayer>())
            {
                Run(mod, $"{player.Name}.SetStaticDefaults", player.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ModRarity rarity in mod.GetContent<ModRarity>())
            {
                Run(mod, $"{rarity.Name}.SetStaticDefaults", rarity.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ModEmoteBubble emote in mod.GetContent<ModEmoteBubble>())
            {
                Run(mod, $"{emote.Name}.SetStaticDefaults", emote.SetupContent);
                yield return 0;
            }
            foreach (ModBossBar bar in mod.GetContent<ModBossBar>())
            {
                Run(mod, $"{bar.Name}.SetStaticDefaults", bar.SetupContent);
                yield return 0;
            }
            foreach (GlobalBossBar bar in mod.GetContent<GlobalBossBar>())
            {
                Run(mod, $"{bar.Name}.SetStaticDefaults", bar.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (DamageClass damageClass in mod.GetContent<DamageClass>())
            {
                Run(mod, $"{damageClass.Name}.SetStaticDefaults", damageClass.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ExtraJump jump in mod.GetContent<ExtraJump>())
            {
                Run(mod, $"{jump.Name}.SetStaticDefaults", jump.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ModResourceOverlay overlay in mod.GetContent<ModResourceOverlay>())
            {
                Run(mod, $"{overlay.Name}.SetStaticDefaults", overlay.SetupContent);
                yield return 0;
            }
        }
        try { MountLoader.ResizeArrays(); }
        catch (Exception e) { Log.Error($"mounts: the tables could not be resized: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (ModMount mount in mod.GetContent<ModMount>())
            {
                Run(mod, $"{mount.Name}.SetStaticDefaults", mount.SetupContent);
                yield return 0;
            }
        }
        try { HairLoader.ResizeArrays(); }
        catch (Exception e) { Log.Error($"hairs: the tables could not be resized: {e}"); }
        yield return 0;
        try { GoreLoader.ResizeArrays(); }
        catch (Exception e) { Log.Error($"gores: the tables could not be resized: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (ModGore gore in mod.GetContent<ModGore>())
            {
                Run(mod, $"{gore.Name}.SetStaticDefaults", gore.SetupContent);
                yield return 0;
                if (gore.UpdateType > 0) GoreHooks.WantUpdate();
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ModHair hair in mod.GetContent<ModHair>())
            {
                Run(mod, $"{hair.Name}.SetStaticDefaults", hair.SetupContent);
                yield return 0;
            }
        }
        try
        {
            WaterStylesLoader.ResizeArrays();
            WaterFallStylesLoader.ResizeArrays();
            BackgroundTextureLoader.ResizeArrays();
            CloudLoader.ResizeArrays();
        }
        catch (Exception e) { Log.Error($"water and backgrounds: the tables could not be resized: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (ModWaterStyle style in mod.GetContent<ModWaterStyle>())
            {
                Run(mod, $"{style.Name}.SetStaticDefaults", style.SetupContent);
                yield return 0;
            }
            foreach (ModWaterfallStyle style in mod.GetContent<ModWaterfallStyle>())
            {
                Run(mod, $"{style.Name}.SetStaticDefaults", style.SetupContent);
                yield return 0;
            }
            foreach (ModBackgroundStyle style in mod.GetContent<ModBackgroundStyle>())
            {
                Run(mod, $"{style.Name}.SetStaticDefaults", style.SetupContent);
                yield return 0;
            }
            foreach (GlobalBackgroundStyle global in mod.GetContent<GlobalBackgroundStyle>())
            {
                Run(mod, $"{global.Name}.SetStaticDefaults", global.SetupContent);
                yield return 0;
            }
            foreach (ModSceneEffect effect in mod.GetContent<ModSceneEffect>())
            {
                Run(mod, $"{effect.Name}.SetStaticDefaults", effect.SetupContent);
                yield return 0;
            }
            foreach (ModCloud cloud in mod.GetContent<ModCloud>())
            {
                Run(mod, $"{cloud.Name}.SetStaticDefaults", cloud.SetupContent);
                yield return 0;
            }
            foreach (ModMenu menu in mod.GetContent<ModMenu>())
            {
                Run(mod, $"{menu.Name}.SetStaticDefaults", menu.SetupContent);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            foreach (ModCommand command in mod.GetContent<ModCommand>())
            {
                Run(mod, $"{command.Name}.SetStaticDefaults", command.SetupContent);
                yield return 0;
            }
            foreach (ModAchievement achievement in mod.GetContent<ModAchievement>())
            {
                Run(mod, $"{achievement.Name}.SetStaticDefaults", achievement.SetupContent);
                yield return 0;
            }
        }
        try { AchievementLoader.FinishSetup(); }
        catch (Exception e) { Log.Error($"achievements: the saved progress could not be read: {e}"); }
        yield return 0;
        try { ExtraJumpLoader.ResizeArrays(); }
        catch (Exception e) { Log.Error($"extra jumps: the order could not be set up: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (PlayerDrawLayer layer in mod.GetContent<PlayerDrawLayer>())
            {
                Run(mod, $"{layer.Name}.SetStaticDefaults", layer.SetupContent);
                yield return 0;
            }
        }
        try { PlayerDrawLayerLoader.ResizeArrays(); }
        catch (Exception e) { Log.Error($"player draw layers: the order could not be set up: {e}"); }
        yield return 0;
        try { PrefixLoader.ContentReady(); }
        catch (Exception e) { Log.Error($"prefixes: the tables could not be set up: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            foreach (ModSystem system in SystemLoader.Of(mod))
            {
                Run(mod, $"{system.Name}.SetStaticDefaults", system.SetupContent);
                yield return 0;
            }
        }
        try { TileLoader.ContentReady(); }
        catch (Exception e) { Log.Error($"tiles: setup failed: {e}"); }
        yield return 0;
        try { PlantLoader.FinishSetup(); }
        catch (Exception e) { Log.Error($"trees: setup failed: {e}"); }
        yield return 0;
        try
        {
            Core.TownNPCFlagHooks.WantActsLike();
            Core.TownNPCFlagHooks.WantNoHappiness();
            Core.NPCFriendlyGate.Want();
        }
        catch (Exception e) { Log.Error($"town NPC flags: setup failed: {e}"); }
        CustomContentSetup.ContentReady();
        foreach (Mod mod in mods)
        {
            Progress.Write($"正在最后处理：{mod.Name}…", 88);
            Run(mod, "PostSetupContent", mod.PostSetupContent);
            yield return 0;
            foreach (ModSystem system in SystemLoader.Of(mod))
            {
                Run(mod, $"{system.Name}.PostSetupContent", system.PostSetupContent);
                yield return 0;
            }
        }
        try
        {
            // v35: inventario dos .fx/.xnb no disco, uma linha por mod. Separa
            // "a pasta do mod nao tem shader" de "tem e o nosso lado falhou".
            BunnyLoader.Graphics.FxInventory.Report();
        }
        catch (Exception e) { Log.Warn($"fx inventario: {e.GetType().Name}: {e.Message}"); }
        try
        {
            // E o gancho de EndCapture: sem ele o Luminance nunca aplica filtro.
            BunnyLoader.ScreenHookProbe.Install();
        }
        catch (Exception e) { Log.Warn($"fx hook EndCapture: {e.GetType().Name}: {e.Message}"); }
        try { BiomeConversionLoader.PostSetupContent(); }
        catch (Exception e) { Core.ItemHooks.Report("BiomeConversionLoader.PostSetupContent", e); }
        yield return 0;
        try { TownShopHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"NPC shops: the tables could not be set up: {e}"); }
        yield return 0;
        try { NPCLootHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"NPC loot: the tables could not be set up: {e}"); }
        yield return 0;
        foreach (int step in Guarded(ItemLootHooks.ContentReady(), "item loot: the tables could not be set up")) yield return step;
        yield return 0;
        try { BannerHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"banners: the tables could not be set up: {e}"); }
        yield return 0;
        try { BestiaryHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"bestiary: the entries could not be set up: {e}"); }
        yield return 0;
        try { NPCProfileHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"town NPC profiles: they could not be set up: {e}"); }
        yield return 0;
        try { ItemOpenHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"item hooks: the right click items could not be listed: {e}"); }
        yield return 0;
        try { AnglerQuestHooks.ContentReady(); }
        catch (Exception e) { Log.Error($"angler quests: the quest fish could not be added: {e}"); }
        yield return 0;
        try { TouchButtonLoader.ContentReady(); }
        catch (Exception e) { Log.Error($"touch buttons: the saved layout could not be applied: {e}"); }
        yield return 0;
        foreach (Mod mod in mods)
        {
            Run(mod, "AddRecipeGroups", mod.AddRecipeGroups);
            yield return 0;
            foreach (ModSystem system in SystemLoader.Of(mod))
            {
                Run(mod, $"{system.Name}.AddRecipeGroups", system.AddRecipeGroups);
                yield return 0;
            }
        }
        RecipeLoader.BeginSetup();
        foreach (Mod mod in mods)
        {
            RecipeLoader.SetCurrentMod(mod);
            Progress.Write($"正在添加配方：{mod.Name}…", 72);
            Run(mod, "AddRecipes", mod.AddRecipes);
            yield return 0;
            foreach (ModSystem system in SystemLoader.Of(mod))
            {
                Run(mod, $"{system.Name}.AddRecipes", system.AddRecipes);
                yield return 0;
            }
            foreach (ModItem item in mod.GetContent<ModItem>())
            {
                Run(mod, $"{item.Name}.AddRecipes", item.AddRecipes);
                yield return 0;
            }
            foreach (GlobalItem global in mod.GetContent<GlobalItem>())
            {
                Run(mod, $"{global.Name}.AddRecipes", global.AddRecipes);
                yield return 0;
            }
        }
        foreach (Mod mod in mods)
        {
            RecipeLoader.SetCurrentMod(mod);
            Run(mod, "PostAddRecipes", mod.PostAddRecipes);
            yield return 0;
            foreach (ModSystem system in SystemLoader.Of(mod))
            {
                Run(mod, $"{system.Name}.PostAddRecipes", system.PostAddRecipes);
                yield return 0;
            }
        }
        RecipeLoader.EndSetup();
        SystemLoader.PostSetupRecipes();
        MenuLoader.Ready = true;
        Progress.Write("内容加载完成", 100);
        foreach (Mod mod in mods)
        {
            bool created = mod.GetContent<ModItem>().Any(i => ItemHooks.Overrides(i.GetType(), typeof(ModItem), nameof(ModItem.OnCreated))) ||
                           mod.GetContent<GlobalItem>().Any(g => ItemHooks.Overrides(g.GetType(), typeof(GlobalItem), nameof(GlobalItem.OnCreated)));
            if (created) RecipeLoader.WantCraft();
        }
        try { BunnyLoader.Hooks.HookGates.ContentReady(); }
        catch (Exception e) { Log.Error($"hook gates: setup failed: {e}"); }
        try { PreJit.Start(Host.Mods); }
        catch (Exception e) { Log.Error($"pre-JIT: setup failed: {e}"); }
        Log.Info($"startup: C# compiled {JitInfo.GetCompiledMethodCount(false)} methods, {JitInfo.GetCompiledILBytes(false) / 1024} KB of IL, {JitInfo.GetCompilationTime(false).TotalMilliseconds:F0} ms of JIT");
    }

    static IEnumerable<int> Guarded(IEnumerable<int> steps, string failure)
    {
        using IEnumerator<int> step = steps.GetEnumerator();
        while (true)
        {
            try
            {
                if (!step.MoveNext()) yield break;
            }
            catch (Exception e)
            {
                Log.Error($"{failure}: {e}");
                yield break;
            }
            yield return 0;
        }
    }

    static void Run(Mod mod, string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Error($"mod {mod.Uid}: {step} threw: {e}");
        }
    }

    /// <summary>
    /// O contrato devolve todo o conteudo carregado de todos os mods
    /// (<c>ContentCache.GetContentForAllMods&lt;T&gt;()</c>). Sem este membro
    /// o mod morre em
    /// <c>MissingMethodException: IEnumerable`1 GetContent()</c>
    /// (FargowiltasSouls EnchantSystem::PostSetupRecipes).
    /// </summary>
    public static IEnumerable<T> GetContent<T>() where T : ILoadable
    {
        Mod[] mods = Host.Mods;
        for (int i = 0; i < mods.Length; i++)
        {
            Mod mod = mods[i];
            if (mod == null) continue;
            foreach (T item in mod.GetContent<T>()) yield return item;
        }
    }
}
