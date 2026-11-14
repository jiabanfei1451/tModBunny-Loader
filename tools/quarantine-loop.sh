#!/usr/bin/env bash
# Descobre automaticamente os metodos que derrubam o JIT (0xC0000005) e
# alimenta out/quarantine.txt. Cada AV mata o processo, entao a unica forma de
# achar o proximo e rodar de novo com o culpado ja na lista.
#
#   ./tools/quarantine-loop.sh [arquivo.tmod] [max_iteracoes]
set -u

TMOD="${1:-../Fargowiltas.tmod}"
MAX="${2:-25}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
Q="$ROOT/../out/quarantine.txt"
TRACE="D:/User/Temp_TEMP/bl-jit-trace.txt"

: > "$Q"
echo "quarentena: $Q"

for i in $(seq 1 "$MAX"); do
    echo "--- rodada $i ---"
    OUT=$(cd "$ROOT" && BL_QUARANTINE="$Q" dotnet run --no-build --project tools/modsandbox -c Release -- "$TMOD" 2>&1)

    if echo "$OUT" | grep -q "0xC0000005"; then
        LAST=$(tail -1 "$TRACE" 2>/dev/null | tr -d '\r')
        if [ -z "$LAST" ]; then
            echo "AV sem rastro do JIT; abortando"
            break
        fi
        if grep -qxF "$LAST" "$Q"; then
            echo "AV repetido em $LAST (quarentena nao resolveu); abortando"
            break
        fi
        echo "$LAST" >> "$Q"
        echo "AV  -> $LAST"
        continue
    fi

    echo "$OUT" | grep -E "IL reescrita|tipos=|ocorrencia|total de metodos|em quarentena|relatorio" | sed 's/^/    /'
    echo "OK: nenhum AV nesta rodada"
    break
done

echo "--- quarentena final ($(wc -l < "$Q") metodo(s)) ---"
cat "$Q"
