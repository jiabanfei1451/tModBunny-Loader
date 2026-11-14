#!/usr/bin/env python3
"""Índice da API das DUAS linhas de tModLoader.

    build              -> varre as duas árvores e gera out/api-contract.txt
    find <nome>        -> mostra onde um membro aparece em cada linha
    report <log.txt>   -> cruza um relatório de erros com o índice

CONTRATO  = Bunny-Loader-main/tmodloader/patches/tModLoader
            (tModLoader 2026.09.3.1 = linha 1.4.4.9: a API que o .tmod via)
IMPLEMENT = tModLoader-1.4.5/patches/tModLoader
            (como a mesma API foi reescrita sobre o Terraria 1.4.5.8)

Uso típico: um `.tmod` dá MissingMethodException -> `report` aponta a linha
exata das duas fontes; corrige-se o shim usando a implementação 1.4.5.
"""

import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent                      # Bunny-Loader-main
WORKSPACE = REPO.parent                        # TerrariaModLoader
CONTRACT = REPO / "tmodloader" / "patches" / "tModLoader"
IMPL = WORKSPACE / "tModLoader-1.4.5" / "patches" / "tModLoader"
OUT = WORKSPACE / "out" / "api-contract.txt"

# Declarações que interessam: public/protected de método, propriedade, campo,
# operador, delegado, struct/classe. Só o primeiro token "nome" é indexado.
DECL = re.compile(
    r"\bpublic\b|\binternal\b.*\bstatic\b|\bprotected\b.*\bvirtual\b")
NAME = re.compile(
    r"(?:operator\s*[=!<>+\-*/]+|\b\w+)\s*(?=\(|\s*\{|\s*=>|\s*;|\s*=)")
BAD = {"public", "static", "readonly", "partial", "virtual", "override",
       "sealed", "abstract", "unsafe", "ref", "out", "in", "new", "const",
       "async", "extern", "void", "int", "bool", "string", "float", "double",
       "byte", "short", "long", "ushort", "uint", "ulong", "object", "var"}


def declarations(root: Path):
    """(nome, caminho_relativo, linha, texto) para cada declaração pública."""
    if not root.is_dir():
        print(f"AVISO: árvore ausente: {root}", file=sys.stderr)
        return
    for path in sorted(root.rglob("*")):
        if path.suffix not in (".cs", ".patch") or not path.is_file():
            continue
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        for n, raw in enumerate(lines, 1):
            line = raw
            if path.name.endswith(".patch"):
                if not line.startswith("+"):
                    continue
                line = line[1:]
            text = line.strip()
            if not DECL.search(text) or text.startswith("//") or text.startswith("///"):
                continue
            candidates = [m.group(0).strip() for m in NAME.finditer(text)]
            name = next((c.split()[-1] for c in candidates
                         if c.split()[-1] not in BAD and c.split()[-1][0].isalpha()), None)
            if not name:
                continue
            yield name, str(path.relative_to(root)), n, text[:160]


def build():
    contract = {}
    for name, rel, line, text in declarations(CONTRACT):
        contract.setdefault(name, []).append((rel, line, text))
    impl = {}
    for name, rel, line, text in declarations(IMPL):
        impl.setdefault(name, []).append((rel, line, text))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    rows = 0
    with OUT.open("w", encoding="utf-8") as f:
        f.write("# API contract: tModLoader 1.4.4.9 (tmodloader/) -> 1.4.5 (tModLoader-1.4.5/)\n")
        f.write(f"# contract = {CONTRACT}\n# implement = {IMPL}\n")
        f.write("# nome | 1.4.4.9 arquivo:linha | 1.4.5 arquivo:linha | declaracao\n")
        for name in sorted(set(contract) | set(impl)):
            c = contract.get(name, [])
            i = impl.get(name, [])
            cpos = ";".join(f"{r}:{ln}" for r, ln, _ in c[:3]) or "-"
            ipos = ";".join(f"{r}:{ln}" for r, ln, _ in i[:3]) or "-"
            text = (c or i)[0][2]
            f.write(f"{name}\t{cpos}\t{ipos}\t{text}\n")
            rows += 1
    print(f"contrato: {OUT} ({rows} membros)")
    print(f"  1.4.4.9: {len(contract)} nomes | 1.4.5: {len(impl)} nomes")


def load_index():
    if not OUT.exists():
        build()
    rows = []
    for line in OUT.read_text(encoding="utf-8").splitlines():
        if line.startswith("#") or not line.strip():
            continue
        parts = line.split("\t", 3)
        if len(parts) == 4:
            rows.append(parts)
    return rows


OPERATORS = {
    "op_Equality": "operator ==", "op_Inequality": "operator !=",
    "op_Addition": "operator +", "op_Subtraction": "operator -",
    "op_Multiply": "operator *", "op_Division": "operator /",
    "op_Explicit": "explicit operator", "op_Implicit": "implicit operator",
}


def find(term: str):
    # Nomes vindos do IL (get_X / op_Equality) não aparecem literalmente na
    # fonte C#: traduz para a forma que o código usa.
    terms = [term]
    if term in OPERATORS:
        terms.append(OPERATORS[term])
    elif term.startswith(("get_", "set_")):
        terms.append(term[4:])
    found = 0
    for name, cpos, ipos, text in load_index():
        if not any(t.lower() in name.lower() or t.lower() in text.lower() for t in terms):
            continue
        print(f"{name}\n  1.4.4.9: {cpos}\n  1.4.5  : {ipos}\n  {text}")
        found += 1
        if found >= 25:
            print("  ... (limite de 25)")
            break
    if not found:
        print(f"sem ocorrências de {term!r}")


def report(path: Path):
    """Extrai SÓ o membro ausente de cada assinatura e cruza com o índice."""
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    sigs = re.findall(r"Method not found:\s*'([^']+)'", text)
    sigs += re.findall(r"Could not load type\s*'([^']+)'", text)
    names = []
    for sig in sigs:
        # `Retorno Tipo::metodo(args)` -> metodo; `Tipo` puro -> ultimo segmento
        m = re.search(r"::(\w+)\s*\(", sig) or re.search(r"\b(\w+)\s*\(", sig)
        name = m.group(1) if m else sig.strip("' ").split(".")[-1].split(",")[0]
        name = name.strip()
        if name and name not in BAD and name not in names:
            names.append(name)
    rows = load_index()
    shown = 0
    for name in names[:40]:
        terms = [name]
        if name in OPERATORS:
            terms.append(OPERATORS[name])
        elif name.startswith(("get_", "set_")):
            terms.append(name[4:])
        hits = [r for r in rows
                if any(t.lower() == r[0].lower() or t.lower() in r[3].lower() for t in terms)]
        if not hits:
            print(f"{name}: sem entrada no contrato")
            continue
        _, cpos, ipos, decl = hits[0]
        print(f"{name}\n  1.4.4.9: {cpos}\n  1.4.5  : {ipos}\n  {decl}")
        shown += 1
    print(f"# {len(names)} membros ausentes | {shown} localizados em {path}")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "build"
    if cmd == "build":
        build()
    elif cmd == "find" and len(sys.argv) > 2:
        find(sys.argv[2])
    elif cmd == "report" and len(sys.argv) > 2:
        report(Path(sys.argv[2]))
    else:
        print(__doc__)
