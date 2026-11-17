# Risk snapshot v2 integration

## Contract and provenance

The delivered producer v2 supersedes the historical [v1 decision record](risk-snapshot-v1-v0.6.md). Exact source/hashes: [data/risk/v2/SOURCE.md](../data/risk/v2/SOURCE.md). The complete v0.5.3 baseline remains preserved. [Incremental OpenAPI](../contracts/v0.6/risk.openapi.yaml) defines the H3 replacement for the previously unimplemented geohash grade operations; the app must adopt that increment explicitly.

`com_dado` requires at least **3** records. `sem_dado` represents zero or suppressed 1–2 records: null level/score and published count zero. `fora_cobertura` is separate, with null count/score/level. Levels remain 0–3; bands remain 0–4. No `tipoDia`, forecast or daily prediction is published.

## Validation

`pocketken.H3` **4.5.0** and NetTopologySuite are managed libraries, without native H3 installation on the VM. The adapter checks actual H3 validity/resolution and independently generates expected cells from the manifest rectangle by cell-boundary intersection. Tests establish exact equality with the producer's overlap release: **279 cells × 5 bands = 1,395 rows**. This local WGS84 geometry is not a general polar/antimeridian polygon API.

Checks include exact SHA bytes, strict UTF-8/LF/header/order, duplicate keys, five bands per expected cell, period/timezone/bands/source/formula/privacy metadata, levels versus manifest training cuts and exact fixture membership across all levels plus sem_dado. Published counts plus suppressed counts must not exceed total source records used; the latter includes unweighted rubrics. Validation does not rerun ML or independently authenticate the raw SSP file.

Production import additionally pins approved CSV, manifest and fixture hashes. Arbitrary modified metadata/cuts are rejected even when CSV hash is unchanged. SQLite stores the entire artifact and changes the current-version pointer in one transaction. Reimport is idempotent; replaying an older existing version does not rewind the head. Failed validation or SQL failure preserves the prior snapshot. No new migration is needed.

## Operator procedure

The operator needs a valid authenticated session and its account ID in `Risk__ImporterUserIds__0` (additional entries `__1`, etc.). Default is empty: normal users cannot publish. Configure the VM and restart first. Do not enable import for every JWT user.

The supplied release's approved hashes are defaults. Updating `Risk__ApprovedCsvSha256`, `Risk__ApprovedManifestSha256` and `Risk__ApprovedFixtureSha256` requires a reviewed producer release and restart. A changed `gerado_em` changes the manifest hash even with the same CSV. Hashes are public integrity metadata, not credentials.

With the supplied files available from the repository:

```bash
curl --fail-with-body "$HOODI_BASE_URL/v1/admin/risco/importar" \
  -H "Authorization: Bearer $HOODI_OPERATOR_JWT" \
  -F 'versao=nando-v2-2026-jan-set' \
  -F 'csv=@data/risk/v2/risco_h3_faixa_v2.csv' \
  -F 'sha256=@data/risk/v2/risco_h3_faixa_v2.csv.sha256' \
  -F 'manifest=@data/risk/v2/manifest.json' \
  -F 'fixture=@data/risk/v2/fixture_v2.json'
```

Keep the JWT private in the operator environment; never commit/share it. First publication: 201; identical reimport: 200; non-operator: 403; invalid session: 401. Request limit is 1 MiB. Startup does not automatically publish a dataset.

## Consumption and deployment limits

- `GET /v1/risco/versao-atual`: current version, CSV hash, publication time and full manifest.
- `GET /v1/risco/grade`: exact complete CSV with quoted SHA ETag; matching `If-None-Match` returns 304.
- `GET /v1/risco/celulas/{h3Cell}?faixa=0`: explicit com_dado/sem_dado/fora_cobertura lookup.

Reads are public aggregates. Before first publication: 404 `SNAPSHOT_INDISPONIVEL`; invalid H3/band: 400. The full CSV replaces old paginated geohash/day JSON. Metadata/download calls may straddle publication: hash the exact bytes and retry both if inconsistent. Never combine mismatched versions or infer risk zero from absent data. Preserve the warning in the app.

Real-data tests exercise H3 geometry, producer files, API authorization, upload, download, fixture and malformed packages. Existing scaffold tests cover concurrent/idempotent publication and rollback after SQL writes. VM/nginx import, mobile/native consumption, route provider and quota policy decisions remain separate integration work.
