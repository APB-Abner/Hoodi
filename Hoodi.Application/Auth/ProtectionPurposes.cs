namespace Hoodi.Application.Auth;

public static class ProtectionPurposes
{
    // Persistent cryptographic identifier. Changing it requires a key/payload migration.
    public const string AuthenticationEmailOutbox = "hoodi.auth.email-outbox.v1";
}
