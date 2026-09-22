namespace My.Shared.Rules;

/// <summary>
/// App Drive can read the whole Shared Drive. Intranet media must stay under
/// the Intranet folder so expense receipts cannot be streamed via a page img src.
/// </summary>
public static class DriveFolderAncestryRules
{
    public const int MaxWalkDepth = 20;

    public static bool DirectParentIsAncestor(IEnumerable<string>? parents, string ancestorFolderId)
    {
        if (parents == null || string.IsNullOrEmpty(ancestorFolderId))
            return false;
        foreach (var parent in parents)
        {
            if (string.Equals(parent, ancestorFolderId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
