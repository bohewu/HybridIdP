#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE_TAG="${1:-hybrididp-idp-service:local-verify}"
DOCKERFILE_PATH="$REPO_ROOT/Web.IdP/Dockerfile"

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }

if [[ ! -f "$DOCKERFILE_PATH" ]]; then
    error "Dockerfile not found: $DOCKERFILE_PATH"
    exit 1
fi

info "Building image: $IMAGE_TAG"
docker build -f "$DOCKERFILE_PATH" -t "$IMAGE_TAG" "$REPO_ROOT"

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
