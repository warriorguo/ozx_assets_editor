using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OAE.Core.Resources;

namespace OAE.App.Views;

public sealed record ResourceRow(string Key, string Path, long FileId, string Guid, int Type);

public partial class ResourcesWindow : Window
{
    private TextBlock _statusText = null!;
    private TextBox _filterBox = null!;
    private ListBox _entryList = null!;

    private readonly ObservableCollection<ResourceRow> _rows = new();
    private List<ResourceRow> _allRows = new();
    private ResourcesDbStore? _store;
    private UnityMetaIndex? _meta;

    public ResourcesWindow()
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

    public void Configure(ResourcesDbStore store, UnityMetaIndex meta)
    {
        _store = store;
        _meta = meta;
        Reload();
    }

    private void Reload()
    {
        if (_store is null) return;
        _allRows = _store.List().Select(e => new ResourceRow(
            Key: e.Key,
            Path: ResolvePath(e.Guid),
            FileId: e.FileId,
            Guid: e.Guid,
            Type: e.Type)).ToList();
        _statusText.Text = $"{_allRows.Count} entries · {System.IO.Path.GetFileName(_store.Path)}";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = (_filterBox.Text ?? string.Empty).Trim();
        _rows.Clear();
        foreach (var r in _allRows)
        {
            if (filter.Length > 0 && !r.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _rows.Add(r);
        }
    }

    private string ResolvePath(string guid)
    {
        var p = _meta?.PathFor(guid);
        if (string.IsNullOrEmpty(p)) return "(no .meta found for this guid)";
        // Trim the project root prefix for compactness.
        var root = _meta!.AssetsRoot;
        var parent = System.IO.Path.GetDirectoryName(root);
        if (parent is not null && p.StartsWith(parent, StringComparison.Ordinal))
            return p[(parent.Length + 1)..];
        return p;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (_store is null || _meta is null) return;
        var dlg = new AddResourceDialog();
        dlg.Configure(_meta, _store);
        var result = await dlg.ShowDialog<AddResourceResult?>(this);
        if (result is null) return;
        try
        {
            _store.Add(result.Key, result.Guid, result.FileId, result.Type);
            Reload();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Add failed: {ex.Message}";
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (_entryList.SelectedItem is not ResourceRow row) return;
        try
        {
            _store.Remove(row.Key);
            Reload();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Remove failed: {ex.Message}";
        }
    }
}
