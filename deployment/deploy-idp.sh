#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

COMPOSE_MAIN="docker-compose.splithost-nginx-nodb.yml"
COMPOSE_OVERRIDE="docker-compose.override.yml"
SERVICE="idp-service"
SKIP_FRONTEND=false
NO_CACHE=false

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }

usage() {
    cat <<EOF
Usage: $0 [options]

Options:
  --compose <file>     Main compose file under deployment/ (default: $COMPOSE_MAIN)
  --override <file>    Override compose file under deployment/ (default: $COMPOSE_OVERRIDE)
  --service <name>     Service name to build/up (default: $SERVICE)
  --skip-frontend      Skip frontend build step
  --no-cache           Build docker image without cache
  -h, --help           Show this help

Example:
  $0
  $0 --compose docker-compose.nginx.yml --service idp-service
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --compose)
            COMPOSE_MAIN="$2"
            shift 2
            ;;
        --override)
            COMPOSE_OVERRIDE="$2"
            shift 2
            ;;
        --service)
            SERVICE="$2"
            shift 2
            ;;
        --skip-frontend)
            SKIP_FRONTEND=true
            shift
            ;;
        --no-cache)
            NO_CACHE=true
            shift
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

MAIN_PATH="$SCRIPT_DIR/$COMPOSE_MAIN"
OVERRIDE_PATH="$SCRIPT_DIR/$COMPOSE_OVERRIDE"
ENV_PATH="$SCRIPT_DIR/.env"

if [[ ! -f "$MAIN_PATH" ]]; then
    error "Compose file not found: $MAIN_PATH"
    exit 1
fi

if [[ "$SKIP_FRONTEND" != true ]]; then
    info "Building frontend assets in container..."
    docker run --rm \
      --user "$(id -u):$(id -g)" \
      -v "$REPO_ROOT:/workspace" \
      -w /workspace/Web.IdP/ClientApp \
      node:22-bookworm-slim \
      sh -lc "npm ci --no-audit --no-fund && npm run build"

    if [[ ! -f "$REPO_ROOT/Web.IdP/wwwroot/dist/.vite/manifest.json" ]]; then
        error "Frontend build did not produce manifest file."
        exit 1
    fi
fi

COMPOSE_ARGS=( -f "$MAIN_PATH" )
if [[ -f "$OVERRIDE_PATH" ]]; then
    COMPOSE_ARGS+=( -f "$OVERRIDE_PATH" )
else
    warn "Override file not found, continuing without it: $OVERRIDE_PATH"
fi

if [[ -f "$ENV_PATH" ]]; then
    COMPOSE_ARGS+=( --env-file "$ENV_PATH" )
fi

UP_ARGS=(up -d --build)
if [[ "$NO_CACHE" == true ]]; then
    UP_ARGS+=(--no-deps)
    info "Running explicit build with --no-cache..."
    docker compose "${COMPOSE_ARGS[@]}" build --no-cache "$SERVICE"
    UP_ARGS=(up -d)
fi

info "Starting service '$SERVICE'..."
docker compose "${COMPOSE_ARGS[@]}" "${UP_ARGS[@]}" "$SERVICE"

info "Done. Current status:"
docker compose "${COMPOSE_ARGS[@]}" ps "$SERVICE"

echo -e "${CYAN}Tip:${NC} use '--skip-frontend' when only backend code changed."
