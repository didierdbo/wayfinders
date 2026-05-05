# Wayfinders ML — M1 Scaffold Handoff

**From:** Coda (Python/ML coach)
**To:** Didier
**Date:** 2026-05-05
**Plan doc:** `Owner's Inbox/Game Design/Coda - M1 Slice Training Pipeline Plan - 2026-05-05.md`
**ONNX contract:** `Owner's Inbox/Game Design/Coda - ONNX Export Contract - 2026-05-05.md`

---

## What's in this folder

```
Owner's Inbox/Wayfinders ML/
├── HANDOFF.md                          ← you are here
├── pyproject.toml                      ← deps pinned, ruff + mypy + pytest config
├── wayfinders/
│   └── ml/
│       ├── README.md                   ← hard contracts (EN-pinned, frozen, etc.)
│       ├── __init__.py
│       ├── schemas/
│       │   ├── __init__.py
│       │   ├── numerals.py             ← int_to_words, hours_to_words, ordinal
│       │   ├── vocabularies.py         ← closed lookup tables (~25 vocabs)
│       │   ├── character.py            ← CharacterState + render_character (DONE)
│       │   ├── action.py               ← ActionCard + render_action (DONE, 9 sentences)
│       │   └── context.py              ← SceneState + render_context (DONE, 11 sentences)
│       ├── data_gen/
│       │   ├── __init__.py
│       │   ├── rollouts.py             ← Rollout model (locked) + generator (substep 6)
│       │   └── designer_modifiers.py   ← compute_label seed table (substep 5 finalizes)
│       ├── training/
│       │   ├── __init__.py
│       │   ├── head.py                 ← ResolutionHead MLP (DONE, 1153→256→64→1, tanh×5)
│       │   ├── loss.py                 ← HuberWithMonotonicity (DONE)
│       │   └── train.py                ← TrainConfig + train_loop scaffold (substep 9)
│       ├── export/__init__.py          ← (substeps 11-12)
│       └── inference/__init__.py       ← (substep 7)
└── tests/
    ├── __init__.py
    └── ml/
        ├── __init__.py
        ├── schemas/
        │   ├── __init__.py
        │   ├── _fixtures.py            ← Kira canonical state + ridge scene fixtures
        │   ├── test_numerals.py        ← parametrize + Hypothesis
        │   ├── test_character_render.py
        │   ├── test_action_render.py
        │   └── test_context_render.py
        └── training/
            ├── __init__.py
            └── test_head_shapes.py
```

---

## How to drop this into your repo

```bash
cd ~/code/wayfinders        # or wherever your local clone lives
# Copy the wayfinders/ml/ tree into wayfinders/ml/ in the repo
cp -r "Owner's Inbox/Wayfinders ML/wayfinders/ml" wayfinders/
# Copy tests
cp -r "Owner's Inbox/Wayfinders ML/tests/ml" tests/
# Merge pyproject.toml — your existing FastAPI deps stay; add the ML ones.
# (Don't blindly overwrite if you already have a working pyproject.)
```

Then install + run tests:

```bash
uv sync --all-extras
uv run pytest tests/ml/ -v
```

Expected: all tests pass on a fresh install. If `torch` install is slow on
A2000 (CUDA wheels), use:

```bash
uv pip install --index-strategy unsafe-best-match torch --extra-index-url https://download.pytorch.org/whl/cu124
```

---

## What's DONE (substeps 1-4 + 8 partially)

- **numerals.py** : `int_to_words` (cap 99), `hours_to_words` (canonical Kira values pinned), `ordinal` (1..12).
- **vocabularies.py** : descriptor lanes, HP/stress buckets, bond verbs, manner lanes, mission types, stakes tiers, weather lanes, terrain lanes, watch lanes, faction relations.
- **schemas/character.py** : `CharacterState`, `EpisodicEvent`, `Bond` Pydantic models + `render_character()` reproducing the canonical Kira example structure.
- **schemas/action.py** : `ActionCard`, `OppositionState` + `render_action()` (9 sentences, sentence 9 dropped per Pax §5).
- **schemas/context.py** : `SceneState`, `PartyState`, `WorldState`, `CampaignState`, `PartyMember`, `FactionStance` + `render_context()` (11 sentences).
- **training/head.py** : `ResolutionHead` MLP (1153→256→64→1, tanh×5, ~312k params, Kaiming + small last-layer init).
- **training/loss.py** : `HuberWithMonotonicity` (Huber δ=1.0 + optional monotonicity penalty).
- **tests/** : 80+ tests covering determinism, structure, edge cases, and head shape/range/gradient.

## What's STUBBED (next substeps)

- **data_gen/rollouts.py** : `generate_rollouts` raises NotImplementedError → substep 6.
- **data_gen/designer_modifiers.py** : seed rules in place → real Varn table in substep 5.
- **training/train.py** : `TrainConfig` locked, `train_loop` is a skeleton → substep 9.
- **export/** : empty package → substeps 11-12.
- **inference/** : empty package → substep 7.

---

## First substep you can attack RIGHT NOW (substep 1)

Even with everything pre-written above, here's the genuine first move :

1. Clone `wayfinders` locally (if not already).
2. Drop the `wayfinders/ml/` tree and `tests/ml/` tree into the repo.
3. Merge `pyproject.toml` carefully (keep your FastAPI deps, add ML ones).
4. Run `uv sync --all-extras`.
5. Run `uv run pytest tests/ml/ -v`.
6. **All tests should pass** out of the box. If any fails, that's the first
   real coaching session: we read the failure together, you fix.
7. Open a PR titled "M1 substep 1 — ML scaffold" against `main`. Body should
   reference the M1 plan doc and the ONNX contract doc.

If everything passes cleanly, you've graduated substep 1. Substep 2 is then
**reading every test you just ran** and explaining out loud why each
assertion exists — that's how you internalize the contract.

---

## A note on the `pronoun_subject` field

You'll notice `CharacterState.pronoun_subject` exists as a forward-compat
field. The renderer **does not use it**. The legacy paragraph (paragraph 6)
hardcodes "She" because Varn's canonical example uses "She" inside that
paragraph as the documented exception to the no-pronouns rule.

When you build a non-female character, the legacy paragraph will currently
say "She has fought through..." which is wrong. **This is intentional M1
scope cut** — substep 4 (or M2) will replace `_pronoun_she` with a real
gendered renderer. For now, all M1 sims use Kira anyway, so it doesn't
bite.

I flagged it in code comments so future-you doesn't get confused.

---

## Coaching mode reminders

- Hints before answers. When you hit something unfamiliar (Pydantic v2
  `model_validator`, `Final[Mapping[...]]`, `nn.Module.eval()` semantics),
  ask me before searching. The goal is interview-ready Python, not just
  working code.
- Test-first when adding new schema fields. If Varn extends a vocabulary,
  add the entry, add a test, then write the renderer change.
- Read every error message twice before changing code. PyTorch error
  messages are dense but informative.

Good luck. — Coda
