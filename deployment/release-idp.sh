#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE_NAME="hybrididp-idp-service"
WORKFLOW_FILE="build-idp-image.yml"
VERSION=""
TAG=""
OWNER=""
REPO=""
TIMEOUT_SECONDS=1800
POLL_INTERVAL_SECONDS=10
SKIP_WAIT=false
SKIP_VERIFY=false

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
Usage: $0 --version <x.y.z> [options]

Options:
  --version <x.y.z>     Release version, example: 1.0.0
  --tag <vX.Y.Z>        Explicit git tag, example: v1.0.0
  --owner <name>        GitHub owner/org (auto-detected from origin by default)
  --repo <name>         GitHub repo name (auto-detected from origin by default)
  --timeout <seconds>   Max wait time for workflow completion (default: $TIMEOUT_SECONDS)
  --interval <seconds>  Poll interval while waiting workflow (default: $POLL_INTERVAL_SECONDS)
  --skip-wait           Do not wait for GitHub Actions workflow result
  --skip-verify         Do not run verify-idp-image.sh against GHCR image
  -h, --help            Show this help

Examples:
  $0 --version 1.0.0
  $0 --version 1.0.1 --skip-wait
  $0 --tag v1.0.2 --owner bohewu --repo HybridIdP
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            VERSION="$2"
            shift 2
            ;;
        --tag)
            TAG="$2"
            shift 2
            ;;
        --owner)
            OWNER="$2"
            shift 2
            ;;
        --repo)
            REPO="$2"
            shift 2
            ;;
        --timeout)
            TIMEOUT_SECONDS="$2"
            shift 2
            ;;
        --interval)
            POLL_INTERVAL_SECONDS="$2"
            shift 2
            ;;
        --skip-wait)
            SKIP_WAIT=true
            shift
            ;;
        --skip-verify)
            SKIP_VERIFY=true
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

if [[ -z "$TAG" && -z "$VERSION" ]]; then
    error "Either --version or --tag is required."
    usage
    exit 1
fi

if [[ -n "$TAG" && -z "$VERSION" ]]; then
    VERSION="${TAG#v}"
fi

if [[ -n "$VERSION" && -z "$TAG" ]]; then
    TAG="v$VERSION"
fi

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
    error "Invalid version format: $VERSION (expected x.y.z)"
    exit 1
fi

if [[ ! "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
    error "Invalid tag format: $TAG (expected vX.Y.Z)"
    exit 1
fi

if [[ ! "$TIMEOUT_SECONDS" =~ ^[0-9]+$ || ! "$POLL_INTERVAL_SECONDS" =~ ^[0-9]+$ ]]; then
    error "--timeout and --interval must be integers."
    exit 1
fi

detect_owner_repo() {
    local origin_url
    origin_url="$(git -C "$REPO_ROOT" remote get-url origin 2>/dev/null || true)"

    if [[ -z "$origin_url" ]]; then
        return
    fi

    if [[ "$origin_url" =~ github\.com[:/]([^/]+)/([^/.]+)(\.git)?$ ]]; then
        if [[ -z "$OWNER" ]]; then
            OWNER="${BASH_REMATCH[1]}"
        fi
        if [[ -z "$REPO" ]]; then
            REPO="${BASH_REMATCH[2]}"
        fi
    fi
}

detect_owner_repo

if [[ -z "$OWNER" || -z "$REPO" ]]; then
    error "Could not detect owner/repo from git origin. Please pass --owner and --repo."
    exit 1
fi

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
    error "Working tree is not clean. Commit or stash changes before releasing."
    exit 1
fi

if git -C "$REPO_ROOT" rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
    error "Tag already exists locally: $TAG"
    exit 1
fi

if git -C "$REPO_ROOT" ls-remote --tags origin "refs/tags/$TAG" | grep -q .; then
    error "Tag already exists on origin: $TAG"
    exit 1
fi

info "Creating tag $TAG on current HEAD..."
git -C "$REPO_ROOT" tag -a "$TAG" -m "release: $TAG"

info "Pushing tag to origin..."
git -C "$REPO_ROOT" push origin "$TAG"

OWNER_LOWER="$(echo "$OWNER" | tr '[:upper:]' '[:lower:]')"
IMAGE_REF="ghcr.io/$OWNER_LOWER/$IMAGE_NAME:$VERSION"

if [[ "$SKIP_WAIT" == true ]]; then
    warn "Skipping workflow wait as requested."
else
    if ! command -v gh >/dev/null 2>&1; then
        warn "GitHub CLI (gh) not found. Skip waiting for workflow."
        SKIP_WAIT=true
    elif ! gh auth status -h github.com >/dev/null 2>&1; then
        warn "GitHub CLI is not authenticated. Skip waiting for workflow."
        SKIP_WAIT=true
    fi
fi

if [[ "$SKIP_WAIT" != true ]]; then
    TAG_SHA="$(git -C "$REPO_ROOT" rev-list -n 1 "$TAG")"
    DEADLINE=$((SECONDS + TIMEOUT_SECONDS))
    RUN_ID=""
    STATUS=""
    CONCLUSION=""

    info "Waiting for workflow '$WORKFLOW_FILE' for tag $TAG (commit $TAG_SHA)..."
    while (( SECONDS < DEADLINE )); do
        RUN_LINE="$(gh run list \
            --repo "$OWNER/$REPO" \
            --workflow "$WORKFLOW_FILE" \
            --branch "$TAG" \
            --event push \
            --limit 1 \
            --json databaseId,status,conclusion,headBranch,event \
            --jq '.[0] | [.databaseId, .status, (.conclusion // ""), (.headBranch // ""), (.event // "")] | @tsv' 2>/dev/null || true)"

        if [[ -z "$RUN_LINE" ]]; then
            info "Tag workflow run not visible yet, waiting..."
            sleep "$POLL_INTERVAL_SECONDS"
            continue
        fi

        RUN_ID="$(echo "$RUN_LINE" | cut -f1)"
        STATUS="$(echo "$RUN_LINE" | cut -f2)"
        CONCLUSION="$(echo "$RUN_LINE" | cut -f3)"
        RUN_BRANCH="$(echo "$RUN_LINE" | cut -f4)"
        RUN_EVENT="$(echo "$RUN_LINE" | cut -f5)"

        if [[ "$RUN_BRANCH" != "$TAG" || "$RUN_EVENT" != "push" ]]; then
            info "Found run id=$RUN_ID branch=$RUN_BRANCH event=$RUN_EVENT (not tag push), waiting..."
            sleep "$POLL_INTERVAL_SECONDS"
            continue
        fi

        info "Workflow run id=$RUN_ID branch=$RUN_BRANCH event=$RUN_EVENT status=$STATUS conclusion=${CONCLUSION:-n/a}"
        if [[ "$STATUS" == "completed" ]]; then
            break
        fi

        sleep "$POLL_INTERVAL_SECONDS"
    done

    if [[ "$STATUS" != "completed" ]]; then
        error "Timed out waiting for workflow completion after ${TIMEOUT_SECONDS}s."
        error "Check run status: gh run list --repo $OWNER/$REPO --workflow $WORKFLOW_FILE --branch $TAG --event push"
        exit 1
    fi

    if [[ "$CONCLUSION" != "success" ]]; then
        error "Workflow finished with conclusion: $CONCLUSION"
        error "Inspect logs: gh run view $RUN_ID --repo $OWNER/$REPO --log"
        exit 1
    fi
fi

if [[ "$SKIP_VERIFY" == true ]]; then
    warn "Skipping GHCR image verification as requested."
else
    info "Verifying GHCR image: $IMAGE_REF"
    bash "$SCRIPT_DIR/verify-idp-image.sh" --source ghcr --image "$IMAGE_REF"
fi

echo -e "${CYAN}Release completed.${NC}"
echo "Git tag:   $TAG"
echo "Image:     $IMAGE_REF"
echo "Deploy cmd: bash $SCRIPT_DIR/deploy-idp.sh --source ghcr --image $IMAGE_REF"
