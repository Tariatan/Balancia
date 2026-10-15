namespace Balancia.Storage;

public sealed class CsvImportPreview
{
    internal CsvImportPreview(string path, string hash, long revision, CsvImportGroup[] groups,
        ImportIssue[] issues, ImportSummary summary)
    {
        Path = path;
        FileHash = hash;
        Revision = revision;
        Groups = groups;
        Issues = issues;
        Summary = summary;
    }
    public string Path
    {
        get;
    }
    public string FileHash
    {
        get;
    }
    public long Revision
    {
        get;
    }
    internal CsvImportGroup[] Groups
    {
        get;
    }
    public IReadOnlyList<ImportIssue> Issues
    {
        get;
    }
    public ImportSummary Summary
    {
        get;
    }
    public IReadOnlyList<ImportDateExample> ResolvedDates =>
    [
        .. Groups.Select(g => g.Rows[0])
            .OrderBy(r => r.Line).Take(10).Select(r => new ImportDateExample(r.Line, r.Date))
    ];
    public bool CanApply => Issues.Count == 0;
}
