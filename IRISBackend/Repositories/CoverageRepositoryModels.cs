namespace IRISBackend.Repositories
{
    public sealed record CoverageItemLookup(
        int Id,
        string Level,
        string Code,
        string Name,
        bool IsActive);

    public sealed record CoverageRelationLookup(
        int ParentId,
        int ChildId,
        string RelationType,
        bool IsMandatory,
        bool IsActive);
}
