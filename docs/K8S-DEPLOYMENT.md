# Rumi Restaurant Web - Kubernetes Deployment

This repository contains the Kubernetes manifests and Docker configuration for deploying the Rumi Restaurant Web application.

## Prerequisites

- Docker
- Kubernetes cluster
- kubectl configured
- NGINX Ingress Controller (for ingress)
- cert-manager (optional, for SSL certificates)
- Metrics Server (for HPA)

## Quick Start

### 1. Build and Deploy

```bash
# Build, push, and deploy in one command
./deploy.sh full

# Or step by step:
./deploy.sh build    # Build Docker image
./deploy.sh push     # Push to registry
./deploy.sh deploy   # Deploy to Kubernetes
```

### 2. Environment Variables

Set these environment variables before deployment:

```bash
export DOCKER_REGISTRY="your-registry.com"
export IMAGE_NAME="rumi-restaurant-web"
export IMAGE_TAG="v1.0.0"
export NAMESPACE="rumi-production"
export ENVIRONMENT="production"
```

## Architecture

### Resource Limits

The application is configured with the following resource limits:

#### Default (Base)
- **CPU Request**: 100m (0.1 CPU)
- **CPU Limit**: 500m (0.5 CPU)
- **Memory Request**: 128Mi
- **Memory Limit**: 512Mi

#### Production
- **CPU Request**: 200m (0.2 CPU)
- **CPU Limit**: 1000m (1 CPU)
- **Memory Request**: 256Mi
- **Memory Limit**: 1Gi

#### Staging
- **CPU Request**: 50m (0.05 CPU)
- **CPU Limit**: 300m (0.3 CPU)
- **Memory Request**: 128Mi
- **Memory Limit**: 512Mi

### Scaling

#### Horizontal Pod Autoscaler (HPA)
- **Min Replicas**: 2
- **Max Replicas**: 10
- **CPU Target**: 70%
- **Memory Target**: 80%

### Health Checks

The application includes comprehensive health checks:

- **Startup Probe**: `/api/health` (30 attempts, 5s interval)
- **Liveness Probe**: `/api/health` (10s interval)
- **Readiness Probe**: `/api/health` (5s interval)

### Security

- **Non-root user**: Runs as user ID 1001
- **Read-only filesystem**: Root filesystem is read-only
- **No privilege escalation**: Security context prevents privilege escalation
- **Capabilities dropped**: All Linux capabilities are dropped
- **Security headers**: Configured via ingress annotations

## Directory Structure

```
k8s/
├── configmap.yaml           # Environment variables
├── deployment.yaml          # Main application deployment
├── service.yaml            # Service definitions
├── ingress.yaml            # External access configuration
├── hpa.yaml                # Horizontal Pod Autoscaler
├── network-policy.yaml     # Network security policies
├── kustomization.yaml      # Base kustomize configuration
└── overlays/
    ├── production/
    │   ├── kustomization.yaml
    │   ├── deployment-patch.yaml
    │   └── ingress-patch.yaml
    └── staging/
        ├── kustomization.yaml
        └── deployment-patch.yaml
```

## Deployment Commands

### Using kubectl

```bash
# Deploy base configuration
kubectl apply -k k8s/

# Deploy production configuration
kubectl apply -k k8s/overlays/production/

# Deploy staging configuration
kubectl apply -k k8s/overlays/staging/
```

### Using kustomize

```bash
# Build and preview
kustomize build k8s/overlays/production/

# Apply directly
kustomize build k8s/overlays/production/ | kubectl apply -f -
```

## Monitoring and Troubleshooting

### Check Deployment Status

```bash
# Get pods
kubectl get pods -l app=rumi-restaurant-web

# Get services
kubectl get svc -l app=rumi-restaurant-web

# Get ingress
kubectl get ingress -l app=rumi-restaurant-web

# Check HPA status
kubectl get hpa rumi-restaurant-web-hpa
```

### View Logs

```bash
# View application logs
kubectl logs -l app=rumi-restaurant-web -f

# View logs for specific pod
kubectl logs <pod-name> -f
```

### Debug Pod Issues

```bash
# Describe pod
kubectl describe pod <pod-name>

# Get pod events
kubectl get events --sort-by='.lastTimestamp'

# Execute commands in pod
kubectl exec -it <pod-name> -- /bin/sh
```

## Configuration

### Environment Variables

Modify `k8s/configmap.yaml` to add your environment variables:

```yaml
data:
  NODE_ENV: "production"
  NEXT_PUBLIC_API_URL: "https://your-api-domain.com"
  NEXT_PUBLIC_APP_URL: "https://your-app-domain.com"
```

### Ingress Configuration

Update `k8s/ingress.yaml` or the overlay patches with your domain:

```yaml
spec:
  rules:
  - host: your-domain.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: rumi-restaurant-web-service
            port:
              number: 80
```

### SSL/TLS

For SSL termination, uncomment the TLS section in ingress:

```yaml
spec:
  tls:
  - hosts:
    - your-domain.com
    secretName: rumi-restaurant-web-tls
```

## Performance Tuning

### Resource Optimization

Monitor your application and adjust resources accordingly:

```bash
# Check resource usage
kubectl top pods -l app=rumi-restaurant-web

# Check HPA status
kubectl describe hpa rumi-restaurant-web-hpa
```

### Database Connections

For production deployments with databases, consider:
- Connection pooling
- Read replicas for read-heavy workloads
- Database connection limits

## Security Considerations

1. **Image Security**: Regularly update base images and scan for vulnerabilities
2. **Network Policies**: Review and adjust network policies as needed
3. **RBAC**: Implement proper Role-Based Access Control
4. **Secrets Management**: Use Kubernetes secrets for sensitive data
5. **Pod Security Standards**: Consider implementing Pod Security Standards

## Backup and Disaster Recovery

1. **Data Backup**: Implement regular backups for persistent data
2. **Configuration Backup**: Version control all Kubernetes manifests
3. **Recovery Testing**: Regularly test disaster recovery procedures

## CI/CD Integration

This setup can be integrated with CI/CD pipelines:

```bash
# GitLab CI/CD example
./deploy.sh full
```

For more advanced deployments, consider using tools like Helm, ArgoCD, or Flux.
