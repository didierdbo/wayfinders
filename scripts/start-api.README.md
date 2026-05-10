# start-api.ps1 -- launch FastAPI for M1 mission-emergence e2e

Helper script wrapping `uv run uvicorn wayfinders.api.app:app` on
`localhost:8000` for the M1 substep 4d end-to-end test (Rune coaching,
2026-05-10).

## Usage

```powershell
.\scripts\start-api.ps1
```

## Verify

```powershell
curl http://localhost:8000/api/health
# expected: {"status":"ok","model_ready":false}
```

`model_ready: false` is normal -- M1 e2e exercises the world-tick +
mission-resolve endpoints, which fall back to uniform sampling when
the UC1 head is not loaded.

## Full e2e checklist

See the 5-step checklist in
`Owner's Inbox/Godot Coaching/2026-05-10-Wayfinders-M1-substep-4d-e2e-checklist.md`
(PKA repo).

## Pre-conditions

- `uv sync --all-extras --dev` has been run at least once.
- Port 8000 is free.
- Python 3.12+ in the uv-managed virtualenv.
