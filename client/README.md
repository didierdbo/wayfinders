# Wayfinders Client (Godot + C#)

The Godot 4.6 .NET client for Wayfinders. Talks to the FastAPI game-logic service
(`../wayfinders/api/`) over HTTP. Steam is the long-term build target.

## Layout

```
client/
  project.godot          Godot project file (entry point — open this in Godot)
  Wayfinders.Client.csproj
  Wayfinders.Client.sln
  icon.svg               App icon (placeholder)
  services/
    ApiClient.cs         Autoload — wraps a single HttpClient
    ApiClient.tscn       Scene wrapper for the autoload registration
  scenes/
    HealthCheck.cs       Phase 3 L1 verification UI (TEMP — retires end of Phase 3)
    HealthCheck.tscn
```

## Dev setup

Prereqs:

- .NET SDK 8.x (`dotnet --version` should resolve)
- Godot 4.6 .NET (the `mono` build, despite the historical name — it ships with C# support)
- A running FastAPI service on `http://localhost:8000` (see repo root README)

First-time:

```bash
cd client
dotnet restore                         # NuGet restore
# Then open project.godot in Godot 4.6.
# Editor will compile the C# project automatically on first open.
```

Common loops:

```bash
dotnet build                           # Compile only (no engine needed)
```

To run the game: open `client/project.godot` in Godot and press F5. The L1
verification scene (`scenes/HealthCheck.tscn`) is wired as the main scene —
press the button to hit `/api/health`.

## Phase 3 status

Currently at **L1**: `ApiClient` autoload + health-check button. The Phase 3
arc continues with typed DTOs (L2), `/api/units` round-trip (L3),
cancellation discipline (L4), and a server-driven roster as the headline
payoff (L5). Until L5 lands, the main scene is a throwaway verification
surface.

## Architectural notes

- **One `HttpClient` per app**, owned by the `ApiClient` autoload. Never
  construct an `HttpClient` per call — that is a well-known .NET socket-leak
  footgun.
- **Autoloads, not DI**, for cross-cutting services. Godot's scene tree
  is the dependency graph; autoloads are its singleton substitute.
- **Snake_case JSON on the wire** (FastAPI default), PascalCase on the C#
  side. The mapping happens via `System.Text.Json` naming policy in L2.
- **C# is the production language.** GDScript reading literacy only —
  translate any GDScript tutorial on the fly.
