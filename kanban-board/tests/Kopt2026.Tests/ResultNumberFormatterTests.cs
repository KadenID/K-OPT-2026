using Kopt2026.Application;

namespace Kopt2026.Tests;

public sealed class ResultNumberFormatterTests
{
    [Theory]
    [InlineData(1.23456789, 6, "1.234568")]
    [InlineData(-1.23456789, 6, "-1.234568")]
    [InlineData(1.125, 2, "1.13")]
    [InlineData(-1.125, 2, "-1.13")]
    [InlineData(1234.5, 0, "1,235")]
    [InlineData(-1234.5, 0, "-1,235")]
    [InlineData(999.999, 2, "1,000")]
    [InlineData(-0.0000001, 6, "0")]
    [InlineData(0.0000005, 6, "0.000001")]
    [InlineData(1.2345678901234567, 15, "1.234567890123457")]
    [InlineData(0.000000000000001, 15, "0.000000000000001")]
    [InlineData(1.2, 6, "1.2")]
    public void Rounds_To_Maximum_Selected_Decimal_Places(double value, int digits, string expected) =>
        Assert.Equal(expected, ResultNumberFormatter.Format(value, digits));

    [Fact]
    public void Defaults_To_Six_And_Does_Not_Alter_The_Input()
    {
        const double value = 1.23456789;
        Assert.Equal("1.234568", ResultNumberFormatter.Format(value));
        Assert.Equal("1.23", ResultNumberFormatter.Format(value, 2));
        Assert.Equal("1.234568", ResultNumberFormatter.Format(value, 6));
        Assert.Equal(1.23456789, value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void Rejects_Invalid_Explicit_Precision(int digits) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultNumberFormatter.Format(1.25, digits));

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
