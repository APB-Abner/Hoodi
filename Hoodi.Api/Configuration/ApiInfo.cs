using System.Reflection;

namespace Hoodi.Api.Configuration;

/// <summary>Shared API version metadata for OpenAPI and the health endpoint.</summary>
public static class ApiInfo
{
    public static string Versao { get; } = typeof(ApiInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("API assembly version metadata is missing.");
}
