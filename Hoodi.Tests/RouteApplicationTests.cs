using Hoodi.Application.Auth;
using Hoodi.Application.Errors;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Microsoft.AspNetCore.DataProtection;

namespace Hoodi.Tests;

public sealed class RouteApplicationTests
{
    [Theory]
    [InlineData("2026-11-17T00:00:00-03:00", 0)]
    [InlineData("2026-11-17T05:59:59-03:00", 0)]
    [InlineData("2026-11-17T06:00:00-03:00", 1)]
    [InlineData("2026-11-17T11:59:59-03:00", 1)]
    [InlineData("2026-11-17T12:00:00-03:00", 2)]
    [InlineData("2026-11-17T17:59:59-03:00", 2)]
    [InlineData("2026-11-17T18:00:00-03:00", 3)]
    [InlineData("2026-11-17T20:59:59-03:00", 3)]
    [InlineData("2026-11-17T21:00:00-03:00", 4)]
    [InlineData("2026-11-17T23:59:59-03:00", 4)]
    [InlineData("2026-11-18T03:00:00Z", 0)]
    public void SharedBands_MustRespectAllLocalBoundaries(string departure, int expected) =>
        Assert.Equal(expected, FaixasRisco.DaPartida(DateTimeOffset.Parse(departure)));

    [Fact]
    public void CentralEmailPurpose_MustDecryptLegacyPurposePayload()
    {
        var provider = new EphemeralDataProtectionProvider();
        // Independent old protocol literal: catches an accidental cryptographic purpose rename.
        var oldPayload = provider.CreateProtector("hoodi.auth.email-outbox.v1").Protect("SYNTHETIC_MESSAGE");
        Assert.Equal("SYNTHETIC_MESSAGE", provider.CreateProtector(ProtectionPurposes.AuthenticationEmailOutbox).Unprotect(oldPayload));
    }

    [Fact]
    public async Task RouteUseCase_MustEnforceWindowBeforeReadingGradeOrCallingProvider()
    {
        var grade = new GradePort(); var provider = new ProviderPort(); var clock = new TestClock();
        var service = new AvaliacaoRotasService(grade, new RouteScoring(new SpatialPort()), provider, new PolicyPort(), clock);
        var error = await Assert.ThrowsAsync<RequestException>(() => service.AvaliarAsync(new(new(1, 2), new(2, 3), clock.GetUtcNow().AddHours(49).ToString("O")), default));
        Assert.Equal("SAIDA_FORA_JANELA", error.Code); Assert.Equal(0, grade.Reads); Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(false, "grade_indisponivel", 0)]
    [InlineData(true, "apenas_uma_rota", 1)]
    public async Task RouteUseCase_CanRunWithoutHttpEfOrOptions(bool available, string reason, int calls)
    {
        var grade = new GradePort { Available = available }; var provider = new ProviderPort(); var clock = new TestClock();
        var service = new AvaliacaoRotasService(grade, new RouteScoring(new SpatialPort()), provider, new PolicyPort(), clock);
        var result = await service.AvaliarAsync(new(new(1, 2), new(1.001, 2), clock.GetUtcNow().ToString("O")), default);
        Assert.Equal(reason, result.Motivo); Assert.Equal(calls, provider.Calls);
        Assert.All(result.Rotas, r => Assert.Null(r.Posicao));
    }
    private sealed class SpatialPort : IIndiceEspacialRotas { public string Cell(PontoRota point) => "synthetic"; }
    private sealed class PolicyPort : IPoliticaRotas { public PoliticaRotas Ler() => new(FonteRisco.PontuacaoMensal, TratamentoSemDado.Desconhecido); }
    private sealed class GradePort : IGradeParaRotas
    {
        public bool Available; public int Reads;
        public Task<GradeParaRotas?> LerAtualAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Available ? new GradeParaRotas("test", Enumerable.Range(0, 5).ToDictionary(b => ("synthetic", b), b => new RiskCell("synthetic", b, "com_dado", 1, 1, 3))) : null);
        }
    }
    private sealed class ProviderPort : IProvedorRotas
    {
        public int Calls;
        public Task<IReadOnlyList<AlternativaRota>> ObterAsync(PontoRota a, PontoRota b, CancellationToken ct)
        { Calls++; return Task.FromResult<IReadOnlyList<AlternativaRota>>([new(new[] { a, b }, 100)]); }
    }
}
