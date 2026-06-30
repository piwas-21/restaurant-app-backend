# Deployment & Rollback

The canonical deploy/rollback runbook lives in the **deploy repo** (single source
of truth for production infra):

➡️ **https://github.com/piwas-21/restaurant-app-deploy/blob/main/DEPLOYMENT.md**

## Backend quick reference

- **Deploy:** merge to `develop` → promote to `main`. The push to `main` builds
  the image and `deploy.yml` auto-deploys `:latest` to the prod box.
- **Rollback:** **Actions → deploy → Run workflow** → `image_tag = sha-<40hex>`
  (a previous build's commit SHA). Rolls back **only the backend** service.
- ⚠️ A backend code-only rollback does **not** revert an EF migration the bad
  build already applied — prefer rolling *forward* with a fix for schema issues.

This repo's `.github/workflows/deploy.yml` is the backend half; see the canonical
runbook for the full flow, verification, and emergency procedures.
