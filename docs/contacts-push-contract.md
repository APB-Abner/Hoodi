# Contacts and push implementation notes

## Contract basis

The implementation follows the contacts and Expo token operations in v0.5.1 plus the v0.5.2/v0.5.3 patches. It adds an incremental `ContactsAndPush` migration without rewriting the authentication migration.

The user-approved v0.5.2 patch resolves the invitation response inconsistency. Validation order is code/state/expiry first (409), then issuer self-redemption (422 VINCULO_PROPRIO), then rejection (204, no duplicate check), then acceptance duplicate check (409) or creation (200). See `docs/CHANGELOG_v0.5.2.md`. Rejected invitations disappear from the issuer pending list; concealment of closure is not promised.

## Implemented operations

| Method | Path | Behavior |
| --- | --- | --- |
| POST | `/v1/contatos/convites` | Authenticated owner creates a single-use code, returned once. |
| POST | `/v1/contatos/convites/resgatar` | Accept: 200 with relationship; reject: 204 without one; own code: 422. |
| GET | `/v1/contatos/convites` | Only the owner's non-expired pending invitations, with no code/hash. |
| DELETE | `/v1/contatos/convites/{conviteId}` | Owner only; pending invitation canceled; closed/expired returns 409. |
| GET | `/v1/contatos` | Own accepted/revoked relationships, current display name of the other user, no email. |
| DELETE | `/v1/contatos/{vinculoId}` | Either participant can revoke; third parties receive 404; repeat is 204. |
| PUT | `/v1/push/tokens` | Register, replace or reactivate the user's installation token; invalid input returns 422. |
| DELETE | `/v1/push/tokens/{tokenId}` | Owner only; idempotent deactivation; absent/other owner's token returns 404. |

All routes require a valid JWT and persisted session. Unknown/foreign resource lookups share the same 404 response. Unexpected exceptions remain 500; domain conflicts remain 409, except the explicit own-invitation code mapped to 422.

## Domain, transactions and database constraints

An invitation identifies its issuer until authenticated redemption determines the other participant. Codes contain 32 cryptographically random bytes encoded as 64 uppercase hexadecimal characters; only their SHA-256 hashes are stored. Input is trimmed and uppercased before format validation and hashing. The app transports the code via `hoodi://convite#codigo=CODE` (link or QR), not manual typing. Malformed codes/bodies return 400; well-formed unknown codes return 409. Issuance does not authorize access to help requests. A rejected, canceled, expired or unused invitation never creates an accepted relationship.

A relationship is directed A -> B. The inverse relationship requires another invitation. Redemption and duplicate checking occur within a SQLite transaction. A filtered unique index allows only one accepted relationship per ordered pair. Revoked rows remain for history and a new invitation may create a new relationship afterward.

The configurable invitation lifetime is `Contatos__ValidadeConviteMinutos`, default 60, valid range 1–10080 minutes. This is an operational default pending team confirmation, not a finalized product decision. Expiry is exclusive: at the exact expiration instant, cancellation/redemption is rejected and the pending list omits the invitation.

New invitation dates are converted to UTC ticks (`INTEGER`) by EF Core, allowing expiry comparison and ordering in SQLite SQL. Legacy authentication timestamp mappings were not changed. Their earlier SQLite comparison caveat still applies.

The issuance limiter allows five requests/minute per authenticated user; redemption allows ten. Authentication middleware now runs before rate limiting so policies can identify the JWT subject. Counters remain in memory for a single API instance.

## Expo token lifecycle

Expo tokens are capabilities needed for future notification delivery. Tokens are encrypted with Data Protection purpose `hoodi.push-token.v1`; a SHA-256 hash supports matching without storing plaintext. Responses contain only ID and active state.

The installation identifier is scoped to its authenticated owner. Re-registering it preserves its row ID and updates/reactivates the token. Only one active row may reference a given Expo token. Registering that same token for another authenticated account deactivates the prior association atomically, retaining its row. This prevents a shared phone from keeping a previous account's active routing association. Knowing an installation ID alone cannot change another owner's record; the actual Expo token is required. Apps should also deactivate their own token before logout.

Structural validation accepts `ExpoPushToken[...]` and `ExponentPushToken[...]`, with `ios` or `android`. No external request to Expo is made. These operations do not prove notification delivery. The help-request worker, tickets, receipts and `DeviceNotRegistered` handling belong to the later notification module.

## Validation

HTTP tests use authenticated users and real temporary SQLite databases. Both service and HTTP tests exercise redemption transactions and the approved success/error contract. Coverage includes ownership isolation, hidden codes, UTC expiry query, duplicate/concurrent acceptance, historical revocation, independent inverse relationships, token rotation/reactivation, ciphertext round trip, account switching and concurrent single-active-token registration.

A migration upgrade test starts at `InitialAuthentication`, stores a synthetic user, applies `ContactsAndPush`, and verifies that user data survives.

No dependencies were installed for these modules. No VM changes, Expo API calls, messages to teammates, commits or pushes were performed.


