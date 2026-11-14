using System.Collections.Generic;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;

namespace Terraria;

public sealed unsafe partial class Main
{
    // v65: tML (Main.cs.patch) acrescenta estes dois. O mod le
    // Main.CurrentPlayer para achar o jogador "dono" do desenho atual; a
    // sobrecarga e' usada pelas telas de slot de acessorio para apontar para
    // outro jogador temporariamente.
    static Player? currentPlayerOverride;

    public static Player CurrentPlayer => currentPlayerOverride ?? player[myPlayer];

    public static Player? CurrentPlayerOverride
    {
        get => currentPlayerOverride;
        set => currentPlayerOverride = value;
    }

    // tML patch (Main.cs.patch) transforma estes `const int` em `static readonly
    // int`, então o mod lê `ldsfld Main::maxNPCs`. O gerado saía `const` (o dump
    // do jogo é const), e const não tem armazenamento em runtime -> `ldsfld`
    // quebra com MissingFieldException. Valores são os do dump 1.4.5.8.6.
    public static readonly int maxChests = 8000;
    public static readonly int maxItems = 400;
    public static readonly int maxProjectiles = 1000;
    public static readonly int maxNPCs = 200;

    // No Android 1.4.5.8.6 `screenWidth`/`screenHeight` são PROPRIEDADES estáticas
    // (dump: `public static int screenWidth { get; set; }`), cujo getter nativo
    // `get_screenWidth()` faz `return instance.screenWidth`. O mod lê isso durante
    // o Load() — via StatSheetUI.OnInitialize — que roda ANTES de a thread do jogo
    // criar/assentar o `Main.instance`. Nessa hora o getter do jogo dereferencia
    // `instance` nulo e estoura NullReferenceException (vira GameException e derruba
    // o Load() inteiro, depois SIGSEGV no primeiro quadro).
    //
    // v37 tentou ler o campo de instância por offset para não chamar o getter, mas
    // `GameInterop.Offset` não acha `screenWidth` nos metadados nativos (é
    // propriedade, não campo) e o fallback fixo de 1152x648 passou a ser o valor
    // "real" para todos os efeitos. Isso custou caro na v47: o painel de carga
    // desenhava 82% de 1152 e, como a matriz de UI ainda reduzia 0.6, aparecia
    // com metade do tamanho em qualquer aparelho.
    //
    // Agora: se `instance` já existe, usa o getter nativo (valor de verdade, muda
    // com a resolução e com a rotação); se ainda não existe, devolve o último
    // valor conhecido. Fallback só na primeiríssima leitura.
    static int knownWidth = 1152;
    static int knownHeight = 648;

    public static int screenWidth
    {
        get
        {
            if (instance == null || instance.Pointer == 0) return knownWidth;
            try
            {
                int w = screenWidth_;
                if (w > 0) knownWidth = w;
            }
            catch (System.Exception) { /* getter do jogo indisponível: fica o último */ }
            return knownWidth;
        }
        set
        {
            knownWidth = value;
            try { if (instance != null && instance.Pointer != 0) screenWidth_ = value; }
            catch (System.Exception) { }
        }
    }

    public static int screenHeight
    {
        get
        {
            if (instance == null || instance.Pointer == 0) return knownHeight;
            try
            {
                int h = screenHeight_;
                if (h > 0) knownHeight = h;
            }
            catch (System.Exception) { }
            return knownHeight;
        }
        set
        {
            knownHeight = value;
            try { if (instance != null && instance.Pointer != 0) screenHeight_ = value; }
            catch (System.Exception) { }
        }
    }

    // `Main.npcFrameCount` e `int[NPCID.Count]` = 697 de fabrica. O CaughtNPCItem
    // do Fargowiltas le `npcFrameCount[AssociatedNpcId]` em SetStaticDefaults,
    // onde AssociatedNpcId e o id de um NPC DE MOD (697..701). O `ItemsInstalled`
    // hook (item) roda ANTES de `tickModNpcs()` (que aumenta 81 tabelas de 697
    // para 703), entao na hora da leitura o array ainda tem 697 posicoes:
    //   item Fargowiltas/Abominationn: SetStaticDefaults threw:
    //     IndexOutOfRangeException: index 697 out of the game array
    //
    // Em vez de depender da ordem dos ticks, o getter cresce o array sob demanda
    // ate `NPCLoader.NPCCount` (NPC registrados no Load, antes disto). Escreve o
    // novo array de volta no slot estatico, entao o `applyFrames` nativo (que
    // re-le o campo estatico) continua escrevendo no MESMO array. Se o nativo
    // ja tiver crescido, `Length >= need` e nao faz nada.
    static StaticRef npcFrameCountSlot;

    public static ref GameArray<int> npcFrameCount
    {
        get
        {
            nint addr = GameInterop.StaticAddress(ref npcFrameCountSlot, Class, "npcFrameCount");
            ref GameArray<int> slot = ref *(GameArray<int>*)addr;
            if (!slot.IsNull)
            {
                int need = Terraria.ModLoader.NPCLoader.NPCCount;
                if (slot.Length < need) slot = slot.Resized(need);
            }
            return ref slot;
        }
    }

    public static void __set_npcFrameCount(GameArray<int> value) =>
        *(GameArray<int>*)GameInterop.StaticAddress(ref npcFrameCountSlot, Class, "npcFrameCount") = value;

    static SceneMetrics currentSceneMetrics;

    public static ref SceneMetrics SceneMetrics
    {
        get
        {
            currentSceneMetrics = _usingSeparateCameraSceneMetrics ? _cameraSceneMetrics : _playerSceneMetrics;
            return ref currentSceneMetrics;
        }
    }

    public static Color DiscoColor => new(DiscoR, DiscoG, DiscoB);

    public static void NewText(object o, Color? color = null) =>
        NewText(o is Terraria.Localization.LocalizedText text ? text.Value : o?.ToString(), color ?? new Color(255, 255, 255, 255));

    public static void EntitySpriteDraw(Texture2D texture, Microsoft.Xna.Framework.Vector2 position, Microsoft.Xna.Framework.Rectangle? sourceRectangle, Color color, float rotation, Microsoft.Xna.Framework.Vector2 origin, float scale, SpriteEffects effects, float worthless = 0f) =>
        EntitySpriteDraw(texture, position, sourceRectangle ?? new Microsoft.Xna.Framework.Rectangle(0, 0, texture.Width, texture.Height), color, rotation, origin, scale, effects, worthless);

    public static void EntitySpriteDraw(Texture2D texture, Microsoft.Xna.Framework.Vector2 position, Microsoft.Xna.Framework.Rectangle? sourceRectangle, Color color, float rotation, Microsoft.Xna.Framework.Vector2 origin, Microsoft.Xna.Framework.Vector2 scale, SpriteEffects effects, float worthless = 0f) =>
        EntitySpriteDraw(texture, position, sourceRectangle ?? new Microsoft.Xna.Framework.Rectangle(0, 0, texture.Width, texture.Height), color, rotation, origin, scale, effects, worthless);

    public static IEnumerable<NPC> ActiveNPCs
    {
        get
        {
            for (int i = 0; i < maxNPCs; i++)
            {
                NPC n = npc[i];
                if (n.active) yield return n;
            }
        }
    }

    public static IEnumerable<Projectile> ActiveProjectiles
    {
        get
        {
            for (int i = 0; i < maxProjectiles; i++)
            {
                Projectile p = projectile[i];
                if (p.active) yield return p;
            }
        }
    }
}
