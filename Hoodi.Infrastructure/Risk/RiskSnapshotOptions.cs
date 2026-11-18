namespace Hoodi.Infrastructure.Risk;

/// <summary>Defaults = pacote do produtor contrato v3 (data/risk/v3). O v2 (data/risk/v2) segue válido para rollback via Risk__Approved*.</summary>
public sealed class RiskSnapshotOptions
{
    public Guid[] ImporterUserIds { get; set; } = [];
    public string ApprovedCsvSha256 { get; set; } = ProducerV2Decoder.DeliveredSha256V3;
    public string ApprovedManifestSha256 { get; set; } = "69a4bf2d0465379d6db473741679d76a31c84b6585a76af29bc38c97606b605c";
    public string ApprovedFixtureSha256 { get; set; } = "5d8fb0c6eaa5d197c41acb9f068f5d203e0afe675c0b1460ffb8ac73866fcc57";
}
