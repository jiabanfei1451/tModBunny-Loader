#include "mods/ModLoader.h"
#include "mods/LoadOrder.h"
#include "mods/ManifestText.h"
#include "core/Log.h"
#include "content/textures/FontPacks.h"
#include "content/textures/TexturePacks.h"
#include "script/dotnet/DotnetHost.h"
#include <algorithm>
#include <cctype>
#include <cstdio>
#include <map>

#include <dirent.h>
#include <sys/stat.h>

namespace bl::mods {

namespace {
std::vector<LoadedMod>& registry() {
    static std::vector<LoadedMod> list;
    return list;
}

std::string& currentDirSlot() {
    static std::string current;
    return current;
}

std::string& currentIdSlot() {
    static std::string current;
    return current;
}

// id do mod -> pasta do entry. O id e tambem o nome do MODULO no QuickJS, e e
// assim que bl.loadTexture descobre de qual mod veio a chamada mesmo depois da
// carga, la dentro de um hook.
std::map<std::string, std::string>& dirsById() {
    static std::map<std::string, std::string> m;
    return m;
}

bool fileExists(const std::string& path) {
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    std::fclose(f);
    return true;
}

// ---------------------------------------------------------------------------
// Fluxo TML
//
// Um mod do tModLoader nao tem `manifest.json`: o que identifica ele e o
// arquivo `Info`, que veio junto do `.tmod`, no formato `chave = valor` — uma
// por linha, nao JSON. Por isso o parser do manifesto nao serve aqui.
// ---------------------------------------------------------------------------

std::string trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && (s[a] == ' ' || s[a] == '\t' || s[a] == '\r' || s[a] == '\n')) ++a;
    while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t' || s[b - 1] == '\r' || s[b - 1] == '\n')) --b;
    return s.substr(a, b - a);
}

std::string lowercased(std::string s) {
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

bool readWholeFile(const std::string& path, std::string* out) {
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    char buf[4096];
    size_t n;
    while ((n = std::fread(buf, 1, sizeof(buf), f)) > 0) out->append(buf, n);
    std::fclose(f);
    return true;
}

// As linhas de um arquivo de texto simples, sem espacos nas pontas e sem as
// vazias/comentario. Serve tanto para o `mode.txt` quanto para o `Info`.
std::vector<std::string> textLines(const std::string& txt) {
    std::vector<std::string> out;
    size_t pos = 0;
    while (pos < txt.size()) {
        size_t end = txt.find('\n', pos);
        if (end == std::string::npos) end = txt.size();
        std::string line = trim(txt.substr(pos, end - pos));
        pos = end + 1;
        if (!line.empty() && line[0] != '#') out.push_back(std::move(line));
    }
    return out;
}

// Todos os valores de uma chave do Info. Chave repetida soma: o `build.txt`
// antigo escrevia uma linha de `modReferences` por mod.
std::vector<std::string> infoValues(const std::string& txt, const std::string& key) {
    std::vector<std::string> out;
    const std::string want = lowercased(key);
    for (const std::string& line : textLines(txt)) {
        const size_t eq = line.find('=');
        if (eq == std::string::npos) continue;
        if (lowercased(trim(line.substr(0, eq))) != want) continue;
        // O valor e uma lista separada por virgula; o espaco em volta de cada
        // nome nao faz parte dele.
        std::string value = trim(line.substr(eq + 1));
        size_t start = 0;
        while (start <= value.size()) {
            size_t comma = value.find(',', start);
            if (comma == std::string::npos) comma = value.size();
            std::string item = trim(value.substr(start, comma - start));
            if (!item.empty()) out.push_back(std::move(item));
            start = comma + 1;
        }
    }
    return out;
}

std::string infoValue(const std::string& txt, const std::string& key) {
    const std::vector<std::string> all = infoValues(txt, key);
    return all.empty() ? std::string() : all.front();
}

void readTmlInfo(LoadedMod* mod, const std::string& txt) {
    mod->internalName = infoValue(txt, "name");
    if (mod->internalName.empty()) mod->internalName = mod->id;
    mod->displayName = infoValue(txt, "displayName");
    if (mod->displayName.empty()) mod->displayName = mod->internalName;
    mod->version = infoValue(txt, "version");
    mod->author = infoValue(txt, "author");
    mod->side = lowercased(infoValue(txt, "side"));
    mod->references = infoValues(txt, "modReferences");
    mod->weakReferences = infoValues(txt, "weakReferences");
}

// O `.dll` do mod: `<Nome>.dll`, ou o primeiro `.dll` da raiz da pasta dele.
std::string findTmlEntry(const std::string& dir, const std::string& name) {
    const std::string preferred = name + ".dll";
    if (fileExists(dir + "/" + preferred)) return preferred;
    DIR* d = opendir(dir.c_str());
    if (!d) return {};
    std::string found;
    while (dirent* e = readdir(d)) {
        const std::string f = e->d_name;
        if (f.size() > 4 && f.compare(f.size() - 4, 4, ".dll") == 0) {
            found = f;
            break;
        }
    }
    closedir(d);
    return found;
}

void readManifest(LoadedMod* mod) {
    // Same names and priority as Catalog.MANIFESTS in the launcher.
    FILE* f = nullptr;
    for (const char* name : {"manifest.json", "mod.json"}) {
        const std::string path = mod->dir + "/" + name;
        struct stat st{};
        if (stat(path.c_str(), &st) == 0 && S_ISREG(st.st_mode)) {
            f = std::fopen(path.c_str(), "rb");
            break;
        }
    }
    std::string txt;
    if (f) {
        char buf[1024];
        size_t n;
        while ((n = std::fread(buf, 1, sizeof(buf), f)) > 0) txt.append(buf, n);
        std::fclose(f);
    }
    mod->internalName = manifestString(txt, "id");
    mod->displayName = manifestString(txt, "name");
    mod->version = manifestString(txt, "version");
    mod->dependencies = manifestStrings(txt, "dependencies");
    if (mod->displayName.empty()) mod->displayName = mod->id;
}
}

void loadAll(const std::string& modsDir, const std::string& tmlDir,
             const std::vector<ModSpec>& enabled) {
    runtime::configureTexturePacks(modsDir, enabled);
    runtime::configureFontPacks(modsDir, enabled);
    registry().clear();
    dirsById().clear();
    BL_INFO("carregando mods de %s (%zu habilitados)", modsDir.c_str(), enabled.size());

    // 1a passada: todos no registro, com manifesto e entry, antes de qualquer
    // main.js rodar. A ordem de carga vem da lista escolhida no launcher;
    // sem isto o ModLoader.TryGetMod de um mod dependeria da posicao do outro.
    std::vector<LoadedMod> listed;
    for (const auto& spec : enabled) {
        if (spec.entry == "@texture" || spec.entry == "@font") continue;
        const std::string& id = spec.id;
        LoadedMod mod;
        mod.id = id;
        mod.dir = modsDir + "/" + id;
        readManifest(&mod);

        const std::string& name = spec.entry;
        const std::string path = mod.dir + "/" + name;
        if (name.empty() || !fileExists(path)) {
            BL_ERROR("mod %s: entry file %s not found", id.c_str(), name.c_str());
            mod.enabled = false;
        } else {
            mod.entry = path.substr(mod.dir.size() + 1);
            // A pasta do ARQUIVO de entrada: Assets/, Content/, Common/ e
            // Localization/ ficam ao lado dele.
            dirsById()[id] = path.substr(0, path.rfind('/'));
        }
        listed.push_back(std::move(mod));
    }

    // Os mods do tModLoader entram no MESMO registro, depois dos pacotes .bl:
    // quem depende de quem e uma pergunta so, e o LoadOrder precisa ver os dois
    // lados de uma vez. A lista dos habilitados vem do `mode.txt`, que o
    // launcher reescreve a cada interruptor da aba "TML mods".
    if (!tmlDir.empty()) {
        std::string modeTxt;
        const std::vector<std::string> names = readWholeFile(tmlDir + "/mode.txt", &modeTxt)
            ? textLines(modeTxt) : std::vector<std::string>();
        if (!names.empty()) {
            BL_INFO("fluxo TML ativo em %s (%zu mod(s) em mode.txt)", tmlDir.c_str(), names.size());
        }
        for (const std::string& name : names) {
            LoadedMod mod;
            mod.id = name;
            mod.tml = true;
            // Layout novo: TML/mods/<Nome>/. O nivel `mods/` separa os mods
            // dos arquivos de controle (mode.txt, enabled.json). O caminho
            // antigo TML/<Nome>/ fica como fallback para quem ja importou
            // antes da mudanca e ainda nao abriu a aba de TML para migrar.
            mod.dir = tmlDir + "/mods/" + name;
            if (!fileExists(mod.dir + "/Info")) {
                const std::string flat = tmlDir + "/" + name;
                if (fileExists(flat + "/Info")) {
                    mod.dir = flat;
                    BL_WARN("TML %s: usando o layout antigo em %s (abra a aba TML mods para migrar)",
                            name.c_str(), flat.c_str());
                }
            }

            std::string info;
            if (readWholeFile(mod.dir + "/Info", &info)) {
                readTmlInfo(&mod, info);
            } else {
                BL_WARN("TML %s: sem Info em %s; usando o nome da pasta", name.c_str(), mod.dir.c_str());
                mod.internalName = name;
                mod.displayName = name;
            }
            // `side = Server` e mod de servidor: nao tem nada a fazer no celular
            // e so gastaria memoria carregando.
            if (mod.side == "server") {
                BL_WARN("TML %s ignorado: side = Server", name.c_str());
                continue;
            }
            const std::string entry = findTmlEntry(mod.dir, name);
            if (entry.empty()) {
                BL_ERROR("TML %s: nenhum .dll em %s", name.c_str(), mod.dir.c_str());
                continue;
            }
            mod.entry = entry;
            // A pasta do .dll e a raiz dos recursos do mod (Images/, Sounds/):
            // e por ela que bl.loadTexture e o ModContent acham os arquivos.
            dirsById()[name] = mod.dir;
            BL_INFO("TML %s: %s %s (%s, %s)", name.c_str(), mod.displayName.c_str(),
                    mod.version.c_str(), entry.c_str(), mod.author.c_str());
            listed.push_back(std::move(mod));
        }
    }

    // O `modReferences` do Info e do tModLoader, que exige o outro mod
    // instalado. Aqui so conta quem esta de fato na lista: um mod que pede um
    // companheiro ausente (musica, por exemplo) carrega assim mesmo.
    for (LoadedMod& mod : listed) {
        if (!mod.tml) continue;
        for (const std::string& ref : mod.references) {
            const bool present = std::any_of(listed.begin(), listed.end(),
                [&ref](const LoadedMod& other) { return other.id == ref || other.internalName == ref; });
            if (present) {
                mod.dependencies.push_back(ref);
            } else {
                BL_WARN("TML %s: referencia %s nao esta instalada (seguindo sem ela)",
                        mod.id.c_str(), ref.c_str());
            }
        }
        for (const std::string& ref : mod.weakReferences) {
            BL_DEBUG("TML %s: referencia fraca %s", mod.id.c_str(), ref.c_str());
        }
    }

    // As dependencias do manifesto carregam antes de quem as pede; o resto
    // fica na ordem do launcher. Faltou uma, ou ha ciclo: o mod fica de fora.
    std::vector<OrderEntry> entries;
    entries.reserve(listed.size());
    for (const LoadedMod& m : listed) entries.push_back({m.id, m.internalName, m.dependencies, m.enabled});
    const OrderResult sorted = sortByDependencies(entries);
    std::vector<size_t> position(listed.size());
    for (size_t k = 0; k < sorted.order.size(); ++k) {
        const size_t i = sorted.order[k];
        position[i] = k;
        LoadedMod& mod = listed[i];
        if (mod.enabled && !sorted.errors[i].empty()) {
            BL_ERROR("mod %s (%s) disabled: %s", mod.displayName.c_str(), mod.id.c_str(), sorted.errors[i].c_str());
            mod.enabled = false;
        }
        registry().push_back(std::move(mod));
    }
    // As dependencias de cada mod, ja como indices do registro.
    std::vector<std::vector<size_t>> needs(registry().size());
    for (size_t i = 0; i < listed.size(); ++i) {
        for (size_t d : sorted.needs[i]) needs[position[i]].push_back(position[d]);
    }

    // 2a passada: cada mod, na mesma ordem. Por indice: o registro nao muda
    // durante a carga, mas uma referencia guardada atravessando o loadMod
    // seria a mesma armadilha do g_frames.back() do JsHook.
    for (size_t i = 0; i < registry().size(); ++i) {
        if (!registry()[i].enabled) continue;
        // Uma dependencia que lancou erro na carga derruba quem precisa dela.
        const auto broken = std::find_if(needs[i].begin(), needs[i].end(),
            [](size_t d) { return !registry()[d].loaded; });
        if (broken != needs[i].end()) {
            const LoadedMod& dep = registry()[*broken];
            BL_ERROR("mod %s (%s) disabled: requires '%s', which failed to load",
                registry()[i].displayName.c_str(), registry()[i].id.c_str(), dep.displayName.c_str());
            registry()[i].enabled = false;
            continue;
        }
        const std::string id = registry()[i].id;
        const std::string path = registry()[i].dir + "/" + registry()[i].entry;
        const std::string dir = dirsById()[id];
        if (!dotnet::isCSharpEntry(path)) {
            BL_WARN("mod %s (%s) skipped: JS mods are no longer supported", registry()[i].displayName.c_str(), id.c_str());
            registry()[i].enabled = false;
            continue;
        }
        currentDirSlot() = dir;
        currentIdSlot() = id;
        // Uma linha antes e uma depois: se o processo abortar no meio da carga
        // (SIGABRT), o ultimo log do nucleo e exatamente este mod.
        BL_INFO("carregando mod %s (%s)", id.c_str(), path.c_str());
        const bool ok = dotnet::host().loadMod(id, path);
        currentDirSlot().clear();
        currentIdSlot().clear();
        if (!ok) {
            BL_ERROR("mod %s falhou ao carregar (%s)", id.c_str(), path.c_str());
            registry()[i].enabled = false;
        } else {
            BL_INFO("mod %s carregado", id.c_str());
            registry()[i].loaded = true;
        }
    }
}

const std::string& currentDir() { return currentDirSlot(); }
const std::string& currentId() { return currentIdSlot(); }

std::string rootOf(const std::string& id) {
    for (const LoadedMod& m : registry()) if (m.id == id) return m.dir;
    return {};
}

std::string displayName(const std::string& id) {
    const LoadedMod* m = find(id);
    return m ? m->displayName : id;
}

const LoadedMod* find(const std::string& id) {
    for (const LoadedMod& m : registry()) if (m.id == id) return &m;
    return nullptr;
}

const std::string& dirOf(const std::string& id) {
    static const std::string none;
    auto it = dirsById().find(id);
    return it == dirsById().end() ? none : it->second;
}

size_t loadedCount() { return registry().size(); }

LoadedMod* get(uint16_t index) {
    if (index >= registry().size()) return nullptr;
    return &registry()[index];
}

} // namespace bl::mods
