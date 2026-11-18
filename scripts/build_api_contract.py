"""Build the current OpenAPI entry point from preserved, explicitly scoped sources."""
from copy import deepcopy
from pathlib import Path
import argparse
import xml.etree.ElementTree as ET
from urllib.parse import quote

import yaml

ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "contracts"
BASELINE = "baseline/openapi.v0.5.3.yaml"
INCREMENTS = ["v0.6/logout.openapi.yaml", "v0.6/demo.openapi.yaml",
              "v0.6/risk.openapi.yaml", "v0.6/routes.openapi.yaml"]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def project_version():
    version = ET.parse(ROOT / "Hoodi.Api/Hoodi.Api.csproj").findtext("PropertyGroup/Version")
    require(bool(version), "Hoodi.Api.csproj must declare Version")
    return version


def read_source(name):
    return yaml.safe_load((CONTRACTS / name).read_text(encoding="utf-8-sig"))


def build():
    baseline = read_source(BASELINE)
    version = project_version()
    # Path-item references retain the originating document's schema reference scope.
    sources = {path: BASELINE for path in baseline["paths"]}
    incremental_paths = {}
    for name in INCREMENTS:
        document = read_source(name)
        require(document["info"]["version"] == version, name + " version differs from the API project")
        require(document.get("security", baseline["security"]) == baseline["security"], name + " global security differs")
        for key, scheme in document.get("components", {}).get("securitySchemes", {}).items():
            require(baseline["components"]["securitySchemes"].get(key) == scheme, "Security scheme collision: " + key)
        for path, item in document["paths"].items():
            require(path not in incremental_paths or incremental_paths[path] == item,
                    "Conflicting increments for " + path)
            incremental_paths[path] = item
            sources[path] = name
    result = {
        "openapi": baseline["openapi"],
        "info": {
            "title": "hoodi API",
            "version": version,
            "description": (
                "Consolidated draft design contract: preserved v0.5.3 operations plus "
                "the v0.6 logout, account/panic, H3 and route increments. Includes planned "
                "operations; /openapi/v1.json lists the operations implemented by the API. "
                "Version comes from Hoodi.Api/Hoodi.Api.csproj. Regenerate with "
                "python scripts/build_api_contract.py."
            ),
        },
    }
    for key in ("servers", "tags", "security"):
        if key in baseline:
            result[key] = deepcopy(baseline[key])
    result["paths"] = {
        path: {"$ref": name + "#/paths/" + quote(path.replace("~", "~0").replace("/", "~1"), safe="~")}
        for path, name in sources.items()
    }
    result["components"] = {"securitySchemes": deepcopy(baseline["components"]["securitySchemes"])}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check the committed entry point without writing")
    args = parser.parse_args()
    expected = build()
    target = CONTRACTS / "openapi.yaml"
    if args.check:
        require(read_source("openapi.yaml") == expected, "Current contract is stale; run scripts/build_api_contract.py")
    else:
        target.write_text(yaml.safe_dump(expected, allow_unicode=True, sort_keys=False), encoding="utf-8", newline="\n")
    print("PASS: current contract " + expected["info"]["version"] + " matches the API project and source increments")


if __name__ == "__main__":
    main()
