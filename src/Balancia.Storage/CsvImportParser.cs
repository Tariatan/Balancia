using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Balancia.Storage;

internal static class CsvImportParser
{
    private static readonly string HeaderMismatchMessage =
        $"Expected the {CsvImportRowValidator.Header.Length} Balancia export columns in their original order: {string.Join(',', CsvImportRowValidator.Header)}.";

    public static (List<CsvImportGroup> Groups, List<ImportIssue> Issues, ImportSummary Summary) Parse(byte[] bytes, DateOnly today)
    {
        var issues = new List<ImportIssue>();
        var rows = new List<CsvImportRow>();
        var sourceRows = ReadRows(bytes, rows, issues, today);
        var groups = CsvImportGroupBuilder.Build(rows, issues);
        if (issues.Count == 0 && groups.Count == 0)
        {
            // An import replaces the ledger, so a file with nothing in it must not be able to empty it.
            issues.Add(new ImportIssue(0, "The file has no rows to import."));
        }

        var summary = CsvImportSummaryBuilder.Build(rows, groups, sourceRows);
        return (groups, issues, summary);
    }

    private static int ReadRows(byte[] bytes, List<CsvImportRow> rows, List<ImportIssue> issues, DateOnly today)
    {
        var sourceRows = 0;
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        using var parser = new TextFieldParser(reader);
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        parser.SetDelimiters(",");

        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(CsvImportRowValidator.Header))
            {
                issues.Add(new ImportIssue(1, HeaderMismatchMessage));
                return 0;
            }

            while (!parser.EndOfData)
            {
                var line = parser.LineNumber;
                string[]? fields;
                try
                {
                    fields = parser.ReadFields();
                }
                catch (MalformedLineException ex)
                {
                    issues.Add(new ImportIssue(line, "Malformed CSV quoting: " + ex.Message));
                    break;
                }

                if (fields is null)
                {
                    break;
                }

                sourceRows++;
                if (fields.Length != CsvImportRowValidator.Header.Length)
                {
                    issues.Add(new ImportIssue(line, $"Expected {CsvImportRowValidator.Header.Length} columns; found {fields.Length}."));
                    continue;
                }

                var (row, errors) = CsvImportRowValidator.Validate(fields, line, today);
                issues.AddRange(errors.Select(error => new ImportIssue(line, error)));

                if (row is not null)
                {
                    rows.Add(row);
                }
            }
        }
        catch (DecoderFallbackException ex)
        {
            issues.Add(new ImportIssue(0, "Invalid UTF-8: " + ex.Message));
        }
        catch (MalformedLineException ex)
        {
            issues.Add(new ImportIssue(1, "Malformed CSV header: " + ex.Message));
        }

        return sourceRows;
    }
}
