namespace Terraria;

/// <summary>
/// Parte escrita à mão de <see cref="On_Item"/> (o resto vem do ProxyGen).
///
/// O jogo no PC tem <c>Item.GetShimmered()</c> e o HookGen publica
/// <c>orig_/hook_GetShimmered</c>; o dump do Android desta build não tem esse
/// método em <c>Item</c> (só <c>CanShimmer</c> / <c>GetShimmerEquivalentType</c>),
/// então o gerador não criou a dupla. O mod (Fargowiltas, para consertar
/// RecipeGroups na interação com Shimmer) referencia o tipo ->
/// <c>TypeLoadException</c> ao carregar a classe do mod -> o mod inteiro falha.
///
/// Aqui só reconstruímos a FORMA: delegate + evento vazio. Assinar funciona e
/// nunca dispara — perde-se o conserto do RecipeGroup no Shimmer, não o mod.
/// </summary>
public static partial class On_Item
{
#pragma warning disable CS0067 // evento nunca disparado (proposital)

    public delegate void orig_GetShimmered(Item self);

    public delegate void hook_GetShimmered(orig_GetShimmered orig, Item self);

    /// <summary>Sem gancho nativo nesta versão: <c>+=</c>/<c>-=</c> ignorados.</summary>
    public static event hook_GetShimmered GetShimmered
    {
        add { }
        remove { }
    }

#pragma warning restore CS0067
}
