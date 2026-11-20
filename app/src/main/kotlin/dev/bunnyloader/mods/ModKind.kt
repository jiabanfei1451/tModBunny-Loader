package dev.bunnyloader.mods

/**
 * O que um mod é, para o rótulo entre parênteses na lista: o mesmo nome que o
 * autor vê ao empacotar.
 *
 * `BL` é o pacote sem código (só Assets/ e texturas); `JS` é o `main.js` da
 * forma antiga; `C#` é o `<id>.cs` ou o `<id>.dll` compilado. `tml` fica
 * separado porque um `.tmod` não é um pacote do launcher: o arquivo vem pronto
 * do tModLoader.
 */
enum class ModKind(val label: String) {
    BL("BL"),
    JS("JS"),
    CSHARP("C#"),
    TML("tml"),
}
