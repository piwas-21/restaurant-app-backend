#!/bin/bash

# Script to enable HTTPS on RUMI Restaurant
# Usage: ./enable-https.sh <certificate-arn>

set -e

if [ -z "$1" ]; then
    echo "❌ Error: Certificate ARN required"
    echo ""
    echo "Usage: ./enable-https.sh <certificate-arn>"
    echo ""
    echo "Example:"
    echo "  ./enable-https.sh arn:aws:acm:eu-central-1:670079155071:certificate/12345678-1234-1234-1234-123456789012"
    echo ""
    echo "Ask your AWS admin for the certificate ARN from ACM (Certificate Manager)"
    exit 1
fi

CERT_ARN="$1"

echo "🔐 Enabling HTTPS for RUMI Restaurant"
echo "======================================"
echo ""
echo "Certificate ARN: $CERT_ARN"
echo ""

# Backup current configuration
cp k8s/eks-deployment.yaml k8s/eks-deployment.yaml.backup
echo "✅ Backed up current configuration to k8s/eks-deployment.yaml.backup"

# Update the Ingress annotations
echo "📝 Updating Ingress configuration..."

# Use sed to update the annotations
sed -i '' "s|alb.ingress.kubernetes.io/listen-ports: '\[{\"HTTP\": 80}\]'|alb.ingress.kubernetes.io/listen-ports: '[{\"HTTP\": 80}, {\"HTTPS\": 443}]'|g" k8s/eks-deployment.yaml

# Add certificate ARN and SSL redirect after listen-ports
sed -i '' "/alb.ingress.kubernetes.io\/listen-ports:/a\\
    alb.ingress.kubernetes.io/ssl-redirect: '443'\\
    alb.ingress.kubernetes.io/certificate-arn: $CERT_ARN
" k8s/eks-deployment.yaml

echo "✅ Configuration updated"
echo ""
echo "📋 Changes made:"
echo "  - Enabled HTTPS on port 443"
echo "  - Added certificate ARN"
echo "  - Enabled HTTP to HTTPS redirect"
echo ""
echo "🚀 Applying changes to cluster..."

kubectl apply -f k8s/eks-deployment.yaml

echo ""
echo "⏰ Waiting for ALB to update (this takes 2-5 minutes)..."
echo ""
echo "You can monitor the progress with:"
echo "  kubectl describe ingress rumi-restaurant-web -n rumi-test"
echo ""
echo "Once ready, test with:"
echo "  curl -I https://rumirestaurant.ch"
echo ""
echo "🎉 HTTPS configuration applied!"
