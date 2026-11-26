using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BunnyLoader;
using log4net;
using Terraria.Localization;

namespace Terraria.ModLoader;

public abstract class Mod
{
    readonly List<ILoadable> content = new();
    ModAssets? assets;

    internal bool loading;

    public string Uid { get; internal set; } = "";

    /// <summary>
    /// Nome interno do mod. Normalmente é o nome do assembly que declara a
    /// classe; o mod sintético (um `.tmod` sem classe `Mod`, permitido no
    /// tML 1.4.5) sobrescreve para usar o nome do container.
    /// </summary>
    public virtual string Name => GetType().Assembly.GetName().Name ?? GetType().Name;

    public string DisplayName { get; internal set; } = "";

    public Version Version { get; internal set; } = new(0, 0);

    internal string ManifestId = "";

    internal string[] Dependencies = Array.Empty<string>();

    public ILog Logger { get; internal set; } = null!;

    /// <summary>
    /// tML 1.4.5: <c>public Assembly Code { get; internal set; }</c> — o
    /// assembly do próprio mod. É por ele que o mod abre os recursos embutidos
    /// (<c>GetManifestResourceStream</c>), e o Luminance usa exatamente isso
    /// para carregar os shaders compilados.
    ///
    /// Aqui não há campo: o tipo do mod já se conhece pelo próprio
    /// <see cref="Type"/> do objeto derivado, então derivar é o mesmo valor
    /// com uma fonte só.
    /// </summary>
    public Assembly Code => GetType().Assembly;

    public string Folder { get; internal set; } = "";

    /// <summary>
    /// tML 1.4.4: <c>public AssetRepository Assets { get; }</c>. O tipo de
    /// retorno faz parte da assinatura que o mod compilado no PC chama
    /// (<c>callvirt ReLogic.Content.AssetRepository Mod::get_Assets()</c>), por
    /// isso é <see cref="ReLogic.Content.AssetRepository"/> e não
    /// <see cref="ModAssets"/>.
    /// </summary>
    public ReLogic.Content.AssetRepository Assets
    {
        get
        {
            if (!loggedAssets)
            {
                loggedAssets = true;
                Log.Info($"[fx] Assets pedido por {Name}");
            }
            return assets ??= new ModAssets(this);
        }
    }

    /// <summary>Marcador do log de um so' disparo em <see cref="Assets"/>.</summary>
    bool loggedAssets;

    public PreJITFilter PreJITFilter { get; protected set; } = new PreJITFilter();

    public bool ContentAutoloadingEnabled { get; init; } = true;

    public bool BackgroundAutoloadingEnabled { get; init; } = true;

    public bool CloudAutoloadingEnabled { get; init; } = true;

    public virtual void Load() { }

    public virtual void PostSetupContent() { }

    public virtual void AddRecipeGroups() { }

    public virtual void AddRecipes() { }

    public virtual void PostAddRecipes() { }

    public Recipe CreateRecipe(int result, int amount = 1) => Recipe.Create(result, amount);

    public virtual void Unload() { }

    public virtual object? Call(params object?[] args) => null;

    public ModPacket GetPacket(int capacity = 256) => new(this, capacity);

    public int AddBossHeadTexture(string texture, int npcType = -1) => NPCLoader.ReserveBossHead(this, texture, npcType);

    public int AddNPCHeadTexture(int npcType, string texture) => NPCHeadLoader.Add(this, npcType, texture);

    public virtual void HandlePacket(BinaryReader reader, int whoAmI) { }

    public bool AddContent(ILoadable instance)
    {
        if (!loading) throw new InvalidOperationException($"{Name}: AddContent can only be called while the mod loads (Mod.Load)");
        if (!instance.IsLoadingEnabled(this)) return false;
        instance.Load(this);
        content.Add(instance);
        ModContent.Add(instance);
        return true;
    }

    public bool AddContent<T>() where T : ILoadable, new() => AddContent(new T());

    public IEnumerable<ILoadable> GetContent() => content.AsReadOnly();

    public IEnumerable<T> GetContent<T>() where T : ILoadable => content.OfType<T>();

    public string GetLocalizationKey(string suffix) => $"Mods.{Name}.{suffix}";

    public LocalizedText GetLocalization(string suffix, Func<string>? makeDefaultValue = null) =>
        Language.GetOrRegister(GetLocalizationKey(suffix), makeDefaultValue);

    public T Find<T>(string name) where T : IModType => ModContent.Find<T>(Name, name);

    public bool TryFind<T>(string name, out T value) where T : IModType => ModContent.TryFind(Name, name, out value);

    public bool FileExists(string name) => ResolveFile(name) is string path && File.Exists(path);

    /// <summary>
    /// Contrato <c>Mod.cs:327</c>. O PC pergunta ao <c>RootContentSource</c>;
    /// aqui a checagem equivalente e <see cref="ModContent.HasAsset(string)"/>,
    /// que cobre textura/fonte (as unicas coisas que o host carrega).
    /// </summary>
    public bool RequestAssetIfExists<T>(string assetName, out ReLogic.Content.Asset<T> asset) where T : class
    {
        if (!ModContent.HasAsset(Name + "/" + assetName))
        {
            asset = default!;
            return false;
        }
        asset = Assets.Request<T>(assetName);
        return true;
    }

    public byte[] GetFileBytes(string name) => File.ReadAllBytes(ExistingFile(name));

    public Stream GetFileStream(string name, bool newFileStream = false) => File.OpenRead(ExistingFile(name));

    /// <summary>
    /// v38: quantas chamadas de <see cref="GetFileNames"/> ainda logam. Interessa
    /// saber se o <c>LoadShaders</c> do Luminance chega a chamar isto — com um
    /// so' disparo nao dava para separar "chamou aqui" de "chamou antes".
    /// </summary>
    static int fileNamesLogLeft = 12;

    public List<string> GetFileNames()
    {
        bool log = System.Threading.Interlocked.Decrement(ref fileNamesLogLeft) >= 0;
        if (!Directory.Exists(Folder))
        {
            if (log) Log.Info($"[fx] GetFileNames({Name}): pasta inexistente '{Folder}'");
            return new List<string>();
        }
        List<string> list = Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Folder, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        // v36: o LoadShaders do Luminance explode antes de pedir qualquer Effect.
        // Esta linha separa "a lista chegou vazia" de "explodiu depois dela".
        if (log)
            Log.Info($"[fx] GetFileNames({Name}): {list.Count} entrada(s)" +
                (list.Count == 0 ? "" : $", ex.: {string.Join(" | ", list.Take(3))}"));
        return list;
    }

    internal string? ResolveFile(string name)
    {
        if (Folder.Length == 0 || string.IsNullOrEmpty(name)) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder));
        string path = Path.GetFullPath(Path.Combine(root, name.Replace('\\', '/').TrimStart('/')));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    string ExistingFile(string name)
    {
        string? path = ResolveFile(name);
        if (path == null || !File.Exists(path)) throw new FileNotFoundException($"{Name}: no file '{name}' in the mod", name);
        return path;
    }

    internal void Autoload(Assembly assembly)
    {
        if (!ContentAutoloadingEnabled) return;
        // Uma listagem só: `GetTypes()` é caro e o resultado tem de ser o mesmo
        // nas duas passadas (a segunda, a dos ICustomAutoload).
        List<Type> types = ModTypes.Of(assembly, Name);
        foreach (Type type in types.Where(IsAutoloaded).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            // Um unico tipo nao pode derrubar o mod INTEIRO. Luminance traz
            // ScreenShaderFixer, cujo VTable depende de tipos aninhados do
            // TerrariaHooks que o shim nao tem (orig_DrawSelf, hook_Check-
            // Monoliths...). O Activator lancava TypeLoadException aqui, o
            // mod era marcado "failed to load" e a cadeia inteira
            // FargowiltasSouls -> FargowiltasMusic era desabilitada — nenhum
            // dos dois chegava a ser reescrito, e o usuario via "o mod de
            // almas e a musica nao carregaram". Isola o tipo e segue.
            try { AddContent((ILoadable)Activator.CreateInstance(type, true)!); }
            catch (Exception e)
            {
                Terraria.ModLoader.Core.ItemHooks.Report($"{Name}.Autoload[{type.FullName}]", e);
            }
        }
        foreach (Type type in types.Where(IsCustomAutoloaded).OrderBy(t => t.FullName, StringComparer.Ordinal))
            type.GetMethod(nameof(ICustomAutoload.Autoload), BindingFlags.Public | BindingFlags.Static, new[] { typeof(Mod) })?.Invoke(null, new object[] { this });
        GoreLoader.AutoloadGores(this);
        if (BackgroundAutoloadingEnabled) BackgroundTextureLoader.AutoloadBackgrounds(this);
        if (CloudAutoloadingEnabled) CloudLoader.AutoloadClouds(this);
    }

    // `IsAssignableFrom` carrega a VTable do tipo. ScreenShaderFixer do
    // Luminance referencia tipos aninhados do TerrariaHooks que o shim nao
    // tem (hook_CheckMonoliths, orig_DrawSelf...) e lancava TypeLoadException
    // AQUI, na enumeracao lazy do Where — fora do try que envolve o
    // Activator.CreateInstance. Por isso o isolamento do v95 nao bastou:
    // o mod inteiro continuava "failed to load". Isola tambem o predicado.
    static bool IsCustomAutoloaded(Type type)
    {
        try
        {
            return !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters &&
                typeof(ICustomAutoload).IsAssignableFrom(type);
        }
        catch (Exception e)
        {
            Terraria.ModLoader.Core.ItemHooks.Report($"{type.FullName}.IsCustomAutoloaded", e);
            return false;
        }
    }

    static bool IsAutoloaded(Type type)
    {
        try
        {
            return !type.IsAbstract && !type.ContainsGenericParameters && typeof(ILoadable).IsAssignableFrom(type) &&
                type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) != null &&
                AutoloadAttribute.GetValue(type).NeedsAutoloading;
        }
        catch (Exception e)
        {
            Terraria.ModLoader.Core.ItemHooks.Report($"{type.FullName}.IsAutoloaded", e);
            return false;
        }
    }
}
