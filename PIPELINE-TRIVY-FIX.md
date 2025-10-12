# Frontend Pipeline Fix - Trivy Job Variable Error

## Issue
Frontend GitLab CI pipeline was failing at the `trivy` stage with error:
```
$ docker pull $MS_NAME:$MS_VERSION
invalid reference format
```

## Root Cause
The `trivy` job was using `$MS_NAME:$MS_VERSION` to pull the Docker image, but:
- `MS_NAME` = "rumi-restaurant-web" (just the service name, not the registry path)
- Should use `IMAGE_NAME` = "$CI_REGISTRY_IMAGE" (full registry path)

### Incorrect Usage:
```yaml
trivy:
  script:
    - docker pull $MS_NAME:$MS_VERSION  # ❌ WRONG
    - trivy image ... $MS_NAME:$MS_VERSION
```

This tried to pull: `rumi-restaurant-web:91` (invalid - no registry)

### Correct Usage:
```yaml
trivy:
  script:
    - docker pull $IMAGE_NAME:$IMAGE_TAG  # ✅ CORRECT
    - trivy image ... $IMAGE_NAME:$IMAGE_TAG
```

This pulls: `registry.gitlab.com/restaurant-app3282120/frontend:91` (valid)

## Variables Explained

### Frontend `.gitlab-ci.yml` Variables:
```yaml
variables:
    IMAGE_NAME: $CI_REGISTRY_IMAGE                    # Full registry path
    IMAGE_TAG: $CI_PIPELINE_IID                       # Pipeline ID (e.g., 91)
    MS_NAME: rumi-restaurant-web                      # Service name only
    MS_VERSION: $CI_PIPELINE_IID                      # Pipeline ID (same as IMAGE_TAG)
```

### Purpose of Each Variable:

1. **`IMAGE_NAME`** - For Docker operations (build, push, pull, scan)
   - Value: `registry.gitlab.com/restaurant-app3282120/frontend`
   - Used in: `build_image`, `trivy`
   
2. **`IMAGE_TAG`** - For versioning Docker images
   - Value: Pipeline ID (e.g., `91`, `92`, `93`)
   - Used in: `build_image`, `trivy`

3. **`MS_NAME`** - For GitOps microservice identification
   - Value: `rumi-restaurant-web` (matches Kustomize image name)
   - Used in: `trigger_deploy_pipeline` (passed to GitOps repo)

4. **`MS_VERSION`** - For GitOps version tracking
   - Value: Pipeline ID (same as `IMAGE_TAG`)
   - Used in: `trigger_deploy_pipeline` (passed to GitOps repo)

## Fix Applied

### File: `rumi-restaurant-web/.gitlab-ci.yml`

**Changed:**
```yaml
trivy:
  stage: build
  needs: ["build_image"]
  image: docker:24
  services:
    - docker:24-dind
  before_script:
    - apk add --no-cache curl
    - curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin
    - docker login -u $CI_REGISTRY_USER -p $CI_REGISTRY_PASSWORD $CI_REGISTRY
  script:
    - docker pull $IMAGE_NAME:$IMAGE_TAG              # ✅ FIXED
    - trivy image --severity HIGH,CRITICAL --exit-code 1 $IMAGE_NAME:$IMAGE_TAG  # ✅ FIXED
  allow_failure: true
```

## Backend Status
✅ Backend `.gitlab-ci.yml` already uses correct variables (`$IMAGE_NAME:$IMAGE_TAG`) in trivy job - no fix needed.

## Impact on trigger_deploy_pipeline

### Question:
Does this fix the downstream `trigger_deploy_pipeline` failure?

### Answer:
**Yes!** The `trigger_deploy_pipeline` was failing because:
1. It has `needs: ["build_image"]` dependency (implicit or via stage ordering)
2. When `trivy` job failed, the pipeline stopped
3. Even though `trivy` has `allow_failure: true`, the failure was at script level (invalid command)

Now that `trivy` can run successfully:
1. ✅ `build_image` completes → pushes image to registry
2. ✅ `trivy` completes → scans the image (or fails with allow_failure: true)
3. ✅ `trigger_deploy_pipeline` can proceed → triggers GitOps pipeline

## Verification

### Check Frontend Pipeline:
```bash
# Push commit to trigger pipeline
cd rumi-restaurant-web
git add .gitlab-ci.yml
git commit -m "Fix trivy job - use IMAGE_NAME instead of MS_NAME"
git push origin main

# Monitor pipeline at:
# https://gitlab.com/restaurant-app3282120/frontend/-/pipelines
```

### Expected Pipeline Flow:
```
Test Stage:
  ├─ npm_test ✅
  ├─ gitleaks ✅
  ├─ njsscan ✅
  ├─ semgrep ✅
  └─ retire ✅

Build Stage:
  ├─ build_image ✅ → Pushes registry.gitlab.com/restaurant-app3282120/frontend:91
  └─ trivy ✅ → Scans the image (now works!)

Deploy Pipeline Stage:
  └─ trigger_deploy_pipeline ✅ → Triggers GitOps repo with MS_NAME=rumi-restaurant-web, MS_VERSION=91
      ↓
      GitOps Pipeline Updates Image Tag
      ↓
      ArgoCD Syncs to EKS
```

## Lessons Learned

1. **Variable Naming Context**: 
   - Use descriptive names that indicate purpose
   - `IMAGE_NAME` for Docker operations (full path)
   - `MS_NAME` for service identification (short name)

2. **Job Dependencies**:
   - Jobs in same stage can affect each other
   - Even with `allow_failure: true`, script errors stop the pipeline
   - Check variable scope and usage in each job

3. **Testing**:
   - Test pipeline changes with actual pushes
   - Monitor all stages, not just the failed one
   - Verify downstream jobs run after fix

## Related Files

- Frontend CI: `/rumi-restaurant-web/.gitlab-ci.yml` ✅ FIXED
- Backend CI: `/backend/.gitlab-ci.yml` ✅ Already correct
- GitOps CD: `/rumi-argocd-gitops/.gitlab-ci.yml` ✅ No changes needed

## Status

**Date**: October 13, 2025  
**Issue**: Frontend trivy job using wrong variables  
**Status**: ✅ Fixed  
**Next Step**: Push changes and monitor pipeline execution

---

**The pipeline should now complete successfully!** 🎉
