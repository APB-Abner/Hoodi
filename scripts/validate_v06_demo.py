"""Validate the incremental demo contract without changing stable baselines."""
from pathlib import Path
import yaml
from jsonschema import Draft202012Validator

root = Path(__file__).resolve().parents[1]
spec = yaml.safe_load((root / "contracts/v0.6/demo.openapi.yaml").read_text(encoding="utf-8"))

def require(condition, message):
    if not condition:
        raise ValueError(message)

def check_refs(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key == "$ref":
                require(item.startswith("#/"), "Unexpected external reference")
                current = spec
                for part in item[2:].split("/"):
                    current = current[part]
            else:
                check_refs(item)
    elif isinstance(value, list):
        for item in value:
            check_refs(item)

check_refs(spec)
require(len(spec["paths"]) == 7, "Expected five panic operations plus logout/deletion")
for path, operations in spec["paths"].items():
    for method, operation in operations.items():
        require(operation["security"] == [{"bearerAuth": []}], path + " must require a user JWT")
        require("401" in operation["responses"], path + " must document 401")
delete = spec["paths"]["/v1/conta"]["delete"]
require(set(delete["responses"]) == {"204", "400", "401", "403", "429", "500"}, "Deletion responses differ")
schema = delete["requestBody"]["content"]["application/json"]["schema"]
validator = Draft202012Validator(schema)
validator.validate({"senha": "wrong-on-purpose"})
for invalid in ({}, {"senha": ""}, {"senha": "wrong", "usuarioId": "other"}):
    require(not validator.is_valid(invalid), "Deletion schema accepted invalid input")
panic = spec["components"]["schemas"]["PanicoEntrada"]
validator = Draft202012Validator(panic)
base = {"idempotencyKey": "11111111-1111-4111-8111-111111111111", "origem": "app"}
validator.validate(base)
validator.validate(dict(base, latitude=None, longitude=None))
validator.validate(dict(base, latitude=-23.5, longitude=-46.6))
for invalid in (dict(base, latitude=None), dict(base, latitude=1, longitude=None), dict(base, latitude=91, longitude=1), dict(base, usuarioId="other")):
    require(not validator.is_valid(invalid), "Panic schema accepted invalid coordinate/identity input")
print("PASS: v0.6 partial demo contract, references, authentication, responses and schema negatives")
