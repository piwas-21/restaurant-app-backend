#!/bin/bash

# Build script for Docker image with environment variables
# Usage: ./build-production.sh

set -e

# Load production environment variables
if [ -f .env.production ]; then
  export $(cat .env.production | grep -v '^#' | xargs)
fi

# Default values if not set
NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL:-"https://rumirestaurant.ch/api"}
NEXT_PUBLIC_IMAGE_BASE_URL=${NEXT_PUBLIC_IMAGE_BASE_URL:-"https://rumi-test-backend-bucket.s3.eu-central-1.amazonaws.com"}

# Docker image details
IMAGE_NAME=${IMAGE_NAME:-"mahmutkaya/rumi-restaurant-web"}
IMAGE_TAG=${IMAGE_TAG:-"latest"}

echo "🔨 Building Docker image with production environment variables..."
echo "   NEXT_PUBLIC_API_URL: ${NEXT_PUBLIC_API_URL}"
echo "   NEXT_PUBLIC_IMAGE_BASE_URL: ${NEXT_PUBLIC_IMAGE_BASE_URL}"
echo "   Image: ${IMAGE_NAME}:${IMAGE_TAG}"
echo ""

# Build the Docker image with build arguments
docker buildx build \
  --platform linux/amd64 \
  --build-arg NEXT_PUBLIC_API_URL="${NEXT_PUBLIC_API_URL}" \
  --build-arg NEXT_PUBLIC_IMAGE_BASE_URL="${NEXT_PUBLIC_IMAGE_BASE_URL}" \
  -t ${IMAGE_NAME}:${IMAGE_TAG} \
  -t ${IMAGE_NAME}:amd64 \
  --push \
  .

echo ""
echo "✅ Docker image built and pushed successfully!"
echo "   ${IMAGE_NAME}:${IMAGE_TAG}"
echo "   ${IMAGE_NAME}:amd64"
