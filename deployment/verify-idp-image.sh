#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

SOURCE="local"
IMAGE_TAG="hybrididp-idp-service:local-verify"
DOCKERFILE_PATH="$REPO_ROOT/Web.IdP/Dockerfile"

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }

usage() {
    cat <<EOF
Usage: $0 [options]

Options:
  --source <local|ghcr>  Image source to verify (default: local)
  --image <ref>          Image reference (default: $IMAGE_TAG)
  -h, --help             Show this help

Examples:
  $0
  $0 --source ghcr --image ghcr.io/bohewu/hybrididp-idp-service:1.0.0
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --source)
            SOURCE="$2"
            shift 2
            ;;
        --image)
            IMAGE_TAG="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            usage
            exit 1
            ;;
    esac
done

if [[ "$SOURCE" != "local" && "$SOURCE" != "ghcr" ]]; then
    error "Invalid --source value: $SOURCE (expected local or ghcr)"
    exit 1
fi

if [[ ! -f "$DOCKERFILE_PATH" ]]; then
    error "Dockerfile not found: $DOCKERFILE_PATH"
    exit 1
fi

if [[ "$SOURCE" == "local" ]]; then
    info "Building local image: $IMAGE_TAG"
    docker build -f "$DOCKERFILE_PATH" -t "$IMAGE_TAG" "$REPO_ROOT"
else
    info "Pulling image from registry: $IMAGE_TAG"
    docker pull "$IMAGE_TAG"
fi

info "Verifying frontend artifacts inside container..."
docker run --rm --entrypoint sh "$IMAGE_TAG" -c "
set -e
test -f /app/wwwroot/dist/.vite/manifest.json
test -d /app/wwwroot/dist/assets
echo 'manifest: /app/wwwroot/dist/.vite/manifest.json'
echo 'assets:   /app/wwwroot/dist/assets'
ls -la /app/wwwroot/dist/.vite/manifest.json
ls -la /app/wwwroot/dist/assets | head -n 10
"

echo -e "${CYAN}Verification passed for image:${NC} $IMAGE_TAG"
