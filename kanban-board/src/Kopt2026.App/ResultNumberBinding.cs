using System.Windows.Controls;
using System.Windows.Data;

namespace Kopt2026.App;

public sealed class ResultNumberBinding : MultiBinding
{
    public ResultNumberBinding(string path)
    {
        Converter = new ResultNumberConverter();
        Bindings.Add(new Binding(path));
        Bindings.Add(new Binding("DataContext.DecimalPlaces")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
    }
}
