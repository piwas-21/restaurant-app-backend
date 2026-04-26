# GitOps Deployment with ArgoCD

### GitOps with ArgoCD
- ✅ K8s manifests in `rumi-argocd-gitops` repo (single source of truth)
- ✅ Automated deployments via ArgoCD
- ✅ GitLab CI/CD triggers ArgoCD updates
- ✅ Version-controlled infrastructure
- ✅ Automatic synchronization

## 📂 Repository Structure

### Frontend Repo (`rumi-restaurant-web`)
```
rumi-restaurant-web/
├── .gitlab-ci.yml          # CI/CD pipeline (builds & triggers GitOps)
├── Dockerfile              # Container image build
├── src/                    # Application code
├── .env.production         # Production environment variables
├── .env.local              # Local development
```

### GitOps Repo (`rumi-argocd-gitops`)
```
rumi-argocd-gitops/
├── .gitlab-ci.yml          # CD pipeline (updates image tags)
├── base/                   # Base Kubernetes manifests
│   ├── frontend-deployment.yaml
│   ├── frontend-configmap.yaml
│   ├── backend-deployment.yaml
│   ├── backend-configmap.yaml
│   ├── ingress.yaml
│   └── kustomization.yaml
├── overlays/dev/           # Environment-specific overrides
│   └── kustomization.yaml  # Image tags updated here by CI/CD
└── components/             # Reusable components (secrets, etc.)
```

## 🔄 Deployment Flow

### 1. Developer Commits Code
```bash
# In rumi-restaurant-web repo
git add .
git commit -m "Add new feature"
git push origin main
```

### 2. GitLab CI Pipeline (Frontend Repo)
```
Stages:
1. test         → Run npm tests, security scans
2. build        → Build Docker image, push to GitLab registry
3. deploy_pipeline → Trigger ArgoCD GitOps pipeline
```

### 3. GitLab CD Pipeline (ArgoCD Repo)
```
Stages:
1. deploy-dev → Update image tag in overlays/dev/kustomization.yaml
              → Commit and push changes to GitOps repo
```

### 4. ArgoCD Syncs Changes
```
ArgoCD detects changes in GitOps repo
→ Pulls new Kubernetes manifests
→ Applies changes to EKS cluster
→ Rolls out new pods with updated image
```

### 5. Application Updated
```
✅ New version deployed automatically
✅ Zero-downtime rolling update
✅ Health checks ensure stability
```

## 🚀 How to Deploy

### Production Deployment
```bash
# 1. Make changes to your code
vim src/components/MyComponent.tsx

# 2. Test locally
npm run dev

# 3. Commit and push (that's it!)
git add .
git commit -m "Update MyComponent"
git push origin main

# The rest is automatic via GitLab CI/CD + ArgoCD!
```

### Check Deployment Status
```bash
# Via kubectl
kubectl get pods -n rumi
kubectl rollout status deployment/rumi-restaurant-web -n rumi

# Via ArgoCD UI
# Open ArgoCD dashboard and check app status
```

## 🔧 Configuration

### Environment Variables

**Production** (in `rumi-argocd-gitops/base/frontend-configmap.yaml`):
```yaml
NEXT_PUBLIC_API_URL: "https://rumirestaurant.ch"
NEXT_PUBLIC_IMAGE_BASE_URL: "https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com"
```

**Local Development** (in `rumi-restaurant-web/.env.local`):
```bash
NEXT_PUBLIC_API_URL=http://localhost:5221
NEXT_PUBLIC_IMAGE_BASE_URL=https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com
```

### Image Registry

- **Frontend**: `registry.gitlab.com/restaurant-app3282120/frontend`
- **Backend**: `registry.gitlab.com/restaurant-app3282120/backend`

### Namespaces

- **Production**: `rumi` namespace
- **Test/Dev**: `rumi-test` namespace (for manual testing)

## 📝 Making Changes

### Updating Frontend Code
1. Edit code in `rumi-restaurant-web`
2. Push to GitLab
3. CI/CD handles the rest

### Updating Kubernetes Configuration
1. Edit manifests in `rumi-argocd-gitops/base/`
2. Push to GitLab
3. ArgoCD syncs changes automatically

### Changing Environment Variables
1. Edit `frontend-configmap.yaml` in GitOps repo
2. Push changes
3. ArgoCD applies new config
4. Restart pods: `kubectl rollout restart deployment/rumi-restaurant-web -n rumi`

## 🧪 Local Testing

### Build Docker Image Locally
```bash
./build-production.sh
```

### Test with Minikube/Local K8s
```bash
# Use archived k8s manifests for local testing
kubectl apply -f .archived/k8s-manual-kubectl-20251013/
```

## ⚠️ Important Notes

1. **Never use `kubectl apply` on production** - Always use GitOps repo
2. **Image tags are auto-updated** - Don't manually edit in GitOps repo
3. **Manual changes will be overwritten** - ArgoCD syncs from Git
4. **Test locally first** - Use `.archived/k8s-*` for local testing

## 🔐 Secrets Management

Secrets are managed via:
- **External Secrets Operator** (for AWS Secrets Manager)
- **GitLab Deploy Tokens** (for pulling images)
- **ConfigMaps** (for non-sensitive config)

## 📊 Monitoring

### Check Application Status
```bash
# Pods
kubectl get pods -n rumi

# Deployments
kubectl get deployments -n rumi

# Services
kubectl get svc -n rumi

# Ingress
kubectl get ingress -n rumi
```

### View Logs
```bash
# Frontend logs
kubectl logs -n rumi -l app=rumi-restaurant-web --tail=100

# Backend logs
kubectl logs -n rumi -l app=rumi-restaurant-backend --tail=100
```

## 🆘 Troubleshooting

### Pipeline Fails at `trigger_deploy_pipeline`
- Check GitLab permissions for triggering downstream pipelines
- Verify `MS_NAME` and `MS_VERSION` variables are set

### ArgoCD Not Syncing
- Check ArgoCD UI for sync errors
- Verify GitOps repo manifests are valid
- Try manual sync in ArgoCD UI

### Pods Not Starting
- Check logs: `kubectl logs -n rumi <pod-name>`
- Check events: `kubectl describe pod -n rumi <pod-name>`
- Verify image exists in registry

## 📚 Additional Resources

- [ENVIRONMENT-SETUP.md](./ENVIRONMENT-SETUP.md) - Environment configuration guide
- [API-URL-FIX.md](./API-URL-FIX.md) - API URL configuration details
- [DEPLOYMENT-STATUS.md](./DEPLOYMENT-STATUS.md) - Current deployment status
- [ArgoCD Documentation](https://argo-cd.readthedocs.io/)
- [Kustomize Documentation](https://kustomize.io/)

## ✨ Benefits of GitOps

1. **Auditable** - All changes tracked in Git
2. **Reversible** - Easy rollback via Git
3. **Declarative** - Desired state in Git
4. **Automated** - No manual deployments
5. **Secure** - No kubectl access needed for deployments
6. **Consistent** - Same process every time

---

**Deployment Method**: GitOps with ArgoCD
**Last Updated**: October 13, 2025
**Status**: ✅ Active
