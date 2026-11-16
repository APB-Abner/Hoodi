# Account deletion and panic — v0.6 increment

## Contract and delivery boundary

[Partial OpenAPI](../contracts/v0.6/demo.openapi.yaml) adds account deletion and implements the five panic operations alongside logout. The complete stable v0.5.3 file is preserved. This increment authenticates panic through user JWTs only, allowing `app` and `watch`. Device-key authentication and `esp32` origin are not enabled. The application must not represent this as direct watch/ESP32 integration already validated.

`DELETE /v1/conta` is provisional pending [real transport tests](account-delete-transport-test.md). It confirms the current user's password (403 `SENHA_INVALIDA` for a wrong value), revalidates the session, then deletes account/authentication records, invitations, links in either direction and push associations in one transaction. Readable queued authentication messages for that email are removed; unreadable ciphertext/JSON is preserved for recovery and does not block deletion. Own panic records lose owner identity, idempotency keys, coordinates/observations and link/token/ticket identifiers, while retaining original recipient IDs. Those recipients keep anonymous history with `titularNome="Conta excluída"` and no position/observation. Deleting a recipient removes that recipient's ID/access from all dispatches. Other accounts remain. An already deleted session returns 401. A test forces failure after bulk SQL mutations and verifies rollback.

Old notifications, screenshots, backups and data already obtained by recipients cannot be recalled by deleting the server account. Do not claim forensic anonymity across external copies.

## Panic registration and access

`POST /v1/panico` commits the request and recipient dispatches together before returning 202. The unique `(TitularId, IdempotencyKey)` index and SQLite transaction serialize competing submissions. Replay returns 200 with the original ID/time/origin and the current summary; it never changes location or adds recipients. A replay after 24 hours still identifies the original request. Idempotency metadata is retained for at least 30 days (this increment does not purge it automatically).

Recipients are frozen from directed, accepted links at initial registration. While the owner exists, every detail/list/opening revalidates the original link ID and its current acceptance. Revoke then create a new link does not restore access to an old live-owner request. After owner deletion, original dispatch recipient IDs authorize anonymous history without a live link; they never authorize position, observation or access by another user. A recipient added after registration gains no old-request access. Status belongs only to the owner. Unauthorized lookups return 404. Received lists include no coordinates/observation. Opening is idempotent and is distinct from a push receipt.

Each active installation gets a dispatch. A contact without active push gets an explicit failed dispatch. `total` counts dispatches, not distinct people. The proposed five-contact cap counts accepted plus pending; its direction still awaits confirmation. The app's three-per-hour rule applies to help requests. The backend has not adopted that cap: blocking requests versus limiting/grouping notifications still requires a team decision.

## Retention and configuration

`Panico__RetencaoLocalizacaoHoras` defaults to **24** for contacts. `Panico__RetencaoTitularHoras` defaults to **72** for the owner (48 can be configured). Both accept 1–720 hours. Position and observation are encrypted with Data Protection (`hoodi.panico.dados.v1`). Each deadline is frozen at creation. Every GET checks the reader's role and its deadline independently of cleanup; after that deadline latitude, longitude and observation are null and `localizacaoDisponivel=false`. The worker removes ciphertext only after both deadlines expire, even if contact retention is longer than owner retention. Migration adds a nullable owner deadline: legacy rows keep their original stored expiry for both roles, and previously purged data is not reconstructed.

The worker removes expired ciphertext every five seconds while the service is running. `PRAGMA secure_delete=ON` is applied on each application SQLite connection, including after pooled reopen, so ordinary deleted database content is overwritten according to SQLite's behavior. This is not a wipe guarantee for WAL/journal copies, storage snapshots, backups or previously exported data. See [SQLite secure_delete](https://www.sqlite.org/pragma.html#pragma_secure_delete). No request-body logging or EF sensitive-data logging was added. Infrastructure and mobile logs must likewise avoid body/Authorization capture.

## Expo dispatch

`Panico__EnvioExpoHabilitado` defaults to **false**. The durable queue and app reads work while disabled; `pendente` then means queued, not sent. Enable it on the demo VM after the app has registered valid Expo tokens and the team is ready for actual notifications. No real notifications were sent by the automated tests.

The worker uses the [official Expo HTTP ticket/receipt API](https://docs.expo.dev/push-notifications/sending-notifications/), with a ten-second HTTP timeout. Push data contains only `tipo=pedido_ajuda` and `pedidoId`, with generic visible text. It rechecks the original link, active installation and token hash before sending. Logout, token replacement, account deletion or revoked consent cancels unsent work. `DeviceNotRegistered` disables that exact active token.

Temporary HTTP/network/JSON failures retry with bounded exponential backoff; sending stops after six attempts. Expo tickets are polled after 15 minutes. After 24 hours without a receipt, the internal state becomes `sem_recibo`, ending polling while preserving `aceitosExpo` and the public `aceito_expo` summary; it does not claim failure or provider acceptance. `aceito_expo`, `aceito_provedor_push` and `aberto_no_app` have different meanings; none of the first two proves a person received/read the alert. A crash after Expo acceptance but before local commit can send a duplicate on retry; exactly-once external delivery is not promised.

This deployment supports a **single API instance and SQLite**. The worker holds a database write transaction across one bounded HTTP call to serialize sending with revocation/deletion. That favors authorization correctness over write throughput and can delay other writes by up to the HTTP timeout. Revisit the dispatch claim/lease architecture before scaling beyond the demo; do not run multiple worker instances.

## Evidence and outstanding integration

Automated tests run the actual API/services, SQLite migrations and Expo HTTP adapter against an in-process HTTP boundary stand-in based on official response envelopes. They cover ticket/receipt distinction, generic payloads, timeout, malformed response, HTTP 503, unregistered token, logout/revocation, concurrent replay, access rules, rollback, secure_delete and retention. They establish local/mocked behavior, not real Expo/native delivery.

The supplied VM health responded 200 through nginx. Its public OpenAPI at inspection did not expose account deletion or panic yet. No VM deployment, authenticated DELETE via nginx, Android/iOS test or native push delivery was performed. No credentials were requested in chat or captured in files. Publish this increment to the VM, then use a synthetic account and record each real-path outcome separately.
