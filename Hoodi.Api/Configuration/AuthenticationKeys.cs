using System.Security.Cryptography;
using Hoodi.Api.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Hoodi.Api.Configuration;

public static class AuthenticationKeys
{
    public static EmissorJwt CreateJwt(IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Jwt:SigningKey is required outside Development. Configure Jwt__SigningKey in the service environment.");
            return new EmissorJwt(RandomNumberGenerator.GetBytes(64));
        }
        byte[] key;
        try { key = Convert.FromBase64String(signingKey); }
        catch (FormatException) { throw new InvalidOperationException("Jwt:SigningKey must be valid Base64 containing at least 32 random bytes."); }
        if (key.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 random bytes.");
        return new EmissorJwt(key);
    }

    public static IDataProtectionProvider CreateDataProtection(IConfiguration configuration, IHostEnvironment environment)
    {
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("DataProtection:KeysPath is required outside Development.");
            return new EphemeralDataProtectionProvider();
        }
        if (!Path.IsPathFullyQualified(keysPath))
            throw new InvalidOperationException("DataProtection:KeysPath must be an absolute path.");
        try { Directory.CreateDirectory(keysPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Cannot create DataProtection:KeysPath. Check service directory permissions."); }
        return DataProtectionProvider.Create(new DirectoryInfo(keysPath), builder =>
            builder.SetApplicationName("hoodi").PersistKeysToFileSystem(new DirectoryInfo(keysPath)));
    }
}

