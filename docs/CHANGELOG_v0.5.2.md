# Patch v0.5.2 — Invitation redemption

## Scope

User-approved correction to the v0.5.1 invitation redemption response. This is independent of v0.6 and does not decide H3, routes or panic scope.

- Frozen baseline: `contracts/baseline/openapi.v0.5.1.yaml`, copied without modification.
- Preserved v0.5.2 normative contract: `contracts/baseline/openapi.v0.5.2.yaml`. The current `contracts/openapi.yaml` has advanced to v0.5.3.
- At this patch release, generated runtime OpenAPI and `/health` reported `0.5.2` (now `0.5.3`); the normative file still describes operations not implemented yet.

## Validation order and responses

1. Resolve the invitation code and validate pending state and exclusive expiration. Unknown, expired, canceled, accepted or rejected codes return 409 for either decision.
2. A valid pending invitation cannot be redeemed by its issuer. Both decisions return 422 with `code=VINCULO_PROPRIO`. The issuer cancels through `DELETE /v1/contatos/convites/{conviteId}`.
3. `rejeitar`: close the invitation as rejected, return 204 without a body, create no relationship, and do not check ordered-pair duplication.
4. `aceitar`: check whether an accepted A -> B relationship exists. If so, return 409 without consuming the invitation. Otherwise consume it and return 200 with the unchanged `Vinculo` schema.

Malformed request JSON/input validation remains distinct (400); rate limiting remains 429. Authorization still requires a valid JWT.

## Visibility

Rejected invitations disappear from the issuer's pending invitation list. The issuer can observe that an invitation is no longer pending; the contract does not promise to hide refusal/closure. The pending list does not expose a terminal reason or return the secret code again.

## Tests

HTTP cases cover both decisions for invalid/expired/closed codes, issuer self-redemption, rejection with an existing active pair, acceptance duplication, empty rejection response, list disappearance, new invitation after rejection and concurrent redemption of a single code. The generated OpenAPI must expose 200, 204, 409 and 422 with an empty 204 response.

`python scripts/validate_contract_patch.py` checks that no unrelated contract content changed, the acceptance schema is preserved, the 422 schema constrains status/code, and all local references resolve. It also runs under `python -O`; checks do not depend on Python assertions. PyYAML and jsonschema were already available in this environment; no Python packages were installed.

