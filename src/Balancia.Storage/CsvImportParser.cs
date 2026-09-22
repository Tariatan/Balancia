using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Balancia.Storage;

internal static class CsvImportParser
{
    public static (List<CsvImportGroup> Groups, List<ImportIssue> Issues, ImportSummary Summary) Parse(byte[] bytes)
    {
        var issues = new List<ImportIssue>();
        var rows = new List<CsvImportRow>();
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
                issues.Add(new ImportIssue(1, "Expected the 11 import columns in their original order."));
            }

            if (issues.Count == 0)
            {
                while (!parser.EndOfData)
                {
                    var line = parser.LineNumber;
                    string[]? f;
                    try
                    {
                        f = parser.ReadFields();
                    }
                    catch (MalformedLineException ex)
                    {
                        issues.Add(new ImportIssue(line, "Malformed CSV quoting: " + ex.Message));
                        break;
                    }

                    if (f is null)
                    {
                        break;
                    }

                    sourceRows++;
                    if (f.Length != 11)
                    {
                        issues.Add(new ImportIssue(line, $"Expected 11 columns; found {f.Length}."));
                        continue;
                    }

                    var (row, errors) = CsvImportRowValidator.Validate(f, line);
                    issues.AddRange(errors.Select(error => new ImportIssue(line, error)));
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
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

        var groups = CsvImportGroupBuilder.Build(rows, issues);
        var summary = CsvImportSummaryBuilder.Build(rows, groups, sourceRows);
        return (groups, issues, summary);
    }
}
