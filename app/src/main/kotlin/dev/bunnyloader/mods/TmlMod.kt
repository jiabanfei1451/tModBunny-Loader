package dev.bunnyloader.mods

import android.content.Context
import android.net.Uri
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Um mod do tModLoader, do arquivo `.tmod` que o autor publica.
 *
 * O `.tmod` **não** é um zip (ver [ModPackage]): é um container do próprio
 * tModLoader, e o `Info` que ele guarda é binário. Tudo isso é resolvido na
 * extração — desta pasta em diante o mod é só arquivos: `Info` em texto
 * `chave = valor`, `<Nome>.dll`, `icon.png`, `description.txt` e os recursos
 * nas mesmas pastas do código-fonte.
 */
data class TmlMod(
    /** `name` do Info: é a identidade, e o nome da pasta de trabalho. */
    val name: String,
    val displayName: String,
    val author: String,
    val version: String,
    val description: String,
    /** `Both`, `Client` ou `Server`. Só os dois primeiros rodam sozinhos. */
    val side: String,
    val modReferences: List<String>,
    val weakReferences: List<String>,
    val fileName: String,
    /** O `.tmod` original, guardado para poder reextrair sem pedir o arquivo. */
    val path: String,
    /** A pasta extraída — é nela que o núcleo do jogo trabalha. */
    val workDir: String,
    val sizeBytes: Long,
    /** O `.dll` de entrada, quando o zip tem um na raiz. */
    val entryDll: String?,
    val resourceCount: Int,
    val iconFile: String?,
) {
    /** O que a lista mostra: o nome bonito, ou o técnico quando não há. */
    val label: String get() = displayName.ifBlank { name }

    /** Roda no cliente? `Server` sozinho não tem o que desenhar aqui. */
    val clientSide: Boolean get() = !side.equals("Server", ignoreCase = true)

    val sizeLabel: String
        get() = when {
            sizeBytes >= 1024L * 1024 * 1024 -> "%.1f GB".format(sizeBytes / 1073741824.0)
            sizeBytes >= 1024L * 1024 -> "%.1f MB".format(sizeBytes / 1048576.0)
            sizeBytes >= 1024L -> "%.0f KB".format(sizeBytes / 1024.0)
            else -> "$sizeBytes B"
        }
}

/**
 * A pasta de trabalho dos mods do tModLoader.
 *
 * `Android/data/<pacote>/TML` — dentro da pasta do app, ao lado de
 * `bunny_packs`. **Não** é a raiz do armazenamento: sem root, o app não escreve
 * em `/`, e o tModLoader original desiste na primeira gravação. Aqui a pasta é
 * do próprio app, então extrair, reler e apagar funcionam.
 *
 * Layout:
 * ```
 * TML/
 *   enabled.json         os ligados (a tela lê daqui)
 *   mode.txt             a mesma lista em texto, para o lado nativo
 *   mods/                a pasta DOS mods (não é um mod)
 *     <Nome>.tmod        o arquivo original, guardado para reextrair
 *     <Nome>/            o mod extraído: Info, <Nome>.dll, icon.png, recursos
 * ```
 * Um mod chamado `mods` fica em `TML/mods/mods/` — dois níveis iguais, e está
 * certo: o primeiro é o container, o segundo é o nome do mod.
 */
object TmlMods {
    const val DIR = "TML"
    /** Onde ficam TODOS os mods: `TML/mods/<Nome>/`. */
    const val MODS = "mods"
    private const val STATE = "enabled.json"
    private const val INCOMING = ".incoming.tmod"

    private val json = Json { ignoreUnknownKeys = true; prettyPrint = true }
    private val strings = ListSerializer(String.serializer())

    /** A raiz do fluxo TML: `mode.txt`, `enabled.json`, e a pasta `mods/`. */
    fun dir(context: Context): File {
        val base = context.getExternalFilesDir(null)?.parentFile ?: context.filesDir
        return File(base, DIR).apply { mkdirs() }
    }

    /**
     * `TML/mods/` — a pasta dos mods, não um mod.
     *
     * Antes era tudo achatado em `TML/<Nome>/`, o que confundia: um mod cujo
     * nome interno é `mods` virava `TML/mods/mods.dll` e parecia estar faltando
     * um nível. Agora cada mod tem a sua pasta DENTRO de `mods/`, como no
     * `Mods/` do tModLoader no PC.
     */
    fun modsDir(context: Context): File = File(dir(context), MODS).apply { mkdirs() }

    /**
     * O nome do arquivo que o seletor do sistema entregou.
     *
     * `uri.lastPathSegment` num `content://` é um id interno (`document/1234`),
     * não o nome: quem importa um `.tmod` precisa do `_display_name` para saber
     * que é `.tmod` antes de abrir o zip.
     */
    fun displayName(context: Context, uri: Uri): String {
        runCatching {
            context.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)
                ?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val index = cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                        if (index >= 0) cursor.getString(index)?.takeIf { it.isNotBlank() }?.let { return it }
                    }
                }
        }
        return uri.lastPathSegment ?: uri.toString()
    }

    /** O arquivo escolhido é um `.tmod`? Decide qual dos dois imports roda. */
    fun isTmod(context: Context, uri: Uri): Boolean = displayName(context, uri).endsWith(".tmod", true)

    fun enabled(context: Context): Set<String> {
        val f = File(dir(context), STATE)
        if (!f.isFile) return emptySet()
        val names = runCatching { json.decodeFromString(strings, f.readText()).toSet() }.getOrDefault(emptySet())
        // `enabled.json` guarda nomes, não pastas: um mod desinstalado (ou o
        // resíduo do layout antigo, onde `TML/mods/` era um mod chamado
        // "mods") continuava contando como "fluxo TML ativo" e o jogo pagava
        // o custo do carregamento inteiro sem nenhum `.tmod` de verdade.
        // Só vale o que ainda existe em `TML/mods/<nome>/Info`.
        val root = modsDir(context)
        return names.filterTo(LinkedHashSet()) { name -> File(File(root, name), "Info").isFile }
    }

    /**
     * Liga ou desliga um mod. A lista de ligados é a única coisa que o núcleo
     * consulta: com ela vazia, nada muda no carregamento do jogo.
     */
    fun setEnabled(context: Context, name: String, on: Boolean) {
        val now = enabled(context).toMutableSet()
        if (on) now.add(name) else now.remove(name)
        runCatching {
            File(dir(context), STATE).writeText(json.encodeToString(strings, now.sorted()))
        }
    }

    /**
     * Todas as pastas de trabalho com `Info`, já lidas. Pasta quebrada é pulada.
     *
     * Um `.tmod` copiado à mão para `TML/` ainda não tem pasta extraída: é
     * extraído aqui, uma vez, para o arquivo largado no gerenciador de arquivos
     * valer tanto quanto o importado pela tela.
     */
    fun list(context: Context): List<TmlMod> {
        upgrade(context)
        val root = modsDir(context)
        root.listFiles { f -> f.isFile && f.name.endsWith(".tmod", true) }?.forEach { file ->
            val declared = runCatching { readInfo(file)["name"] }.getOrNull()
                ?.takeIf { it.isNotBlank() } ?: file.nameWithoutExtension
            val safe = declared.replace(Regex("[^A-Za-z0-9._-]"), "_").ifBlank { "mod" }
            val work = File(root, safe)
            if (!File(work, "Info").isFile) {
                val error = runCatching { extract(file, work) }.exceptionOrNull()
                ImportLog.add(
                    context, "TML",
                    if (error == null) "extraído da pasta: ${file.name} -> $safe/"
                    else "falhou ao extrair ${file.name}: ${error.message ?: error.javaClass.simpleName}"
                )
            }
        }
        // `upgrade()` já converteu os `.rawimg` deixados por versões antigas.
        val folders = root.listFiles { f -> f.isDirectory && File(f, "Info").isFile }
            .orEmpty()
        return folders.mapNotNull { f -> runCatching { read(context, f) }.getOrNull() }
            .sortedBy { it.label.lowercase() }
    }

    /** Tudo que uma pasta `TML/` antiga precisa para funcionar com esta versão. */
    fun upgrade(context: Context) {
        migrateFlatLayout(context)
        modsDir(context).listFiles { f -> f.isDirectory && File(f, "Info").isFile }
            .orEmpty().forEach { convertRawImages(context, it) }
    }

    /**
     * O tModLoader 1.4.5 guarda as texturas como `.rawimg`, não PNG. Quem
     * importou antes desta versão tem o arquivo cru na pasta de trabalho e o
     * loader só procura `.png` — daí o "texture ... not found; drawing a
     * placeholder". Converte no lugar, uma vez, e apaga o original.
     */
    private fun convertRawImages(context: Context, folder: File) {
        val files = folder.walkTopDown()
            .filter { it.isFile && it.name.endsWith(".rawimg", ignoreCase = true) }
            .toList()
        if (files.isEmpty()) return
        var converted = 0
        for (file in files) {
            val png = File(file.parentFile, file.name.dropLast(7) + ".png")
            if (png.isFile) {
                file.delete()
                continue
            }
            val bytes = runCatching { RawImgPng.convert(file.readBytes()) }.getOrNull() ?: continue
            runCatching {
                png.writeBytes(bytes)
                file.delete()
                converted++
            }
        }
        if (converted > 0) {
            ImportLog.add(
                context, "TML",
                "convertido(s) $converted .rawimg -> .png em ${folder.name}/ (mod já importado)"
            )
        }
    }

    /**
     * Traz o layout antigo (tudo achatado em `TML/`) para `TML/mods/`.
     *
     * Roda uma vez: depois de mover, a raiz só tem `enabled.json`, `mode.txt`
     * e a pasta `mods/`. O caso chato é o mod de teste cujo nome interno é
     * justamente `mods`: a pasta de trabalho dele É o novo container, então
     * o conteúdo precisa descer um nível antes de qualquer outra coisa.
     */
    private fun migrateFlatLayout(context: Context) {
        val root = dir(context)
        val container = modsDir(context)
        if (File(container, "Info").isFile && !File(container, MODS).isDirectory) {
            val staging = File(root, ".migrate-mods")
            staging.deleteRecursively()
            staging.mkdirs()
            container.listFiles()?.forEach { child ->
                if (child.name == MODS) return@forEach
                val dest = File(staging, child.name)
                if (child.renameTo(dest)) return@forEach
                child.copyRecursively(dest, overwrite = true)
                child.deleteRecursively()
            }
            if (staging.renameTo(File(container, MODS))) {
                ImportLog.add(context, "TML", "layout: TML/mods/ -> TML/mods/mods/")
            }
            staging.deleteRecursively()
        }
        root.listFiles { f -> f.isFile && f.name.endsWith(".tmod", true) }?.forEach { file ->
            val dest = File(container, file.name)
            if (!dest.exists() && file.renameTo(dest)) {
                ImportLog.add(context, "TML", "layout: ${file.name} -> mods/${file.name}")
            }
        }
        root.listFiles { f -> f.isDirectory && f.name != MODS && File(f, "Info").isFile }
            ?.forEach { folder ->
                val dest = File(container, folder.name)
                if (!dest.exists() && folder.renameTo(dest)) {
                    ImportLog.add(context, "TML", "layout: ${folder.name}/ -> mods/${folder.name}/")
                }
            }
    }

    private fun read(context: Context, folder: File): TmlMod {
        val info = parseInfo(File(folder, "Info").readBytes().decodeToString())
        val name = info["name"]?.takeIf { it.isNotBlank() } ?: folder.name
        val stored = File(modsDir(context), "$name.tmod")
        val dll = folder.listFiles { f: File -> f.isFile && f.name.endsWith(".dll", true) }
            ?.firstOrNull()?.name
        val icon = File(folder, "icon.png").takeIf { it.isFile }
            ?: folder.listFiles { f: File -> f.isFile && f.name.equals("icon.png", true) }?.firstOrNull()
        // `description.txt` guarda as quebras de linha que o Info binário perde
        // ao virar texto; quando existe, é ele que a tela mostra.
        val description = File(folder, "description.txt").takeIf { it.isFile }
            ?.runCatching { readText().trim() }?.getOrNull()?.takeIf { it.isNotBlank() }
            ?: info["description"].orEmpty()
        return TmlMod(
            name = name,
            displayName = info["displayname"].orEmpty(),
            author = info["author"].orEmpty(),
            version = info["version"].orEmpty(),
            description = description,
            side = info["side"].orEmpty(),
            modReferences = splitRefs(info["modreferences"]),
            weakReferences = splitRefs(info["weakreferences"]),
            fileName = stored.name,
            path = stored.absolutePath,
            workDir = folder.absolutePath,
            sizeBytes = stored.takeIf { it.isFile }?.length() ?: folderSize(folder),
            entryDll = dll,
            resourceCount = folder.walkTopDown().count { it.isFile },
            iconFile = icon?.absolutePath,
        )
    }

    private fun folderSize(folder: File): Long =
        folder.walkTopDown().filter { it.isFile }.sumOf { it.length() }

    /**
     * Importa de uma Uri do seletor do sistema.
     *
     * O `.tmod` é guardado inteiro em `TML/` e extraído em `TML/<Nome>/`; é a
     * pasta extraída que vira o diretório de trabalho. Tudo vai para
     * [ImportLog] — o nome que o Info declara pode não ser o do arquivo, e é o
     * do Info que manda daí em diante.
     */
    fun import(context: Context, uri: Uri): TmlMod {
        ImportLog.add(context, "TML", "iniciando: ${displayName(context, uri)}")
        migrateFlatLayout(context)

        val temp = File(dir(context), INCOMING)
        try {
            temp.delete()
            context.contentResolver.openInputStream(uri)?.use { input ->
                temp.outputStream().use { input.copyTo(it) }
            } ?: throw IllegalStateException("não foi possível abrir o arquivo escolhido")
            if (temp.length() == 0L) throw IllegalStateException("o arquivo veio vazio")

            val info = inspect(temp)
            ImportLog.add(
                context, "TML",
                "recebido: ${temp.length()} byte(s), formato ${info.format}, " +
                    "${info.entries.size} entrada(s)"
            )
            val name = info.entries["name"]?.takeIf { it.isNotBlank() }
                ?: info.containerName?.takeIf { it.isNotBlank() }
                ?: temp.nameWithoutExtension.ifBlank { "mod" }
            val safe = name.replace(Regex("[^A-Za-z0-9._-]"), "_").ifBlank { "mod" }

            val stored = File(modsDir(context), "$safe.tmod")
            if (stored.isFile) stored.delete()
            if (!temp.renameTo(stored)) {
                temp.copyTo(stored, overwrite = true)
                temp.delete()
            }

            val work = File(modsDir(context), safe)
            ImportLog.add(context, "TML", "extraindo $safe.tmod -> ${work.name}/")
            val files = extract(stored, work) { line -> ImportLog.add(context, "TML", line) }

            val mod = read(context, work)
            ImportLog.add(
                context, "TML",
                "ok: ${mod.label} v${mod.version.ifBlank { "?" }}, " +
                    "${mod.author.ifBlank { "autor não declarado" }}, " +
                    "$files arquivo(s), ${mod.resourceCount} recurso(s), dll=${mod.entryDll ?: "nenhuma"}"
            )
            return mod
        } catch (e: Exception) {
            ImportLog.add(context, "ERRO", "tmod ${displayName(context, uri)}: ${e.message ?: e.javaClass.simpleName}")
            throw e
        } finally {
            temp.delete()
        }
    }

    /** O que o arquivo declara sobre si, sem extrair nada. */
    private class PackageInfo(
        val entries: Map<String, String>,
        val containerName: String?,
        val format: String,
    )

    private fun inspect(file: File): PackageInfo = ModPackages.open(file).use { pkg ->
        // `Info` é o nome do container do tModLoader. Um `.tmod` antigo (ou
        // exportado pelo `ContentConverters.Reverse`, que renomeia Info para
        // build.txt) pode trazer só o build.txt — e um zip feito à mão pode
        // ter o Info dentro de uma pasta. Aceita os três, nessa ordem.
        val infoName = pkg.entryNames.firstOrNull { it.equals("Info", true) }
            ?: pkg.entryNames.firstOrNull { it.equals("build.txt", true) }
            ?: pkg.entryNames.firstOrNull {
                it.count { c -> c == '/' } == 1 && it.substringAfterLast('/').equals("Info", true)
            }
            ?: throw IllegalArgumentException(
                "sem Info/build.txt na raiz (tem: ${pkg.entryNames.take(6).joinToString(", ")})"
            )
        val bytes = pkg.read(infoName) ?: throw IllegalArgumentException("$infoName não pôde ser lido")
        PackageInfo(ModInfo.read(bytes), pkg.containerName, pkg.format)
    }

    private fun readInfo(file: File): Map<String, String> = inspect(file).entries

    /**
     * Extrai o pacote em `target`, apagando o que havia antes.
     *
     * Zip slip é barrado comparando o caminho canônico com o da pasta: um
     * `../` numa entrada escreveria fora de `TML/`, que é a única pasta que o
     * app pode garantir.
     */
    private fun extract(zip: File, target: File, note: (String) -> Unit = {}): Int =
        ModPackages.open(zip).use { it.extractTo(target, note) }

    /**
     * `Info` é texto `chave = valor`. As chaves valem em minúsculas — o
     * tModLoader grava `displayName`, mas um mod antigo pode gravar
     * `displayname` e nem por isso deve perder o nome bonito.
     */
    private fun parseInfo(text: String): Map<String, String> = ModInfo.text(text)

    private fun splitRefs(value: String?): List<String> =
        value.orEmpty().split(',', '\n').map { it.trim() }.filter { it.isNotEmpty() }
}

/**
 * O interruptor entre os dois caminhos de carregamento.
 *
 * `enabled.json` é a verdade (a tela lê dele); `mode.txt` existe porque o lado
 * nativo lê texto simples, não JSON: uma lista de nomes, um por linha, e o
 * arquivo só existe quando há pelo menos um `.tmod` ligado.
 */
object TmlFlow {
    const val MARKER = "mode.txt"

    /** Há mod do tModLoader ligado? */
    fun active(context: Context): Boolean = TmlMods.enabled(context).isNotEmpty()

    /** Reescreve o marcador a partir de `enabled.json`; apaga quando fica vazio. */
    fun writeMarker(context: Context) {
        val on = TmlMods.enabled(context)
        runCatching {
            val f = File(TmlMods.dir(context), MARKER)
            if (on.isEmpty()) f.delete() else f.writeText(on.sorted().joinToString("\n") + "\n")
        }
    }
}
