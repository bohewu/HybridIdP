#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

COMPOSE_MAIN="docker-compose.splithost-nginx-nodb.yml"
COMPOSE_OVERRIDE="docker-compose.override.yml"
COMPOSE_GHCR_OVERRIDE="docker-compose.ghcr-image.yml"
ENV_FILE=".env"
SERVICE="idp-service"
SOURCE="local"
IMAGE_REF=""
NO_CACHE=false

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }

resolve_path() {
    local path="$1"
    if [[ "$path" = /* ]]; then
        echo "$path"
    else
        echo "$SCRIPT_DIR/$path"
    fi
}

extract_idp_image_from_env_file() {
    local env_path="$1"
    if [[ ! -f "$env_path" ]]; then
        return
    fi

    local raw
    raw="$(grep -E '^[[:space:]]*IDP_IMAGE=' "$env_path" | tail -n 1 | cut -d '=' -f 2- || true)"
    raw="${raw%%#*}"
    raw="$(echo "$raw" | xargs)"
    raw="${raw%\"}"
    raw="${raw#\"}"
    raw="${raw%\'}"
    raw="${raw#\'}"
    echo "$raw"
}

usage() {
    cat <<EOF
Usage: $0 [options]

Options:
  --source <local|ghcr>  Deployment source (default: $SOURCE)
  --image <ref>          Image reference for ghcr source (e.g. ghcr.io/org/hybrididp-idp-service:main)
  --compose <file>       Main compose file under deployment/ (default: $COMPOSE_MAIN)
  --override <file>      Override compose file under deployment/ (default: $COMPOSE_OVERRIDE)
  --env-file <file>      Env file under deployment/ or absolute path (default: $ENV_FILE)
  --service <name>       Service name to deploy (default: $SERVICE)
  --no-cache             Build docker image without cache (local source only)
  --skip-frontend        Deprecated (frontend is built in Dockerfile)
  -h, --help             Show this help

Examples:
  $0 --source local
  $0 --source ghcr --image ghcr.io/my-org/hybrididp-idp-service:main
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --source)
            SOURCE="$2"
            shift 2
            ;;
        --image)
            IMAGE_REF="$2"
            shift 2
            ;;
        --compose)
            COMPOSE_MAIN="$2"
            shift 2
            ;;
        --override)
            COMPOSE_OVERRIDE="$2"
            shift 2
            ;;
        --env-file)
            ENV_FILE="$2"
            shift 2
            ;;
        --service)
            SERVICE="$2"
            shift 2
            ;;
        --no-cache)
            NO_CACHE=true
            shift
            ;;
        --skip-frontend)
            warn "'--skip-frontend' is deprecated. Frontend is built in Web.IdP/Dockerfile."
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

if [[ "$SOURCE" != "local" && "$SOURCE" != "ghcr" ]]; then
    error "Invalid --source value: $SOURCE (expected local or ghcr)"
    exit 1
fi

if [[ "$SOURCE" == "ghcr" && "$NO_CACHE" == true ]]; then
    warn "'--no-cache' only applies to local source and will be ignored."
fi

MAIN_PATH="$(resolve_path "$COMPOSE_MAIN")"
OVERRIDE_PATH="$(resolve_path "$COMPOSE_OVERRIDE")"
GHCR_OVERRIDE_PATH="$(resolve_path "$COMPOSE_GHCR_OVERRIDE")"
ENV_PATH="$(resolve_path "$ENV_FILE")"

if [[ ! -f "$MAIN_PATH" ]]; then
    error "Compose file not found: $MAIN_PATH"
    exit 1
fi

COMPOSE_ARGS=( -f "$MAIN_PATH" )

if [[ "$SOURCE" == "ghcr" ]]; then
    if [[ ! -f "$GHCR_OVERRIDE_PATH" ]]; then
        error "GHCR compose override not found: $GHCR_OVERRIDE_PATH"
        exit 1
    fi
    COMPOSE_ARGS+=( -f "$GHCR_OVERRIDE_PATH" )
fi

if [[ -f "$OVERRIDE_PATH" ]]; then
    COMPOSE_ARGS+=( -f "$OVERRIDE_PATH" )
else
    warn "Override file not found, continuing without it: $OVERRIDE_PATH"
fi

if [[ -f "$ENV_PATH" ]]; then
    COMPOSE_ARGS+=( --env-file "$ENV_PATH" )
    export IDP_ENV_FILE="$ENV_PATH"
else
    error "Env file not found: $ENV_PATH"
    exit 1
fi

if [[ -n "$IMAGE_REF" ]]; then
    export IDP_IMAGE="$IMAGE_REF"
fi

if [[ "$SOURCE" == "ghcr" && -z "${IDP_IMAGE:-}" ]]; then
    IDP_IMAGE="$(extract_idp_image_from_env_file "$ENV_PATH")"
    if [[ -n "$IDP_IMAGE" ]]; then
        export IDP_IMAGE
    fi
fi

if [[ "$SOURCE" == "ghcr" ]]; then
    if [[ -z "${IDP_IMAGE:-}" ]]; then
        error "IDP image is not set. Use --image or define IDP_IMAGE in $ENV_PATH."
        exit 1
    fi

    info "Using configured GHCR image."
fi

info "Validating deployment configuration..."
if ! docker compose "${COMPOSE_ARGS[@]}" config --quiet >/dev/null; then
    error "Deployment configuration validation failed; correct the named variable(s)."
    exit 1
fi

if [[ "$SOURCE" == "ghcr" ]]; then
    info "Pulling service '$SERVICE' image..."
    docker compose "${COMPOSE_ARGS[@]}" pull "$SERVICE"

    info "Starting service '$SERVICE' without build..."
    docker compose "${COMPOSE_ARGS[@]}" up -d --no-build "$SERVICE"
else
    if [[ "$NO_CACHE" == true ]]; then
        info "Building service '$SERVICE' with --no-cache..."
        docker compose "${COMPOSE_ARGS[@]}" build --no-cache "$SERVICE"
        info "Starting service '$SERVICE'..."
        docker compose "${COMPOSE_ARGS[@]}" up -d "$SERVICE"
    else
        info "Building and starting service '$SERVICE'..."
        docker compose "${COMPOSE_ARGS[@]}" up -d --build "$SERVICE"
    fi
fi

info "Done. Current status:"
docker compose "${COMPOSE_ARGS[@]}" ps "$SERVICE"

if [[ "$SOURCE" == "ghcr" ]]; then
    echo -e "${CYAN}Tip:${NC} use '--image' to pin an exact release tag."
else
    echo -e "${CYAN}Tip:${NC} use '--source ghcr --image <tag>' to avoid remote host build time."
fi
