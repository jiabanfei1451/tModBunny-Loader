import java.net.URI
import java.security.MessageDigest
import java.util.Properties

// Fase 1 nao precisa da libbunny.so. Desligue o build nativo com
//   -Pbl.nativeBuild=false   (ou a mesma linha em gradle.properties)
// para compilar e rodar o launcher sem instalar NDK/CMake.
val nativeBuild = (project.findProperty("bl.nativeBuild") as String?)?.toBoolean() ?: true

// A fonte do launcher e do menu de cheats é a Andy Bold (Monotype), a mesma do
// Terraria. Não vai para o Git: fica em src/main/res/font/bunny.ttf, integrada
// à mão como os binários do jogo. Sem ela, entra a Comic Neue (OFL) de
// src/fontFallback, com o mesmo nome de recurso.
// Os mods C# (runtime Mono + a API em src/main/csharp/). Pede o SDK do .NET
// no PATH (`dotnet`) e baixa o runtime do nuget.org uma vez. Sem eles:
//   -Pbl.csharp=false
// e o APK sai sem os mods C#.
val csharpEnabled = (project.findProperty("bl.csharp") as String?)?.toBoolean() ?: true

// O app de medicao dos hooks C# (ver BENCH_TESTS).
val benchBuild = (project.findProperty("bl.bench") as String?)?.toBoolean() ?: false
val betaBuild = (project.findProperty("bl.beta") as String?)?.toBoolean() ?: false
// A convivencia: com.bunnyloader.side instala ao lado do Bunny Loader normal,
// para comparar duas versoes no mesmo aparelho sem desinstalar nenhuma.
// Fica LIGADA por padrao: o build local e sempre um teste ao lado do APK que
// ja esta no aparelho. Para o pacote de distribuicao, -Pbl.side=false.
val sideBuild = (project.findProperty("bl.side") as String?)?.toBoolean() ?: true

val hasGameFont = file("src/main/res/font/bunny.ttf").isFile
val fontDir = file(if (hasGameFont) "src/main/res/font" else "src/fontFallback/res/font")

// A chave do APK de release fica fora do Git: keystore.properties na raiz
// (storeFile, storePassword, keyAlias, keyPassword; modelo em
// keystore.properties.example) ou as variáveis BL_RELEASE_STORE_FILE,
// BL_RELEASE_STORE_PASSWORD, BL_RELEASE_KEY_ALIAS e BL_RELEASE_KEY_PASSWORD.
// Sem nenhuma, o assembleRelease sai sem assinatura, como antes; com só parte
// delas, o build para em vez de soltar um APK que não instala.
val releaseKeys: Map<String, String>? = run {
    val props = Properties()
    val file = rootProject.file("keystore.properties")
    if (file.isFile) file.inputStream().use { props.load(it) }
    val keys = mapOf(
        "storeFile" to "BL_RELEASE_STORE_FILE",
        "storePassword" to "BL_RELEASE_STORE_PASSWORD",
        "keyAlias" to "BL_RELEASE_KEY_ALIAS",
        "keyPassword" to "BL_RELEASE_KEY_PASSWORD",
    ).mapValues { (name, env) -> props.getProperty(name) ?: System.getenv(env) }
    val missing = keys.filterValues { it.isNullOrBlank() }.keys
    when {
        missing.size == keys.size -> null
        missing.isNotEmpty() -> throw GradleException(
            "assinatura de release incompleta: falta ${missing.joinToString()} " +
                "(keystore.properties ou BL_RELEASE_*)")
        else -> keys.mapValues { it.value!! }
    }
}

// O Android só atualiza no lugar um APK com versionCode maior ou igual e a
// mesma assinatura; alguns instaladores (One UI, HyperOS) recusam o igual. O
// número de commits sobe a cada versão sem ninguém lembrar de mexer nele. Sem
// git (fonte baixada em zip), fica 1.
val commitCount: Int = runCatching {
    providers.exec {
        commandLine("git", "rev-list", "--count", "HEAD")
        workingDir = rootDir
    }.standardOutput.asText.get().trim().toInt()
}.getOrDefault(1)

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

android {
    namespace = "dev.bunnyloader"
    compileSdk = 34
    // NDK 26.1 veio incompleto no SDK Manager (sem source.properties); fixa a
    // versao integra instalada.
    ndkVersion = "30.0.15729638"

    defaultConfig {
        applicationId = "com.bunnyloader"
        minSdk = 26
        targetSdk = 34
        // Sem .git (fonte em zip) o commitCount fica 1; alguns instaladores
        // recusam atualizar no lugar com versionCode igual. -Pbl.versionCode=N
        // permite subir entre iteracoes locais sem editar este arquivo.
        versionCode = (project.findProperty("bl.versionCode") as String?)?.toInt() ?: commitCount
        versionName = "0.5.2-beta"

        // -Pbl.bench=true: o app de medicao (com.bunnyloader.bench), ao lado
        // do Bunny Loader de verdade. Traz os testes de BENCH_TESTS ligados e
        // nenhum outro mod; o resultado sai no relatorio de Diagnostico.
        if (benchBuild) {
            applicationIdSuffix = ".bench"
            // Cada APK de medicao instala por cima do anterior (a One UI recusa
            // versionCode igual) e o personagem e o mundo criados la ficam.
            versionCode = (System.currentTimeMillis() / 60_000).toInt()
        }
        if (betaBuild) applicationIdSuffix = ".beta"
        // A convivencia vem por ultimo de proposito: se alguem ligar duas
        // flags juntas, o sufixo que vale e este (e o applicationId tem de
        // ser unico entre os APKs instalados).
      if (sideBuild) {
        applicationIdSuffix = ".side"
        // Instalador nenhum recusa: cada build e um upgrade do anterior.
        versionCode = (System.currentTimeMillis() / 60_000).toInt()
        // A linha "TmodBunny Loader" (o app que roda em paralelo do Bunny
        // Loader, com o TModLoader embutido) comeca a contagem de versao em
        // 0.1.0: e um produto novo, nao uma continuacao de 0.5.x. O
        // applicationId NAO muda (com.bunnyloader.side) — e o mesmo app, so
        // com outro nome.
        versionName = "0.1.0"
      }
        // -Pbl.buildTag=tml2: uma etiqueta curta no versionName. Sem ela, duas
        // iteracoes do mesmo dia chegam no relatorio com o MESMO nome e nao da
        // para saber qual build gerou aquele log.
        val buildTag = (project.findProperty("bl.buildTag") as String?)?.trim().orEmpty()
        if (buildTag.isNotEmpty()) versionName = "$versionName-$buildTag"
      manifestPlaceholders["appLabel"] = when {
        benchBuild -> "Bunny Loader Bench"
        betaBuild -> "Bunny Loader Beta"
        sideBuild -> "TmodBunny Loader"
        else -> "@string/app_name"
      }
        // 合并上游 0.5.4：AndroidManifest 里 EditorBridge 的权限名走占位符，
        // 上游在 play 变体下换成另一个包名。缺这个占位符清单合并直接失败。
        manifestPlaceholders["editorPermission"] = "com.bunnyloader.permission.EDITOR"
        buildConfigField("boolean", "BENCH", benchBuild.toString())

        // Só arm64. O runtime do jogo é integrado e existe apenas para
        // arm64-v8a; permitir armeabi-v7a instalaria o Bunny Loader em
        // aparelhos onde o jogo nunca poderia subir.
        ndk {
            abiFilters.clear()
            abiFilters += "arm64-v8a"
        }
        if (nativeBuild) {
            externalNativeBuild {
                cmake {
                    cppFlags += "-std=c++20"
                }
            }
        }
    }

    if (nativeBuild) {
        externalNativeBuild {
            cmake {
                path = file("src/main/cpp/CMakeLists.txt")
                version = "4.1.2"
            }
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true // VERSION_NAME no BootLog e nas Configurações
        prefab = true // necessário para consumir o ShadowHook via prefab
    }

    // Gradle 8.11 valida dependencias implicitas como erro: o
    // generateReleaseLintVitalReportModel le build/generated/blAssets (o
    // source set de assets) sem declarar dependencia dos sync* que o geram.
    // O lint vital so roda checagem estatica no release; desligar nao muda
    // o APK. Mantido o lint comum para debug.
    lint {
        checkReleaseBuilds = false
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }

    sourceSets["main"].java.srcDir("src/main/kotlin")
    if (!hasGameFont) sourceSets["main"].res.srcDir("src/fontFallback/res")

    // Os assets do jogo vão no APK, sempre.
    //
    // Eles já moraram num asset pack install-time, para caber no teto de
    // 200 MB da Play. Não funciona: a Unity 2021.3 deste build localiza
    // bin/Data abrindo o APK BASE pelo sourceDir e lendo o zip por mmap — é o
    // mesmo motivo de os assets precisarem de noCompress. Num split de asset
    // pack ela não olha, e o jogo morre em "Unable to initialize the Unity
    // Engine". Provado com bundletool: o pack instalou como
    // split_terraria1456_assets.apk e a Unity não achou nada.
    //
    // Como a distribuição é APK pelo GitHub, o teto da Play não se aplica e o
    // caminho do bundle não tem para que existir.
    //
    // -Pbl.uiOnly=true deixa os assets de fora (ver `packaging`): é o modo de
    // iterar em tela, onde o jogo nem sobe.
    if ((project.findProperty("bl.uiOnly") as String?)?.toBoolean() != true) {
        sourceSets["main"].assets.srcDir("../terraria1456_assets/src/main/assets")
    }

    // Os mods de samples/ em BUNDLED_SAMPLES viram o catálogo embutido do launcher. Uma cópia, não
    // uma segunda fonte: samples/ continua sendo a verdade, e o que o app lista
    // é exatamente o que o motor carrega.
    sourceSets["main"].assets.srcDir(layout.buildDirectory.dir("generated/blAssets"))
    // A libmonosgen e as libs nativas da BCL, tiradas do pacote do NuGet (ver
    // fetchMonoRuntime, lá embaixo).
    if (csharpEnabled) sourceSets["main"].jniLibs.srcDir(layout.buildDirectory.dir("generated/monoJniLibs"))

    packaging {
        jniLibs {
            useLegacyPackaging = true // extractNativeLibs=true

            // A outra metade do -Pbl.uiOnly=true: aqui saem as libs do jogo
            // (os assets saem lá em cima, não entrando no source set). Juntos
            // são ~225 MB de um APK de ~236 MB e nunca mudam enquanto se mexe
            // em tela — zipar e instalar isso a cada ajuste de layout é o que
            // fazia o ciclo levar quase um minuto.
            //
            // O launcher funciona inteiro assim; só o JOGAR não sobe. Para
            // voltar a jogar, refaça sem a flag.
            if ((project.findProperty("bl.uiOnly") as String?)?.toBoolean() == true) {
                excludes += listOf(
                    "**/libil2cpp.so", "**/libunity.so",
                    "**/libmain.so", "**/libc++_shared.so",
                )
            }
        }
    }

    androidResources {
        // A Unity abre data.unity3d e resources.resource por mmap. Comprimidos,
        // ela tem de inflar ~150 MB a cada boot — o APK do próprio jogo guarda
        // esses dois STORED justamente por isso. É a mesma lista que o Gradle
        // exportado pela Unity usa.
        noCompress += listOf(
            ".unity3d", ".ress", ".resource", ".obb", ".bundle", ".unityexp",
            // PNG ja e comprimido: deflatar de novo nao ganha byte e faz o
            // menu pagar uma inflada por miniatura aberta. Sao ~6800 arquivos.
            ".png",
        )
    }

    signingConfigs {
        if (releaseKeys != null) {
            create("release") {
                storeFile = rootProject.file(releaseKeys.getValue("storeFile"))
                storePassword = releaseKeys.getValue("storePassword")
                keyAlias = releaseKeys.getValue("keyAlias")
                keyPassword = releaseKeys.getValue("keyPassword")
            }
        }
    }

    buildTypes {
        // Com a chave oficial configurada, o debug sai com ela também: é o APK
        // que vai para out/ e para o Release. Sem ela, a chave de debug do PC
        // (o APK não atualiza por cima do oficial).
        debug {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            signingConfig = signingConfigs.findByName("release")
        }
    }

}

/** Os mods de samples/ que vêm dentro do APK. */
val BUNDLED_SAMPLES = listOf("ExampleModCSharp")

/**
 * samples/<Mod>/ -> assets/mods/<Mod>/, antes do merge de assets.
 *
 * Só o Example Mod C# vai no APK.
 */
/**
 * Os testes que o app de medicao (-Pbl.bench=true) traz embutidos e liga:
 * a medicao e o teste dos hooks C#. So o manifesto e content/.
 */
val BENCH_TESTS = listOf("benchcs", "hookcs", "compilebench")

/** Compila os .dll dos testes C# de BENCH_TESTS (tools/tests/<teste>/content/). */
val buildBenchTests by tasks.registering {
    enabled = benchBuild
    dependsOn("buildBunnyLoaderCs")
    doLast {
        for (test in listOf("benchcs", "hookcs", "compilebench")) {
            val csproj = rootProject.file("tools/tests/$test/src").listFiles { f -> f.extension == "csproj" }!!.single()
            project.exec { commandLine("dotnet", "build", csproj.absolutePath, "-c", "Release", "-nologo", "-v", "q") }
        }
    }
}

val syncSampleMods by tasks.registering(Sync::class) {
    from(rootProject.file("samples")) {
        include(BUNDLED_SAMPLES.map { "$it/**" })
        exclude("**/bin/**", "**/obj/**")
    }
    if (benchBuild) {
        dependsOn(buildBenchTests)
        from(rootProject.file("tools/tests")) {
            include(BENCH_TESTS.flatMap { listOf("$it/manifest.json", "$it/content/**") })
        }
    }
    into(layout.buildDirectory.dir("generated/blAssets/mods"))
}

/**
 * _sprites/{item,npc}/<id>.png -> assets/sprites/, para o menu de cheats.
 *
 * Vem de fora porque nao da para tirar de dentro: o atlas do jogo vive so na
 * GPU (medido — isReadable = 0, e Blit/ReadPixels/GetNativeTexturePtr foram
 * removidos deste binario). Sao 3,9 MB num APK de 184.
 */
val syncSprites by tasks.registering(Sync::class) {
    from(rootProject.file("_sprites")) { exclude("README.md") }
    into(layout.buildDirectory.dir("generated/blAssets/sprites"))
}
/**
 * res/font/bunny.ttf -> assets/fonte/, para o menu de cheats.
 *
 * O menu roda de um dex carregado em memoria, sem a classe R do app, entao nao
 * alcanca res/font — e `Resources.getFont` so existe da API 26 para cima,
 * enquanto o app vai ate a 24. Pelos assets funciona em todas.
 */
val syncFonte by tasks.registering(Sync::class) {
    from(fontDir)
    into(layout.buildDirectory.dir("generated/blAssets/fonte"))
}
/**
 * LICENSE.md e THIRD_PARTY_NOTICES.md -> assets/legal/, para a tela de licenças
 * (Config > Sobre). As licenças MIT e BSD das bibliotecas pedem que o aviso vá
 * junto com o binário; a raiz continua sendo o único lugar de editar.
 */
val syncLegal by tasks.registering(Sync::class) {
    from(rootProject.file("LICENSE.md"), rootProject.file("THIRD_PARTY_NOTICES.md"))
    into(layout.buildDirectory.dir("generated/blAssets/legal"))
}
// --- Runtime .NET dos mods C# ---
//
// O Mono oficial para Android (o mesmo do .NET for Android), do pacote
// Microsoft.NETCore.App.Runtime.Mono.android-arm64. A versão e o SHA-256 andam
// juntos: trocar uma sem a outra para o build.
val monoVersion = "10.0.14"
val monoPackSha256 = "4c33596a1d084cae705933632528834cbcd07526a54ae30d5bf96f556c945204"
val monoPack = rootProject.layout.buildDirectory.file("mono/$monoVersion/pack.nupkg")
/** As libs nativas que vão para o nativeLibraryDir. */
val monoNativeLibs = listOf(
    "libmonosgen-2.0.so", "libSystem.Native.so", "libSystem.Globalization.Native.so",
    "libmono-component-marshal-ilgen.so",
    // A criptografia do .NET no Android (SHA-1, SHA-256...) passa pelo Java:
    // esta lib e o .jar com as classes que ela procura (monoCryptoJar). O
    // compilador dos mods a usa: o selo do build e o SHA-1 de cada referência.
    "libSystem.Security.Cryptography.Native.Android.so",
    "libSystem.IO.Compression.Native.so",
)
val monoCryptoJar = layout.buildDirectory.file("generated/monoCrypto/libSystem.Security.Cryptography.Native.Android.jar")
/**
 * Os assemblies da BCL que vão no APK (assets/dotnet/, extraídos pelo
 * DotnetRuntime.kt). Só o que a API e um mod comum usam; o pacote tem 19 MB
 * deles.
 */
val monoManagedLibs = listOf(
    "System.Runtime", "System.Runtime.Extensions", "System.Runtime.InteropServices", "System.Runtime.Loader",
    "System.Runtime.Intrinsics", "System.Collections", "System.Collections.Concurrent", "System.Linq",
    "System.Console", "System.Memory", "System.Threading", "System.Threading.Thread", "System.Private.Uri",
    "System.Text.Encoding.Extensions", "System.Numerics.Vectors", "System.ObjectModel",
    // As traducoes dos mods (content/Localization/*.json).
    "System.Text.Json", "System.Text.Encodings.Web", "System.IO.Pipelines",
    "System.ComponentModel.TypeConverter",
    // Mods que usam FileSystemWatcher (ex.: CalamityMod.Utilities.
    // MainThreadedFileSystemWatcher) referenciam este assembly. Sem ele no
    // TPA o Mono aborta em mini_instantiate_gshared_info: ao instanciar o
    // tipo generico compartilhado ele precisa carregar a classe do ELEMENTO
    // do array (FileSystemWatcher) e a assert "is_ok(error)" mata o processo.
    "System.IO.FileSystem", "System.IO.FileSystem.Watcher",
    "System.IO.FileSystem.Primitives", "System.IO.FileSystem.AccessControl",
    // O Cecil (IlPatcher) e compilado contra netstandard2.0: no Mono
    // embutido o facade `netstandard.dll` tem de estar no TPA, senao o
    // patch falha ("Could not load netstandard") e o mod carrega SEM
    // reescrita de IL.
    "netstandard",
)
/**
 * O que o compilador dos mods com fontes (BunnyLoader.Compiler + Roslyn) puxa
 * além dos de cima: o fecho das referências do Microsoft.CodeAnalysis*.dll no
 * pacote Mono (~10 MB).
 */
val monoCompilerLibs = listOf(
    "Microsoft.Win32.Primitives", "System.Collections.Immutable", "System.Collections.NonGeneric",
    "System.Collections.Specialized", "System.ComponentModel", "System.ComponentModel.Primitives",
    "System.Diagnostics.DiagnosticSource", "System.Diagnostics.StackTrace", "System.Diagnostics.TraceSource",
    "System.Diagnostics.Tracing", "System.Formats.Asn1", "System.IO.Compression", "System.IO.Compression.Brotli",
    "System.IO.MemoryMappedFiles", "System.Linq.Expressions", "System.Net.Http", "System.Net.NameResolution",
    "System.Net.NetworkInformation", "System.Net.Primitives", "System.Net.Quic", "System.Net.Security",
    "System.Net.Sockets", "System.Private.Xml", "System.Private.Xml.Linq", "System.Reflection.Emit",
    "System.Reflection.Emit.ILGeneration", "System.Reflection.Emit.Lightweight", "System.Reflection.Metadata",
    "System.Reflection.Primitives", "System.Runtime.Numerics", "System.Runtime.Serialization.Primitives",
    "System.Security.Claims", "System.Security.Cryptography", "System.Text.Encoding.CodePages",
    "System.Text.RegularExpressions", "System.Threading.Channels", "System.Threading.Tasks.Parallel",
    "System.Threading.ThreadPool", "System.Xml.ReaderWriter", "System.Xml.XDocument", "System.Xml.XPath.XDocument",
)
/** A BCL que vai no APK. */
val monoShippedLibs = (monoManagedLibs + monoCompilerLibs).distinct()
/** O compilador e o Roslyn, que o build da API copia para csharp/BunnyLoader. */
val compilerAssemblies = listOf("BunnyLoader.Compiler", "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp")

// Mono.Cecil: o IlPatcher reescreve o IL dos mods PC ANTES de carrega-los.
// Sem o Cecil no diretorio de assemblies o patch falha em silencio (fallback
// sem reescrita) e todo .tmod do PC morre no primeiro `ldfld`.
val ilPatcherAssemblies = listOf("Mono.Cecil", "Mono.Cecil.Rocks", "Mono.Cecil.Pdb", "Mono.Cecil.Mdb")

fun sha256(file: File): String =
    MessageDigest.getInstance("SHA-256").digest(file.readBytes()).joinToString("") { "%02x".format(it) }

val fetchMonoRuntime by tasks.registering {
    inputs.property("sha256", monoPackSha256)
    outputs.file(monoPack)
    doLast {
        val pack = monoPack.get().asFile
        if (pack.isFile && sha256(pack) == monoPackSha256) return@doLast
        pack.parentFile.mkdirs()
        val id = "microsoft.netcore.app.runtime.mono.android-arm64"
        val url = "https://api.nuget.org/v3-flatcontainer/$id/$monoVersion/$id.$monoVersion.nupkg"
        URI(url).toURL().openStream().use { input -> pack.outputStream().use { input.copyTo(it) } }
        val got = sha256(pack)
        if (got != monoPackSha256) {
            pack.delete()
            throw GradleException("runtime Mono $monoVersion: SHA-256 $got, esperado $monoPackSha256")
        }
    }
}

val syncMonoJniLibs by tasks.registering(Sync::class) {
    dependsOn(fetchMonoRuntime)
    // O include vem de um zipTree preguiçoso: sem isto, trocar a lista não
    // refazia a cópia.
    inputs.property("libs", monoNativeLibs)
    from(monoPack.map { zipTree(it) }) {
        include(monoNativeLibs.map { "runtimes/android-arm64/native/$it" })
        eachFile { path = name }
    }
    includeEmptyDirs = false
    into(layout.buildDirectory.dir("generated/monoJniLibs/arm64-v8a"))
}

val syncMonoCryptoJar by tasks.registering(Sync::class) {
    dependsOn(fetchMonoRuntime)
    from(monoPack.map { zipTree(it) }) {
        include("runtimes/android-arm64/native/libSystem.Security.Cryptography.Native.Android.jar")
        eachFile { path = name }
    }
    includeEmptyDirs = false
    into(monoCryptoJar.map { it.asFile.parentFile })
}

/**
 * A API C# (src/main/csharp/BunnyLoader), compilada pelo SDK do .NET. O
 * proprio build dela roda o ProxyGen, que gera as proxies do jogo a partir de
 * refs/DummyDll (o dump local, ver refs/README.md).
 */
val buildBunnyLoaderCs by tasks.registering(Exec::class) {
    val source = file("src/main/csharp/BunnyLoader")
    val out = layout.buildDirectory.dir("csharp/BunnyLoader")
    inputs.files(fileTree(source) { exclude("bin/**", "obj/**") })
    inputs.files(fileTree(file("src/main/csharp/ProxyGen")) { exclude("bin/**", "obj/**") })
    inputs.files(fileTree(file("src/main/csharp/BunnyLoader.Compiler")) { exclude("bin/**", "obj/**") })
    inputs.files(rootProject.file("refs/DummyDll/Assembly-CSharp.dll"))
    outputs.dir(out)
    commandLine(
        "dotnet", "build", File(source, "BunnyLoader.csproj").absolutePath,
        "-c", "Release", "-nologo", "-v", "q", "-o", out.get().asFile.absolutePath,
    )
}

/**
 * `tModLoader.dll` de fachada.
 *
 * Um mod compilado pelo tModLoader 1.4.5 referencia o assembly do JOGO, que
 * se chama `tModLoader` (versão 1.4.4.9) — ver
 * `patches/tModLoader/Terraria/release_extras/tMLMod.targets`. Aqui só existe
 * `BunnyLoader.dll`, e devolver o shim por um evento de resolução é recusado
 * pelo runtime: `AssemblyLoadContext.ValidateAssemblyNameWithSimpleName`
 * exige que o nome simples bata (o tML 1.4.5 contorna isso com um Hook em
 * `Core/AssemblyManager.cs`, que não temos).
 *
 * Então geramos um assembly que realmente se chama `tModLoader`, com
 * `TypeForwardedTo` para todos os tipos públicos do shim. A identidade dos
 * tipos continua uma só, e o Mono acha o arquivo pelo nome, sem gambiarra.
 */
val generatedForwarders = layout.buildDirectory.file("generated/tmlFacade/Forwarders.g.cs")

val generateTmlForwarders by tasks.registering(Exec::class) {
    dependsOn(buildBunnyLoaderCs)
    val tool = file("src/main/csharp/ForwarderGen/ForwarderGen.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    inputs.files(tool, file("src/main/csharp/ForwarderGen/Program.cs"), shim)
    outputs.file(generatedForwarders)
    commandLine(
        "dotnet", "run", "--project", tool.absolutePath, "-c", "Release", "--",
        shim.get().asFile.absolutePath, generatedForwarders.get().asFile.absolutePath,
    )
}

val buildTmlFacade by tasks.registering(Exec::class) {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val project = file("src/main/csharp/TmlFacade/TmlFacade.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(project, generatedForwarders, shim)
    outputs.file(layout.buildDirectory.file("csharp/tmlFacade/tModLoader.dll"))
    commandLine(
        "dotnet", "build", project.absolutePath, "-c", "Release", "-nologo", "-v", "q",
        "-p:ShimPath=${shim.get().asFile.absolutePath}",
        "-p:GeneratedForwarders=${generatedForwarders.get().asFile.absolutePath}",
        "-o", out.get().asFile.absolutePath,
    )
}

// Fachadas `FNA.dll` e `ReLogic.dll`: mesmo mecanismo — mods PC referenciam
// essas Libraries do tML 1.4.5 e o runtime recusa devolver o shim com outro
// nome simples.
val buildFnaFacade by tasks.registering(Exec::class) {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val project = file("src/main/csharp/FnaFacade/FnaFacade.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(project, generatedForwarders, shim)
    outputs.file(layout.buildDirectory.file("csharp/tmlFacade/FNA.dll"))
    commandLine(
        "dotnet", "build", project.absolutePath, "-c", "Release", "-nologo", "-v", "q",
        "-p:ShimPath=${shim.get().asFile.absolutePath}",
        "-p:GeneratedForwarders=${generatedForwarders.get().asFile.absolutePath}",
        "-o", out.get().asFile.absolutePath,
    )
}

// Fachada `TerrariaHooks.dll`: os tipos `Terraria.On_*` (HookGen) que o mod
// do PC referencia vivem no shim, mas o AssemblyRef pede um assembly chamado
// TerrariaHooks — sem esta fachada o mod nem carrega os tipos.
val buildTerrariaHooksFacade by tasks.registering(Exec::class) {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val project = file("src/main/csharp/TerrariaHooksFacade/TerrariaHooksFacade.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(project, generatedForwarders, shim)
    outputs.file(layout.buildDirectory.file("csharp/tmlFacade/TerrariaHooks.dll"))
    commandLine(
        "dotnet", "build", project.absolutePath, "-c", "Release", "-nologo", "-v", "q",
        "-p:ShimPath=${shim.get().asFile.absolutePath}",
        "-p:GeneratedForwarders=${generatedForwarders.get().asFile.absolutePath}",
        "-o", out.get().asFile.absolutePath,
    )
}

val buildReLogicFacade by tasks.registering(Exec::class) {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val project = file("src/main/csharp/ReLogicFacade/ReLogicFacade.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(project, generatedForwarders, shim)
    outputs.file(layout.buildDirectory.file("csharp/tmlFacade/ReLogic.dll"))
    commandLine(
        "dotnet", "build", project.absolutePath, "-c", "Release", "-nologo", "-v", "q",
        "-p:ShimPath=${shim.get().asFile.absolutePath}",
        "-p:GeneratedForwarders=${generatedForwarders.get().asFile.absolutePath}",
        "-o", out.get().asFile.absolutePath,
    )
}

// Fachada `log4net.dll`: `Mod.Logger` é `log4net.ILog` no tML, então mods que
// logam trazem um AssemblyRef para log4net 2.0.8. Sem a fachada o resolver
// devolvia o shim, o Mono recusava pelo nome e o mod estourava com
// FileNotFoundException na primeira linha de log.
val buildLog4netFacade by tasks.registering(Exec::class) {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val project = file("src/main/csharp/Log4netFacade/Log4netFacade.csproj")
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(project, generatedForwarders, shim)
    outputs.file(layout.buildDirectory.file("csharp/tmlFacade/log4net.dll"))
    commandLine(
        "dotnet", "build", project.absolutePath, "-c", "Release", "-nologo", "-v", "q",
        "-p:ShimPath=${shim.get().asFile.absolutePath}",
        "-p:GeneratedForwarders=${generatedForwarders.get().asFile.absolutePath}",
        "-o", out.get().asFile.absolutePath,
    )
}

/**
 * O references.txt: contra o que um mod com fontes compila (a base do .NET
 * que vai no APK e a API). O mesmo no app, no catálogo e no PC.
 */
val writeDotnetReferences by tasks.registering {
    val out = layout.buildDirectory.file("generated/dotnetReferences/references.txt")
    val names = listOf("System.Private.CoreLib.dll") + monoShippedLibs.map { "$it.dll" } + "BunnyLoader.dll"
    inputs.property("names", names)
    outputs.file(out)
    doLast {
        out.get().asFile.apply { parentFile.mkdirs() }.writeText(
            "# Gerado pelo app/build.gradle.kts: as referências dos mods C# com fontes.\n" +
                names.joinToString("\n", postfix = "\n"))
    }
}

// Fachadas `MonoMod.Utils.dll` e `MonoMod.RuntimeDetour.dll`: mods que fazem
// patch de IL (Luminance, FargowiltasSouls) referenciam o MonoMod que o tML
// 1.4.5 traz em `Libraries/`. Os tipos estão no shim (Mono.Cecil por baixo),
// mas o AssemblyRef pede esses nomes — mesma razão das fachadas acima.
val monoModFacades = listOf(
    "MonoMod.Utils" to "MonoModUtilsFacade",
    "MonoMod.RuntimeDetour" to "MonoModRuntimeDetourFacade",
)

val buildMonoModFacades by tasks.registering {
    dependsOn(generateTmlForwarders, buildBunnyLoaderCs)
    val shim = layout.buildDirectory.file("csharp/BunnyLoader/BunnyLoader.dll")
    val forwarded = generatedForwarders
    val out = layout.buildDirectory.dir("csharp/tmlFacade")
    inputs.files(forwarded, shim)
    outputs.files(monoModFacades.map { layout.buildDirectory.file("csharp/tmlFacade/${it.first}.dll").get().asFile })
    doLast {
        monoModFacades.forEach { (name, project) ->
            providers.exec {
                commandLine(
                    "dotnet", "build", file("src/main/csharp/$project/$project.csproj").absolutePath,
                    "-c", "Release", "-nologo", "-v", "q",
                    "-p:ShimPath=${shim.get().asFile.absolutePath}",
                    "-p:GeneratedForwarders=${forwarded.get().asFile.absolutePath}",
                    "-o", out.get().asFile.absolutePath,
                )
            }.result.get()
            check(file("${out.get().asFile}/${name}.dll").isFile) { "fachada $name não foi gerada" }
        }
    }
}

val syncDotnetAssets by tasks.registering(Sync::class) {
    dependsOn(fetchMonoRuntime, buildBunnyLoaderCs, buildTmlFacade, buildFnaFacade, buildReLogicFacade, buildTerrariaHooksFacade, buildLog4netFacade, buildMonoModFacades, writeDotnetReferences)
    inputs.property("libs", monoShippedLibs)
    from(monoPack.map { zipTree(it) }) {
        include("runtimes/android-arm64/native/System.Private.CoreLib.dll")
        include(monoShippedLibs.map { "runtimes/android-arm64/lib/net10.0/$it.dll" })
        // O app de medicao leva a biblioteca inteira: o compilador do C#
        // (tools/tests/compilebench) puxa dezenas delas.
        if (benchBuild) include("runtimes/android-arm64/lib/net10.0/*.dll")
        include("THIRD-PARTY-NOTICES.TXT")
        eachFile { path = name }
    }
    from(layout.buildDirectory.dir("csharp/BunnyLoader")) {
        include("BunnyLoader.dll")
        include(compilerAssemblies.map { "$it.dll" })
        include(ilPatcherAssemblies.map { "$it.dll" })
    }
    // O assembly de fachada precisa estar NO MESMO diretório do runtime:
    // `mono_set_assemblies_path` e TRUSTED_PLATFORM_ASSEMBLIES apontam para
    // ele, e é por nome de arquivo que o Mono resolve o AssemblyRef do mod.
    from(layout.buildDirectory.dir("csharp/tmlFacade")) {
        include("tModLoader.dll")
        include("FNA.dll")
        include("ReLogic.dll")
        include("TerrariaHooks.dll")
        include("log4net.dll")
        include("MonoMod.Utils.dll")
        include("MonoMod.RuntimeDetour.dll")
    }
    from(writeDotnetReferences)
    includeEmptyDirs = false
    into(layout.buildDirectory.dir("generated/blAssets/dotnet"))
}

tasks.matching { it.name.startsWith("merge") && it.name.endsWith("Assets") }
    .configureEach {
        dependsOn(syncSampleMods, syncSprites, syncFonte, syncLegal)
        if (csharpEnabled) dependsOn(syncDotnetAssets)
    }
if (csharpEnabled) {
    tasks.matching { it.name.startsWith("merge") && it.name.endsWith("JniLibFolders") }
        .configureEach { dependsOn(syncMonoJniLibs) }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.02"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.6")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")

    // Unity Android Player 2021.3.56f2 (il2cpp/Release) — hospedamos o jogo no
    // nosso processo e precisamos de uma UnityPlayer LIMPA; a do APK do Terraria
    // vem com as strings cifradas pelo PairIP. Ver app/libs/README.md.
    // O jar é da Unity e fica fora do Git. Sem ele o app compila (a UnityPlayer
    // é carregada por reflection), mas o jogo não sobe, como sem os .so do jogo.
    val unityClasses = file("libs/unity-classes.jar")
    if (unityClasses.isFile) implementation(files(unityClasses))

    if (csharpEnabled) implementation(files(monoCryptoJar).builtBy(syncMonoCryptoJar))

    // Hooking nativo (ARM32/ARM64). Consumido via prefab.
    // Confirme a versão mais recente em Maven Central.
    if (nativeBuild) {
        implementation("com.bytedance.android:shadowhook:1.0.10")
    }

    debugImplementation("androidx.compose.ui:ui-tooling")
    testImplementation("junit:junit:4.13.2")
}
