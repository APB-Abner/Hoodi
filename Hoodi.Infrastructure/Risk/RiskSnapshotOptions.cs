namespace Hoodi.Infrastructure.Risk;

/// <summary>Defaults = pacote do produtor contrato v2 (data/risk/v2), o fechado na entrega. O v3 semestral (data/risk/v3) é aceito pelo decoder para a fase 2, aprovando seus hashes via Risk__Approved*.</summary>
public sealed class RiskSnapshotOptions
{
    public Guid[] ImporterUserIds { get; set; } = [];
    public string ApprovedCsvSha256 { get; set; } = ProducerV2Decoder.DeliveredSha256;
    public string ApprovedManifestSha256 { get; set; } = "fc89b872bc003ff45c4a98b740fec4fc03c56bcf9633d834c4503dc57af158ab";
    public string ApprovedFixtureSha256 { get; set; } = "337c5dcf35667752e1ee950425135f17e985c6d0e42e85f7f5df6ddcc043235d";
}
