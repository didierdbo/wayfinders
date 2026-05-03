# Wayfinders

Indirect-command party-management RPG. Long-term target: 2D tactical (Battle
Brothers / XCOM-shaped) on Steam.

This repo is a polyglot monorepo:

- **Python game-logic layer** (`wayfinders/`) — rule engine, character
  generation, mission resolution, prose renderers (forthcoming), ML
  classifier (forthcoming). Stateless and HTTP-API-driven (FastAPI).
- **Godot + C# client** (`client/`) — the game itself. Calls the FastAPI
  service over HTTP. Steam is the long-term build target.
- **React companion app** (`web/`) — dev viewers, lesson scaffolding.

For project context (stack, ML approach, review expectations), see
[`CLAUDE.md`](CLAUDE.md).

## Layout

```
wayfinders/        Python package — game logic, rules, models
wayfinders/api/    FastAPI HTTP boundary (called by the Godot+C# client)
tests/             pytest suite (unit + e2e)
scripts/           Operator scripts (e.g. dump_character.py)
client/            Godot 4.6 .NET client (C#) — see client/README.md
web/               React 19 + TypeScript companion app — see web/README.md
.github/           CI workflows, PR template, CODEOWNERS
```

## Dev setup — Python

Requires Python 3.12 and [uv](https://docs.astral.sh/uv/).

```bash
git clone https://github.com/didierdbo/wayfinders.git
cd wayfinders
uv sync --all-extras --dev
uv run pre-commit install        # one-time; runs ruff/mypy on every commit
uv run pytest -ra                # full test suite
```

Common loops:

```bash
uv run ruff check .              # lint
uv run ruff format .             # format
uv run mypy wayfinders           # types
uv run pytest -ra                # tests
uv run python scripts/dump_character.py   # regenerate web/ fixtures
```

## HTTP API

The game-logic layer exposes a FastAPI HTTP boundary in
[`wayfinders/api/`](wayfinders/api/). The Godot+C# client calls in over HTTP;
the layer stays stateless and language-agnostic.

```bash
uv run uvicorn wayfinders.api.app:app --reload    # dev server, http://127.0.0.1:8000
```

Current endpoints:

- `GET /api/health` — liveness/readiness probe.
- `GET /api/units` — roster of unit types (matches the C# `UnitData` record).

## Godot + C# client (`client/`)

The Godot 4.6 .NET client lives in [`client/`](client/). Open
`client/project.godot` in Godot to edit; `dotnet build` from `client/` to
compile from terminal. See [`client/README.md`](client/README.md) for setup.

## Web companion (`web/`)

A Vite + React + TypeScript app for inspecting Python-generated game state and
for working through React lessons. See [`web/README.md`](web/README.md) and
[`web/KAI.md`](web/KAI.md). Tested independently with Vitest.

## Architecture status

- Rule engine (resolution + clocks + conditions + chargen) — stable
- Prose renderers (character/action/context) — design locked, code pending
- ML classifier (sentence-transformer + concat-MLP head) — design locked, training pipeline pending
- FastAPI HTTP boundary — initial scaffold (`/api/health`, `/api/units`); endpoints expanding
- Godot + C# client — Phase 3 L1 (ApiClient autoload + health-check); roster wiring lands at Phase 3 L5
- Java multiplayer service — far future, separate repo

## License

See [`LICENSE`](LICENSE) — Apache License, Version 2.0.
