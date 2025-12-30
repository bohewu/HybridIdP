#!/bin/bash
#
# check-legacyauth.sh
# Test LegacyAuth endpoint connectivity for HybridIdP.
#
# Run from deployment/ directory.
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_PATH="$SCRIPT_DIR/.env"

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }

echo -e "${CYAN}HybridIdP Legacy Auth Endpoint Tester${NC}"
echo "======================================="

LOGIN_URL=""

# 1. Try to read from .env
if [ -f "$ENV_PATH" ]; then
    info "Found .env file at: $ENV_PATH"
    
    # Parse LegacyAuth__LoginUrl from .env
    LOGIN_URL=$(grep -E "^LegacyAuth__LoginUrl=" "$ENV_PATH" | cut -d'=' -f2- | tr -d "'" | tr -d '"' | xargs)
fi

# 2. If not found, prompt user
if [ -z "$LOGIN_URL" ]; then
    if [ -f "$ENV_PATH" ]; then
        warn "Could not find LegacyAuth__LoginUrl in .env."
    else
        warn ".env file not found."
    fi
    read -p "Enter LegacyAuth Login URL (e.g. https://legacy-system.internal/api/authenticate/login): " LOGIN_URL
fi

if [ -z "$LOGIN_URL" ]; then
    error "No URL provided. Exiting."
    exit 1
fi

info "Testing connectivity to: $LOGIN_URL"

# Parse URL components
# Extract protocol, host, port, path
PROTOCOL=$(echo "$LOGIN_URL" | grep -oP '^https?')
HOST=$(echo "$LOGIN_URL" | sed -e 's|^[^/]*//||' -e 's|/.*$||' -e 's|:.*$||')
PORT=$(echo "$LOGIN_URL" | grep -oP ':\K[0-9]+' || echo "")

if [ -z "$PORT" ]; then
    if [ "$PROTOCOL" = "https" ]; then
        PORT=443
    else
        PORT=80
    fi
fi

info "Host: $HOST, Port: $PORT, Protocol: $PROTOCOL"

# 3. Test TCP connectivity
info "Step 1: Testing TCP connectivity to $HOST:$PORT..."

if command -v nc &> /dev/null; then
    if nc -z -w 5 "$HOST" "$PORT" 2>/dev/null; then
        info "TCP connection successful."
    else
        error "TCP connection failed."
        echo "Suggestions:"
        echo "  1. Check if the legacy system is running."
        echo "  2. Verify firewall rules allow traffic on port $PORT."
        echo "  3. Ensure DNS resolution is working for $HOST."
        exit 1
    fi
elif command -v timeout &> /dev/null; then
    if timeout 5 bash -c "echo >/dev/tcp/$HOST/$PORT" 2>/dev/null; then
        info "TCP connection successful."
    else
        error "TCP connection failed."
        echo "Suggestions:"
        echo "  1. Check if the legacy system is running."
        echo "  2. Verify firewall rules allow traffic on port $PORT."
        exit 1
    fi
else
    warn "Neither 'nc' nor 'timeout' available. Skipping TCP test."
fi

# 4. Test HTTP connectivity - Try /health endpoint first
info "Step 2: Testing HTTP connectivity..."

# Derive base URL and try /health endpoint
BASE_URL="${PROTOCOL}://${HOST}:${PORT}"
HEALTH_URL="${BASE_URL}/health"

if command -v curl &> /dev/null; then
    info "Trying health endpoint: $HEALTH_URL"
    HEALTH_CODE=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 10 "$HEALTH_URL" 2>/dev/null || echo "000")
    
    if [ "$HEALTH_CODE" = "200" ]; then
        info "Health endpoint responded! Status: $HEALTH_CODE"
        echo ""
        info "=== Legacy Auth Connectivity Test Complete ==="
        info "The legacy system is healthy and reachable."
        exit 0
    elif [ "$HEALTH_CODE" = "404" ]; then
        warn "No /health endpoint found. Testing login URL directly..."
    elif [ "$HEALTH_CODE" != "000" ]; then
        warn "Health endpoint returned: $HEALTH_CODE. Testing login URL directly..."
    else
        warn "Could not reach health endpoint. Testing login URL directly..."
    fi
    
    # Fallback: Test the login URL directly
    info "Testing login endpoint: $LOGIN_URL"
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 10 -X HEAD "$LOGIN_URL" 2>/dev/null || echo "000")
    
    if [ "$HTTP_CODE" = "000" ]; then
        HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 10 -X GET "$LOGIN_URL" 2>/dev/null || echo "000")
    fi
    
    if [ "$HTTP_CODE" = "000" ]; then
        error "HTTP request failed. Could not connect."
        echo "Suggestions:"
        echo "  1. Check if the legacy system is responding to HTTP requests."
        echo "  2. Verify SSL certificate is valid (if using HTTPS)."
        echo "  3. Try: curl -v $LOGIN_URL"
        exit 1
    elif [ "$HTTP_CODE" = "401" ] || [ "$HTTP_CODE" = "403" ] || [ "$HTTP_CODE" = "405" ]; then
        info "HTTP endpoint reachable. Got expected status: $HTTP_CODE (Auth required or Method not allowed)"
    elif [ "$HTTP_CODE" = "404" ]; then
        warn "HTTP 404 - Endpoint not found. Check if the URL path is correct."
        exit 1
    elif [ "$HTTP_CODE" -ge 200 ] && [ "$HTTP_CODE" -lt 300 ]; then
        info "HTTP request successful. Status: $HTTP_CODE"
    else
        warn "HTTP request returned status: $HTTP_CODE"
        echo "This may still be OK if the endpoint requires POST with credentials."
    fi
else
    warn "'curl' not found. Skipping HTTP test."
fi

echo ""
info "=== Legacy Auth Connectivity Test Complete ==="
info "The endpoint appears to be reachable from the host."
echo ""

# 5. Offer Docker network test options
echo -e "${YELLOW}=== Docker Network Test (Recommended) ===${NC}"
echo "Since the legacy system is typically only accessible from within Docker,"
echo "you should test from the idp-service container:"
echo ""

# Detect container name
IDP_CONTAINER=$(docker ps --filter "name=idp-service" --format "{{.Names}}" 2>/dev/null | head -n1)

if [ -n "$IDP_CONTAINER" ]; then
    echo -e "${CYAN}Option 1 (Recommended): Test from idp-service container:${NC}"
    echo "  docker exec $IDP_CONTAINER curl -v $HEALTH_URL"
    echo ""
    echo -e "${CYAN}Option 2: Test with standalone curl container:${NC}"
else
    warn "idp-service container not found. Make sure it's running."
    echo -e "${CYAN}Option: Test with standalone curl container:${NC}"
fi

# Try to detect the network name
NETWORK_NAME=""
if [ -f "$SCRIPT_DIR/docker-compose.splithost-nginx-nodb.yml" ]; then
    NETWORK_NAME="deployment_backend"
elif [ -f "$SCRIPT_DIR/docker-compose.nginx.yml" ]; then
    NETWORK_NAME="deployment_default"
fi

if [ -n "$NETWORK_NAME" ]; then
    echo "  docker run --rm --network $NETWORK_NAME curlimages/curl -v $HEALTH_URL"
else
    echo "  docker run --rm --network <YOUR_NETWORK> curlimages/curl -v $HEALTH_URL"
    echo "  (To find network: docker network ls)"
fi

echo ""
echo "Choose test method:"
echo "  1) Test from idp-service container (recommended)"
echo "  2) Test with standalone curl container"
echo "  n) Skip"
read -p "Enter choice [1/2/n]: " TEST_CHOICE

case "$TEST_CHOICE" in
    1)
        if [ -n "$IDP_CONTAINER" ]; then
            info "Running: docker exec $IDP_CONTAINER curl -v $HEALTH_URL"
            docker exec "$IDP_CONTAINER" curl -v "$HEALTH_URL"
        else
            error "idp-service container not found. Please start it first."
        fi
        ;;
    2)
        if [ -z "$NETWORK_NAME" ]; then
            read -p "Enter Docker network name: " NETWORK_NAME
        fi
        if [ -n "$NETWORK_NAME" ]; then
            info "Running: docker run --rm --network $NETWORK_NAME curlimages/curl -v $HEALTH_URL"
            docker run --rm --network "$NETWORK_NAME" curlimages/curl -v "$HEALTH_URL"
        else
            error "No network name provided."
        fi
        ;;
    *)
        info "Skipping Docker network test."
        ;;
esac

echo ""
echo -e "${YELLOW}Next Steps:${NC}"
echo "  1. Ensure LegacyAuth__Secret is correctly configured."
echo "  2. Test actual login with a legacy user account."
exit 0
