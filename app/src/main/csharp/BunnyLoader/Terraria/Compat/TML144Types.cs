using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

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
    /// carregamento falha com "does not implement interface member". A lista
    /// veio do IL das implementações do próprio FargowiltasSouls
    /// (<c>NoDLCNotification</c>: ShouldBeRemoved, Scale, Opacity, Update,
    /// DrawInGame, PushAnchor).
    /// </summary>
    public interface IInGameNotification
    {
        bool ShouldBeRemoved { get; }

        float Scale { get; }

        float Opacity { get; }

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
    /// <summary>
    /// Dados de um shader de jogo. Existe para o campo
    /// <c>FargowiltasSouls.Content.Bosses.VanillaEternity.QueenSlime.queenSlimeBuffer</c>
    /// resolver: sem o tipo, a classe não carrega e o mod inteiro cai.
    ///
    /// Os membros (<c>UseColor</c>, <c>UseImage</c>, <c>Apply</c>...) ficam de
    /// fora de propósito: quem os chama já é quarentenado pelo reescritor, e um
    /// método vazio aqui daria a impressão de que o shader foi aplicado.
    /// </summary>
    public class MiscShaderData
    {
    }
}

namespace Terraria.GameContent.UI.Elements
{
    /// <summary>
    /// Um item da lista de mundos da tela de seleção.
    ///
    /// O <c>FargoChinese</c> guarda uma referência num campo de closure
    /// (<c>WorldDifficulty/&lt;&gt;c.&lt;&gt;9__5_4</c>), e o tipo ausente derrubava
    /// a classe no carregamento.
    /// </summary>
    public class UIWorldListItem
    {
    }
}

