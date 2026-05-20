using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OAE.Core.Resources;

namespace OAE.App.Views;

public partial class PpuAuditResultsWindow : Window
{
    private TextBlock _statusText = null!;
    private ListBox _list = null!;

    public PpuAuditResultsWindow() { InitializeComponent(); }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _list = this.FindControl<ListBox>("ResultList")!;
    }

    public void Configure(IReadOnlyList<PpuAuditRow> rows)
    {
        _list.ItemsSource = rows;
        var strict = rows.Count(r => r.Severity == PpuSeverity.Strict);
        var pref = rows.Count - strict;
        _statusText.Text = rows.Count == 0
            ? "No PPU mismatches found."
            : $"{rows.Count} mismatch(es) · {strict} Strict · {pref} Preferred";
    }
}
