namespace Terraria.GameContent.UI.Elements;

/// <summary>
/// O construtor de <c>UIWorldListItem</c> que o <c>FargowiltasSouls</c>
/// (WorldBorderSystem.UseCustomBorder) intercepta. O dump do Android nao traz
/// essa sobrecarga, entao a dupla do HookGen nao existia e o campo de closure
/// <c>hook_ctor</c> derrubava <c>WorldBorderSystem</c> no carregamento.
///
/// Assinatura exata tirada dos refs do mod:
/// <c>orig_ctor.Invoke(UIWorldListItem, WorldFileData, int32, bool)</c> e
/// <c>add_ctor(hook_ctor)</c>.
/// </summary>
public static partial class On_UIWorldListItem
{
    public delegate void orig_ctor(UIWorldListItem self, Terraria.IO.WorldFileData data, int snapPointsUsed, bool something);

    public delegate void hook_ctor(orig_ctor orig, UIWorldListItem self, Terraria.IO.WorldFileData data, int snapPointsUsed, bool something);

    public static event hook_ctor? ctor
    {
        add { }
        remove { }
    }
}
