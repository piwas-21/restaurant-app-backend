# Runbook — Deployment & Rollback (production)

How RUMI ships to production and how to roll back. Production is a single Netcup
box running the stack via Docker Compose; images are built in CI and pulled from
GHCR. This runbook is mirrored in the frontend repo — the mechanism is identical,
only the service tag differs (`BACKEND_TAG` here, `FRONTEND_TAG` there).

> **Audience:** anyone promoting a release or responding to a bad deploy.
> **Prereqs / secrets setup:** see `deploy/README.md` in the workspace (keypair,
> `DEPLOY_*` Actions secrets, passwordless `chown` sudoers).

---

## Topology

```
merge to main ─► build-image.yml ─► GHCR (:latest, :sha-<commit>) ─► deploy.yml ─► SSH ─► box: deploy.sh
                  (build + push)        (image registry)              (auto/manual)        (pull + up -d)
```

- **Two repos, one box.** `restaurant-app-backend` and `restaurant-app-frontend`
  each build their own image and each have their own `deploy.yml`. A deploy from
  one repo only re-points **that repo's** service.
- **Image tags.** Every push to `main` publishes two GHCR tags: `latest` (moving)
  and `sha-<40-hex-commit>` (immutable). Rollbacks target the immutable `sha-` tag.
- **Source of truth = `.env` on the box.** `/opt/rumi/deploy/.env` pins
  `BACKEND_TAG` and `FRONTEND_TAG`. `deploy.sh` persists whatever tag it deploys,
  so a rollback survives restarts, and the next real release moves the service
  forward again.

---

## Normal deployment (automatic)

1. Merge your PR into `develop`; validate on the test environment.
2. Promote `develop` → `main` (the release PR).
3. The push to `main` triggers `build-image` → publishes `:latest` + `:sha-<commit>`.
4. On success, `deploy.yml` fires automatically (`workflow_run`) and deploys
   `latest` for that repo's service. **No manual step.**

Nothing to do beyond the promote. Watch **Actions → deploy** for the run.

## Manual deploy / redeploy (no rollback)

Use when you want to re-run a deploy without a new merge (e.g. after changing a
box-side secret).

- GitHub UI: **Actions → deploy → Run workflow** → leave `image_tag = latest`.
- This is identical to the automatic path.

---

## Rollback

A rollback re-points the running container at an **already-published** image — it
builds nothing and is fast. It only affects the service whose repo you run it from.

### 1. Find the tag to roll back to
You need the **immutable `sha-` tag** of the last good build:

- From `main` history: `git -C <repo> log --oneline main` → copy the full 40-char
  SHA of the known-good commit → the tag is `sha-<that-sha>`.
- Or browse the GHCR package's **Tags** (repo → Packages → the image) and pick the
  `sha-…` published just before the bad one.

### 2. Run the rollback
**Actions → deploy → Run workflow** (in the affected repo) → set
`image_tag = sha-<40hex>` → Run.

That sets `BACKEND_TAG` (or `FRONTEND_TAG`) in the box `.env`, pulls that image,
and restarts the one service. The other service is untouched.

### 3. Confirm
See **Verifying a deploy** below.

### Rolling forward again
Once a fix is merged and promoted to `main`, the automatic deploy ships `latest`
and the service leaves the pinned `sha-`. To clear a pin without a new release,
run the workflow with `image_tag = latest`.

> ⚠️ **Backend schema caveat.** The backend auto-runs EF migrations on startup, so
> rolling the **backend** image back does **not** revert database migrations the
> bad build already applied. If the bad deploy added a migration, a code-only
> rollback can mismatch the schema. Prefer rolling *forward* with a fix for
> schema problems; only hard-rollback the backend when the bad build had no
> migration. (See the `project_netcup_box` history: deploy code with/before the
> schema change, never the schema ahead of the code.)

---

## Verifying a deploy

```bash
# from a machine with box SSH access (see deploy/.ssh/box.sh — runs as root):
bash deploy/.ssh/box.sh 'cd /opt/rumi/deploy && grep -E "^(BACKEND|FRONTEND)_TAG=" .env && docker compose -f docker-compose.prod.yml ps'
```
External smoke check:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://www.rumirestaurant.ch/         # frontend -> 200
curl -sS -o /dev/null -w '%{http_code}\n' https://www.rumirestaurant.ch/api/health # backend  -> 200
```
Backend startup / migration logs:
```bash
bash deploy/.ssh/box.sh 'cd /opt/rumi/deploy && docker compose -f docker-compose.prod.yml logs --tail=80 backend'
```

---

## Emergency manual deploy (CI/SSH-from-Actions unavailable)

SSH to the box as `rumi` and drive `deploy.sh` directly:
```bash
ssh rumi@159.195.137.101
cd /opt/rumi/deploy
BACKEND_TAG=sha-<40hex> ./deploy.sh      # rollback backend
FRONTEND_TAG=latest    ./deploy.sh       # redeploy frontend
./deploy.sh                              # deploy whatever .env currently pins
```
`deploy.sh` is idempotent and persists the tag to `.env`.

---

## How it works (reference)

- **`.github/workflows/build-image.yml`** — builds + pushes the image to GHCR on
  push to `main`/`develop` and `v*` tags. Does **not** deploy.
- **`.github/workflows/deploy.yml`** — `workflow_run` after a successful
  `build-image` on `main` (auto, deploys `latest`) **or** `workflow_dispatch`
  (manual, deploys the `image_tag` input). Validates the tag shape, then SSHes to
  the box and runs `deploy.sh` with the service tag set. A `prod-deploy`
  concurrency group serializes runs within a repo; a `flock` on the box serializes
  across repos.
- **box `/opt/rumi/deploy/deploy.sh`** — upserts the tag into `.env`, `compose
  pull`, fixes `app-secrets.json` perms for the backend container gid, `compose up
  -d`, prunes dangling images.
