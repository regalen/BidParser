# Contributing

## Branch model

```
feature/*  ──►  testing  ──►  main
hotfix/*   ──────────────────►  main
```

| Branch | Purpose | Deploys to |
|---|---|---|
| `main` | Production-ready code | Production (Docker `:latest` on `v*` tag) |
| `testing` | Integration gate before production | Test environment (Docker `:testing`) |
| `feature/*` | Individual work items | — |
| `hotfix/*` | Urgent production fixes | — |

## Feature workflow

1. Branch from `testing`:
   ```
   git checkout testing && git pull
   git checkout -b feature/my-thing
   ```
2. Develop, commit locally.
3. Open a PR targeting `testing`. CI runs build + tests; no image is published.
4. Merge the PR into `testing`. CI publishes `bidparser:testing` to GHCR and a
   `BidParserLite-testing-win-x64.exe` artifact in the Actions tab — use these to
   validate on the test environment.
5. Once `testing` is stable, open a PR from `testing` into `main`.
6. Merge and tag:
   ```
   git checkout main && git pull
   git tag v1.2.3
   git push origin v1.2.3
   ```
   The `v*` tag triggers the release pipeline: Docker `:<version>` + `:latest` to
   GHCR, Windows exe to GitHub Releases.

## Hotfix procedure

For urgent fixes that cannot wait for the normal `testing` → `main` flow:

1. Branch from `main`:
   ```
   git checkout main && git pull
   git checkout -b hotfix/description
   ```
2. Fix, commit, open a PR targeting `main`. Merge when CI is green.
3. Tag the fix immediately:
   ```
   git checkout main && git pull
   git tag v1.2.4
   git push origin v1.2.4
   ```
4. Resync `testing` with `main` so the fix is not lost:
   ```
   git checkout testing && git pull
   git merge main
   git push origin testing
   ```

## CI image tag reference

| Event | Docker tags | Windows exe |
|---|---|---|
| Push to `testing` | `:testing`, `sha-<hash>` | Actions artifact (`BidParserLite-testing-win-x64.exe`) |
| Push to `main` (no tag) | *(tests only — no image)* | *(no build)* |
| `v*` tag | `:<version>`, `:latest` | GitHub Release asset |
| PR into `testing`/`main` | *(tests only)* | *(no build)* |
