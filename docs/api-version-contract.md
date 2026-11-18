# API version and current contract

`Hoodi.Api/Hoodi.Api.csproj` owns the API's `Version`. The SDK emits it as assembly
informational version metadata; `ApiInfo` reads that metadata from the API assembly,
not from the test host or a second constant. Automatic source revision suffixes are
disabled so the public version stays stable between commits of the same draft.
`/health` and generated OpenAPI use this value.

`contracts/openapi.yaml` is the current draft design entry point. Its version comes
from the same project property. Its path-item references select the preserved v0.5.3
operations or their v0.6 replacements, including logout, account/panic, H3 and routes.
References to schemas keep the scope of their source document, avoiding accidental
collisions between historical and current component definitions. OpenAPI tooling
must resolve the local files alongside the entry point.

The entry point includes planned operations inherited from the design contract.
It does not claim they are implemented: runtime `/openapi/v1.json` remains the
inventory of implemented operations. The v0.5.1, v0.5.2 and v0.5.3 baselines are
preserved under `contracts/baseline/`.

For a version change, update the API project and current increments, then run:

```powershell
python scripts/build_api_contract.py
python scripts/build_api_contract.py --check
python scripts/validate_api_contract.py
python scripts/validate_contract_patch.py
```

Validation fails on version drift, a stale entry point, conflicting increments,
changed v0.5.3 baseline content, unresolved references or duplicate operation IDs.
The existing HTTP integration test compares health/OpenAPI versions to the API's
actual assembly informational version. No dependency or wire version is changed
by moving the existing `0.6.0-draft` value into project metadata.

Validation for this change: 23/23 deployment and security-header integration tests
passed (`artifacts/test-results/api-project-version.trx`). Current-contract validation
and the existing baseline/demo/risk/route validators passed normally and with `python -O`.
