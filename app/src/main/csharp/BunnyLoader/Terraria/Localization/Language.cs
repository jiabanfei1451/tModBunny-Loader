using System;
using Terraria.ModLoader;

namespace Terraria.Localization;

public static partial class Language
{
    public static string GetTextValue(string key, params object?[] args) => Formatting.Apply(GetTextValue(key) ?? key, args);

    /// <summary>
    /// O jogo expõe <c>GetTextValue(string, object)</c> e o tML também; um mod
    /// do PC chama exatamente essa assinatura (não a de <c>params</c>), que
    /// compila para <c>object[]</c>.
    /// </summary>
    public static string GetTextValue(string key, object? arg0) => GetTextValue(key, new[] { arg0 });

    // tML 1.4.4.9 expõe 1, 2 e 3 argumentos como sobrecargas EXPLÍCITAS. Um
    // mod que chama `Language.GetTextValue(key, a, b, c)` compila para a
    // assinatura de 4 parâmetros, não para o `params object[]`.
    public static string GetTextValue(string key, object? arg0, object? arg1) => GetTextValue(key, new[] { arg0, arg1 });

    public static string GetTextValue(string key, object? arg0, object? arg1, object? arg2) => GetTextValue(key, new[] { arg0, arg1, arg2 });

    /// <summary>
    /// tML 1.4.5 (`Language.cs.patch:56`): <c>LocalizedText[] FindAll(LanguageSearchFilter)</c>.
    /// O ProxyGen gera a chamada nativa como <c>FindAll_</c> devolvendo
    /// <c>GameRefArray&lt;LocalizedText&gt;</c>; <c>implicit operator T[]</c>
    /// faz a conversão sem perder os textos.
    /// </summary>
    public static LocalizedText[] FindAll(LanguageSearchFilter filter) => (LocalizedText[])FindAll_(filter);

    /// <summary>
    /// No tML `LanguageSearchFilter` é um DELEGATE; aqui o dump o declara como
    /// classe, por isso o ProxyGen não gera a sobrecarga que aceita lambda.
    /// Sem este overload o próprio GameTipHooks (e mods com fontes) não compila.
    /// </summary>
    public static LocalizedText[] FindAll(Func<string?, LocalizedText, bool>? filter) => (LocalizedText[])FindAll_(filter);

    public static LocalizedText GetOrRegister(string key, Func<string>? makeDefaultValue = null) =>
        LocalizationLoader.GetOrRegister(key, makeDefaultValue);
}
