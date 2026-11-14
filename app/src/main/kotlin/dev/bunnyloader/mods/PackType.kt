package dev.bunnyloader.mods

/**
 * O tipo de um pacote, pelo `type` do manifesto. Só separa as listas do
 * launcher (Explorar e Pacotes). Texturas carregam PNGs e fontes, Fonts/,
 * ambas sem arquivo JavaScript.
 *
 * O manifesto pode escrever em inglês ou português, no singular ou plural:
 * quem faz pacote não deveria ter de adivinhar a grafia. O que não bate com
 * nada é mod, o mesmo que não escrever o campo.
 */
enum class PackType(val label: String) {
    MOD("Mods"),
    /**
     * Mods do tModLoader, os `.tmod` que o autor publica.
     *
     * Não sai de manifesto nenhum: não existe `.bl` com este tipo. A lista é
     * montada lendo `bunny_packs/tml`, e por isso o rótulo é próprio em vez de
     * vir do campo `type` de um pacote.
     */
    TML("TmlMods"),
    TEXTURE("Textures"),
    FONT("Fonts");

    companion object {
        private val ALIASES = mapOf(
            "texture" to TEXTURE, "textures" to TEXTURE, "textura" to TEXTURE, "texturas" to TEXTURE,
            "resourcepack" to TEXTURE, "resource-pack" to TEXTURE,
            "font" to FONT, "fonts" to FONT, "fonte" to FONT, "fontes" to FONT,
        )

        fun of(type: String): PackType = ALIASES[type.trim().lowercase()] ?: MOD
    }
}
