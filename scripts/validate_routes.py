"""Validate route-reference provenance and the incremental request schema."""
import hashlib
import json
from pathlib import Path
import yaml
from jsonschema import Draft202012Validator

root = Path(__file__).resolve().parents[1]
VERSIONS = {
    2: ("data/routes/v2", "vetores_rota_v2.json", "data/risk/v2/manifest.json", "93b1980b88eecae77c744bc4b78084b3d6eb26ae3484908d5c077d4b656fac84"),
    3: ("data/routes/v3", "vetores_rota_v3.json", "data/risk/v3/manifest.json", "47bc78f8adc2ceb7dec46be5cc5539b032cfa3dae00e9b0d14dbad21cc19798a"),
}
def require(value, message):
    if not value:
        raise ValueError(message)
for contract, (folder_name, name, manifest_path, expected) in VERSIONS.items():
    folder = root / folder_name
    vectors = (folder / name).read_bytes()
    sha = hashlib.sha256(vectors).hexdigest()
    require(sha == expected, f"Producer vectors v{contract} changed")
    require((folder / (name + ".sha256")).read_text().split()[0] == sha, f"Vector v{contract} checksum differs")
    require(json.loads(vectors)["snapshot_csv_sha256"] == json.loads((root / manifest_path).read_bytes())["csv_sha256"], f"Vector/snapshot v{contract} mismatch")
spec = yaml.safe_load((root / "contracts/v0.6/routes.openapi.yaml").read_text(encoding="utf-8"))
def refs(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key == "$ref":
                current = spec
                require(item.startswith("#/"), "External reference")
                for part in item[2:].split("/"):
                    current = current[part]
            else:
                refs(item)
    elif isinstance(value, list):
        for item in value:
            refs(item)
refs(spec)
op = spec["paths"]["/v1/rotas/avaliar"]["post"]
require(op["security"] == [{"bearerAuth": []}], "JWT required")
require(set(op["responses"]) == {"200", "400", "401", "413", "422", "429"}, "Response set differs")
validator = Draft202012Validator(spec["components"]["schemas"]["Point"])
validator.validate({"latitude": -23.56, "longitude": -46.65})
for invalid in ({}, {"latitude": 1}, {"latitude": 91, "longitude": 1}, {"latitude": 1, "longitude": 180}):
    require(not validator.is_valid(invalid), "Invalid coordinate accepted")
print("PASS: route vector SHA, snapshot binding, schema references and request bounds")
