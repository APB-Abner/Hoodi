// Testes unitários das regras de mapeamento de PII (semgrep --test security/rules).
// Dados fictícios.

// ruleid: hoodi-pii-inventory-record, hoodi-pii-record-tostring-exposure
public sealed record CadastroInput(string Nome, string Senha);

// ruleid: hoodi-pii-inventory-record, hoodi-pii-record-tostring-exposure
public sealed record LocationInput(double Latitude, double Longitude)
{
    public bool Valid => Latitude != 0;
}

// ruleid: hoodi-pii-inventory-record
public sealed record SafeLocationInput(double Latitude, double Longitude)
{
    public override string ToString() => "SafeLocationInput { omitido }";
}

// ok: hoodi-pii-record-tostring-exposure
public sealed record Snapshot(string Version, int Cells);

public class Pessoa
{
    // ruleid: hoodi-pii-inventory-property
    public string Cpf { get; private set; } = string.Empty;
    // ok: hoodi-pii-inventory-property
    public int Idade { get; set; }
}

public class Secrets
{
    // ruleid: hoodi-pii-hardcoded-secret
    private const string ApiKey = "sk_live_FAKE_0000000000";
    // ok: hoodi-pii-hardcoded-secret
    private const string ApiKeyName = "";
    // ok: hoodi-pii-hardcoded-secret
    private const string Version = "2026.11.17-release";

    public string Example()
    {
        // ruleid: hoodi-pii-cpf-literal
        var cpf = "123.456.789-09";
        // ok: hoodi-pii-cpf-literal
        var phone = "11 91234-5678";
        // ruleid: hoodi-pii-query-string
        var url = "https://x.example.invalid/track?lat=-23.5&lng=-46.6";
        // ok: hoodi-pii-query-string
        var ok = "https://x.example.invalid/track?page=2";
        return cpf + phone + url + ok;
    }
}
