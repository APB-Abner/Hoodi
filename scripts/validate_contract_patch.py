"""Validate the scoped v0.5.2/v0.5.3 patches. Requires existing PyYAML and jsonschema."""
from copy import deepcopy
from pathlib import Path
import hashlib
import yaml
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "contracts/baseline/openapi.v0.5.1.yaml"
PREVIOUS = ROOT / "contracts/baseline/openapi.v0.5.2.yaml"
PATCH = ROOT / "contracts/baseline/openapi.v0.5.3.yaml"
REDEEM = "/v1/contatos/convites/resgatar"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validator(schema, document):
    schema = deepcopy(schema)
    schema["components"] = document["components"]
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema)


def without_info_metadata(document):
    result = deepcopy(document)
    result["info"].pop("version")
    result["info"].pop("description")
    return result


def validate_052(baseline, previous):
    old = without_info_metadata(baseline)
    new = without_info_metadata(previous)
    old["paths"].pop(REDEEM)
    new["paths"].pop(REDEEM)
    require(old == new, "v0.5.2 modified unrelated content")
    before = baseline["paths"][REDEEM]["post"]
    after = previous["paths"][REDEEM]["post"]
    require(before["requestBody"] == after["requestBody"], "v0.5.2 request format changed")
    require(before["responses"]["200"]["content"] == after["responses"]["200"]["content"], "Acceptance schema changed")
    require("content" not in after["responses"]["204"], "Rejection must be empty")


def validate_053(previous, patched):
    old = without_info_metadata(previous)
    new = without_info_metadata(patched)
    for path in (REDEEM, "/v1/auth/login", "/v1/auth/renovar"):
        response = new["paths"][path]["post"]["responses"].pop("400")
        require(response["content"]["application/problem+json"]["schema"]["$ref"] == "#/components/schemas/Erro", "Wrong 400 response schema")
    for name in ("NovoConvite", "ResgateConviteEntrada"):
        code = new["components"]["schemas"][name]["properties"]["codigo"]
        require(code["minLength"] == code["maxLength"] == 64, "Code length must be 64")
        require(code["pattern"] == "^[0-9A-F]{64}$", "Canonical code pattern changed")
        require("hoodi://convite#codigo=" in code["description"], "Fragment transport missing")
        new["components"]["schemas"][name]["properties"]["codigo"] = old["components"]["schemas"][name]["properties"]["codigo"]
    example = new["paths"]["/v1/contatos/convites"]["post"]["responses"]["201"]["content"]["application/json"]["example"]
    validator(patched["components"]["schemas"]["NovoConvite"], patched).validate(example)
    example["codigo"] = old["paths"]["/v1/contatos/convites"]["post"]["responses"]["201"]["content"]["application/json"]["example"]["codigo"]
    require(old == new, "v0.5.3 modified unrelated content")
    redeem_schema = validator(patched["components"]["schemas"]["ResgateConviteEntrada"], patched)
    code = "0123456789ABCDEF" * 4
    for incoming in (code, code.lower(), "  " + code.lower() + "  "):
        redeem_schema.validate({"codigo": incoming.strip().upper(), "decisao": "aceitar"})
    for invalid in ("ABC", "A" * 65, "Z" * 64):
        require(not redeem_schema.is_valid({"codigo": invalid, "decisao": "aceitar"}), "Malformed code accepted")
    require(not redeem_schema.is_valid({"decisao": "aceitar"}), "Missing code accepted")


def main():
    baseline, previous, patched = [yaml.safe_load(path.read_text(encoding="utf-8-sig")) for path in (BASELINE, PREVIOUS, PATCH)]
    require(baseline["info"]["version"] == "0.5.1", "Wrong baseline version")
    require(previous["info"]["version"] == "0.5.2", "Wrong previous patch version")
    require(patched["info"]["version"] == "0.5.3", "Wrong current patch version")
    validate_052(baseline, previous)
    validate_053(previous, patched)
    responses = patched["paths"][REDEEM]["post"]["responses"]
    require("content" not in responses["204"], "Rejection body introduced")
    error = validator(responses["422"]["content"]["application/problem+json"]["schema"], patched)
    own = {"title": "Own relationship", "status": 422, "code": "VINCULO_PROPRIO", "traceId": "synthetic"}
    error.validate(own)
    require(not error.is_valid({**own, "status": 409}), "Wrong own-link status allowed")
    require(not error.is_valid({**own, "code": "OTHER"}), "Wrong own-link code allowed")
    def check_refs(value):
        if isinstance(value, dict):
            ref = value.get("$ref")
            if ref and ref.startswith("#/"):
                target = patched
                for part in ref[2:].split("/"):
                    target = target[part.replace("~1", "/").replace("~0", "~")]
            for item in value.values():
                check_refs(item)
        elif isinstance(value, list):
            for item in value:
                check_refs(item)
    check_refs(patched)
    print("PASS: v0.5.2 preserved; v0.5.3 limited to invitation codes/examples and actual 400 responses; normalization/schema negatives and references pass.")
    for path in (BASELINE, PREVIOUS):
        print(path.name, "SHA-256:", hashlib.sha256(path.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()
