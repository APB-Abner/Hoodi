# Producer artifact provenance (contrato v3)

Repository: https://github.com/nandoantonio-git/hoodi-ml

Original directory: `ml/dados/snapshot_v3/` (branch `feat/snapshot-carro-raio-10km`; commit a registrar quando o produtor publicar). The four files retain original bytes. No producer code or raw individual records were imported/executed.

| File | SHA-256 |
| --- | --- |
| risco_h3_faixa_v3.csv | `e33bd004ffcf0c47378ef0c9518bdf19a9ea84ca007222d798a2003a7c71c0da` |
| risco_h3_faixa_v3.csv.sha256 | `232e0fcf7921585217648cc44fb72bcb79869386a115b37d6c4e416f0b0bb430` |
| manifest.json | `69a4bf2d0465379d6db473741679d76a31c84b6585a76af29bc38c97606b605c` |
| fixture_v3.json | `5d8fb0c6eaa5d197c41acb9f068f5d203e0afe675c0b1460ffb8ac73866fcc57` |

V3 keeps the v2 schema, 5×5 km perimeter, H3 res. 9, five bands, weights and k ≥ 3 suppression. What changes: the window is semestral (Apr–Sep 2026, 6 months; `pontuacao_mensal` = weighted sum ÷ 6 months ÷ band hours) and the level cuts are quartiles of 6-month training windows (`cortes_nivel` 0.305556, 0.611111, 1.277778). 1,395 keys: 824 `com_dado`, 571 `sem_dado` (v2: 954 / 441). Levels equal v2 in 78.6% of keys present in both; within one level in 99.4%.

Why: on the exploratory test (Aug–Sep 2026, already seen) the semestral window was the only one where the regularized RF beat the historical-mean baseline with an interval excluding zero; a wider radius did not improve classification. This is not a forecast, not validated out of sample; the definitive check is the blind test on Oct–Nov 2026 once SSP publishes those months. The producer center remains provisional. v2 (`data/risk/v2`) is kept for rollback.
