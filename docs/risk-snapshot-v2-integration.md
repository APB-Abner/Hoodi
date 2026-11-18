# Risk snapshot integration (contract v2 and v3)

> **Current release: contract v3** (`data/risk/v3`, semestral window Apr–Sep 2026, same 5×5 km perimeter, same CSV schema, k ≥ 3). The v2 release (`data/risk/v2`, Jan–Sep 2026) stays valid for rollback. The decoder reads `contrato_versao` from the manifest and applies that version's exact window, formula and nature text; any other value is rejected. v3 has 824 `com_dado` and 571 `sem_dado` keys (v2: 954/441); level cuts 0.305556 / 0.611111 / 1.277778. Hashes: [data/risk/v3/SOURCE.md](../data/risk/v3/SOURCE.md). Route vectors exist for both releases (`data/routes/v2`, `data/routes/v3`) and `validate_routes.py` checks each against its own snapshot hash.

The sections below describe the shared mechanism; examples use the v3 file names. For v2 replace `v3` by `v2`, `nando-v3-2026-abr-set` by `nando-v2-2026-jan-set`, and set the three `Risk__Approved*` hashes from `data/risk/v2/SOURCE.md`.

## Contract and provenance

The delivered producer v2 supersedes the historical [v1 decision record](risk-snapshot-v1-v0.6.md). Exact source/hashes: [data/risk/v2/SOURCE.md](../data/risk/v2/SOURCE.md). The complete v0.5.3 baseline remains preserved. [Incremental OpenAPI](../contracts/v0.6/risk.openapi.yaml) defines the H3 replacement for the previously unimplemented geohash grade operations; the app must adopt that increment explicitly.

`com_dado` requires at least **3** records. `sem_dado` represents zero or suppressed 1–2 records: null level/score and published count zero. `fora_cobertura` is separate, with null count/score/level. Levels remain 0–3; bands remain 0–4. No `tipoDia`, forecast or daily prediction is published.

## Validation

`pocketken.H3` **4.5.0** and NetTopologySuite are managed libraries, without native H3 installation on the VM. The adapter checks actual H3 validity/resolution and independently generates expected cells from the manifest rectangle by cell-boundary intersection. Tests establish exact equality with the producer's overlap release (v2 and v3): **279 cells × 5 bands = 1,395 rows**. This local WGS84 geometry is not a general polar/antimeridian polygon API.

Checks include exact SHA bytes, strict UTF-8/LF/header/order, duplicate keys, five bands per expected cell, period/timezone/bands/source/formula/privacy metadata, levels versus manifest training cuts and exact fixture membership across all levels plus sem_dado. Published counts plus suppressed counts must not exceed total source records used; the latter includes unweighted rubrics. Validation does not rerun ML or independently authenticate the raw SSP file.

Production import additionally pins approved CSV, manifest and fixture hashes. Arbitrary modified metadata/cuts are rejected even when CSV hash is unchanged. SQLite stores the entire artifact and changes the current-version pointer in one transaction. Reimport is idempotent; replaying an older existing version does not rewind the head. Failed validation or SQL failure preserves the prior snapshot. No new migration is needed.

## Operator procedure

The operator needs a valid authenticated session and its account ID in `Risk__ImporterUserIds__0` (additional entries `__1`, etc.). Default is empty: normal users cannot publish. Configure the VM and restart first. Do not enable import for every JWT user.

The v3 release's approved hashes are the defaults (v2 hashes are the rollback values). Updating `Risk__ApprovedCsvSha256`, `Risk__ApprovedManifestSha256` and `Risk__ApprovedFixtureSha256` requires a reviewed producer release and restart. A changed `gerado_em` changes the manifest hash even with the same CSV. Hashes are public integrity metadata, not credentials.

With the supplied files available from the repository:

```bash
curl --fail-with-body "$HOODI_BASE_URL/v1/admin/risco/importar" \
  -H "Authorization: Bearer $HOODI_OPERATOR_JWT" \
  -F 'versao=nando-v3-2026-abr-set' \
  -F 'csv=@data/risk/v3/risco_h3_faixa_v3.csv' \
  -F 'sha256=@data/risk/v3/risco_h3_faixa_v3.csv.sha256' \
  -F 'manifest=@data/risk/v3/manifest.json' \
  -F 'fixture=@data/risk/v3/fixture_v3.json'
```

Keep the JWT private in the operator environment; never commit/share it. First publication: 201; identical reimport: 200; non-operator: 403; invalid session: 401. Request limit is 1 MiB. Startup does not automatically publish a dataset.

## Consumption and deployment limits

- `GET /v1/risco/versao-atual`: current version, CSV hash, publication time and full manifest.
- `GET /v1/risco/grade`: exact complete CSV with quoted SHA ETag; matching `If-None-Match` returns 304.
- `GET /v1/risco/celulas/{h3Cell}?faixa=0`: explicit com_dado/sem_dado/fora_cobertura lookup.

Reads are public aggregates. Before first publication: 404 `SNAPSHOT_INDISPONIVEL`; invalid H3/band: 400. The full CSV replaces old paginated geohash/day JSON. Metadata/download calls may straddle publication: hash the exact bytes and retry both if inconsistent. Never combine mismatched versions or infer risk zero from absent data. Preserve the warning in the app.

Real-data tests exercise H3 geometry, producer files, API authorization, upload, download, fixture and malformed packages. Existing scaffold tests cover concurrent/idempotent publication and rollback after SQL writes. Route slicing/scoring/ranking and the HeiGIT adapter are implemented in the [route increment](routes-v0.6.md); live provider calls, VM/nginx import, native consumption and quota policy decisions remain integration work.

## Public read cost and rate limiting

Cell lookup uses a process-local immutable dictionary keyed by `(h3_cell, faixa)`. Each request queries only the current version/SHA/publication-time descriptor from SQLite. Artifact blobs and producer validation are loaded only on a cache miss. A shared semaphore coalesces concurrent misses; a failed or cancelled decode cannot publish a partial index. The cache holds one index, so old versions do not accumulate indefinitely. An import changes the descriptor and the next cell request rebuilds automatically, including a new version with identical CSV bytes. A request racing publication may complete with its coherent earlier version; responses never mix version metadata and another version's rows. Failure rebuilding the new head does not serve the cached old version as current.

The three public read routes share a fixed-window quota of **120 requests per minute per client IP**, with no queue; excess returns 429 `LIMITE_TENTATIVAS`. Trusted forwarded headers run before this limiter. The quota and cache are in-process and reset at restart, consistent with the current single-instance deployment. Metadata and CSV reads retain their existing behavior; the cell hot path avoids full-artifact reads and geometric work.

Tests record actual SQLite command projections on warm lookup, verify one decode for concurrent cold requests, refresh with the same CSV hash under a new version, rejection/retry after failed refresh, and separate forwarded-client IP quotas. The producer's provisional center remains unchanged; a finalized perimeter requires a newly reviewed package/hashes and import, never an in-place edit of the approved files.

## Open items for v3

- Imported v3 is an exploratory choice (test Aug–Sep 2026 already seen). The definitive check is the blind test on Oct–Nov 2026 data.
- Rollback to v2: set `Risk__ApprovedCsvSha256`, `Risk__ApprovedManifestSha256`, `Risk__ApprovedFixtureSha256` to the v2 values, restart, import with the v2 files. Replaying an older version does not rewind the head, so use a new `versao` label.
