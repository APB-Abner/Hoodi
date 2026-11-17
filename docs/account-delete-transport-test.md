# Account deletion transport test

The provisional v0.6 operation is `DELETE /v1/conta` with a JSON password. Do not select a replacement verb until the actual proxy and native app paths have been exercised. Local HTTP binding tests do not establish nginx/Android/iOS behavior.

Use a synthetic demo account and an intentionally incorrect password. Never put a real password or JWT into this document, Git, shared screenshots or chat. Enter the JWT privately in the local test environment.

```bash
curl -i -X DELETE "$HOODI_BASE_URL/v1/conta" \
  -H "Authorization: Bearer $HOODI_TEST_JWT" \
  -H 'Content-Type: application/json' \
  --data-binary '{"senha":"wrong-on-purpose"}'
```

For the app, use its existing authenticated API client/base URL and the current in-memory access token:

```ts
const response = await fetch(`${baseUrl}/v1/conta`, {
  method: 'DELETE',
  headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
  body: JSON.stringify({ senha: 'wrong-on-purpose' }),
});
const problem = await response.json();
// Record response.status and problem.code only. Never log headers or accessToken.
```

Record separate evidence for nginx on the VM, Expo Android and Expo iOS (if available). `403 / SENHA_INVALIDA` proves this request body was bound and the password was checked. `400 / DADOS_INVALIDOS` requires investigation: it can indicate a missing body, malformed JSON, wrong serialization or other validation errors; it does not by itself identify which layer discarded data. `401`, `404`, redirects, TLS failures and `429` do not test body transport.

No successful account deletion is needed for this transport test. If a supported real path cannot carry the body reliably, change the incremental contract/client/server together to `POST /v1/conta/exclusao`. Do not silently expose two destructive operations or claim the transport test passed from a local integration test.

Current evidence: local ASP.NET integration path only; VM/nginx and native app paths remain unverified.

## Mobile integration note for Mariana

The confirmation screen needs a password input. JWT-only deletion is insufficient: send `{ "senha": enteredPassword }` in the authenticated DELETE body, without logging or persisting the password. Keep the screen/session on 400/403 and clear local credentials only after confirmed 204. The full Expo application is not present in this repository, so this note does not establish that its screen or request has been updated. Android/nginx transport remains pending.
