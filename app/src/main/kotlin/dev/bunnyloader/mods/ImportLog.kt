package dev.bunnyloader.mods

import android.content.Context
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * O diário dos imports: uma linha por arquivo que entrou, com hora, nome,
 * tamanho e o motivo quando dá errado.
 *
 * Fica em `bunny_packs/import.log` — visível no gerenciador de arquivos, ao
 * lado dos mods. Sem ele, uma recusa some sem deixar rastro: o aviso da tela é
 * uma linha só e o import seguinte a apaga.
 *
 * Escreve e lê serializado: o import roda em thread de IO e a tela pode pedir
 * o texto no meio dele.
 */
object ImportLog {
    private const val FILE = "import.log"
    /** Rotação: um aparelho que importa todo dia não enche o armazenamento. */
    private const val MAX_BYTES = 256L * 1024
    private const val KEEP_LINES = 500

    private val lock = Any()
    private val clock = SimpleDateFormat("HH:mm:ss", Locale.US)

    private fun file(context: Context) = File(ModRepository.packsDir(context), FILE)

    /** Uma linha do diário. `tag` é o que aparece antes do texto: `TML`, `BL`, `ERRO`. */
    fun add(context: Context, tag: String, message: String) {
        synchronized(lock) {
            val f = file(context)
            runCatching {
                if (f.isFile && f.length() > MAX_BYTES) {
                    f.writeText(f.readLines().takeLast(KEEP_LINES).joinToString("\n", postfix = "\n"))
                }
                f.appendText("${clock.format(Date())}  [$tag]  $message\n")
            }
        }
    }

    /** O diário inteiro, para a tela mostrar e o botão copiar. */
    fun read(context: Context): String = synchronized(lock) {
        runCatching { file(context).readText() }.getOrDefault("")
    }

    fun clear(context: Context) {
        synchronized(lock) { runCatching { file(context).delete() } }
    }
}
