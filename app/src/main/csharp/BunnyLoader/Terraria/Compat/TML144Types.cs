using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.UI;

namespace Terraria.DataStructures
{
    /// <summary>
    /// O botão de link da tela de título (os ícones do canto).
    ///
    /// Este tipo não estava no shim, e a falta dele derrubava o
    /// <c>FargowiltasSouls</c> inteiro: a classe <c>Mod</c> do mod tem um campo
    /// <c>List&lt;TitleLinkButton&gt; fargoTitleLinks</c>, e carregar a classe
    /// exige resolver o tipo do campo. Sem ele,
    /// <c>TypeLoadException: Could not load type of field ...:fargoTitleLinks</c>
    /// dentro de <c>Mod.Autoload</c> → <c>IsAssignableFrom</c>, e o assembly
    /// todo era descartado.
    ///
    /// São CAMPOS, não propriedades: o mod faz <c>stfld</c> direto, e o
    /// reescritor de IL deixa a instrução intacta quando o alvo é um campo
    /// real do shim (ver <c>HasRealField</c> em IlPatcher). O nome
    /// <c>FrameWehnSelected</c> com o erro de digitação é o do tML — não
    /// corrigir, é por ele que o mod procura.
    /// </summary>
    public class TitleLinkButton
    {
        public string TooltipTextKey;
        public string LinkUrl;
        public Rectangle? FrameWehnSelected;
        public Rectangle? FrameWhenNotSelected;
        public Asset<Texture2D> Image;
    }
}

namespace Terraria.UI
{
    /// <summary>
    /// Contrato das notificações que descem no canto da tela.
    ///
    /// Só os membros que o tML 1.4.4 declara — e nada além: se a interface
    /// trouxer um membro a mais, a classe do mod deixa de satisfazê-la e o
    /// carregamento falha com "does not implement interface member".
    ///
    /// A lista vem de <c>tmodloader/patches/tModLoader/Terraria/UI/
    /// IInGameNotification.cs.patch</c>, que só APAGA dois membros do vanilla
    /// (<c>CreationObject</c> e <c>DrawInNotificationsArea</c>) e não acrescenta
    /// nenhum. Sobram portanto quatro: ShouldBeRemoved, Update, DrawInGame e
    /// PushAnchor.
    ///
    /// <c>Scale</c> e <c>Opacity</c> NÃO podem entrar aqui. O IL do
    /// FargowiltasSouls mostra que os getters dessas duas propriedades em
    /// <c>NoDLCNotification</c>/<c>NoMusicNotification</c> são
    /// <c>Private, HideBySig, SpecialName</c> — métodos privados comuns, sem
    /// slot de vtable. Declará-los na interface faz o CLR exigir uma
    /// implementação virtual que não existe, e o resultado era
    /// <c>TypeLoadException: Method 'get_Scale' in type ... does not have an
    /// implementation</c> (três falhas no relatório do sandbox, incluindo
    /// <c>FargoSoulsPlayer.OnEnterWorld</c>).
    /// </summary>
    public interface IInGameNotification
    {
        bool ShouldBeRemoved { get; }

        void Update();

        void DrawInGame(SpriteBatch spriteBatch, Vector2 bottomAnchorPosition);

        void PushAnchor(ref Vector2 position);
    }
}

namespace Terraria
{
    /// <summary>
    /// Caixa de um valor mutável passada por referência entre chamadas — o
    /// Luminance guarda os shaders assim (<c>Ref&lt;Effect&gt;</c>).
    /// </summary>
    public class Ref<T>
    {
        public T Value;

        public Ref()
        {
        }

        public Ref(T value)
        {
            Value = value;
        }
    }
}

namespace Terraria.Graphics.Shaders
{
    // MiscShaderData saiu daqui: agora e' uma classe de verdade em
    // MiscShaderData.GLES.cs, porque o FargowiltasSouls registra os shaders
    // pelo construtor e os desenha em PreDraw/PostDraw.
}

namespace Terraria.GameContent.UI.Elements
{
    /// <summary>
    /// Um item da lista de mundos da tela de seleção.
    ///
    /// O <c>FargoChinese</c> guarda uma referência num campo de closure
    /// (<c>WorldDifficulty/&lt;&gt;c.&lt;&gt;9__5_4</c>), e o tipo ausente derrubava
    /// a classe no carregamento.
    ///
    /// <c>_worldIcon</c> é um campo de instância do vanilla, atribuído no
    /// construtor por <c>_worldIcon = GetIconElement();</c> (ver
    /// <c>tmodloader/patches/tModLoader/Terraria/GameContent/UI/Elements/
    /// UIWorldListItem.cs.patch</c>). O <c>WorldBorderSystem</c> do
    /// FargowiltasSouls o devolve por <c>ref</c>
    /// (<c>&lt;UseCustomBorder&gt;g__GetWorldIcon|2_0</c> retorna
    /// <c>Terraria.UI.UIElement&amp;</c>) e em seguida chama
    /// <c>RemoveAllChildren()</c> nele — por isso o tipo tem de ser
    /// <c>UIElement</c>, e não o <c>Asset&lt;Texture2D&gt;</c> que se poderia
    /// supor pelo nome.
    /// </summary>
    public class UIWorldListItem
    {
        public UIElement _worldIcon;
    }
}

