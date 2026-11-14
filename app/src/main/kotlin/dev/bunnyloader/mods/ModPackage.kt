package dev.bunnyloader.mods

import java.io.Closeable
import java.io.File
import java.io.RandomAccessFile
import java.util.zip.Inflater
import java.util.zip.ZipFile

/**
 * O conteúdo de um `.tmod`, aberto — seja qual for o formato do arquivo.
 *
 * Duas gerações convivem no acervo do tModLoader:
 *
 *  - **antigo (até 1.4.3)**: um zip comum, com `Info` na raiz em texto puro;
 *  - **atual (1.4.4+, e todo build 2026.x)**: um container próprio que começa
 *    com a assinatura `TMOD`. Não é zip: tem cabeçalho binário, tabela de
 *    arquivos e os blobs em seguida, cada um comprimido em deflate cru.
 *
 * Ler o segundo com `java.util.zip` dá exatamente o erro que o usuário viu:
 * *"invalid LFH signature"* — o zip procura `PK\x03\x04` no byte zero e acha
 * `TMOD`.
 */
interface ModPackage : Closeable {
    /** "TMOD", "zip". Vai para o diário de import. */
    val format: String

    /** O nome interno do mod, quando o container declara um. */
    val containerName: String?

    val entryNames: List<String>

    /** O conteúdo de uma entrada, já descomprimido; `null` se não existe. */
    fun read(name: String): ByteArray?

    /**
     * Grava todas as entradas em [target], apagando o que havia.
     *
     * [note] recebe observações para o diário de import (por exemplo quantos
     * `.rawimg` viraram PNG).
     */
    fun extractTo(target: File, note: (String) -> Unit = {}): Int
}

object ModPackages {
    /** A assinatura do container do tModLoader, em ASCII. */
    private const val TMOD_MAGIC = "TMOD"

    /** Primeiros 4 bytes são `TMOD`? Decide o leitor sem abrir o arquivo inteiro. */
    fun isTmodContainer(file: File): Boolean = runCatching {
        RandomAccessFile(file, "r").use { raf ->
            if (raf.length() < 4) return@use false
            val head = ByteArray(4)
            raf.readFully(head)
            String(head, Charsets.US_ASCII) == TMOD_MAGIC
        }
    }.getOrDefault(false)

    /**
     * Abre o arquivo no leitor certo.
     *
     * O `Info` do formato novo é binário; o do antigo é texto. Quem chama não
     * precisa saber: [ModInfo.read] decide pela cara do primeiro byte.
     */
    fun open(file: File): ModPackage =
        if (isTmodContainer(file)) TmodPackage.open(file) else ZipPackage.open(file)
}

/**
 * O container `TMOD` do tModLoader.
 *
 * Layout (tudo little-endian, texto no formato `BinaryWriter` = tamanho em
 * 7 bits + UTF-8):
 * ```
 * "TMOD"
 * string  versão do tModLoader que compilou
 * byte[20]  SHA-1 do miolo
 * byte[256] assinatura
 * int32   tamanho do miolo
 *   string  nome interno do mod
 *   string  versão do mod
 *   int32   quantidade de arquivos
 *   por arquivo: string nome, int32 tamanho, int32 tamanho comprimido
 *   os blobs, na ordem da tabela
 * ```
 * Um arquivo está comprimido quando os dois tamanhos diferem (`FileEntry.
 * IsCompressed` no tModLoader) e a compressão é deflate **cru**, sem cabeçalho
 * zlib — daí `Inflater(true)`.
 */
private class TmodPackage private constructor(
    private val raf: RandomAccessFile,
    override val containerName: String?,
    val tmlVersion: String,
    val modVersion: String,
    private val table: List<Entry>,
) : ModPackage {

    class Entry(val name: String, val length: Int, val compressedLength: Int, var offset: Long) {
        val compressed: Boolean get() = length != compressedLength
    }

    override val format: String get() = "TMOD $tmlVersion"

    override val entryNames: List<String> get() = table.map { it.name }

    override fun read(name: String): ByteArray? {
        val entry = table.firstOrNull { it.name == name } ?: return null
        raf.seek(entry.offset)
        val raw = ByteArray(entry.compressedLength)
        raf.readFully(raw)
        return if (!entry.compressed) raw else inflate(raw, entry.length)
    }

    override fun extractTo(target: File, note: (String) -> Unit): Int {
        target.deleteRecursively()
        target.mkdirs()
        val root = target.canonicalPath + File.separator
        // `icon.png` e companhia continuam PNG (keepAsPng do ContentConverters).
        val alreadyPng = table.map { it.name.lowercase() }.filter { it.endsWith(".png") }.toSet()
        var count = 0
        var converted = 0
        for (entry in table) {
            val out = File(target, entry.name)
            if (!out.canonicalPath.startsWith(root)) continue
            if (entry.name.endsWith("/")) {
                out.mkdirs()
                continue
            }
            out.parentFile?.mkdirs()
            val bytes = read(entry.name) ?: continue
            when {
                // O `Info` sai em texto: o lado nativo (e o gerenciador de
                // arquivos) lê `chave = valor`; o binário original fica
                // guardado ao lado, para quem quiser conferir.
                entry.name == "Info" -> {
                    out.writeText(ModInfo.binaryToText(bytes, containerName.orEmpty(), modVersion))
                    File(target, "Info.original").writeBytes(bytes)
                    count++
                }
                // tML 1.4.5: toda textura PNG (menos os ícones) é guardada
                // como `.rawimg`. O loader daqui só procura `.png`, então
                // fazemos a conversão NA EXTRAÇÃO — exatamente o que o
                // `Core/ContentConverters.Reverse` do tML faz ao exportar.
                entry.name.endsWith(".rawimg", ignoreCase = true) -> {
                    val pngName = entry.name.dropLast(7) + ".png"
                    if (alreadyPng.contains(pngName.lowercase())) continue
                    val png = RawImgPng.convert(bytes)
                    if (png == null) {
                        // Formato desconhecido: guarda o original em vez de
                        // perder o arquivo, e segue com os outros.
                        out.writeBytes(bytes)
                        count++
                        continue
                    }
                    val pngFile = File(target, pngName)
                    if (!pngFile.canonicalPath.startsWith(root)) continue
                    pngFile.parentFile?.mkdirs()
                    pngFile.writeBytes(png)
                    count++
                    converted++
                }
                else -> {
                    out.writeBytes(bytes)
                    count++
                }
            }
        }
        if (converted > 0) {
            note("convertido(s) $converted .rawimg -> .png (formato de imagem do tML 1.4.5)")
        }
        return count
    }

    override fun close() = raf.close()

    companion object {
        fun open(file: File): TmodPackage {
            val raf = RandomAccessFile(file, "r")
            try {
                val magic = ByteArray(4)
                raf.readFully(magic)
                require(String(magic, Charsets.US_ASCII) == "TMOD") {
                    "não é um container TMOD"
                }
                val tmlVersion = raf.readDotnetString()
                raf.skipBytes(20 + 256)
                val blobLength = raf.readIntLe()
                if (blobLength < 0 || blobLength > file.length()) {
                    throw IllegalStateException("tamanho do miolo inválido: $blobLength")
                }
                val name = raf.readDotnetString()
                val modVersion = raf.readDotnetString()
                val count = raf.readIntLe()
                if (count < 0 || count > 1_000_000) {
                    throw IllegalStateException("tabela de arquivos inválida: $count entradas")
                }
                val table = ArrayList<Entry>(count)
                var offset = 0L
                repeat(count) {
                    val entryName = raf.readDotnetString().replace('\\', '/')
                    val length = raf.readIntLe()
                    val compressedLength = raf.readIntLe()
                    if (length < 0 || compressedLength < 0) {
                        throw IllegalStateException("entrada $entryName com tamanho negativo")
                    }
                    table += Entry(entryName, length, compressedLength, offset)
                    offset += compressedLength
                }
                // O deslocamento da tabela é relativo ao começo dos blobs.
                val blobStart = raf.filePointer
                table.forEach { it.offset += blobStart }
                return TmodPackage(raf, name, tmlVersion, modVersion, table)
            } catch (e: Exception) {
                raf.close()
                throw e
            }
        }
    }
}

/** O `.tmod` das versões antigas: zip comum, `Info` em texto. */
private class ZipPackage private constructor(
    private val zip: ZipFile,
    private val cleanup: File?,
    override val format: String,
) : ModPackage {

    override val containerName: String? get() = null

    override val entryNames: List<String>
        get() = zip.entries().asSequence().map { it.name }.toList()

    override fun read(name: String): ByteArray? {
        val entry = zip.getEntry(name) ?: return null
        return zip.getInputStream(entry).use { it.readBytes() }
    }

    override fun extractTo(target: File, note: (String) -> Unit): Int {
        target.deleteRecursively()
        target.mkdirs()
        val root = target.canonicalPath + File.separator
        var count = 0
        for (entry in zip.entries()) {
            val out = File(target, entry.name)
            if (!out.canonicalPath.startsWith(root)) continue
            if (entry.isDirectory) {
                out.mkdirs()
                continue
            }
            out.parentFile?.mkdirs()
            zip.getInputStream(entry).use { input -> out.outputStream().use { input.copyTo(it) } }
            count++
        }
        return count
    }

    override fun close() {
        zip.close()
        cleanup?.delete()
    }

    companion object {
        /**
         * Abre o zip, descartando um prefixo quando existe.
         *
         * Alguns `.tmod` que circulam vêm com bytes antes do `PK` (empacotador,
         * download pela metade). O `java.util.zip` exige o cabeçalho no byte
         * zero e recusa o arquivo inteiro; aqui o prefixo é medido, copiado o
         * resto para um temporário e o temporário é apagado ao fechar.
         */
        fun open(file: File): ZipPackage {
            val direct = runCatching { ZipFile(file) }.getOrNull()
            if (direct != null) return ZipPackage(direct, null, "zip")

            val offset = findLocalHeader(file)
            if (offset <= 0) {
                val head = ByteArray(minOf(16L, file.length()).toInt()).also {
                    RandomAccessFile(file, "r").use { raf -> raf.readFully(it) }
                }
                throw IllegalArgumentException(
                    "não é zip nem TMOD (${file.length()} bytes, começa com ${hex(head)})"
                )
            }
            val clean = File(file.parentFile, file.name + ".zip")
            file.inputStream().use { input ->
                var skipped = 0L
                while (skipped < offset) skipped += input.skip(offset - skipped)
                clean.outputStream().use { input.copyTo(it) }
            }
            return ZipPackage(ZipFile(clean), clean, "zip (prefixo de $offset bytes)")
        }

        /** Onde o primeiro cabeçalho local `PK\x03\x04` aparece, ou -1. */
        private fun findLocalHeader(file: File): Long {
            val limit = minOf(file.length(), 1L shl 20).toInt()
            if (limit < 4) return -1
            val head = ByteArray(limit)
            RandomAccessFile(file, "r").use { raf -> raf.readFully(head) }
            for (i in 0..limit - 4) {
                if (head[i] == 0x50.toByte() && head[i + 1] == 0x4B.toByte() &&
                    head[i + 2] == 0x03.toByte() && head[i + 3] == 0x04.toByte()
                )return i.toLong()
            }
            return -1
        }

        private fun hex(bytes: ByteArray): String =
            bytes.joinToString(" ") { "%02x".format(it) }
    }
}

/**
 * O `Info`: as propriedades que o mod declara sobre si.
 *
 * Duas codificações, e é o primeiro byte que diz qual:
 *  - **texto** (até 1.4.3): `chave = valor` por linha;
 *  - **binário** (1.4.4+): sequência de textos do `BinaryWriter` — chave,
 *    valor, chave, valor... até uma string vazia. As chaves de lista
 *    (`modReferences`, `weakReferences`...) trazem os itens e terminam numa
 *    string vazia; as de booleano (`!hideCode`, `noCompile`) vêm sozinhas.
 */
object ModInfo {

    private val listKeys = setOf("dllreferences", "modreferences", "weakreferences", "sortafter", "sortbefore")
    private val byteKeys = setOf("side")
    private val valueKeys = setOf(
        "author", "version", "homepage", "description", "eacpath", "modsource",
        "buildversion", "name", "displayname", "updateurl", "modside",
    )

    /**
     * Chaves que vêm SOZINHAS, sem valor. União das duas linhas do tModLoader:
     * a estável 1.4.4.9 (`Core/BuildProperties.ToBytes`) traz `libraryMod`,
     * que a 1.4.5 de teste não tem; a 1.4.5 traz `displayName.<cultura>`.
     * Conhecer todas evita que uma chave nova desalinhe a leitura do resto.
     */
    private val flagKeys = setOf(
        "nocompile", "!playableonpreview", "translationmod", "librarymod",
        "!hidecode", "!hideresources", "includesource",
    )

    private val allKeys = listKeys + byteKeys + valueKeys + flagKeys

    /** `displayName.en-US`, `displayName.zh-Hans`... também são chaves com valor. */
    private fun isKnownTag(tag: String): Boolean =
        tag in allKeys || tag.startsWith("displayname.")

    /** Todas as chaves em minúsculas: `displayName` e `displayname` valem igual. */
    fun read(bytes: ByteArray): Map<String, String> =
        if (looksBinary(bytes)) binary(bytes) else text(bytes.decodeToString())

    fun text(raw: String): Map<String, String> {
        val out = mutableMapOf<String, String>()
        for (line in raw.lineSequence()) {
            val clean = line.trim().removePrefix("\uFEFF")
            if (clean.isEmpty() || clean.startsWith("#") || clean.startsWith("//")) continue
            val eq = clean.indexOf('=')
            if (eq <= 0) continue
            val key = clean.substring(0, eq).trim().lowercase()
            if (key.isEmpty()) continue
            val value = clean.substring(eq + 1).trim().removeSurrounding("\"")
            out[key] = if (out.containsKey(key)) "${out[key]}, $value" else value
        }
        return out
    }

    /** O `Info` binário, como mapa de texto — o mesmo formato do antigo. */
    fun binary(bytes: ByteArray): Map<String, String> {
        val cursor = Cursor(bytes)
        val out = mutableMapOf<String, String>()
        while (cursor.remaining > 0) {
            val key = cursor.string()
            if (key.isEmpty()) break
            val lower = key.lowercase()
            when {
                lower in listKeys -> {
                    val items = mutableListOf<String>()
                    while (true) {
                        val item = cursor.string()
                        if (item.isEmpty()) break
                        items += item
                    }
                    out[lower] = items.joinToString(", ")
                }
                lower in byteKeys -> {
                    // `side` é um byte cru do enum ModSide, não texto.
                    val value = if (cursor.remaining >= 1) cursor.byte() else 0
                    out[lower] = when (value.toInt()) {
                        1 -> "Client"
                        2 -> "Server"
                        3 -> "NoSync"
                        else -> "Both"
                    }
                }
                lower in valueKeys || lower.startsWith("displayname.") -> {
                    // `displayName.en-US` é o nome por cultura: vale como o
                    // nome, se ainda não houver um.
                    val value = cursor.string()
                    val key = if (lower.startsWith("displayname.")) "displayname" else lower
                    if (out[key].isNullOrBlank() && value.isNotBlank()) out[key] = value
                }
                else -> Unit // booleano: só a chave, sem valor
            }
        }
        return out
    }

    /**
     * O binário de volta para texto, para a pasta extraída ficar legível (o
     * lado nativo lê `chave = valor`) e para o editor do usuário abrir.
     *
     * Valor com quebra de linha vira uma linha só: o formato de texto é uma
     * linha por chave. A descrição longa continua intacta em `description.txt`.
     */
    fun binaryToText(bytes: ByteArray, containerName: String, modVersion: String): String {
        val map = binary(bytes).toMutableMap()
        if (map["name"].isNullOrBlank() && containerName.isNotBlank()) map["name"] = containerName
        if (map["version"].isNullOrBlank() && modVersion.isNotBlank()) map["version"] = modVersion
        val order = listOf(
            "name", "displayname", "author", "version", "side", "description",
            "modreferences", "weakreferences", "dllreferences", "sortafter", "sortbefore",
            "homepage", "modsource", "buildversion",
        )
        val out = StringBuilder()
        for (key in order) {
            val value = map.remove(key) ?: continue
            out.append(key).append(" = ").append(value.replace('\r', ' ').replace('\n', ' ')).append('\n')
        }
        for ((key, value) in map) {
            out.append(key).append(" = ").append(value.replace('\r', ' ').replace('\n', ' ')).append('\n')
        }
        return out.toString()
    }

    /**
     * Binário ou texto? Decide pelo CONTEÚDO do primeiro token, não pelo byte.
     *
     * O primeiro byte de um `Info` binário é o tamanho (7 bits) da primeira
     * chave. Esse tamanho pode ser 0x0A (10 = `libraryMod`, chave que existe
     * na estável 1.4.4.9 mas não na 1.4.5), 0x09 (`noCompile`), 0x23 (35)...
     * Classificar "byte de controle = texto" fazia um mod de biblioteca ser
     * lido como `key = value` e perdia TODAS as propriedades.
     *
     * Então: se o primeiro token decodificado for uma chave conhecida das duas
     * linhas do tModLoader, é binário. Texto (`author = ...`, `displayName = x`)
     * nunca começa com um desses nomes puros.
     */
    private fun looksBinary(bytes: ByteArray): Boolean {
        if (bytes.isEmpty()) return false
        val first = bytes[0].toInt() and 0xFF
        if (first == 0xEF) return false                 // BOM UTF-8 de texto
        if (first in 0x41..0x5A || first in 0x61..0x7A || first == 0x23 || first == 0x20) {
            // Começa com letra/`#`/espaço: texto se a primeira linha tem `=`.
            return bytes.decodeToString().substringBefore('\n').indexOf('=') < 0
        }
        // Tamanho em 7 bits de uma chave curta (< 128): um byte só.
        if (first == 0 || first > 0x7F || first >= bytes.size) return false
        val tag = String(bytes, 1, first, Charsets.UTF_8).lowercase()
        return isKnownTag(tag)
    }

    /** Leitor de `BinaryWriter`: tamanho em 7 bits, depois UTF-8. */
    private class Cursor(val bytes: ByteArray, var pos: Int = 0) {
        val remaining: Int get() = bytes.size - pos

        fun byte(): Byte = bytes[pos++]

        fun string(): String {
            var length = 0
            var shift = 0
            while (true) {
                if (remaining <= 0) return ""
                val b = byte().toInt() and 0xFF
                length = length or ((b and 0x7F) shl shift)
                shift += 7
                if (b and 0x80 == 0) break
                if (shift > 28) return ""
            }
            if (length <= 0 || length > remaining) return ""
            val out = String(bytes, pos, length, Charsets.UTF_8)
            pos += length
            return out
        }
    }
}

private fun RandomAccessFile.readIntLe(): Int = Integer.reverseBytes(readInt())

private fun RandomAccessFile.readDotnetString(): String {
    var length = 0
    var shift = 0
    while (true) {
        val b = readUnsignedByte()
        length = length or ((b and 0x7F) shl shift)
        shift += 7
        if (b and 0x80 == 0) break
        if (shift > 28) throw IllegalStateException("texto sem fim no cabeçalho")
    }
    if (length < 0 || length > 8 * 1024 * 1024) {
        throw IllegalStateException("texto de $length bytes no cabeçalho")
    }
    val buf = ByteArray(length)
    readFully(buf)
    return String(buf, Charsets.UTF_8)
}

private fun inflate(raw: ByteArray, expected: Int): ByteArray {
    val inflater = Inflater(true)
    try {
        inflater.setInput(raw)
        val out = java.io.ByteArrayOutputStream(if (expected > 0) expected else 64)
        val buf = ByteArray(64 * 1024)
        while (!inflater.finished()) {
            val n = inflater.inflate(buf)
            if (n == 0) {
                if (inflater.needsInput() || inflater.needsDictionary()) break
                continue
            }
            out.write(buf, 0, n)
        }
        return out.toByteArray()
    } finally {
        inflater.end()
    }
}
