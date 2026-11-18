# Patch v0.5.3 — Demo transport and invitation integration

## Contract changes

This release's v0.5.3 contract is preserved at `contracts/baseline/openapi.v0.5.3.yaml`, alongside the unchanged v0.5.2 and v0.5.1 baselines. The current `contracts/openapi.yaml` entry point has since advanced to the consolidated v0.6 draft.

- Invitation codes are canonically 64 uppercase hexadecimal characters. The server trims surrounding whitespace and normalizes case before validation and hashing. The schema's length/pattern constraints describe this normalized representation.
- Transport is the app deep link `hoodi://convite#codigo=CODE`, shared as a link or encoded in QR by the app. No new response field or endpoint is introduced. Codes must not be placed in query strings or logs, and manual typing is not the expected flow.
- Malformed code/body returns 400 `DADOS_INVALIDOS`; a well-formed unknown code still returns 409. The existing v0.5.2 decision/validation order is unchanged.
- The contract now documents the actual 400 response on redemption, login and refresh. Other implemented auth body endpoints already had 400; Expo token input continues using the existing 422 behavior.
- The inline creation example now satisfies the 64-character code schema. All other contract operations and schemas remain unchanged.

## Deployment changes

HTTPS redirection remains enabled by default. `Https__RedirectEnabled=false` explicitly supports the IP-only HTTP demo. `Https__Port` controls the redirect destination, default 443. The HTTPS nginx template requires a real domain and trusted certificate; a separate HTTP-only template does not require certificates or listen on 443. Only one template should be installed.

Persistent JWT/Data Protection configuration requirements are unchanged for Production. No DNS, certificates, nginx, systemd or VM resources were created or changed. React Native mobile network policy still needs validation by the app owner for HTTP.

The README includes the exact Mailpit SSH tunnel and current client-side exit sequence: deactivate the installation's push token before deleting local auth data. Session logout remains planned for v0.6; no endpoint was added here.

## Other corrections and limits

The README author table now uses the four full names/RMs explicitly supplied by the user and the correct responsibilities. `VinculoContato` now uses `VINCULO_PROPRIO`, consistently mapped to 422. Its legacy UTC `DateTime` fields remain unchanged; moving them to `DateTimeOffset` is deferred to a separately tested schema change.

Tests cover lowercase/whitespace normalization, malformed/missing codes without consuming invitations, HTTP without redirects, configurable HTTPS port, default proxy behavior, and generated OpenAPI response metadata. Contract validation checks both preserved patches, the bounded v0.5.3 changes, canonical schemas and negatives. No dependencies were installed.

Final validation: 76/76 tests passed. Report: artifacts/test-results/review-v0.5.3.trx. Contract checks passed normally and with Python optimization; EF reported no pending model changes.
