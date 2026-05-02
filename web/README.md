# `web/` — Wayfinders companion app

React 19 + TypeScript + Vite front-end living inside the `wayfinders` monorepo.
The Python simulation engine is at the repo root (`wayfinders/`); this subfolder is
its visual companion — and the user's training ground for modern React.

## What it is for

1. **Companion tools** for the Python sim: character viewers, encounter timelines,
   scenario dashboards. Reads JSON produced by the simulator.
2. **React practice repo**. Each lesson (see `KAI.md`) adds a real, useful feature
   to this app rather than building throwaway toys.

## Stack

- Vite 8 · React 19.2 · TypeScript 6 (strict)
- ESLint 9 flat config · Prettier · Vitest 4 + React Testing Library + user-event
- Plain CSS for now. Tailwind arrives between Project 1 and Project 2.
- No router and no data-fetching library yet — added at their lessons.

## Scripts

```
npm run dev         # Vite dev server
npm run build       # typecheck + production build
npm run typecheck   # tsc --noEmit, no bundling
npm run lint        # ESLint flat config
npm run test        # Vitest watch
npm run test:run    # Vitest single pass (CI)
npm run format      # Prettier write
```

## Layout

```
web/
├── src/
│   ├── components/   # UI building blocks (one file per component)
│   ├── fixtures/     # Sample character.json / encounter.json for local dev + tests
│   ├── types/        # TS types mirroring the Python simulator's schemas
│   ├── test/         # Vitest setup (RTL + jest-dom matchers)
│   ├── App.tsx
│   ├── main.tsx
│   └── index.css
├── KAI.md            # Rolling lesson scratchpad (current lesson + exit criterion)
└── ...config files
```

## Relationship to the repo root

- The root `.gitignore` already covers Node artifacts under `web/`.
- Do not run `git init` here — this folder is part of the parent repo.
- Python tooling (venv, pytest, ruff) lives at the root and is unrelated to this subfolder.

See `KAI.md` for the current lesson and exit criterion.
