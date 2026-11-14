# Risk snapshot v1 — v0.6 decision record

Status: producer definitions confirmed by the user on 2026-11-14. This supersedes the 09/11 consolidated document and the earlier draft. It is not an implemented risk API or evidence that producer files have been received.

## Spatial and temporal keys

- Spatial index: H3, resolution 9.
- Identifier: `h3_cell`, 15 lowercase hexadecimal characters. Format alone is not enough to establish a valid resolution-9 H3 cell.
- Composite key: (`h3_cell`, `faixa`). There is no `tipoDia` in the producer's confirmed v1 key.
- Timezone: `America/Sao_Paulo`; use local civil hour to select the half-open interval.

| faixa | Meaning | Local interval | Hours |
| --- | --- | --- | --- |
| 0 | madrugada | [00:00, 06:00) | 6 |
| 1 | manhã | [06:00, 12:00) | 6 |
| 2 | tarde | [12:00, 18:00) | 6 |
| 3 | noite_cedo | [18:00, 21:00) | 3 |
| 4 | noite | [21:00, 24:00) | 3 |

`faixa` has values 0–4. `nivel` has values 0–3. These are different domains; the earlier 0–4 risk-level statement is superseded.

## Producer artifacts

- `risco_h3_faixa_v1.csv`: complete app snapshot, UTF-8, comma-delimited, LF, decimal point.
- Accompanying `.sha256`: verify against the exact CSV bytes, before any normalization or serialization. Exact checksum filename/layout must be checked against the delivered artifact.
- `manifest.json`: publication metadata, timezone, the five intervals, resolution, provisional center and perimeter dimensions, total row count, formula and `cortes_nivel` learned from training.
- `fixture_v1.json`: test sample extracted from real source data; it is not the full app snapshot.

Confirmed app CSV columns, in order:

```text
h3_cell,faixa,faixa_nome,status,nivel,pontuacao_mensal,n_registros
```

`nivel_previsto` is excluded from the app file. August–September prediction assessment is ML backtesting and stays in the ML deliverable; it is not a future forecast for the app.

Do not assume exact JSON property names/shape beyond what the producer has actually specified. The delivered manifest and fixture are needed before freezing the v0.6 DTOs and executable schemas. In particular, `faixa_nome` spellings/encoding, numeric cut representation and provisional center must be checked against those files.

## Status, coverage and risk levels

- `com_dado`: at least one scorable record; there is no minimum sample threshold above one.
- `sem_dado`: zero records, explicit CSV row, empty `nivel`. Never translate it into level 0 or omit its row.
- Outside coverage is handled by the consumer/API and is outside the file; it must remain distinct from an explicit in-coverage `sem_dado` row.
- Coverage is the full H3 cell set for the 5 × 5 km FIAP Paulista perimeter, crossed with all five intervals.
- Reject duplicate keys and missing intervals for an included cell. A missing expected row is an invalid/incomplete publication, not an implicit `sem_dado`.
- Proving that no entire cells are missing also requires the producer's exact perimeter/selection reference, including edge-cell handling; five rows per present cell alone does not prove complete geographical coverage.

Let c1, c2 and c3 be the training-derived `cortes_nivel` from the manifest:

| Level | Score |
| --- | --- |
| 0 | score < c1 |
| 1 | c1 ≤ score < c2 |
| 2 | c2 ≤ score < c3 |
| 3 | score ≥ c3 |

The API must not recalculate cuts from the full published CSV or from the fixture. Actual cut values arrive with the producer execution. Equal quartiles need not be silently changed to invented thresholds.

## Score and source period

Facts cover 2026-01-01 through 2026-09-30. The score is:

```text
pontuacao_mensal = sum(crime weights in the key over Jan–Sep) / 9 / interval_hours
```

Unit: severity points per month per hour, comparable across cells and intervals under the producer's stated method.

| Weight | Crimes |
| --- | --- |
| 1 | furto, drogas, lesão culposa no trânsito |
| 3 | roubo, lesão corporal |
| 5 | homicídio, feminicídio, estupro |

Levels are relative to recorded keys in the region, not absolute danger or individual probability. Resolution 9 can place adjacent streets in one cell; this is a declared limitation, not evidence of street-level precision.

The manifest warning must be preserved:

> Melhor dado disponível no período. Não indica tempo real nem local seguro.

## Delivery checkpoints

The quoted hours are milestones from the producer conversation, without an inferred calendar date:

- Contract at 12h: agree on keys, five intervals, statuses, null semantics, column names, score formula/unit, file encodings and artifact roles. Cuts and provisional geographical values must not be invented to fill this checkpoint.
- Artifacts at 20h: complete CSV, matching checksum, final manifest with actual cuts/counts/geographical reference, and the real-data sample fixture. This supplies implementation/integration evidence; it does not by itself establish that a route-comparison endpoint or the native app is implemented.

## Backend implementation boundary

The next C# module imports the full CSV, validates the producer artifacts and key coverage, persists the publication, and publishes a versioned snapshot atomically. A failed candidate must preserve the currently published version. Announce a version only after its complete artifact is available and its integrity metadata matches.

Existing v0.5.1/v0.5.2 baselines remain preserved. The current v0.5.3 contract still describes the older grid; the replacement is a separate v0.6 patch once the producer files and manifest structure are available. Do not mechanically rename geohash columns and treat the old data as H3.

No import, administrative authorization, persistence or snapshot endpoint is implemented by this decision record. Source files were not located by the current filename search; use the delivered producer artifacts when available, rather than constructing substitutes and presenting them as Nando's data.
