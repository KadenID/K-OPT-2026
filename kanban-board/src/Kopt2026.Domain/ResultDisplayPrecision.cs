namespace Kopt2026.Domain;

public static class ResultDisplayPrecision
{
    public const int Default = 6;
    public const int Minimum = 0;
    public const int Maximum = 15;

    public static bool IsValid(int value) => value is >= Minimum and <= Maximum;
    public static int Normalize(int value) => IsValid(value) ? value : Default;
}
