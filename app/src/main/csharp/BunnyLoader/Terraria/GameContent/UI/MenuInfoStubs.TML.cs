#pragma warning disable CS0067 // eventos nunca disparados: gancho de menu não existe neste shim

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.UI;

namespace Terraria.GameContent.UI.Elements
{
    /// <summary>
    /// Item da lista de mundos. O dump do Android não tem o tipo; o Luminance e o
    /// FargoChinese só leem <c>Data</c> dentro de um gancho de desenho que este
    /// shim não implanta (os eventos abaixo são vazios de propósito).
    /// </summary>
    public class AWorldListItem : UIElement
    {
        public virtual IO.WorldFileData Data => Main.ActiveWorldFileData;
    }

    public class UICharacterListItem : UIElement
    {
    }

    public static partial class On_UICharacterListItem
    {
        public delegate void orig_DrawSelf(UICharacterListItem self, SpriteBatch spriteBatch);

        public delegate void hook_DrawSelf(orig_DrawSelf orig, UICharacterListItem self, SpriteBatch spriteBatch);

        public static event hook_DrawSelf? DrawSelf
        {
            add { }
            remove { }
        }
    }

    public static partial class On_UIWorldListItem
    {
        public delegate void orig_DrawSelf(UIWorldListItem self, SpriteBatch spriteBatch);

        public delegate void hook_DrawSelf(orig_DrawSelf orig, UIWorldListItem self, SpriteBatch spriteBatch);

        public static event hook_DrawSelf? DrawSelf
        {
            add { }
            remove { }
        }
    }

    /// <summary>
    /// O `TerrariaHooks` do 1.4.4.9 publica <c>GetDifficulty(out string, out Color)</c>
    /// em <c>AWorldListItem</c>; o FargoChinese assina para reescrever o nome da
    /// dificuldade (<c>WorldDifficulty.AWorldListItem_GetDifficulty</c>). O dump do
    /// Android não tem o método (nem o tipo), então a dupla de delegates é escrita
    /// à mão: assinar JITA, mas o evento nunca dispara — perde-se o rótulo
    /// traduzido da dificuldade na lista de mundos, não o mod.
    /// </summary>
    public static partial class On_AWorldListItem
    {
        public delegate void orig_GetDifficulty(AWorldListItem self, ref string text, ref Color color);

        public delegate void hook_GetDifficulty(orig_GetDifficulty orig, AWorldListItem self, ref string text, ref Color color);

        public static event hook_GetDifficulty? GetDifficulty
        {
            add { }
            remove { }
        }
    }
}

namespace Terraria.GameContent.UI.States
{
    public class UICharacterSelect : UIState
    {
    }

    public class UIWorldSelect : UIState
    {
    }

    public static partial class On_UICharacterSelect
    {
        public delegate void orig_Draw(UICharacterSelect self, SpriteBatch spriteBatch);

        public delegate void hook_Draw(orig_Draw orig, UICharacterSelect self, SpriteBatch spriteBatch);

        public static event hook_Draw? Draw
        {
            add { }
            remove { }
        }
    }

    public static partial class On_UIWorldSelect
    {
        public delegate void orig_Draw(UIWorldSelect self, SpriteBatch spriteBatch);

        public delegate void hook_Draw(orig_Draw orig, UIWorldSelect self, SpriteBatch spriteBatch);

        public static event hook_Draw? Draw
        {
            add { }
            remove { }
        }
    }
}
