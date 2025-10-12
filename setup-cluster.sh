#!/bin/bash

# Rumi Restaurant - Kubernetes Cluster Setup Script
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_step() { echo -e "${BLUE}[STEP]${NC} $1"; }

echo "🚀 Rumi Restaurant Kubernetes Setup"
echo "===================================="
echo ""

# Check prerequisites
log_step "Checking prerequisites..."
if ! command -v kubectl &> /dev/null; then
    log_error "kubectl not found. Please install kubectl first."
    exit 1
fi

if ! command -v docker &> /dev/null; then
    log_error "docker not found. Please install Docker first."
    exit 1
fi

log_info "✅ kubectl found: $(kubectl version --client 2>/dev/null | head -1)"
log_info "✅ docker found: $(docker --version)"

echo ""
echo "Choose your Kubernetes setup option:"
echo "1) 🏠 Local Development (Minikube) - Free, good for testing"
echo "2) ☁️  DigitalOcean Kubernetes - Recommended for production (~$24/month)"
echo "3) ☁️  Google GKE - Enterprise-grade (~$30/month)"
echo "4) 📖 Show me the manual setup guide"
echo ""

read -p "Enter your choice (1-4): " choice

case $choice in
    1)
        log_step "Setting up local Minikube cluster..."

        # Install minikube if not present
        if ! command -v minikube &> /dev/null; then
            log_info "Installing minikube..."
            if [[ "$OSTYPE" == "darwin"* ]]; then
                if command -v brew &> /dev/null; then
                    brew install minikube
                else
                    log_error "Homebrew not found. Please install minikube manually."
                    exit 1
                fi
            else
                log_error "Please install minikube manually for your OS."
                exit 1
            fi
        fi

        log_info "Starting minikube cluster..."
        minikube start --driver=docker --cpus=2 --memory=4g

        log_info "Enabling ingress addon..."
        minikube addons enable ingress

        log_info "✅ Minikube cluster is ready!"
        echo ""
        log_warn "For local development, you'll need to:"
        echo "1. Run 'minikube tunnel' in a separate terminal"
        echo "2. Or edit /etc/hosts to map rumirestaurant.ch to $(minikube ip)"
        ;;

    2)
        log_step "Setting up DigitalOcean Kubernetes..."

        # Install doctl if not present
        if ! command -v doctl &> /dev/null; then
            log_info "Installing DigitalOcean CLI..."
            if [[ "$OSTYPE" == "darwin"* ]]; then
                if command -v brew &> /dev/null; then
                    brew install doctl
                else
                    log_error "Homebrew not found. Please install doctl manually."
                    exit 1
                fi
            else
                log_error "Please install doctl manually for your OS."
                exit 1
            fi
        fi

        log_info "Please authenticate with DigitalOcean:"
        doctl auth init

        log_info "Creating Kubernetes cluster (this will take 5-10 minutes)..."
        doctl kubernetes cluster create rumi-restaurant \
            --region fra1 \
            --version 1.28.2-do.0 \
            --count 2 \
            --size s-2vcpu-2gb \
            --node-pool-name worker-pool

        log_info "Configuring kubectl..."
        doctl kubernetes cluster kubeconfig save rumi-restaurant

        log_info "Installing NGINX Ingress Controller..."
        kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.8.2/deploy/static/provider/do/deploy.yaml

        log_info "✅ DigitalOcean cluster is ready!"
        log_warn "Waiting for external IP (this may take 2-5 minutes)..."
        kubectl get svc -n ingress-nginx -w
        ;;

    3)
        log_step "Setting up Google GKE..."
        log_info "Please follow the manual setup guide in CLUSTER-SETUP.md"
        log_warn "You'll need to install 'gcloud' CLI and set up a Google Cloud project first."
        ;;

    4)
        log_info "Opening setup guide..."
        if [[ "$OSTYPE" == "darwin"* ]]; then
            open CLUSTER-SETUP.md 2>/dev/null || cat CLUSTER-SETUP.md
        else
            cat CLUSTER-SETUP.md
        fi
        ;;

    *)
        log_error "Invalid choice. Please run the script again."
        exit 1
        ;;
esac

echo ""
log_step "Next steps after cluster is ready:"
echo "1. Run: kubectl get nodes (to verify cluster)"
echo "2. Run: kubectl get svc -n ingress-nginx (to get external IP)"
echo "3. Run: ./deploy.sh full (to deploy your app)"
echo "4. Update DNS to point to the external IP"
echo ""
log_info "🎉 Happy deploying!"
