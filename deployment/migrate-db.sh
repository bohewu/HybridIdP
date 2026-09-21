#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

COMPOSE_MAIN="docker-compose.splithost-nginx-nodb.yml"
COMPOSE_OVERRIDE="docker-compose.override.yml"
COMPOSE_GHCR_OVERRIDE="docker-compose.ghcr-image.yml"
ENV_FILE=".env"
SERVICE="idp-service"
SOURCE="ghcr"
IMAGE_REF=""
BACKUP_CONFIRMED=false

info() { printf '[INFO] %s\n' "$1"; }
error() { printf '[ERROR] %s\n' "$1" >&2; }

resolve_path() {
    local path="$1"
    if [[ "$path" = /* ]]; then
        printf '%s\n' "$path"
    else
        printf '%s\n' "$SCRIPT_DIR/$path"
    fi
}

extract_env_value() {
    local key="$1"
    local env_path="$2"
    local raw

    raw="$(grep -E "^[[:space:]]*${key}=" "$env_path" | tail -n 1 | cut -d '=' -f 2- || true)"
    raw="${raw%%#*}"
    raw="$(printf '%s' "$raw" | xargs)"
    raw="${raw%\"}"
    raw="${raw#\"}"
    raw="${raw%\'}"
    raw="${raw#\'}"
    printf '%s\n' "$raw"
}

is_pinned_image() {
    local image="$1"

    [[ "$image" =~ @sha256:[[:xdigit:]]{64}$ ]] ||
        [[ "$image" =~ :v?[0-9]+(\.[0-9]+){1,3}([._-][[:alnum:]._-]+)?$ ]]
}

usage() {
    cat <<EOF
Usage: $0 [options] --confirm-backup

Options:
  --source <ghcr>        Deployment source (default: $SOURCE)
  --image <ref>          Exact pinned release image
  --compose <file>       Main compose file under deployment/ (default: $COMPOSE_MAIN)
  --override <file>      Override compose file under deployment/ (default: $COMPOSE_OVERRIDE)
  --env-file <file>      Env file under deployment/ or absolute path (default: $ENV_FILE)
  --service <name>       Service name to migrate (default: $SERVICE)
  --confirm-backup       Confirm a restorable database backup exists
  -h, --help             Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --source|--image|--compose|--override|--env-file|--service)
            if [[ $# -lt 2 ]]; then
                error "Missing value for $1."
                exit 1
            fi
            case "$1" in
                --source) SOURCE="$2" ;;
                --image) IMAGE_REF="$2" ;;
                --compose) COMPOSE_MAIN="$2" ;;
                --override) COMPOSE_OVERRIDE="$2" ;;
                --env-file) ENV_FILE="$2" ;;
                --service) SERVICE="$2" ;;
            esac
            shift 2
            ;;
        --confirm-backup)
            BACKUP_CONFIRMED=true
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

if [[ "$BACKUP_CONFIRMED" != true ]]; then
    error "Database backup confirmation is required."
    exit 1
fi

if [[ "$SOURCE" != "ghcr" ]]; then
    error "Database migration requires the ghcr release source."
    exit 1
fi

if [[ "$SERVICE" != "idp-service" ]]; then
    error "Database migration only supports the idp-service entrypoint."
    exit 1
fi

MAIN_PATH="$(resolve_path "$COMPOSE_MAIN")"
OVERRIDE_PATH="$(resolve_path "$COMPOSE_OVERRIDE")"
GHCR_OVERRIDE_PATH="$(resolve_path "$COMPOSE_GHCR_OVERRIDE")"
ENV_PATH="$(resolve_path "$ENV_FILE")"

if [[ ! -s "$ENV_PATH" ]]; then
    error "Deployment env file is missing or empty."
    exit 1
fi

if [[ ! -f "$MAIN_PATH" || ! -f "$GHCR_OVERRIDE_PATH" ]]; then
    error "Deployment compose configuration is incomplete."
    exit 1
fi

DATABASE_PROVIDER="$(extract_env_value "DATABASE_PROVIDER" "$ENV_PATH")"
case "${DATABASE_PROVIDER,,}" in
    sqlserver|postgresql) ;;
    *)
        error "Database provider is unsupported."
        exit 1
        ;;
esac

if [[ -n "$IMAGE_REF" ]]; then
    IDP_IMAGE="$IMAGE_REF"
else
    IDP_IMAGE="$(extract_env_value "IDP_IMAGE" "$ENV_PATH")"
fi

if ! is_pinned_image "$IDP_IMAGE"; then
    error "IDP image must be an exact pinned release reference."
    exit 1
fi

COMPOSE_ARGS=( -f "$MAIN_PATH" -f "$GHCR_OVERRIDE_PATH" )
if [[ -f "$OVERRIDE_PATH" ]]; then
    COMPOSE_ARGS+=( -f "$OVERRIDE_PATH" )
fi
COMPOSE_ARGS+=( --env-file "$ENV_PATH" )

export IDP_ENV_FILE="$ENV_PATH"
export IDP_IMAGE
export DATABASE_PROVIDER

info "Validating migration configuration..."
if ! docker compose "${COMPOSE_ARGS[@]}" config --quiet >/dev/null; then
    error "Migration configuration validation failed."
    exit 1
fi

info "Running schema migration container..."
docker compose "${COMPOSE_ARGS[@]}" run --rm --no-deps "$SERVICE" --migrate-only
