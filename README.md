# Wayfinders

Indirect-command party-management RPG. Long-term target: 2D tactical (Battle
Brothers / XCOM-shaped) on Steam.

This repo holds the **game-logic layer** in Python: rule engine, character
generation, mission resolution, prose renderers (forthcoming), and the ML
classifier (forthcoming). The layer is designed stateless and HTTP-API-driven
(FastAPI) so the future Godot+C# client and Java multiplayer service can call
in without language coupling.

For project context (stack, ML approach, review expectations), see
[`CLAUDE.md`](CLAUDE.md).

## Layout

```
wayfinders/        Python package — game logic, rules, models
tests/             pytest suite (unit + e2e)
scripts/           Operator scripts (e.g. dump_character.py)
web/               React 19 + TypeScript companion app (lessons + dev viewers)
.github/           CI workflows, PR template, CODEOWNERS
```

## Dev setup

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

## Web companion (`web/`)

A Vite + React + TypeScript app for inspecting Python-generated game state and
for working through React lessons. See [`web/README.md`](web/README.md) and
[`web/KAI.md`](web/KAI.md). Tested independently with Vitest.

## Architecture status

- Rule engine (resolution + clocks + conditions + chargen) — stable
- Prose renderers (character/action/context) — design locked, code pending
- ML classifier (sentence-transformer + concat-MLP head) — design locked, training pipeline pending
- FastAPI HTTP boundary — design locked, scaffold pending
- Godot+C# client, Java multiplayer service — separate repos, future

## License

See [`LICENSE`](LICENSE) — Apache License, Version 2.0.
