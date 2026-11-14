#include "hook/HookManager.h"
#include "core/Log.h"
#include "shadowhook.h"
#include <mutex>
#include <vector>

namespace bl::hook {

namespace {

/**
 * Todos os hooks de UM endereco.
 *
 * O ShadowHook roda em modo UNIQUE: um endereco aceita uma substituicao so, e
 * o segundo pedido e recusado. Era o que acontecia com Vida Cheia + Sem Queda,
 * os dois em Player.Update — o segundo mod ligado simplesmente nao carregava.
 *
 * Em vez de pedir de novo ao ShadowHook, o segundo hook entra NA CADEIA: ele
 * passa a ser o "original" do anterior, e o original dele e a funcao real.
 *
 *     jogo -> prologo patcheado -> R1 -> R2 -> ... -> Rn -> funcao real
 *
 * Todos os Ri hookam a mesma funcao, entao tem a mesma ABI e qualquer um pode
 * chamar o proximo como se fosse o original. Acrescentar um elo e trocar UM
 * ponteiro (o `original` de Rn-1): nada e desinstalado, nenhum trampolim do
 * ShadowHook e liberado com alguem possivelmente executando dentro dele.
 */
struct Chain {
    void* address = nullptr;
    void* stub = nullptr;             // o que o shadowhook_unhook quer
    std::vector<void**> originals;    // o `original` de cada elo, de fora pra dentro
};

std::mutex g_mutex;

std::vector<Chain>& chains() {
    static std::vector<Chain> list;
    return list;
}

} // namespace

bool install(void* address, void* replacement, void** original, const char* label) {
    if (!address || !replacement || !original) return false;
    std::lock_guard<std::mutex> guard(g_mutex);

    for (Chain& c : chains()) {
        if (c.address != address) continue;
        // O MESMO replacement entrando de novo fecharia um ciclo (A chama A):
        // a primeira chamada do metodo vira recursao infinita e derruba o
        // processo. Repetir a instalacao do mesmo gancho nao acrescenta nada,
        // entao e recusada aqui — e o aviso aponta a origem da duplicata.
        for (void** slot : c.originals) {
            if (__atomic_load_n(slot, __ATOMIC_ACQUIRE) == replacement) {
                BL_WARN("hook repetido em %p [%s]: mesmo replacement, ignorado", address,
                        label ? label : "?");
                return true;
            }
        }
        // O novo elo aponta para onde o ultimo apontava (a funcao real, via
        // trampolim) ANTES de ser publicado: quem ler o ponteiro do ultimo elo
        // a partir de agora ja encontra um elo pronto para seguir.
        void** last = c.originals.back();
        __atomic_store_n(original, __atomic_load_n(last, __ATOMIC_ACQUIRE), __ATOMIC_RELEASE);
        __atomic_store_n(last, replacement, __ATOMIC_RELEASE);
        c.originals.push_back(original);
        BL_DEBUG("hook encadeado em %p (%zu no mesmo metodo) [%s]", address, c.originals.size(),
                 label ? label : "?");
        return true;
    }

    // Antes e depois: se o processo morrer dentro do shadowhook, o crash dump
    // mostra "instalando hook" sem o "hook instalado" correspondente e o nome
    // do metodo fica identificado.
    BL_DEBUG("instalando hook em %p [%s]", address, label ? label : "?");
    void* stub = shadowhook_hook_func_addr(address, replacement, original);
    if (!stub) {
        BL_ERROR("Hook falhou em %p [%s]: %s", address, label ? label : "?",
                 shadowhook_to_errmsg(shadowhook_get_errno()));
        return false;
    }
    chains().push_back({address, stub, {original}});
    // A linha de cada instalacao, e nao so a do encadeamento: o ultimo log
    // antes de um crash nativo costuma ser exatamente este, e sem o nome do
    // metodo ele nao ajuda em nada.
    BL_DEBUG("hook instalado em %p [%s]", address, label ? label : "?");
    return true;
}

bool install(const MethodInfo* method, void* replacement, void** original) {
    if (!method) return false;
    const auto& api = il2cpp::api();
    const char* label = api.method_get_name ? api.method_get_name(method) : nullptr;
    return install(il2cpp::methodPointer(method), replacement, original, label);
}

} // namespace bl::hook
