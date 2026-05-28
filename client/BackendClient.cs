using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using SysHttpClient = System.Net.Http.HttpClient;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Godot;

// Thin HTTP client wrapping a single shared System.Net.Http.HttpClient.
// Not a Godot Node — pure C#. The Godot main thread sees only Tasks coming back;
// callers are responsible for marshalling onto the scene tree via CallDeferred
// when they need to mutate nodes (see Main.cs).
//
// Design notes:
//   - Single static HttpClient. Re-creating one per call leaks sockets on Windows
//     (TIME_WAIT exhaustion); the canonical .NET fix is one instance, app-lifetime.
//   - 5s timeout — backend on localhost should be near-instant; anything longer
//     means it's down and we should fail soft rather than hang the client.
//   - All errors are swallowed and logged via GD.PrintErr. The contract is
//     "return null / empty list on failure, never throw" so the caller stays simple.
public static class BackendClient
{
	private const string BaseUrl = "http://localhost:8000";

	// Shared instance, configured once. Static ctor runs lazily on first use.
	private static readonly SysHttpClient _http = CreateClient();

	// snake_case JSON convention matches FastAPI defaults. We also set
	// PropertyNameCaseInsensitive as a belt-and-suspenders for fields without
	// explicit JsonPropertyName attributes.
	private static readonly JsonSerializerOptions _json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private static SysHttpClient CreateClient()
	{
		var c = new SysHttpClient
		{
			BaseAddress = new Uri(BaseUrl),
			Timeout = TimeSpan.FromSeconds(5),
		};
		return c;
	}

	// POST /api/world/tick — drives the world simulation forward one tick.
	// Returns the emergent mission for this tick, or null if none / on failure.
	public static async Task<MissionData?> PostWorldTick(int tick, int seed, string contextProse, CancellationToken ct = default)
	{
		try
		{
			var payload = new TickRequest(tick, seed, contextProse, new List<object>());
			using var resp = await _http.PostAsJsonAsync("/api/world/tick", payload, _json, ct);
			if (!resp.IsSuccessStatusCode)
			{
				GD.PushWarning($"BackendClient: /api/world/tick returned {(int)resp.StatusCode}");
				return null;
			}

			var body = await resp.Content.ReadFromJsonAsync<TickResponse>(_json, ct);
			return body?.Mission;
		}
		catch (Exception ex)
		{
			// TaskCanceledException covers both client-side cancellation and the
			// 5s timeout — both mean "no mission this tick" from the caller's POV.
			GD.PushWarning($"BackendClient: PostWorldTick failed ({ex.GetType().Name}): {ex.Message}");
			return null;
		}
	}

	// GET /api/missions/active — full list of currently active missions.
	// Returns an empty list on failure so callers can iterate unconditionally.
	public static async Task<List<MissionData>> GetActiveMissions(CancellationToken ct = default)
	{
		try
		{
			var list = await _http.GetFromJsonAsync<List<MissionData>>("/api/missions/active", _json, ct);
			return list ?? new List<MissionData>();
		}
		catch (Exception ex)
		{
			GD.PushWarning($"BackendClient: GetActiveMissions failed ({ex.GetType().Name}): {ex.Message}");
			return new List<MissionData>();
		}
	}

	// Inline request/response shapes. Kept private — nothing outside this file
	// should care about the wire format.
	private record TickRequest(
		[property: JsonPropertyName("tick")] int Tick,
		[property: JsonPropertyName("seed")] int Seed,
		[property: JsonPropertyName("context_prose")] string ContextProse,
		[property: JsonPropertyName("company_personas")] List<object> CompanyPersonas
	);

	private record TickResponse(
		[property: JsonPropertyName("mission")] MissionData? Mission
	);
}
