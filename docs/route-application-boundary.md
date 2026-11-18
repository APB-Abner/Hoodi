# Route application boundary

The route evaluation use case lives in `Hoodi.Application/Rotas/AvaliacaoRotasService.cs`.
`RotasController` handles HTTP metadata, binding, authorization and response transport,
then delegates to `IAvaliacaoRotas`. It does not read EF, options, the snapshot cache,
or provider-specific types.

The application owns departure-window validation, territory checks, provider fallback,
comparison outcomes, scoring and ranking. Its ports are `IGradeParaRotas`,
`IIndiceEspacialRotas`, `IPoliticaRotas` and `IProvedorRotas`.
Infrastructure implements them using the existing immutable snapshot cache, H3,
configuration and HeiGIT. One captured snapshot is used for the entire evaluation.
Only the two comparison settings cross the policy port; the API key stays in the
provider adapter configuration.

`RouteScoring` lives in Application and depends on the spatial port rather than the
H3 package. The shared `FaixasRisco` definition supplies both departure-time selection
and producer manifest/CSV validation. Application has no dependency on Infrastructure,
ASP.NET, EF, H3 or NetTopologySuite.

This refactor preserves the response schema, window and territory rules, five time
bands, slicing, score arithmetic, missing-data settings, 70% coverage threshold,
tie handling and rounding. In particular, accumulated scoring with up to 30% unknown
distance remains the current behavior; changing that policy requires a product/data
decision. Golden producer vectors and HTTP integration tests remain the references.

The authentication email Data Protection purpose is centralized in
`ProtectionPurposes.AuthenticationEmailOutbox`. Its existing string is unchanged:
renaming a cryptographic purpose would require a migration for queued messages.
An independent legacy-payload test guards this compatibility.

The PR #7 security header middleware owns the default `Cache-Control: no-store`
policy, including documentation and exception responses. Explicit endpoint cache
headers remain authoritative. The exception middleware only translates/logs errors.
API version metadata lives in `Hoodi.Api.Configuration.ApiInfo`, in its own file,
independently of the security header middleware. It reads assembly metadata generated
from the API project's `Version`; health and OpenAPI share this value. The current
design contract is generated from that same version and the preserved source contracts.

## Deliberately deferred

Other application service implementations still live in Infrastructure and use EF
directly. Application defines their ports; Infrastructure implements persistence and
the use cases without a generic repository over EF. This is a documented pragmatic
boundary, not a claim that all use cases already live in Application.

HTTP status-bearing application exceptions, the remaining API/Infrastructure
couplings, composition-root extraction, naming cleanup and model-validation routing
are outside this focused refactor. No dependency, migration or public contract change
is introduced here.
