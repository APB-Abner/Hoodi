"""Validate project/runtime contract version inputs and every local contract reference."""
import hashlib
from pathlib import Path
from urllib.parse import unquote, urlsplit

from build_api_contract import BASELINE, CONTRACTS, INCREMENTS, build, read_source, require


def main():
    current = read_source("openapi.yaml")
    require(current == build(), "Current contract is stale; run scripts/build_api_contract.py")
    baseline = (CONTRACTS / BASELINE).read_bytes().replace(b"\r\n", b"\n")
    require(hashlib.sha256(baseline).hexdigest() ==
            "fdbb7632fe6e80a1ed0c45983e21965c0caa5ba6c4a41049f4e132370054489c",
            "Preserved v0.5.3 baseline changed")
    documents = {str((CONTRACTS / name).resolve()): read_source(name)
                 for name in ["openapi.yaml", BASELINE, *INCREMENTS]}
    visited = set()

    def resolve(reference, origin):
        uri = urlsplit(reference)
        require(not uri.scheme and not uri.netloc and not uri.query,
                "Only repository-local references are allowed: " + reference)
        target = str((Path(origin).parent / unquote(uri.path)).resolve()) if uri.path else origin
        require(target in documents, "Unexpected contract source: " + reference)
        fragment = unquote(uri.fragment)
        require(not fragment or fragment.startswith("/"), "Invalid JSON pointer: " + reference)
        value = documents[target]
        for token in fragment[1:].split("/") if fragment else []:
            token = token.replace("~1", "/").replace("~0", "~")
            value = value[int(token)] if isinstance(value, list) else value[token]
        return target, fragment, value

    def check(value, origin):
        if isinstance(value, dict):
            if "$ref" in value:
                target, fragment, resolved = resolve(value["$ref"], origin)
                identity = (target, fragment)
                if identity not in visited:
                    visited.add(identity)
                    check(resolved, target)
            for child in value.values():
                check(child, origin)
        elif isinstance(value, list):
            for child in value:
                check(child, origin)

    for origin, document in documents.items():
        check(document, origin)
    operations = set()
    methods = {"get", "post", "put", "patch", "delete", "head", "options", "trace"}
    for path, item in current["paths"].items():
        _, _, resolved = resolve(item["$ref"], str((CONTRACTS / "openapi.yaml").resolve()))
        for method, operation in resolved.items():
            if method in methods:
                identity = operation["operationId"]
                require(identity not in operations, "Duplicate operationId: " + identity)
                operations.add(identity)
    print("PASS: version " + current["info"]["version"] + "; v0.5.3 preserved; " +
          str(len(operations)) + " design operations and all scoped references resolve")


if __name__ == "__main__":
    main()
