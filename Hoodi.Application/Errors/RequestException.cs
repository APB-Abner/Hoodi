namespace Hoodi.Application.Errors;

public sealed class RequestException(string code, int status) : Exception("Request rejected.")
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
