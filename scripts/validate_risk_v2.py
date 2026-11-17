"""Verify the supplied producer package and the incremental public risk schema."""
import csv
import hashlib
import io
import json
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator

root = Path(__file__).resolve().parents[1]
folder = root / "data/risk/v2"
spec = yaml.safe_load((root / "contracts/v0.6/risk.openapi.yaml").read_text(encoding="utf-8"))

def require(condition, message):
    if not condition:
        raise ValueError(message)

expected = {
    "risco_h3_faixa_v2.csv": "2dddef701bdecb65142874980f1ae5aba7b2ac6ab2abad5c47b07f07ecc5080a",
    "manifest.json": "fc89b872bc003ff45c4a98b740fec4fc03c56bcf9633d834c4503dc57af158ab",
    "fixture_v2.json": "337c5dcf35667752e1ee950425135f17e985c6d0e42e85f7f5df6ddcc043235d",
}
for name, sha in expected.items():
    require(hashlib.sha256((folder / name).read_bytes()).hexdigest() == sha, name + " bytes changed")
manifest = json.loads((folder / "manifest.json").read_bytes())
require((folder / "risco_h3_faixa_v2.csv.sha256").read_text().split() == [manifest["csv_sha256"], "risco_h3_faixa_v2.csv"], "Checksum file differs")
raw = (folder / "risco_h3_faixa_v2.csv").read_bytes()
require(b"\r" not in raw, "CSV must retain LF")
rows = list(csv.DictReader(io.StringIO(raw.decode("utf-8"))))
require(len(rows) == 1395 and len({r["h3_cell"] for r in rows}) == 279, "Producer counts changed")

def refs(value):
    if isinstance(value, dict):
        for key, child in value.items():
            if key == "$ref":
                current = spec
                require(child.startswith("#/"), "Unexpected external reference")
                for part in child[2:].split("/"):
                    current = current[part]
            else:
                refs(child)
    elif isinstance(value, list):
        for child in value:
            refs(child)

refs(spec)
require(spec["paths"]["/v1/admin/risco/importar"]["post"]["security"] == [{"bearerAuth": []}], "Import authentication missing")
schema = spec["paths"]["/v1/risco/celulas/{h3Cell}"]["get"]["responses"]["200"]["content"]["application/json"]["schema"]
validator = Draft202012Validator(schema)
base = dict(versao="v2", sha256=manifest["csv_sha256"], h3Cell="89a8100c00bffff", faixa=0)
for fields in (
    dict(status="com_dado", nivel=0, pontuacaoMensal=0.1, nRegistros=3),
    dict(status="sem_dado", nivel=None, pontuacaoMensal=None, nRegistros=0),
    dict(status="fora_cobertura", nivel=None, pontuacaoMensal=None, nRegistros=None),
):
    validator.validate(base | fields)
for fields in (
    dict(status="com_dado", nivel=0, pontuacaoMensal=0.1, nRegistros=2),
    dict(status="sem_dado", nivel=0, pontuacaoMensal=None, nRegistros=0),
    dict(status="fora_cobertura", nivel=None, pontuacaoMensal=None, nRegistros=0),
):
    require(not validator.is_valid(base | fields), "Risk schema accepted inconsistent status")
print("PASS: producer v2 exact bytes, counts, risk contract references and unknown-data semantics")
