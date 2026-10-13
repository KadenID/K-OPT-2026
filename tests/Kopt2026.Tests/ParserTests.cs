using System.Text;
using Kopt2026.Domain;
using Kopt2026.Language;

namespace Kopt2026.Tests;

public sealed class ParserTests
{
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
