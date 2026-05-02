# KAI.md — rolling lesson scratchpad

> The single-source notebook for whatever lesson is currently in flight.
> Kai updates this file between sessions. The user reads it before each session
> to remember where things stood.

---

## Project framing (as of 2026-04-24)

- **CharacterCard = admin dev tool.** It exists to dogfood the data contract
  coming out of the Python simulator and to teach React fundamentals. It is
  **NOT** the eventual player-facing UI.
- **Player-facing UI = future Project 2** (diegetic — no visible stat numbers,
  prose descriptors, portraits, behavioral telegraphing). See Varn's design doc
  when published.

### Implications for coaching

1. **Low polish bar on CharacterCard.tsx.** No styling investment, no
   illustration, no player-pretty visuals. Scaffolding only — audience is the
   owner inspecting game state.
2. **Lessons unchanged.** Composition, conditional rendering, state, effects,
   TanStack Query, routing — all stay on plan. The component just happens to
   be admin-only.
3. **Project 2 is where polish lives.** Different visual language, possibly
   different component decomposition, same React skills underneath.

---

## Current lesson: 1.1 — Character Card Viewer

### Goal

Render a single party member as a `CharacterCard` component, fed from a typed
`character.json` fixture. One component, one render, no state yet. Focus is on:

- JSX as function calls (not a template engine)
- Typing props with `interface`, passing data down
- Reading a JSON fixture typed against `Character`
- Composition: `CharacterCard` is a leaf; `App` is the host

### Source of data

A `character.json` exported by the Python simulator's **Step 5 generator**
(coming from `wayfinders/` at the repo root). Until that export lands, we
work from a hand-written fixture that matches the expected schema.

### Exit criterion

All of the following are true:

1. `src/types/character.ts` defines a `Character` type that mirrors the
   Python generator's JSON schema (name, class, stats, HP, notes, whatever
   the Step 5 output contains).
2. `src/fixtures/character.json` is a real sample character, typed on import.
3. `src/components/CharacterCard.tsx` is a pure function component that
   takes `{ character: Character }` and renders it. No state, no effects.
4. `App.tsx` imports the fixture and renders one `<CharacterCard />`.
5. `src/components/CharacterCard.test.tsx` asserts the card renders the
   character's name and at least one stat, using role-based queries
   (`getByRole('heading', ...)`), not `getByTestId`.
6. All of these pass clean:
   - `npm run typecheck`
   - `npm run lint`
   - `npm run test:run`
   - `npm run build`

### Kai's talking points for this lesson

- JSX compiles to `React.createElement` — show the compiled output once so the
  user stops thinking of it as HTML-in-JS.
- Props are function arguments. A component is a function from props to UI.
- "Where does the data come from?" is the first question for every component.
  Today: parent passes it as a prop. Later: URL, server, global store.
- Don't reach for `useState` yet. Static render first, interactivity next lesson.
- Role-based queries in tests aren't stylistic — they're the same accessibility
  tree a screen reader walks. If your test can't find the heading by role,
  neither can a user with assistive tech.

### Anti-patterns to flag if they appear

- Typing props as `any` or inline object literals instead of a named type.
- Array index as `key` (won't appear this lesson, but stay alert).
- Importing JSON without a matching TS type — the whole point of TS is the contract.
- Storing the character in `useState` "because you might change it later." YAGNI.

---

## Lesson log (rolling)

_Nothing shipped yet — scaffold landed 2026-04-22._
