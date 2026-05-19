using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OAE.Core.Resources;

namespace OAE.App.Views;

public sealed record SoundRow(string SoundId, string Summary);

public partial class SoundConfigWindow : Window
{
    private TextBlock _statusText = null!;
    private TextBox _filterBox = null!;
    private ListBox _entryList = null!;
    private readonly ObservableCollection<SoundRow> _rows = new();
    private List<SoundRow> _all = new();
    private SoundConfigStore? _store;

    public SoundConfigWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _filterBox = this.FindControl<TextBox>("FilterBox")!;
        _entryList = this.FindControl<ListBox>("EntryList")!;
        _entryList.ItemsSource = _rows;
        _filterBox.TextChanged += (_, _) => ApplyFilter();
    }

    public void Configure(SoundConfigStore store)
    {
        _store = store;
        Reload();
    }

    private void Reload()
    {
        if (_store is null) return;
        _all = _store.List().Select(e => new SoundRow(
            e.SoundId,
            BuildSummary(e))).ToList();
        _statusText.Text = $"{_all.Count} SFX entries · {System.IO.Path.GetFileName(_store.Path)}";
        ApplyFilter();
    }

    private static string BuildSummary(SfxEntry e)
    {
        var c = CultureInfo.InvariantCulture;
        var spatial = e.Spatial != 0 ? " · spatial" : string.Empty;
        return $"vol {e.Volume.ToString("0.##", c)} · pitch {e.PitchMin.ToString("0.##", c)}-{e.PitchMax.ToString("0.##", c)} · cd {e.Cooldown.ToString("0.###", c)} · {e.ClipCount} clip(s){spatial}";
    }

    private void ApplyFilter()
    {
        var filter = (_filterBox.Text ?? string.Empty).Trim();
        _rows.Clear();
        foreach (var r in _all)
        {
            if (filter.Length > 0 && !r.SoundId.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _rows.Add(r);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (_entryList.SelectedItem is not SoundRow row) return;
        try
        {
            if (_store.Remove(row.SoundId)) Reload();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Remove failed: {ex.Message}";
        }
    }
}
