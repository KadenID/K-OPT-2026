using System.Text;
using Kopt2026.Domain;
using Kopt2026.Language;

namespace Kopt2026.Tests;

public sealed class LegacyKoptImporterTests
{
    private readonly OptParser parser = new();

    [Fact]
    public void Keeps_Valid_Modern_Document_Unchanged()
    {
        var source = SourceDocument.FromText("MAX\nX1\nST\nX1 <= 10\nEND\n", "CP949");

        var result = new LegacyKoptImporter(parser).Import(source);

        Assert.False(result.WasConverted);
        Assert.Same(source, result.Document);
        Assert.Empty(result.Changes);
        Assert.Equal("CP949", result.Document.EncodingName);
    }

    [Fact]
    public void Converts_Combined_Keywords_And_Legacy_Relations()
    {
        var source = SourceDocument.FromText(
            "MAX 20X1 + 10X2\nST X1 > 1000\nX2 > 1000\n-X1 + X2 < 500\nEND\n");

        var result = new LegacyKoptImporter(parser).Import(source);
        var parsed = parser.Parse(result.Document);

        Assert.True(result.WasConverted);
        Assert.Equal("MAX\n20X1 + 10X2\nST\nX1 >= 1000\nX2 >= 1000\n-X1 + X2 <= 500\nEND\n", result.Document.Text);
        Assert.Equal("UTF-8", result.Document.EncodingName);
        Assert.True(parsed.IsValid);
        Assert.Equal(20, parsed.Model!.Objective.Coefficients["X1"]);
        Assert.Equal(Relation.GreaterOrEqual, parsed.Model.Constraints[0].Relation);
        Assert.Equal(Relation.LessOrEqual, parsed.Model.Constraints[2].Relation);
    }

    [Fact]
    public void Converts_Min_Objective_On_Keyword_Line()
    {
        var result = new LegacyKoptImporter(parser).Import(
            SourceDocument.FromText("MIN 2X1 + X2\nST\nX1 + X2 >= 3\nEND\n"));

        Assert.True(result.WasConverted);
        Assert.StartsWith("MIN\n2X1 + X2\n", result.Document.Text);
        Assert.True(parser.Parse(result.Document).IsValid);
    }

    [Fact]
    public void Preserves_Korean_Comments_When_Importing_Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(949).GetBytes(
            "# 구형 예제 MAX X99\r\nMAX X1\r\n# ST X99 < 1\r\nST X1 < 10\r\nEND\r\n");
        var source = SourceDocument.FromBytes(bytes);

        var result = new LegacyKoptImporter(parser).Import(source);

        Assert.True(result.WasConverted);
        Assert.Contains("# 구형 예제 MAX X99", result.Document.Text);
        Assert.Contains("# ST X99 < 1", result.Document.Text);
        Assert.Contains("X1 <= 10", result.Document.Text);
        Assert.Equal("UTF-8", result.Document.EncodingName);
        Assert.True(parser.Parse(result.Document).IsValid);
    }

    [Fact]
    public void Does_Not_Treat_Keyword_Prefixes_In_Variable_Names_As_Keywords()
    {
        var source = SourceDocument.FromText(
            "MAX MAXVALUE + STOCK\nST MAXVALUE + STOCK < 10\nEND\n");

        var result = new LegacyKoptImporter(parser).Import(source);
        var parsed = parser.Parse(result.Document);

        Assert.True(result.WasConverted);
        Assert.Contains("MAXVALUE + STOCK", result.Document.Text);
        Assert.True(parsed.IsValid);
        Assert.Equal(["MAXVALUE", "STOCK"], parsed.Model!.Variables.Select(variable => variable.Name));
    }

    [Fact]
    public void Rejects_Ambiguous_Multiple_Sections_On_One_Line()
    {
        var source = SourceDocument.FromText("MAX X1 ST X1 < 10\nEND\n");

        var result = new LegacyKoptImporter(parser).Import(source);

        Assert.False(result.WasConverted);
        Assert.Same(source, result.Document);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Keeps_Original_When_Normalized_Result_Is_Still_Invalid()
    {
        var source = SourceDocument.FromText("MAX X1\nST X1 ??? 10\nEND\n");

        var result = new LegacyKoptImporter(parser).Import(source);

        Assert.False(result.WasConverted);
        Assert.Same(source, result.Document);
    }
}
