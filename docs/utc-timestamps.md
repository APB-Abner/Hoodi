# UTC domain timestamps

## Refactor target

`Usuario.CriadoEmUtc`, `VinculoContato.CriadoEmUtc` and `VinculoContato.RevogadoEmUtc` now use `DateTimeOffset` with offset zero. Construction/revocation still receive time explicitly and normalize it to UTC.

## Behavior preserved

The wire contract v0.5.3 does not expose these entity timestamp properties and is unchanged. Password hashes, security stamps, relationship status and revocation authorization are unchanged.

EF converts the three properties to the existing `DateTime` provider representation in SQLite `TEXT` columns. On read, `DateTime.SpecifyKind(value, Utc)` prevents timezone-less legacy values from being treated as machine-local time. The nullable revocation column retains null semantics. Existing migrations and database values do not need rewriting.

Other timestamp mappings are unchanged. Invitation dates remain UTC ticks, and native DateTimeOffset mappings elsewhere retain their existing SQLite query limitations.

## Focused validation

`LegacyUtcPersistenceTests` inserts synthetic rows in the old SQL timestamp format, then reads them through the new domain model. It verifies UTC interpretation, fractional precision, null revocation, existing revocation and updating/reloading an active relationship. A second test writes a non-zero offset crossing midnight, reloads it in UTC and exercises SQL filtering/ordering through the converter.

Existing user/relationship tests now assert DateTimeOffset values. Full-suite and EF model-consistency results are recorded in the execution report/README after validation.

## Remaining work

VM/systemd, SMTP delivery and mobile integration still require target-environment access. No infrastructure or logout endpoint was added by this refactor.

Validation result: all 78 tests passed (15 focused UTC/user/relationship tests). EF reported no pending model changes, so no new migration was generated. Evidence: artifacts/test-results/utc-refactor.trx.
