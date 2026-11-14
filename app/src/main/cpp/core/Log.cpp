#include "core/Log.h"
#include <algorithm>
#include <atomic>
#include <csignal>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <deque>
#include <fcntl.h>
#include <mutex>
#include <string>
#include <unistd.h>
#include <ucontext.h>
#include <dlfcn.h>

namespace bl::log {

namespace {
constexpr const char* TAG = "BunnyLoader";
constexpr size_t kMaxErrorChars = 8 * 1024;  // o painel nao serve pra 1 MB
// O logd corta a linha em ~4 KB; acima disso ela vai ao logcat em pedacos.
constexpr size_t kLogcatChunk = 4000;
FILE* g_file = nullptr;
std::mutex g_mutex;
std::atomic<bool> g_verbose{false};

std::string g_errors;
int g_errorCount = 0;
void (*g_onError)(const char*) = nullptr;
std::atomic<void (*)(int, const char*)> g_onLine{nullptr};

// As ultimas linhas, para o retrato do crash. 192 cobrem um boot inteiro: com
// 96 o bloco comecava ja no meio da instalacao de conteudo e escondia a causa.
constexpr size_t kRecentLines = 192;
std::deque<std::string> g_recent;

/** Ao logcat, em pedacos que nao cortam um caractere UTF-8 ao meio. */
void toLogcat(int level, const char* text, size_t len) {
    if (len <= kLogcatChunk) {
        __android_log_write(level, TAG, text);
        return;
    }
    std::string piece;
    for (size_t at = 0; at < len;) {
        size_t n = len - at < kLogcatChunk ? len - at : kLogcatChunk;
        while (n > 1 && at + n < len && (static_cast<unsigned char>(text[at + n]) & 0xC0) == 0x80) --n;
        piece.assign(text + at, n);
        __android_log_write(level, TAG, piece.c_str());
        at += n;
    }
}
}

void open(const char* path) {
    {
        std::lock_guard<std::mutex> guard(g_mutex);
        if (g_file) fclose(g_file);
        g_file = path ? fopen(path, "w") : nullptr;
    }
    // O retrato do crash e instalado aqui: e o primeiro ponto em que existe
    // um arquivo de sessao para receber as ultimas linhas.
    installCrashHandler();
}

void setVerbose(bool on) { g_verbose.store(on, std::memory_order_relaxed); }

/** A letra do nivel, como no logcat: I, W, E... */
static char levelLetter(int level) {
    switch (level) {
        case ANDROID_LOG_VERBOSE: return 'V';
        case ANDROID_LOG_DEBUG: return 'D';
        case ANDROID_LOG_WARN: return 'W';
        case ANDROID_LOG_ERROR: return 'E';
        case ANDROID_LOG_FATAL: return 'F';
        default: return 'I';
    }
}

void write(int level, const char* fmt, ...) {
    // Quase toda linha cabe aqui; a que nao cabe (um bl.log de objeto grande,
    // uma pilha de erro de mod) ia cortada em 4 KB. Agora vai inteira.
    char stackBuffer[4096];
    std::string heapBuffer;
    va_list args;
    va_start(args, fmt);
    va_list again;
    va_copy(again, args);
    const int needed = vsnprintf(stackBuffer, sizeof(stackBuffer), fmt, args);
    va_end(args);
    if (needed < 0) stackBuffer[0] = '\0';   // formato invalido: linha vazia, nao lixo
    const char* buffer = stackBuffer;
    size_t length = needed > 0 ? static_cast<size_t>(needed) : 0;
    if (length >= sizeof(stackBuffer)) {
        heapBuffer.resize(length + 1);
        vsnprintf(&heapBuffer[0], heapBuffer.size(), fmt, again);
        heapBuffer.resize(length);
        buffer = heapBuffer.c_str();
    }
    va_end(again);

      toLogcat(level, buffer, length);

      void (*notify)(const char*) = nullptr;
      {
          std::lock_guard<std::mutex> guard(g_mutex);
          // O retrato do crash: as ultimas linhas, mesmo as de DEBUG (que nao
          // vao para o arquivo) dizem onde o nucleo estava.
          {
              std::string line(buffer, length);
              g_recent.push_back(line);
              while (g_recent.size() > kRecentLines) g_recent.pop_front();
          }
          if (g_file && (level >= ANDROID_LOG_INFO || g_verbose.load(std::memory_order_relaxed))) {
            // No arquivo, com hora e nivel: e ele que o modder abre, sem logcat.
            //
            // "Log detalhado" ligado: as linhas de INFO passam a sair como D.
            // Motivo: com o modo detalhado o arquivo mistura o resumo do nucleo
            // com o detalhe (tabela por tabela, hook por hook) e nao dava para
            // separar um do outro por nivel. W/E/F continuam com a letra deles:
            // o que importa num relatorio de falha nao pode virar D e sumir
            // num filtro por nivel.
            const bool verbose = g_verbose.load(std::memory_order_relaxed);
            const char letter = (verbose && level >= ANDROID_LOG_DEBUG && level < ANDROID_LOG_WARN)
                                    ? 'D'
                                    : levelLetter(level);
            timespec ts{};
            clock_gettime(CLOCK_REALTIME, &ts);
            tm t{};
            localtime_r(&ts.tv_sec, &t);
            fprintf(g_file, "%02d:%02d:%02d.%03ld %c %s\n", t.tm_hour, t.tm_min, t.tm_sec,
                    ts.tv_nsec / 1000000, letter, buffer);
            fflush(g_file);
        }
        if (level >= ANDROID_LOG_ERROR) {
            ++g_errorCount;
            // Guarda os PRIMEIROS: o erro que importa e o que comeca a cascata,
            // e um hook que falha a cada frame afogaria o resto.
            // A linha agora vem inteira: uma so nao pode passar do teto.
            if (g_errors.size() < kMaxErrorChars) {
                g_errors.append(buffer, std::min(length, kMaxErrorChars - g_errors.size())).append("\n");
            }
            notify = g_onError;
        }
    }
    // Fora do lock: quem escuta vai mexer em UI e pode logar de volta.
    if (notify) notify(buffer);
    if (level >= ANDROID_LOG_INFO) {
        if (auto line = g_onLine.load(std::memory_order_acquire)) line(level, buffer);
    }
}

std::string errorsSoFar() {
    std::lock_guard<std::mutex> guard(g_mutex);
    return g_errors;
}

std::string recentLines(int count) {
    std::lock_guard<std::mutex> guard(g_mutex);
    std::string out;
    int taken = 0;
    for (auto it = g_recent.rbegin(); it != g_recent.rend() && taken < count; ++it, ++taken) {
        out += *it;
        out += '\n';
    }
    return out;
}

namespace {
const int g_signalList[] = {SIGABRT, SIGSEGV, SIGILL, SIGFPE, SIGBUS};
struct sigaction g_previous[5] {};

/**
 * Nome do metodo por tras de um endereco de codigo gerado pelo JIT.
 *
 * O Mono compila os metodos do mod para memoria anonima: `dladdr` devolve 0
 * ali, entao o relatorio so mostrava "PC: 0x7c9c788758" e nao havia como saber
 * ONDE o mod morreu. O proprio Mono exporta `mono_pmip(ip)`, que responde
 * exatamente isso ("Classe:Metodo(assinatura)"). Resolvido por dlsym porque o
 * nucleo nao linka contra o Mono.
 */
const char* jitMethodName(void* pc) {
    static const char* (*pmip)(void*) = nullptr;
    static bool looked = false;
    if (!looked) {
        looked = true;
        pmip = reinterpret_cast<const char* (*)(void*)>(dlsym(RTLD_DEFAULT, "mono_pmip"));
    }
    if (!pmip || !pc) return nullptr;
    return pmip(pc);
}

/**
 * A pilha do C# por tras do PC.
 *
 * O PC do crash quase sempre cai dentro do proprio Mono (runtime), onde
 * `mono_pmip` nao responde — e o relatorio ficava so com "PC: 0x7c...". Mas os
 * enderecos de retorno empilhados apontam para dentro dos metodos JIT do mod, e
 * esses o `mono_pmip` nomeia. Varre-se a pilha a partir do SP e imprime-se o
 * nome de cada endereco que resolver: e a pilha gerenciada que faltava.
 */
int managedFrames(void* sp, char* out, size_t size) {
    if (!sp || size < 2) return 0;
    const uintptr_t start = reinterpret_cast<uintptr_t>(sp) & ~static_cast<uintptr_t>(7);
    const uintptr_t* words = reinterpret_cast<const uintptr_t*>(start);
    size_t used = 0;
    int found = 0;
    char last[192] = {};
    // 384 palavras (~3 KB) cobrem um callback de mod inteiro. Mais que isso so
    // alcanca lixo de quadros antigos — e, num thread do Mono com pilha de
    // 64 KB, arrisca sair do mapeamento dentro do proprio handler de sinal.
    for (int i = 0; i < 384 && found < 12 && used + 220 < size; ++i) {
        const uintptr_t v = words[i];
        if (v < 0x10000 || v == start) continue;
        const char* name = jitMethodName(reinterpret_cast<void*>(v));
        if (!name || !name[0]) continue;
        if (std::strncmp(name, last, sizeof(last) - 1) == 0) continue;
        std::snprintf(last, sizeof(last), "%s", name);
        used += static_cast<size_t>(std::snprintf(out + used, size - used, "JIT: %s\n", name));
        ++found;
    }
    return found;
}

/** Le hex ate o primeiro caractere que nao for digito hexadecimal. */
bool readHex(const char*& at, uintptr_t& value) {
    uintptr_t v = 0;
    bool any = false;
    for (;;) {
        const char c = *at;
        int digit;
        if (c >= '0' && c <= '9') digit = c - '0';
        else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
        else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
        else break;
        v = (v << 4) | static_cast<uintptr_t>(digit);
        any = true;
        ++at;
    }
    value = v;
    return any;
}

/**
 * Em qual mapeamento de /proc/self/maps o endereco caiu.
 *
 * Sem isso nao se distingue "codigo JIT do Mono" de "trampolim do shadowhook":
 * os dois sao mapeamentos anonimos. A linha inteira (intervalo + nome) responde.
 */
void mappingOf(void* pc, char* out, size_t size) {
    out[0] = '\0';
    if (!pc || size < 2) return;
    // `::` explicito: dentro de bl::log existe um `open(const char*)` (o do log
    // de sessao) que sombreia o do <fcntl.h>.
    const int fd = ::open("/proc/self/maps", O_RDONLY | O_CLOEXEC);
    if (fd < 0) return;
    const uintptr_t target = reinterpret_cast<uintptr_t>(pc);
    char buffer[1024];
    char line[256];
    size_t length = 0;
    ssize_t got;
    while ((got = ::read(fd, buffer, sizeof(buffer))) > 0) {
        for (ssize_t i = 0; i < got; ++i) {
            if (buffer[i] != '\n') {
                if (length + 1 < sizeof(line)) line[length++] = buffer[i];
                continue;
            }
            line[length] = '\0';
            const char* at = line;
            uintptr_t start = 0, end = 0;
            if (readHex(at, start) && *at == '-' && readHex(++at, end) &&
                target >= start && target < end) {
                const size_t n = length < size - 1 ? length : size - 1;
                memcpy(out, line, n);
                out[n] = '\0';
                ::close(fd);
                return;
            }
            length = 0;
        }
    }
    ::close(fd);
}

/**
 * O retrato do crash.
 *
 * Um SIGABRT nativo (status=6) nao deixa rastro util: o tombstone do ART vem
 * em binario em alguns aparelhos e o relatorio sai ilegivel. Mas o nucleo
 * deixou um rastro claro — as ultimas linhas que ele escreveu. Este
 * manipulador as joga no log de sessao (e no logcat) e depois devolve o sinal
 * ao padrao, para o ART ainda fazer o tombstone.
 *
 * Tudo em buffer fixo: num handler de sinal nao se aloca memoria.
 */
void crashHandler(int sig, siginfo_t* info, void* ctx) {
    // try_lock: se o abort aconteceu com a trava do log presa, seguimos sem
    // o retrato em vez de deadlockar dentro do handler.
    std::unique_lock<std::mutex> guard(g_mutex, std::try_to_lock);
    // `static`, nao no stack: 32 KB no stack de um thread do Mono (alguns tem
    // 64 KB ou menos) estouram a pilha antes de sair qualquer linha. Foi o que
    // aconteceu na v28: nem o cabecalho do crash apareceu no log.
    static char dump[32 * 1024];
    size_t used = 0;
    FILE* file = g_file;
    const int fd = file ? fileno(file) : -1;
    // Grava e zera: o que ja saiu conta como entregue, mesmo que o passo
    // seguinte derrube o processo de novo.
    auto flush = [&]() {
        if (used == 0) return;
        if (fd >= 0) {
            ssize_t ignored = ::write(fd, dump, used);
            (void)ignored;
        }
        // Tambem no logcat: quem tem adb ve na hora.
        __android_log_write(ANDROID_LOG_FATAL, TAG, dump);
        used = 0;
    };
    used += snprintf(dump + used, sizeof(dump) - used,
                     "\n=== CRASH NATIVO: sinal %d ===\n", sig);
    // O cabecalho sai antes de tudo: e a prova de que o manipulador rodou,
    // mesmo que qualquer passo seguinte falhe.
    flush();

    // Onde o PC estava, e o estado dos registradores.
    //
    // O PC sozinho aponta para a instrucao, mas nao diz QUAL dado estava nulo.
    // No crash do libmonosgen a instrucao era `ldr x1, [x2]` com
    // `x2 = [x22, #0x78]`: sem x22 nao da para saber se o nulo veio do objeto
    // (x22 == 0) ou de um campo dele (o campo e que estava zerado).
    void* pc = nullptr;
    uintptr_t regs[31] = {};
    uintptr_t sp = 0;
    if (ctx) {
        auto* uc = static_cast<ucontext_t*>(ctx);
        pc = reinterpret_cast<void*>(uc->uc_mcontext.pc);
        for (int i = 0; i < 31; ++i) regs[i] = uc->uc_mcontext.regs[i];
        sp = uc->uc_mcontext.sp;
    }
    Dl_info symbol{};
    if (pc && dladdr(pc, &symbol) && symbol.dli_sname) {
        used += snprintf(dump + used, sizeof(dump) - used,
                         "PC: %s (%s+%p)\n", symbol.dli_sname,
                         symbol.dli_fname ? symbol.dli_fname : "?",
                         reinterpret_cast<void*>(
                             reinterpret_cast<uintptr_t>(pc) -
                             reinterpret_cast<uintptr_t>(symbol.dli_saddr)));
    } else {
        used += snprintf(dump + used, sizeof(dump) - used, "PC: %p\n", pc);
    }
    if (info) {
        used += snprintf(dump + used, sizeof(dump) - used,
                         "endereco do sinal: %p  codigo: %d\n", info->si_addr, info->si_code);
    }
    used += snprintf(dump + used, sizeof(dump) - used, "SP: %p  LR: %p\n",
                     reinterpret_cast<void*>(sp), reinterpret_cast<void*>(regs[30]));
    for (int base = 0; base < 31 && used + 64 < sizeof(dump); base += 8) {
        used += snprintf(dump + used, sizeof(dump) - used, "x%d:", base);
        for (int i = base; i < base + 8 && i < 31; ++i) {
            used += snprintf(dump + used, sizeof(dump) - used, " %p",
                             reinterpret_cast<void*>(regs[i]));
        }
        used += snprintf(dump + used, sizeof(dump) - used, "\n");
    }

    // O essencial sai ANTES de chamar codigo do runtime (mono_pmip aloca e
    // consulta a tabela de JIT): se ele falhar, o PC ja esta no arquivo.
    flush();

    // Sem simbolo, o PC nao diz nem em que biblioteca estava. Aqui saem o
    // mapeamento real (/proc/self/maps) e, para codigo JIT do Mono, o nome do
    // metodo compilado.
    if (pc) {
        char mapping[256];
        mappingOf(pc, mapping, sizeof(mapping));
        if (mapping[0]) {
            used += snprintf(dump + used, sizeof(dump) - used, "PC mapa: %s\n", mapping);
        }
        const char* method = jitMethodName(pc);
        used += snprintf(dump + used, sizeof(dump) - used, "PC metodo: %s\n",
                         method && method[0] ? method : "(nao e codigo JIT do Mono)");
        // O LR diz QUEM chamou: se cair em codigo do jogo, o culpado e o metodo
        // nativo; se cair num mapeamento anonimo, e o JIT/Mono que chamou.
        const uintptr_t lr = regs[30];
        if (lr) {
            mappingOf(reinterpret_cast<void*>(lr), mapping, sizeof(mapping));
            if (mapping[0]) {
                used += snprintf(dump + used, sizeof(dump) - used, "LR mapa: %s\n", mapping);
            }
            const char* lrMethod = jitMethodName(reinterpret_cast<void*>(lr));
            if (lrMethod && lrMethod[0]) {
                used += snprintf(dump + used, sizeof(dump) - used, "LR metodo: %s\n", lrMethod);
            }
        }
        flush();

        // A pilha do C#: mesmo com o PC dentro do runtime do Mono, os retornos
        // empilhados dizem qual metodo do mod estava rodando.
        static char frames[4 * 1024];
        if (managedFrames(reinterpret_cast<void*>(sp), frames, sizeof(frames)) > 0) {
            used += snprintf(dump + used, sizeof(dump) - used,
                             "--- pilha do C# (retornos na pilha) ---\n%s", frames);
        } else {
            used += snprintf(dump + used, sizeof(dump) - used,
                             "--- pilha do C#: nenhum metodo JIT resolvido ---\n");
        }
        flush();
    }

    // As ultimas linhas do nucleo, DA MAIS ANTIGA PARA A MAIS NOVA.
    //
    // Antes iam ao contrario (a mais nova primeiro), e por isso a leitura do
    // relatorio sempre comecava pela cauda do historico: conclusoes do tipo
    // "o crash foi depois de X" apontavam para um X que nem era o ultimo.
    used += snprintf(dump + used, sizeof(dump) - used, "--- ultimas linhas do nucleo ---\n");
    for (const std::string& line : g_recent) {
        if (used + line.size() + 2 >= sizeof(dump)) break;
        used += snprintf(dump + used, sizeof(dump) - used, "%s\n", line.c_str());
    }
    used += snprintf(dump + used, sizeof(dump) - used, "=== fim do crash ===\n");
    flush();
    guard.unlock();

    // Encadeia com o manipulador anterior.
    //
    // O Mono usa SIGSEGV para transformar desreferencia de null em
    // NullReferenceException. Enquanto este handler substituia o do Mono e
    // matava o processo, QUALQUER NullReferenceException dentro de um mod virava
    // "crash nativo sinal 11 / endereco 0" sem uma linha de erro no log — o
    // sintoma exato de "so fecha, sem mensagem".
    for (int i = 0; i < 5; i++) {
        if (g_signalList[i] != sig) continue;
        const struct sigaction& previous = g_previous[i];
        if (previous.sa_flags & SA_SIGINFO) {
            if (previous.sa_sigaction) {
                previous.sa_sigaction(sig, info, ctx);
                return;
            }
        } else if (previous.sa_handler != SIG_DFL && previous.sa_handler != SIG_IGN) {
            previous.sa_handler(sig);
            return;
        }
        break;
    }

    // Sem manipulador anterior: devolve o sinal ao padrao, o ART ainda
    // registra o tombstone.
    signal(sig, SIG_DFL);
    raise(sig);
}
} // namespace

void installCrashHandler() {
    static bool installed = false;
    if (installed) return;
    installed = true;
    // Altstack proprio. SA_ONSTACK sem sigaltstack instalado continua usando a
    // pilha normal — e justamente quando a pilha estoura (thread do Mono com
    // 64 KB, por exemplo) que o handler precisa de outra pilha para rodar.
    static char altStack[64 * 1024];
    stack_t alt{};
    alt.ss_sp = altStack;
    alt.ss_size = sizeof(altStack);
    alt.ss_flags = 0;
    sigaltstack(&alt, nullptr);
    struct sigaction action {};
    action.sa_sigaction = crashHandler;
    action.sa_flags = SA_SIGINFO | SA_RESTART | SA_ONSTACK;
    sigemptyset(&action.sa_mask);
    for (int i = 0; i < 5; i++)
        sigaction(g_signalList[i], &action, &g_previous[i]);
}

int errorCount() {
    std::lock_guard<std::mutex> guard(g_mutex);
    return g_errorCount;
}

void onLine(void (*fn)(int level, const char* line)) {
    g_onLine.store(fn, std::memory_order_release);
}

void onError(void (*fn)(const char*)) {
    bool atrasados = false;
    {
        std::lock_guard<std::mutex> guard(g_mutex);
        g_onError = fn;
        atrasados = fn && g_errorCount > 0;
    }
    // O painel so nasce depois que a Activity do jogo existe, e a sonda carrega
    // os mods ANTES disso. Sem este aviso, o erro mais importante do boot — um
    // mod que nao carregou — era justamente o unico que nunca chegava a tela.
    // O ouvinte ignora a linha e mostra log::errorsSoFar(), entao um toque
    // basta para trazer tudo o que ficou para tras.
    if (atrasados) fn("(errors from before the panel)");
}

} // namespace bl::log
