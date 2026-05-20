using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OAE.Core.GameApi;

namespace OAE.App.Views;

public partial class MinimapWindow : Window
{
    // Pixel size of one grid cell on the canvas. The OZX BFS lays out rooms
    // on an integer grid (1 cell = 1 room unit), so this is just visual scale.
    private const double CellSize = 56;
    private const double CellGap = 4;
    private const double CanvasPad = 24;

    private TextBlock _statusText = null!;
    private Canvas _mapCanvas = null!;
    private TextBlock _errorText = null!;
    private StackPanel _detailHost = null!;

    private GameApiClient? _client;
    private FloorState? _floor;
    private string? _selectedRoomId;
    private readonly Dictionary<string, Border> _roomCells = new();

    public MinimapWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _mapCanvas = this.FindControl<Canvas>("MapCanvas")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
        _detailHost = this.FindControl<StackPanel>("DetailHost")!;
    }

    public void Configure(GameApiClient client)
    {
        _client = client;
        _ = LoadAsync();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        if (_client is null) return;
        _statusText.Text = "Loading…";
        _errorText.IsVisible = false;
        _mapCanvas.Children.Clear();
        _roomCells.Clear();
        try
        {
            _floor = await _client.GetFloorAsync();
            RenderFloor();
        }
        catch (Exception ex)
        {
            _floor = null;
            _statusText.Text = "(not loaded)";
            _errorText.Text = $"Could not reach the OZX game API at {_client.BaseUrl}.\n\n"
                + "Make sure the game is running in the Unity editor with the\n"
                + "GameAPI enabled (GameAPIConfig.enableOnStart = true).\n\n"
                + $"Error: {ex.Message}";
            _errorText.IsVisible = true;
            ClearDetail();
        }
    }

    private void RenderFloor()
    {
        if (_floor is null) return;
        var rooms = _floor.Rooms.Where(r => r.HasLayout).ToList();

        var withoutLayout = _floor.Rooms.Count - rooms.Count;
        var theme = string.IsNullOrEmpty(_floor.ThemeId) ? "—" : _floor.ThemeId;
        var current = string.IsNullOrEmpty(_floor.CurrentRoomId) ? "—" : _floor.CurrentRoomId;
        var skipped = withoutLayout > 0 ? $" · {withoutLayout} room(s) without layout (hidden)" : "";
        _statusText.Text =
            $"Floor {_floor.FloorIndex} · theme {theme} · current {current} · {rooms.Count} rooms{skipped}";

        if (rooms.Count == 0)
        {
            _errorText.Text = "Floor returned no rooms with layout. Start a run in the editor and click Refresh.";
            _errorText.IsVisible = true;
            ClearDetail();
            return;
        }

        // Game grid Y goes up; canvas Y goes down — flip.
        var minX = rooms.Min(r => r.GridX);
        var maxX = rooms.Max(r => r.GridX + Math.Max(1, r.Cols) - 1);
        var minY = rooms.Min(r => r.GridY);
        var maxY = rooms.Max(r => r.GridY + Math.Max(1, r.Rows) - 1);

        var cols = maxX - minX + 1;
        var rowsCount = maxY - minY + 1;
        _mapCanvas.Width = CanvasPad * 2 + cols * CellSize;
        _mapCanvas.Height = CanvasPad * 2 + rowsCount * CellSize;

        foreach (var room in rooms)
        {
            var cellX = room.GridX - minX;
            // Flip Y so "up in game" renders "up on screen".
            var cellY = maxY - (room.GridY + Math.Max(1, room.Rows) - 1);
            var widthCells = Math.Max(1, room.Cols);
            var heightCells = Math.Max(1, room.Rows);

            var x = CanvasPad + cellX * CellSize + CellGap / 2;
            var y = CanvasPad + cellY * CellSize + CellGap / 2;
            var w = widthCells * CellSize - CellGap;
            var h = heightCells * CellSize - CellGap;

            var (fill, stroke) = ColorsFor(room);
            var isCurrent = !string.IsNullOrEmpty(room.RoomId) && room.RoomId == _floor.CurrentRoomId;

            var cell = new Border
            {
                Width = w,
                Height = h,
                Background = fill,
                BorderBrush = isCurrent ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)) : stroke,
                BorderThickness = new Thickness(isCurrent ? 2.5 : 1.5),
                CornerRadius = new CornerRadius(4),
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                Padding = new Thickness(6),
                Tag = room.RoomId,
            };
            ToolTip.SetTip(cell, BuildTooltip(room));

            var labelPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            labelPanel.Children.Add(new TextBlock
            {
                Text = ShortLabel(room),
                FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF8)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrEmpty(room.RoomId))
            {
                labelPanel.Children.Add(new TextBlock
                {
                    Text = room.RoomId,
                    FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC8, 0xD4)),
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            cell.Child = labelPanel;
            cell.PointerPressed += (_, _) =>
            {
                if (cell.Tag is string id) SelectRoom(id);
            };

            Canvas.SetLeft(cell, x);
            Canvas.SetTop(cell, y);
            _mapCanvas.Children.Add(cell);

            if (!string.IsNullOrEmpty(room.RoomId))
                _roomCells[room.RoomId!] = cell;

            // Door notches on the room edges.
            foreach (var door in room.Doors)
            {
                var notch = BuildDoorNotch(x, y, w, h, door);
                if (notch is not null) _mapCanvas.Children.Add(notch);
            }
        }

        // Default selection: current room if known, else first room.
        var defaultId = !string.IsNullOrEmpty(_floor.CurrentRoomId) && _roomCells.ContainsKey(_floor.CurrentRoomId!)
            ? _floor.CurrentRoomId
            : rooms[0].RoomId;
        if (!string.IsNullOrEmpty(defaultId)) SelectRoom(defaultId);
    }

    private static Avalonia.Controls.Shapes.Rectangle? BuildDoorNotch(double x, double y, double w, double h, FloorDoorState door)
    {
        const double thickness = 4;
        const double length = 16;
        var fill = door.Locked
            ? new SolidColorBrush(Color.FromRgb(0xE3, 0x9B, 0x9B))
            : new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xDC));
        var rect = new Avalonia.Controls.Shapes.Rectangle { Fill = fill };
        switch (door.Direction)
        {
            case "Up":
                rect.Width = length; rect.Height = thickness;
                Canvas.SetLeft(rect, x + (w - length) / 2);
                Canvas.SetTop(rect, y - thickness / 2);
                return rect;
            case "Down":
                rect.Width = length; rect.Height = thickness;
                Canvas.SetLeft(rect, x + (w - length) / 2);
                Canvas.SetTop(rect, y + h - thickness / 2);
                return rect;
            case "Left":
                rect.Width = thickness; rect.Height = length;
                Canvas.SetLeft(rect, x - thickness / 2);
                Canvas.SetTop(rect, y + (h - length) / 2);
                return rect;
            case "Right":
                rect.Width = thickness; rect.Height = length;
                Canvas.SetLeft(rect, x + w - thickness / 2);
                Canvas.SetTop(rect, y + (h - length) / 2);
                return rect;
            default:
                return null;
        }
    }

    private static (IBrush fill, IBrush stroke) ColorsFor(FloorRoomState room)
    {
        // Boss rooms always read as "boss" regardless of category.
        if (!string.IsNullOrEmpty(room.BossId))
            return (new SolidColorBrush(Color.FromRgb(0x4A, 0x1F, 0x25)),
                    new SolidColorBrush(Color.FromRgb(0x8A, 0x3A, 0x42)));

        switch (room.StageType)
        {
            case "start":
                return (new SolidColorBrush(Color.FromRgb(0x1B, 0x3D, 0x2C)),
                        new SolidColorBrush(Color.FromRgb(0x3F, 0x6E, 0x52)));
            case "teaching":
                return (new SolidColorBrush(Color.FromRgb(0x1E, 0x2F, 0x42)),
                        new SolidColorBrush(Color.FromRgb(0x3A, 0x5A, 0x82)));
        }

        switch (room.Category)
        {
            case "cave":
                return (new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x18)),
                        new SolidColorBrush(Color.FromRgb(0x5C, 0x45, 0x2D)));
            case "basement":
                return (new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x2E)),
                        new SolidColorBrush(Color.FromRgb(0x45, 0x3D, 0x66)));
            case "test":
                return (new SolidColorBrush(Color.FromRgb(0x2E, 0x29, 0x18)),
                        new SolidColorBrush(Color.FromRgb(0x66, 0x55, 0x2A)));
        }

        // Bridge/platform get a slightly different baseline so shape reads visually.
        if (room.Shape == "bridge" || room.Shape == "platform")
            return (new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x2E)),
                    new SolidColorBrush(Color.FromRgb(0x3E, 0x55, 0x5E)));

        return (new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x2C)),
                new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x5E)));
    }

    private static string ShortLabel(FloorRoomState room)
    {
        if (!string.IsNullOrEmpty(room.BossId)) return "BOSS";
        if (room.StageType == "start") return "START";
        if (!string.IsNullOrEmpty(room.Shape) && room.Shape != "normal") return room.Shape!.ToUpperInvariant();
        if (!string.IsNullOrEmpty(room.Category) && room.Category != "normal") return room.Category!.ToUpperInvariant();
        if (!string.IsNullOrEmpty(room.StageType)) return room.StageType!.ToUpperInvariant();
        return "ROOM";
    }

    private static string BuildTooltip(FloorRoomState room)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(room.RoomId)) parts.Add(room.RoomId);
        if (!string.IsNullOrEmpty(room.StageType)) parts.Add($"stage={room.StageType}");
        if (!string.IsNullOrEmpty(room.Category)) parts.Add($"cat={room.Category}");
        if (!string.IsNullOrEmpty(room.Shape)) parts.Add($"shape={room.Shape}");
        if (room.Cleared) parts.Add("cleared");
        if (room.Visited) parts.Add("visited");
        return string.Join(" · ", parts);
    }

    private void SelectRoom(string roomId)
    {
        if (_floor is null) return;
        var room = _floor.Rooms.FirstOrDefault(r => r.RoomId == roomId);
        if (room is null) return;

        if (!string.IsNullOrEmpty(_selectedRoomId)
            && _selectedRoomId != roomId
            && _roomCells.TryGetValue(_selectedRoomId!, out var prev))
        {
            var prevIsCurrent = _selectedRoomId == _floor.CurrentRoomId;
            prev.BorderThickness = new Thickness(prevIsCurrent ? 2.5 : 1.5);
        }

        if (_roomCells.TryGetValue(roomId, out var cell))
        {
            cell.BorderThickness = new Thickness(3);
        }

        _selectedRoomId = roomId;
        BuildDetail(room);
    }

    private void ClearDetail()
    {
        _detailHost.Children.Clear();
        _detailHost.Children.Add(new TextBlock
        {
            Text = "(click a room on the map)",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x90)),
            FontStyle = FontStyle.Italic,
        });
    }

    private void BuildDetail(FloorRoomState room)
    {
        _detailHost.Children.Clear();

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = "ROOM",
            Classes = { "label" },
        });
        header.Children.Add(new TextBlock
        {
            Text = room.RoomId ?? "(no id)",
            FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
        });
        _detailHost.Children.Add(header);

        var kv = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            RowSpacing = 4,
        };
        var rowIdx = 0;
        void AddRow(string key, string value)
        {
            kv.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = key, Classes = { "detailKey" }, VerticalAlignment = VerticalAlignment.Top };
            var v = new TextBlock { Text = value, Classes = { "detailVal" }, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(k, rowIdx);
            Grid.SetColumn(k, 0);
            Grid.SetRow(v, rowIdx);
            Grid.SetColumn(v, 1);
            kv.Children.Add(k);
            kv.Children.Add(v);
            rowIdx++;
        }

        AddRow("Stage", room.StageType ?? "—");
        AddRow("Category", room.Category ?? "—");
        if (!string.IsNullOrEmpty(room.Shape)) AddRow("Shape", room.Shape!);
        if (!string.IsNullOrEmpty(room.BossId)) AddRow("Boss", room.BossId!);
        AddRow("Theme", string.IsNullOrEmpty(_floor?.ThemeId) ? "—" : _floor!.ThemeId!);
        AddRow("Cleared", room.Cleared ? "yes" : "no");
        AddRow("Visited", room.Visited ? "yes" : "no");
        AddRow("Grid", $"({room.GridX}, {room.GridY}) · {Math.Max(1, room.Cols)}×{Math.Max(1, room.Rows)}");
        if (!string.IsNullOrEmpty(room.SpawnPlanId)) AddRow("SpawnPlan", room.SpawnPlanId!);
        if (!string.IsNullOrEmpty(room.LootPlanId)) AddRow("LootPlan", room.LootPlanId!);
        if (room.HasTeleportSpot) AddRow("Teleport", "yes");
        _detailHost.Children.Add(kv);

        _detailHost.Children.Add(BuildSection(
            "ENEMIES",
            room.Enemies.Count == 0 ? "(none)" : null,
            room.Enemies.Select(e =>
            {
                var src = string.IsNullOrEmpty(e.Source) ? "" : $"  ({e.Source})";
                return $"{e.EnemyId}  ×{e.Count}{src}";
            })));

        _detailHost.Children.Add(BuildSection(
            "LOOTABLES",
            room.Lootables.Count == 0 ? "(none)" : null,
            room.Lootables.Select(l =>
            {
                var range = l.MinCount == l.MaxCount ? $"×{l.MinCount}" : $"×{l.MinCount}-{l.MaxCount}";
                return $"{l.ItemId}  {range}  w={l.Weight}";
            })));

        if (room.Doors.Count > 0)
        {
            _detailHost.Children.Add(BuildSection(
                "DOORS",
                null,
                room.Doors.Select(d =>
                {
                    var lk = d.Locked ? " (locked"
                        + (string.IsNullOrEmpty(d.KeyId) ? "" : $", key={d.KeyId}")
                        + ")" : "";
                    return $"{d.Direction}  →  {d.ToRoomId}{lk}";
                })));
        }
    }

    private static Control BuildSection(string title, string? emptyText, IEnumerable<string> lines)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = title, Classes = { "label" } });
        var list = lines.ToList();
        if (list.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = emptyText ?? "(none)",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x65, 0x73)),
                FontStyle = FontStyle.Italic,
                FontSize = 11,
            });
            return panel;
        }
        foreach (var line in list)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily("Menlo,Monaco,Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return panel;
    }
}
