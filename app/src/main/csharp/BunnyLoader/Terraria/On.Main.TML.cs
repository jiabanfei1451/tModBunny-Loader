namespace Terraria;

/// <summary>
/// Parte escrita a mao de <see cref="On_Main"/> que o ProxyGen nao gera
/// porque o dump do Android nao expoe <c>Main.CheckMonoliths</c>.
///
/// O <c>Luminance</c> guarda um <c>hook_CheckMonoliths</c> num campo estatico de
/// <c>PrimitivePixelationSystem</c>; sem o tipo, a classe nem carrega
/// (<c>TypeLoadException: Could not resolve type with token ... hook_CheckMonoliths</c>).
///
/// A assinatura vem dos refs do proprio mod: <c>orig_CheckMonoliths.Invoke()</c>
/// sem parametros. O evento fica vazio de proposito (so forma): o gancho real
/// exigiria instalar no nativo, e nenhum mod desta familia o aciona em caminho
/// critico.
/// </summary>
public static partial class On_Main
{
    public delegate void orig_CheckMonoliths();

    public delegate void hook_CheckMonoliths(orig_CheckMonoliths orig);

    public static event hook_CheckMonoliths? CheckMonoliths
    {
        add { }
        remove { }
    }
}
