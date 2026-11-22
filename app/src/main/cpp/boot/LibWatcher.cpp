#include "boot/LibWatcher.h"
#include "core/Log.h"
#include "il2cpp/Api.h"
#include "boot/Boot.h"
#include "shadowhook.h"

#include <atomic>
#include <condition_variable>
#include <mutex>

namespace bl::loader {

namespace {

using InitFn = int (*)(const char*);
InitFn origInit = nullptr;
void* initStub = nullptr;
std::atomic<bool> g_installed{false};
std::mutex g_initMutex;
std::condition_variable g_initCv;
bool g_initDone = false;

void markInitDone() {
    {
        std::lock_guard<std::mutex> lock(g_initMutex);
        g_initDone = true;
    }
    g_initCv.notify_all();
}

int hkInit(const char* domainName) {
    // Deixa o IL2CPP terminar: só depois existem domínio, classes e metadados.
    int result = origInit(domainName);
    BL_DEBUG("il2cpp_init concluido (domain=%s)", domainName ? domainName : "?");
    runtime::boot();
    markInitDone();
    return result;
}

} // namespace

bool watcherInstalled() { return g_installed.load(std::memory_order_acquire); }

bool waitForIl2cppInit(std::chrono::steady_clock::time_point deadline) {
    std::unique_lock<std::mutex> lock(g_initMutex);
    return g_initCv.wait_until(lock, deadline, [] { return g_initDone; });
}

void waitForIl2cppInit() {
    std::unique_lock<std::mutex> lock(g_initMutex);
    g_initCv.wait(lock, [] { return g_initDone; });
}

bool installWatcher() {
    // Idempotente de propósito. Dois caminhos chamam isto:
    //   - o construtor da libbunny (boot/Entry.cpp), herdado do modelo LD_PRELOAD,
    //     que roda no System.loadLibrary("bunny");
    //   - NativeBridge.init(), logo em seguida, vindo do Kotlin.
    // Sem esta guarda o segundo recebia "Duplicate hook" e devolvia JNI_FALSE,
    // o que fazia o launcher anunciar "rodando sem mods" com o núcleo
    // perfeitamente instalado pelo primeiro.
    static bool installed = false;
    if (installed) return true;
    if (g_installed.load(std::memory_order_acquire)) return true;

    shadowhook_init(SHADOWHOOK_MODE_UNIQUE, false);
    initStub = shadowhook_hook_sym_name(
        "libil2cpp.so",
        "il2cpp_init",
        reinterpret_cast<void*>(hkInit),
        reinterpret_cast<void**>(&origInit));

    if (!initStub && shadowhook_get_errno() != SHADOWHOOK_ERRNO_PENDING) {
        BL_ERROR("Falha ao registrar hook de il2cpp_init: %s",
                 shadowhook_to_errmsg(shadowhook_get_errno()));
        return false;
    }
    installed = true;
    BL_DEBUG("watcher de il2cpp_init registrado");
    g_installed.store(true, std::memory_order_release);
    BL_DEBUG("il2cpp_init watcher registered");
    return true;
}

} // namespace bl::loader
