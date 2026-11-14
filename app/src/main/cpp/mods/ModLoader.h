#pragma once
#include <cstdint>
#include <string>
#include <vector>

#include "core/Config.h"  // ModSpec

namespace bl::mods {

struct LoadedMod {
    std::string id;             // o uid: nome da pasta e do modulo no QuickJS
    std::string internalName;   // o "id" do manifesto: por ele outro mod o acha (ModLoader.TryGetMod)
    std::string displayName;    // o "name" do manifesto
    std::string version;
    std::vector<std::string> dependencies;  // o "dependencies" do manifesto: id ou uid de outro mod
    std::string dir;
    std::string entry;   // ex: "main.js"
    uint32_t errorCount = 0;
    bool enabled = true;
    bool loaded = false;        // o main.js ja rodou sem erro
    // Veio de um `.tmod` do tModLoader (pasta em TML/), e nao de um pacote
    // `.bl`. Muda o que o registro mostra e de onde sai o Info.
    bool tml = false;
    std::string author;
    // `side` do Info, em minusculas: "both", "client" ou "server". O server so
    // fica de fora aqui; o client e o caso normal.
    std::string side;
    // `modReferences` do Info. Viram `dependencies` so para os mods que estao
    // de fato na lista — um pedido por um mod ausente nao pode derrubar este.
    std::vector<std::string> references;
    std::vector<std::string> weakReferences;
};

/**
 * Carrega os mods habilitados de modsDir.
 *
 * Todos entram no registro ANTES de o primeiro main.js rodar: um mod que
 * pergunta por outro (ModLoader.TryGetMod) acha o que ainda vai carregar.
 *
 * `tmlDir` e a pasta dos mods do tModLoader; vazio desliga o fluxo TML. Os
 * habilitados dela vem de `tmlDir/mode.txt`, escrito pelo launcher.
 */
void loadAll(const std::string& modsDir, const std::string& tmlDir,
             const std::vector<ModSpec>& enabled);

// Quantos mods estão registrados.
size_t loadedCount();

LoadedMod* get(uint16_t index);

/**
 * Pasta do mod que está sendo carregado agora, ou vazio fora da carga.
 *
 * É o que deixa `bl.loadTexture('icone.png')` achar o arquivo ao lado do
 * main.js sem o autor precisar saber o caminho absoluto do aparelho.
 */
const std::string& currentDir();

/** Pasta do entry de um mod ja carregado, pelo id. Vazio se nao conhece. */
const std::string& dirOf(const std::string& id);

/** Id (uid) do mod que esta carregando agora, ou vazio fora da carga. */
const std::string& currentId();

/** Pasta raiz do pacote (onde mora o manifest.json). Vazio se nao conhece. */
std::string rootOf(const std::string& id);

/** O "name" do manifesto, para mostrar a gente; o id se nao houver. */
std::string displayName(const std::string& id);

/** O mod pelo uid, ou nullptr. */
const LoadedMod* find(const std::string& id);

// Um mod que falha demais é desativado em runtime; falha NUNCA derruba o jogo.
constexpr uint32_t MaxErrors = 20;

} // namespace bl::mods
