namespace Kopt2026.App;
internal static class SampleModels
{
    public const string Lp = "MAX\n3 X1 + 2 X2\nST\nCAPACITY: 2 X1 + X2 <= 10\nDEMAND: X1 + 3 X2 <= 12\nEND\n";
    public const string Mip = "MAX\n5 X1 + 3 X2\nST\n2 X1 + X2 <= 7\nX1 + 2 X2 <= 8\nINTEGER X1, X2\nEND\n";
}
