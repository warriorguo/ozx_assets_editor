using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace OAE.Core.GameApi;

/// <summary>
/// Thin client for the OZX runtime <c>GameAPIServer</c>. Default base URL
/// matches <c>GameAPIConfigAsset.port = 18080</c>; the server only listens
/// while the game is running in the Unity editor.
/// </summary>
public sealed class GameApiClient
{
    public const string DefaultBaseUrl = "http://localhost:18080";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public GameApiClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public string BaseUrl => _baseUrl;

    public async Task<FloorState> GetFloorAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"{_baseUrl}/api/state/floor", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var state = JsonSerializer.Deserialize<FloorState>(json, JsonOpts);
        return state ?? new FloorState();
    }
}
