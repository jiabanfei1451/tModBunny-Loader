using MonoMod.Cil;

namespace Terraria.GameContent.UI.Elements;

/// <summary>
/// <c>TerrariaHooks</c> do 1.4.4.9 gera <c>IL_UIWorldListItem</c> (o par IL do
/// <c>TerrariaHooks</c> para cada tipo, um evento por método). O FargoChinese
/// assina <c>DrawSelf</c> em <c>WorldDifficulty.Load</c> e desassina em
/// <c>Unload</c>; sem o tipo, os dois métodos não JITavam (diagnóstico do
/// <c>tools/modsandbox</c>: <c>-> Terraria.GameContent.UI.Elements.IL_UIWorldListItem</c>).
///
/// Aqui só a FORMA: <c>+=</c>/<c>-=</c> são aceitos e ignorados — o shim não
/// reescreve IL de UI do jogo, e <c>UIWorldListItem.DrawSelf</c> nem existe
/// neste dump. Mesmo padrão de <c>Terraria.IL_Main</c> e
/// <c>Terraria.On.SceneMetrics</c>.
/// </summary>
public static partial class IL_UIWorldListItem
{
    // evento nunca disparado (proposital)
    public static event ILContext.Manipulator DrawSelf
    {
        add { }
        remove { }
    }
}
