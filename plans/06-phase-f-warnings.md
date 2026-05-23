# Phase F — Warning cleanup

Goal: establish a `-Wall -Wextra` baseline, triage warnings by category, fix the easy/wide categories. Total cleanup is multi-week.

## Items

- [ ] Add `-Wall -Wextra -Wno-unused-parameter` to `server/CMakeLists.txt` default flags. Build.
- [ ] Capture full warning log → `server/WARNINGS_BASELINE.md` with histogram by warning type (e.g. `-Wsign-compare`, `-Wparentheses`, `-Wreorder`, `-Wuninitialized`).
- [ ] Fix the top three categories with safe global transforms (the rest become long-tail).
- [ ] Document remaining categories + counts in `server/WARNINGS_BASELINE.md`.
- [ ] Add CI step (allowed to fail) that re-runs the build and stores the warning log as an artifact.

## Verification

- Baseline file committed.
- A measurable reduction (any) in the top categories.
- Proceed to Phase G.
