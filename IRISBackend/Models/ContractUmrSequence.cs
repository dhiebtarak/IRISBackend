namespace IRISBackend.Models;

public sealed class ContractUmrSequence
{
    public int InceptionYear { get; set; }

    public string ContractTypeCode { get; set; } = string.Empty;

    public string MainCoverCode { get; set; } = string.Empty;

    public int CurrentSequence { get; set; }
}
