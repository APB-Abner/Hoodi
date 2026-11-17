# Route slicing, scoring and ranking

## Configuration and key

Set these environment variables on the API process and restart:

| Variable | Values/default |
| --- | --- |
| `Rotas__FonteRisco` | `PontuacaoMensal` (default) or `Nivel` |
| `Rotas__SemDado` | `Desconhecido` (default) or `RiscoBaixo` |
| `Rotas__ApiKey` | Private HeiGIT API key; no value is shipped in the repository |

On the VM, add the key privately to the existing systemd EnvironmentFile (root-owned, permission 600). Locally use a private process environment. Never send the key in chat, a PR, a query string or app source. The mobile app calls Hoodi using its own JWT; it must not receive the provider key.

The fixed provider URL is `https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson`. Requests send `[longitude, latitude]`, the key only in the Authorization header, no instructions and up to three walking alternatives. See the [official migration notice](https://ask.openrouteservice.org/t/deprecating-api-openrouteservice-org-in-favour-of-api-heigit-org/7912/) and [GeoJSON response documentation](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/requests-and-return-types). Redirects and HTTP client logging are disabled. Timeout: 15 seconds; response cap: 2 MiB. No key means no outbound request and a provider-unavailable comparison result, not fake routes.

## Step 2: slicing

Coordinates are WGS84 `(latitude, longitude)`. Reject non-finite/out-of-range values and fewer than two distinct consecutive points. Remove consecutive duplicates. For every segment, calculate haversine length with radius 6,371,008.8 m, split into `ceil(length/5)` pieces by interpolation in degrees (last endpoint exactly preserved), measure each piece and assign it to the resolution-9 H3 cell of its midpoint. Sum meters by cell in first-occurrence order. Limit: 10,000 input vertices and 20,000 slices per alternative.

The band is selected from the departure instant in `America/Sao_Paulo`: five half-open intervals `[0,6)`, `[6,12)`, `[12,18)`, `[18,21)`, `[21,24)`. It remains fixed for the whole route, following Nando's reference; crossing a time boundary does not select a different band mid-trip. There is **no tipoDia**.

## Step 3: score and coverage

Capture one immutable cached snapshot index for the entire comparison. Never read the database or decode the snapshot per subsegment.

- `PontuacaoMensal`: use the published continuous score, sum `score × meters/1000`. Unit: points/(month-hour) × km.
- `Nivel`: use the published 0–3 level, sum `level × meters/1000`. Unit: level × km. It is a different comparison index, not the same physical quantity.
- `Desconhecido`: sem_dado does not contribute score or known coverage.
- `RiscoBaixo`: sem_dado contributes zero score and counts toward effective known coverage. This is an explicit product hypothesis, **not evidence of low risk**.
- Outside the snapshot is always unknown in both modes; never map it to zero or include it in known coverage.

`MetrosComDado` always reports genuinely observed distance. `PercentualSemDado` and `PercentualForaCobertura` preserve physical missing-data percentages even in RiscoBaixo mode. Effective `CoberturaPercentual` adds sem_dado distance only in RiscoBaixo. `IndicePorKm` divides accumulated score by effective known kilometers; null when none are known. Less than 70% effective coverage is insufficient; exactly 70% qualifies. Responses include both selected mode names and the unit.

## Step 4: ranking

Follow Nando's reference: accumulated score ascending, not normalized/extrapolated old v0.5 exposure. Compare unrounded doubles with `abs(a-b) <= 1e-9 * max(abs(a),abs(b),1)`. Form anchored score classes, then anchored kilometer classes within each score class, then input index. Positions are sequential, not dense shared tie positions. Round only for display: six decimal places for indices/percentages and 0.01 m for lengths, half-even.

The pure scorer excludes insufficient routes from ranking, matching producer vectors. The HTTP comparison withholds **all** positions if any alternative is insufficient, and returns inconclusiva/cobertura_insuficiente. One sufficient alternative is inconclusiva/apenas_uma_rota with a score but no position. Two or three sufficient alternatives give concluida. This deliberately replaces v0.5's extrapolation/5% tie/round-before-ranking semantics in the incremental contract; clients must adopt the new response fields.

## Endpoint and privacy

`POST /v1/rotas/avaliar`, authenticated JWT, body `{origem:{latitude,longitude}, destino:{latitude,longitude}, saidaEm:"ISO8601-with-offset"}`. Departure must be between server now minus five minutes and plus 48 hours; all arrivals must fit plus 48 hours. Invalid windows return 422 SAIDA_FORA_JANELA. Grade/provider absence or outside-territory endpoints return 200 indisponivel with no routes. No snapshot is auto-imported: publish the grade first. Limit: 15 evaluations/minute per authenticated account, input cap 4 KiB.

Response geometry is an array of `{latitude,longitude}`, not the old encoded-polyline field. Geometry, endpoints and score work stay request-local and are not persisted or added to logs. Calling the provider necessarily transmits the endpoints to HeiGIT; there is no claim of on-device-only routing. All scores describe historical Jan-Sep 2026 data, not present safety or predictions. Native app consumption and live provider calls require integration testing after key configuration.

## Evidence

Producer files: `data/routes/v2`, copied unchanged from `nandoantonio-git/hoodi-ml` commit `3c8b6f4a924799b8a5d4924d2d24e921c9d203e6`. Vector SHA: `93b1980b88eecae77c744bc4b78084b3d6eb26ae3484908d5c077d4b656fac84`; linked snapshot SHA: `2dddef701bdecb65142874980f1ae5aba7b2ac6ab2abad5c47b07f07ecc5080a`. Tests match all route/boundary cases, cell order, lengths (0.01 m), indices (1e-6 relative), 40 interior/boundary H3 points and numeric ties. The managed H3 port passed these core-C vectors; this does not prove universal equivalence for every geometry.

Additional tests cover all four configuration combinations, anchored ranking, provider HTTP envelopes/order/header, malformed JSON, timeout, HTTP 503, missing key, JWT validation, malformed input, time windows and existing risk cache behavior. HTTP tests use a synthetic provider boundary; no real key or live provider traffic was used.
