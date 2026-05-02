# Wayfinders — Project Context

## What this is

Indirect-command party-management RPG. Long-term destination: 2D tactical (Battle Brothers / XCOM-shaped) shipped on Steam.

## Stack

- **Game logic** (this repo, Python) — rule engine, prose renderers, ML classifier, mission generation. Stateless, API-driven (FastAPI). Must remain language-agnostic at the API boundary.
- **Client** — Godot + C# (separate repo, future). Will call this service over HTTP.
- **Service layer** — Java for multiplayer (separate repo, far future).
- **Marketing site** — React (the `web/` folder for now, later split out).

## ML approach (locked May 2026)

Approach B: three sentence-transformer embeddings (`all-MiniLM-L6-v2`, chosen for ONNX/C# portability) over deterministic prose representations of character/action/context → small concat-MLP head → scalar `Δ ∈ [-5, +5]` added to a base d20 roll. Huber loss with monotonicity regularizer.

Full design lives in the design docs (currently outside this repo, in the owner's Game Design folder; will migrate when stable).

## Code review expectations

- All merges via PR; `main` is protected.
- Tests required for new functionality (pytest).
- Type hints everywhere; mypy clean.
- Ruff clean (lint + format).
- Keep PRs small. One concern per PR.
- The deterministic prose renderers (when added) are a load-bearing contract — any change to render output must be reviewed for byte-stability and embedding-cache impact.

## When @claude is mentioned in a PR review

The reviewer is the project owner (a senior backend engineer learning Python ergonomics on the way to a Godot client). Read the PR diff in full plus the file context around the comment. Push back when a request is weaker than the existing design, but cite specific lines or design docs. Default to making the requested change.
