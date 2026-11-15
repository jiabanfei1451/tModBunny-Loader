package dev.bunnyloader.ui

import android.content.Context
import android.widget.Toast
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import dev.bunnyloader.mods.TmlMod
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.util.Locale

/**
 * O cache de pré-compilação dos mods ("Pré-compilar mods", Configurações).
 *
 * O shim guarda o IL **já reescrito** de cada mod em `<pasta do mod>/.ilcache/`
 * e, na abertura seguinte, carrega esses bytes direto — o Cecil não roda de
 * novo. É o que transforma os 170 s do FargowiltasSouls em ~1 s na segunda
 * abertura.
 *
 * Isto aqui é só a manutenção do diretório: ver o tamanho e apagar. O shim
 * poda sozinho as entradas antigas quando grava uma nova, mas um cache de um
 * shim que já não existe (ou de um mod desinstalado) ficaria para trás.
 */
object CompileCache {
    private const val FOLDER = ".ilcache"

    /**
     * As pastas `.ilcache` existentes. Ficam DENTRO da pasta de cada mod
     * (`TML/mods/<uid>/`), que é o único diretório que o shim conhece com
     * certeza — o caminho do assembly chega por parâmetro.
     */
    fun folders(context: Context): List<File> {
        val mods = TmlMod.modsDir(context)
        val children = mods.listFiles() ?: return emptyList()
        return children.filter { it.isDirectory }
            .map { File(it, FOLDER) }
            .filter { it.isDirectory }
    }

    /** Bytes ocupados hoje. Varredura em IO: são poucos arquivos, mas grandes. */
    fun size(context: Context): Long = folders(context).sumOf { dir ->
        dir.walkTopDown().filter { it.isFile }.sumOf { it.length() }
    }

    fun entries(context: Context): Int = folders(context).sumOf { dir ->
        dir.walkTopDown().count { it.isFile }
    }

    /**
     * Apaga tudo. A lista de arquivos é montada ANTES: `walkTopDown` lendo um
     * diretório que está sendo esvaziado devolve resultados parciais.
     */
    fun clear(context: Context): Long {
        var freed = 0L
        for (dir in folders(context)) {
            val files = dir.walkTopDown().filter { it.isFile }.toList()
            for (file in files) {
                freed += file.length()
                runCatching { file.delete() }
            }
            runCatching { dir.delete() }
        }
        return freed
    }
}

private fun humanBytes(bytes: Long): String {
    if (bytes <= 0) return "0 B"
    val units = arrayOf("B", "KB", "MB", "GB")
    var value = bytes.toDouble()
    var unit = 0
    while (value >= 1024 && unit < units.lastIndex) {
        value /= 1024
        unit++
    }
    return if (unit == 0) "${bytes} B" else String.format(Locale.US, "%.1f %s", value, units[unit])
}

/**
 * O cartão do cache, logo abaixo do interruptor. Quem acabou de ligar a chave
 * não vê diferença nenhuma na abertura seguinte se o cache já estava cheio —
 * ver o tamanho em disco é o que prova que a chave está fazendo algo.
 */
@Composable
fun CompileCacheCard() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var bytes by remember { mutableStateOf(-1L) }
    var files by remember { mutableStateOf(0) }

    LaunchedEffect(Unit) {
        bytes = withContext(Dispatchers.IO) { CompileCache.size(ctx) }
        files = withContext(Dispatchers.IO) { CompileCache.entries(ctx) }
    }

    PixelCard(Modifier.fillMaxWidth().padding(top = 10.dp)) {
        Column(Modifier.padding(12.dp)) {
            val size = if (bytes < 0) tr("CompileCacheReading") else humanBytes(bytes)
            PixelText(
                if (files > 0) "$size · $files" else size,
                size = Ts.Small,
                color = if (files > 0) Bl.TextDim else Bl.TextFaint,
            )
            // A limpeza é para o caso de o resultado ter ficado estranho
            // (mod atualizado sem o cache invalidar, por exemplo). Não é
            // preciso apertar isto no uso normal.
            Row(Modifier.padding(top = 10.dp)) {
                PixelButton(tr("ClearCompileCache"), {
                    scope.launch {
                        val freed = withContext(Dispatchers.IO) { CompileCache.clear(ctx) }
                        bytes = withContext(Dispatchers.IO) { CompileCache.size(ctx) }
                        files = withContext(Dispatchers.IO) { CompileCache.entries(ctx) }
                        Toast.makeText(
                            ctx,
                            tr("CompileCacheCleared") + " " + humanBytes(freed),
                            Toast.LENGTH_SHORT,
                        ).show()
                    }
                }, fontSize = Ts.Small)
            }
        }
    }
}
