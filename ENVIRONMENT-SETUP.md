# Environment Configuration Guide

This guide explains how to configure environment variables for different environments (local development, staging, production).

## Environment Files

- `.env.example` - Template file with all available environment variables
- `.env.local` - Local development environment (not committed to git)
- `.env.production` - Production environment (committed to git)

## Environment Variables

### `NEXT_PUBLIC_API_URL`
The base URL for the backend API.

- **Local Development**: `http://localhost:5221` (or your backend port)
- **Production**: `https://rumirestaurant.ch/api`

### `NEXT_PUBLIC_IMAGE_BASE_URL`
The base URL for serving images and assets.

- **Default**: `https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com`

## Setup Instructions

### For Local Development

1. Copy the example file:
   ```bash
   cp .env.example .env.local
   ```

2. Update `.env.local` with your local backend URL:
   ```bash
   NEXT_PUBLIC_API_URL=http://localhost:5221
   ```

3. Start the development server:
   ```bash
   npm run dev
   ```

### For Production Deployment

The production environment variables are already configured in `.env.production` and the Kubernetes ConfigMap.

1. **Build Docker Image with Production Environment**:
   ```bash
   ./build-production.sh
   ```

   This script will:
   - Load variables from `.env.production`
   - Build the Docker image with the correct environment variables
   - Push to Docker Hub

2. **Deploy to Kubernetes**:
   ```bash
   kubectl apply -f k8s/eks-deployment.yaml
   ```

   The ConfigMap in `k8s/eks-deployment.yaml` contains the production environment variables.

## How It Works

### During Development (`npm run dev`)
Next.js automatically loads environment variables from `.env.local`.

### During Docker Build
The Dockerfile accepts build arguments:
```dockerfile
ARG NEXT_PUBLIC_API_URL
ARG NEXT_PUBLIC_IMAGE_BASE_URL
ENV NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL}
ENV NEXT_PUBLIC_IMAGE_BASE_URL=${NEXT_PUBLIC_IMAGE_BASE_URL}
```

### In Kubernetes
The ConfigMap injects environment variables into the container:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: rumi-app-config
data:
  NEXT_PUBLIC_API_URL: "https://rumirestaurant.ch/api"
  NEXT_PUBLIC_IMAGE_BASE_URL: "https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com"
```

## Important Notes

⚠️ **Environment variables with `NEXT_PUBLIC_` prefix are embedded into the JavaScript bundle at build time**, not runtime. This means:

1. You must rebuild the Docker image when changing production environment variables
2. The Kubernetes ConfigMap values are used during the Docker build process
3. For runtime configuration, consider using a different approach (API endpoints, server-side env vars)

## Troubleshooting

### Backend API not accessible in production

Check:
1. Ingress is routing `/api` to the backend service
2. Backend pods are running: `kubectl get pods -n rumi-test`
3. Test the API: `curl https://rumirestaurant.ch/api/[endpoint]`

### Environment variables not updating

Remember to:
1. Rebuild the Docker image: `./build-production.sh`
2. Update Kubernetes deployment: `kubectl rollout restart deployment/rumi-restaurant-web -n rumi-test`

## Additional Resources

- [Next.js Environment Variables](https://nextjs.org/docs/basic-features/environment-variables)
- [Docker Build Arguments](https://docs.docker.com/engine/reference/builder/#arg)
- [Kubernetes ConfigMaps](https://kubernetes.io/docs/concepts/configuration/configmap/)
