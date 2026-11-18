"""Monta wokwi/projeto_web/ para colar no wokwi.com com o minimo de abas:

  sketch.ino     (so um comentario)
  hoodi.cpp      (todo o firmware num arquivo, com HOODI_SEM_BLE 1: o Wokwi nao simula Bluetooth)
  diagram.json   (SuperMini, OLED, LED no lugar do motor, buzzer no lugar do alto-falante, 3 botoes)
  libraries.txt  (Adafruit SSD1306 + GFX)

O codigo vai num .cpp (e nao no .ino) para o Arduino nao gerar prototipos automaticos dentro de namespace.
Uso (na raiz do repositorio): python3 scripts/gerar_wokwi_web.py
"""
from pathlib import Path
import re
import shutil

raiz = Path(__file__).resolve().parent.parent
origem = raiz / "firmware" / "hoodi_pet"
destino = raiz / "wokwi" / "projeto_web"
ordem = ["config.h", "protocolo.h", "vibracao.h", "som.h", "tela.h", "ble_pet.h", "tela.cpp", "pet.cpp"]

partes = [
    "// hoodi Pet - firmware num arquivo so, gerado por scripts/gerar_wokwi_web.py. Nao edite aqui:\n"
    "// edite firmware/hoodi_pet/ e gere de novo. Bluetooth desligado (o Wokwi nao simula BLE).\n"
    "#define HOODI_SEM_BLE 1\n"
]
for nome in ordem:
    texto = (origem / nome).read_text(encoding="utf-8")
    texto = re.sub(r'^\s*#pragma once\s*$', "", texto, flags=re.M)
    texto = re.sub(r'^\s*#include "[^"]+"\s*$', "", texto, flags=re.M)
    partes.append(f"\n// ===== {nome} =====\n{texto.strip()}\n")

if destino.exists():
    shutil.rmtree(destino)
destino.mkdir(parents=True)
(destino / "hoodi.cpp").write_text("".join(partes), encoding="utf-8")
(destino / "sketch.ino").write_text(
    "// hoodi Pet no Wokwi: o codigo esta na aba hoodi.cpp (setup e loop estao la).\n"
    "// Serial Monitor: n0 n1 n2 n3 n? id sil fim st\n"
    "// Botoes: 1 amarelo (GPIO4) acende a tela | 2 preto (GPIO10) silencia | 3 vermelho (GPIO1 aqui,\n"
    "// GPIO20 no Pet real) segurar 3 s = pedido de ajuda. Buzzer no GPIO5 (no Pet real, via transistor).\n",
    encoding="utf-8",
)
for arquivo in ("diagram.json", "libraries.txt"):
    shutil.copy(raiz / "wokwi" / arquivo, destino / arquivo)

print(f"ok: {destino}")
for f in sorted(destino.iterdir()):
    print(f"   {f.name} ({f.stat().st_size} bytes)")
