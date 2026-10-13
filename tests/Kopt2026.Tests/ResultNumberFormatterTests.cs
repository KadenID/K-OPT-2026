using Kopt2026.Application;

namespace Kopt2026.Tests;

public sealed class ResultNumberFormatterTests
{
    [Theory]
    [InlineData(2053333333.33, "2,053,333,333.33")]
    [InlineData(53333.33333, "53,333.33333")]
    [InlineData(173333.3333, "173,333.3333")]
    [InlineData(1000, "1,000")]
    [InlineData(-1234567.5, "-1,234,567.5")]
    [InlineData(0, "0")]
    public void Formats_Result_Numbers_With_Thousands_Separators(double value, string expected) =>
        Assert.Equal(expected, ResultNumberFormatter.Format(value));

    [Fact]
    public void Formats_Special_Values_Explicitly()
    {
        Assert.Equal("-", ResultNumberFormatter.Format(null));
        Assert.Equal("+∞", ResultNumberFormatter.Format(double.PositiveInfinity));
        Assert.Equal("-∞", ResultNumberFormatter.Format(double.NegativeInfinity));
        Assert.Equal("NaN", ResultNumberFormatter.Format(double.NaN));
    }
}
