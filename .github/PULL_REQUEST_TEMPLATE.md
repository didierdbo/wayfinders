## Summary

<one or two sentences — what changes, why now>

## Scope

<paths or modules touched; if multi-area, why this can't be split>

## Tests

- [ ] New tests added for new behavior, or
- [ ] Existing tests cover this change, or
- [ ] N/A (explain)

## Verification done locally

- [ ] `uv run ruff check .` clean
- [ ] `uv run ruff format --check .` clean
- [ ] `uv run mypy wayfinders` clean
- [ ] `uv run pytest -ra` green

## Notes for reviewer

<anything load-bearing for the review — design alternatives considered, follow-up tickets, deliberate non-goals>
