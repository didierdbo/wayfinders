# start-api.ps1 -- launch the FastAPI service for M1 mission-emergence
# end-to-end testing (etape 4d).
#
# Usage (from repo root, PowerShell) :
#   .\scripts\start-api.ps1
#
# What it does :
#   1. Sets WAYFINDERS_HEAD_CKPT = empty (predictor not-ready ; engine
#      falls back to uniform sampling per spec). The mission-emergence
#      endpoint POST /api/world/tick still returns 200.
#   2. Runs uvicorn against wayfinders.api.app:app on localhost:8000
#      with --reload for live edits.
#
# Pre-conditions :
#   - `uv sync --all-extras --dev` has been run at least once (memory
#     feedback_uv_sync_all_extras : never use --extra <single>).
#   - port 8000 is free.
#
# To verify the service is up after launch (in a second shell) :
#   curl http://localhost:8000/api/health
#   (expected response: {"status":"ok","model_ready":false})

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "[start-api] working dir = $repoRoot"
Write-Host "[start-api] starting uvicorn on http://localhost:8000 ..."
Write-Host "[start-api] press Ctrl+C to stop."
Write-Host ""

# --reload watches wayfinders/ for changes ; convenient during the
# 4d e2e run when Tess + Rune are co-iterating.
uv run uvicorn wayfinders.api.app:app --reload --host 127.0.0.1 --port 8000
