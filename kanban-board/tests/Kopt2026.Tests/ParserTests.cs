using System.Text;
using Kopt2026.Domain;
using Kopt2026.Language;

namespace Kopt2026.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("MAX", "\n")]
    [InlineData("min", "\r\n")]
    public void Parses_Multiline_Objective_With_Comments_Constants_And_Trailing_Operator(string direction, string newline)
    {
        var source = SourceDocument.FromText(string.Join(newline,
            direction, "  21 + 17 X1 +", "# ignored", "", "    10 X2", " - 1e0", " st ", "X1 + X2 <= 10", "END"));
        var result = new OptParser().Parse(source);

        Assert.True(result.IsValid);
        Assert.Equal(direction == "MAX" ? ObjectiveSense.Maximize : ObjectiveSense.Minimize, result.Model!.Sense);
        Assert.Equal(20, result.Model.Objective.Constant);
        Assert.Equal(17, result.Model.Objective.Coefficients["X1"]);
        Assert.Equal(10, result.Model.Objective.Coefficients["X2"]);
        Assert.Equal(["X1", "X2"], result.Model.Variables.Select(v => v.Name));
        Assert.Equal(8, Assert.Single(result.Model.Constraints).Span.Line);
        Assert.False(new LegacyKoptImporter().Import(source).WasConverted);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Reports_Multiline_Error_At_Original_Line_And_Indented_Column(string newline)
    {
        var result = new OptParser().Parse(SourceDocument.FromText(string.Join(newline,
            "MAX", "X1", "# comment", "", "    2 X2", "ST", "X1 <= 10", "END")));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "KOP011");

        Assert.False(result.IsValid);
        Assert.Equal(5, diagnostic.Span.Line);
        Assert.Equal(5, diagnostic.Span.Column);
    }

    [Fact]
    public void Reports_Duplicate_Term_At_Original_Variable_Position()
    {
        var result = new OptParser().Parse(SourceDocument.FromText("MAX\nX1\n# comment\n    + 2 X1\nST\nX1 <= 10\nEND"));
        var warning = Assert.Single(result.Diagnostics, d => d.Code == "KOP040");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Model!.Objective.Coefficients["X1"]);
        Assert.Equal(4, warning.Span.Line);
        Assert.Equal(9, warning.Span.Column);
        Assert.Equal(2, warning.Span.Length);
    }

    [Theory]
    [InlineData("MAX\nST\nX1 <= 10\nEND", "KOP011")]
    [InlineData("MAX\n# empty\n\nST\nX1 <= 10\nEND", "KOP011")]
    [InlineData("MAX\nX1\n+ X2\nEND", "KOP002")]
    [InlineData("MAX\nX1\nEND\nST\nX1 <= 10\nEND", "KOP002")]
    [InlineData("MAX\nX1 +\n+ X2\nST\nX1 <= 10\nEND", "KOP011")]
    [InlineData("MAX\nX1\n+ @\nST\nX1 <= 10\nEND", "KOP011")]
    [InlineData("MAX\nX\n1\nST\nX <= 10\nEND", "KOP011")]
    [InlineData("MAX\n1e\n3 X1\nST\nX1 <= 10\nEND", "KOP011")]
    public void Rejects_Invalid_Objective_Boundaries_And_Continuation(string text, string code)
    {
        var result = new OptParser().Parse(SourceDocument.FromText(text));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public void Does_Not_Treat_ST_Prefix_In_Variable_Name_As_Separator()
    {
        var result = new OptParser().Parse(SourceDocument.FromText("MAX\nSTOCK\n+ X2\nST\nSTOCK + X2 <= 10\nEND"));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Model!.Objective.Coefficients["STOCK"]);
    }

    [Fact]
    public void Parses_Lp_And_Preserves_Order()
    {
        var source = SourceDocument.FromText("MAX\n3 X1 + 2 X2\nST\nC1: 2 X1 + X2 <= 10\nX1 + 3 X2 <= 12\nEND\n");
        var result = new OptParser().Parse(source);
        Assert.True(result.IsValid);
        Assert.Equal(ObjectiveSense.Maximize, result.Model!.Sense);
        Assert.Equal(["X1", "X2"], result.Model.Variables.Select(v => v.Name));
        Assert.Equal(2, result.Model.Constraints.Count);
    }

    [Fact]
    public void Parses_Objective_Constants_In_Any_Position()
    {
        var source = SourceDocument.FromText("MAX\n21 + 17 X1 + 10 X2 - 1e0\nST\nX1 + X2 <= 10\nEND\n");
        var result = new OptParser().Parse(source);

        Assert.True(result.IsValid);
        Assert.Equal(20, result.Model!.Objective.Constant, 10);
        Assert.Equal(17, result.Model.Objective.Coefficients["X1"], 10);
        Assert.Equal(10, result.Model.Objective.Coefficients["X2"], 10);
    }

    [Fact]
    public void Moves_Constraint_Left_Constant_To_Right_Hand_Side()
    {
        var source = SourceDocument.FromText("MAX\nX1\nST\n2 X1 + 5 <= 15\nEND\n");
        var result = new OptParser().Parse(source);

        Assert.True(result.IsValid);
        var constraint = Assert.Single(result.Model!.Constraints);
        Assert.Equal(0, constraint.Expression.Constant, 10);
        Assert.Equal(10, constraint.RightHandSide, 10);
    }

    [Fact]
    public void Rejects_Missing_Operator_Between_Linear_Terms()
    {
        var result = new OptParser().Parse(SourceDocument.FromText("MAX\n3 X1 2 X2\nST\nX1 <= 1\nEND\n"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "KOP011");
    }

    [Fact]
    public void Reports_Missing_Relation_With_Position()
    {
        var result = new OptParser().Parse(SourceDocument.FromText("MAX\nX\nST\nX 10\nEND\n"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "KOP012");
        Assert.Equal(4, diagnostic.Span.Line);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Detects_Cp949_Without_Replacement()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(949).GetBytes("# 한글 주석\r\nMAX\r\nX\r\nST\r\nX <= 1\r\nEND\r\n");
        var document = SourceDocument.FromBytes(bytes);
        Assert.Equal("CP949", document.EncodingName);
        Assert.Contains("한글", document.Text);
        Assert.DoesNotContain('\uFFFD', document.Text);
    }
}
