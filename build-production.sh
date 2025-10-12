#!/bin/bash

# Build script for Docker image with environment variables
# This script is for local testing only. Production builds are done by GitLab CI/CD.
# Usage: ./build-production.sh

set -e

echo "⚠️  Warning: This script is for local testing only!"
echo "    Production builds are automated via GitLab CI/CD pipeline."
echo ""

# Load production environment variables
if [ -f .env.production ]; then
  export $(cat .env.production | grep -v '^#' | xargs)
fi

# Default values if not set
NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL:-"https://rumirestaurant.ch"}
NEXT_PUBLIC_IMAGE_BASE_URL=${NEXT_PUBLIC_IMAGE_BASE_URL:-"https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com"}

# Docker image details (for local testing)
IMAGE_NAME=${IMAGE_NAME:-"rumi-restaurant-web"}
IMAGE_TAG=${IMAGE_TAG:-"local"}

echo "🔨 Building Docker image with production environment variables..."
echo "   NEXT_PUBLIC_API_URL: ${NEXT_PUBLIC_API_URL}"
echo "   NEXT_PUBLIC_IMAGE_BASE_URL: ${NEXT_PUBLIC_IMAGE_BASE_URL}"
echo "   Image: ${IMAGE_NAME}:${IMAGE_TAG}"
echo ""

# Build the Docker image with build arguments (local only, no push)
docker buildx build \
  --platform linux/amd64 \
  --build-arg NEXT_PUBLIC_API_URL="${NEXT_PUBLIC_API_URL}" \
  --build-arg NEXT_PUBLIC_IMAGE_BASE_URL="${NEXT_PUBLIC_IMAGE_BASE_URL}" \
  -t ${IMAGE_NAME}:${IMAGE_TAG} \
  --load \
  .

echo ""
echo "✅ Docker image built successfully for local testing!"
echo "   ${IMAGE_NAME}:${IMAGE_TAG}"
echo ""
echo "📝 Note: For production deployment, commit your changes and push to GitLab."
echo "   The GitLab CI/CD pipeline will automatically build and deploy via ArgoCD."
