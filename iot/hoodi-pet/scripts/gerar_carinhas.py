"""Fonte unica das carinhas do hoodi Pet, no estilo do app da Mari (grade 18 x 12, pixel art).

Gera, a partir das matrizes abaixo:
  firmware/hoodi_pet/carinhas.h   matrizes para o OLED
  docs/carinhas.json              mesmas matrizes para o app (React Native) usar igual
  docs/carinhas_preview.png       folha de previa para aprovar

'#' = quadradinho aceso, '.' = apagado. Mude aqui e rode: python3 scripts/gerar_carinhas.py
"""
from pathlib import Path
import json

LARGURA, ALTURA = 18, 12

CARINHAS = {
    # nivel 0: olhos abertos, sorriso
    "tranquila": [
        "..................",
        "..................",
        "..................",
        ".....##....##.....",
        ".....##....##.....",
        ".....##....##.....",
        "..................",
        "..................",
        ".......#..#.......",
        "........##........",
        "..................",
        "..................",
    ],
    # nivel 1: olhos abertos, boca reta
    "atenta": [
        "..................",
        "..................",
        "..................",
        ".....##....##.....",
        ".....##....##.....",
        ".....##....##.....",
        "..................",
        "..................",
        "..................",
        ".......####.......",
        "..................",
        "..................",
    ],
    # nivel 2: sobrancelhas de preocupacao, boca triste
    "preocupada": [
        "..................",
        "......#....#......",
        ".....#......#.....",
        "..................",
        ".....##....##.....",
        ".....##....##.....",
        ".....##....##.....",
        "..................",
        "..................",
        "........##........",
        ".......#..#.......",
        "..................",
    ],
    # nivel 3: olhos arregalados, boca aberta, exclamacao
    "alerta": [
        "..................",
        "..................",
        "................#.",
        "....###....###..#.",
        "....###....###..#.",
        "....###....###..#.",
        "................#.",
        "........##........",
        ".......#..#.....#.",
        ".......#..#.......",
        "........##........",
        "..................",
    ],
    # sem dado / sem GPS / fora da cobertura: interrogacao, nunca vibra
    "desconhecido": [
        "..................",
        "........###.......",
        ".......#...#......",
        "...........#......",
        "..........#.......",
        ".........#........",
        ".........#........",
        "..................",
        ".........#........",
        "..................",
        "..................",
        "..................",
    ],
    # fora de trajeto: olhos fechados e um "z"
    "dormindo": [
        ".............####.",
        "...............#..",
        "..............#...",
        ".............####.",
        "..................",
        ".....##....##.....",
        "..................",
        "..................",
        "..................",
        "........##........",
        "..................",
        "..................",
    ],
    # celular desconectou no meio do trajeto: olhar baixo, boca pequena e um "x" no canto
    "desconectado": [
        "..............#.#.",
        "...............#..",
        "..............#.#.",
        "..................",
        ".....##....##.....",
        ".....##....##.....",
        "..................",
        "..................",
        "..................",
        "........##........",
        "..................",
        "..................",
    ],
}

# Piscada: troca os olhos por uma linha so (como no app). Vale para quem tem olho 2x3 aberto.
PISCAM = ["tranquila", "atenta"]


def piscar(grade):
    nova = [list(l) for l in grade]
    for r in (3, 4):  # apaga as duas linhas de cima do olho, fica so a de baixo (linha 5)
        for c in range(LARGURA):
            if nova[r][c] == "#":
                nova[r][c] = "."
    return ["".join(l) for l in nova]


def validar():
    for nome, grade in CARINHAS.items():
        assert len(grade) == ALTURA, f"{nome}: {len(grade)} linhas"
        for i, linha in enumerate(grade):
            assert len(linha) == LARGURA, f"{nome} linha {i}: {len(linha)} colunas"
            assert set(linha) <= {".", "#"}, f"{nome} linha {i}: caractere invalido"


def gerar_header(raiz: Path):
    ordem = ["tranquila", "atenta", "preocupada", "alerta", "desconhecido", "dormindo", "desconectado"]
    linhas = [
        "// GERADO por scripts/gerar_carinhas.py. Nao edite aqui: edite o script e gere de novo.",
        "// Grade 18 x 12 no estilo do app (pixel art). '#' aceso, '.' apagado.",
        "#pragma once",
        "",
        "namespace carinhas {",
        "",
        f"constexpr int LARGURA = {LARGURA};",
        f"constexpr int ALTURA = {ALTURA};",
        "",
        "// Mesma ordem do enum hoodi::Expressao (0..5) + Desconectado (6).",
    ]
    for nome in ordem:
        linhas.append(f"constexpr const char* {nome.upper()}[ALTURA] = {{")
        linhas += [f'  "{l}",' for l in CARINHAS[nome]]
        linhas.append("};")
    for nome in PISCAM:
        linhas.append(f"constexpr const char* {nome.upper()}_PISCANDO[ALTURA] = {{")
        linhas += [f'  "{l}",' for l in piscar(CARINHAS[nome])]
        linhas.append("};")
    linhas += ["", "}  // namespace carinhas", ""]
    (raiz / "firmware" / "hoodi_pet" / "carinhas.h").write_text("\n".join(linhas), encoding="utf-8")


def gerar_json(raiz: Path):
    dados = {
        "largura": LARGURA,
        "altura": ALTURA,
        "legenda": {"#": "aceso", ".": "apagado"},
        "porNivel": {"0": "tranquila", "1": "atenta", "2": "preocupada", "3": "alerta", "semDado": "desconhecido"},
        "carinhas": CARINHAS,
        "piscando": {n: piscar(CARINHAS[n]) for n in PISCAM},
    }
    (raiz / "docs" / "carinhas.json").write_text(json.dumps(dados, indent=2, ensure_ascii=False), encoding="utf-8")


def gerar_previa(raiz: Path):
    from PIL import Image, ImageDraw

    cel, gap, margem, rotulo = 14, 2, 16, 26
    nomes = list(CARINHAS) + [f"{n} (piscando)" for n in PISCAM]
    grades = list(CARINHAS.values()) + [piscar(CARINHAS[n]) for n in PISCAM]
    w_card = LARGURA * cel + 2 * margem
    h_card = ALTURA * cel + 2 * margem + rotulo
    cols = 3
    linhas = (len(grades) + cols - 1) // cols
    img = Image.new("RGB", (cols * (w_card + 12) + 12, linhas * (h_card + 12) + 12), (17, 17, 19))
    d = ImageDraw.Draw(img)
    for i, (nome, grade) in enumerate(zip(nomes, grades)):
        x0 = 12 + (i % cols) * (w_card + 12)
        y0 = 12 + (i // cols) * (h_card + 12)
        d.rounded_rectangle([x0, y0, x0 + w_card, y0 + h_card - rotulo], radius=14, outline=(110, 110, 115), width=2,
                            fill=(26, 26, 28))
        for r, linha in enumerate(grade):
            for c, ch in enumerate(linha):
                cx, cy = x0 + margem + c * cel, y0 + margem + r * cel
                cor = (236, 236, 230) if ch == "#" else (34, 34, 37)
                d.rectangle([cx, cy, cx + cel - gap, cy + cel - gap], fill=cor)
        d.text((x0 + 4, y0 + h_card - rotulo + 6), nome, fill=(200, 200, 200))
    img.save(raiz / "docs" / "carinhas_preview.png")


if __name__ == "__main__":
    raiz = Path(__file__).resolve().parent.parent
    validar()
    gerar_header(raiz)
    gerar_json(raiz)
    gerar_previa(raiz)
    print("ok: carinhas.h, carinhas.json, carinhas_preview.png")
