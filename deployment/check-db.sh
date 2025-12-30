#!/bin/bash
#
# HybridIdP Database Connection Tester
# Diagnoses database connectivity by reading .env (if available) or prompting for details.
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_PATH="$SCRIPT_DIR/.env"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
print_error() { echo -e "${RED}[ERROR]${NC} $1"; }
print_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }

echo -e "${CYAN}HybridIdP Database Connection Tester${NC}"
echo "===================================="

host_name=""
port=0

# 1. Try to read from .env
if [ -f "$ENV_PATH" ]; then
    print_info "Found .env file at: $ENV_PATH"
    
    # Grep connection strings
    # Note: This is a simple regex assumption based on setup-env.sh output format
    
    # Try SQL Server first
    sql_conn=$(grep "ConnectionStrings__SqlServerConnection=" "$ENV_PATH" || true)
    if [ -n "$sql_conn" ]; then
        # Extract Server=...;
        if [[ "$sql_conn" =~ Server=([^;]+) ]]; then
            full_server="${BASH_REMATCH[1]}"
            # check for comma port
            if [[ "$full_server" == *","* ]]; then
                host_name=$(echo "$full_server" | cut -d',' -f1)
                port=$(echo "$full_server" | cut -d',' -f2)
            else
                host_name="$full_server"
                port=1433
            fi
        fi
    fi
    
    # Try Postgres if not found
    if [ -z "$host_name" ]; then
        pg_conn=$(grep "ConnectionStrings__PostgreSqlConnection=" "$ENV_PATH" || true)
        if [ -n "$pg_conn" ]; then
             # Extract Host=...;Port=...
             if [[ "$pg_conn" =~ Host=([^;]+) ]]; then
                host_name="${BASH_REMATCH[1]}"
             fi
             if [[ "$pg_conn" =~ Port=([^;]+) ]]; then
                port="${BASH_REMATCH[1]}"
             fi
        fi
    fi
else
    print_warn ".env file not found."
fi

# 2. If not parsed, prompt
if [ -z "$host_name" ]; then
    print_warn "Could not parse database connection details from .env."
    read -rp "Enter Database Host (e.g. localhost, db.internal): " host_name
    read -rp "Enter Database Port (Default: 1433 for SQL, 5432 for Pg): " port_input
    port=${port_input:-1433}
fi

if [ -z "$host_name" ]; then
    print_error "No host provided. Exiting."
    exit 1
fi

print_info "Testing connection to [$host_name] on port [$port]..."

# 3. Test connection (prefer nc, fallback to /dev/tcp)
if command -v nc >/dev/null 2>&1; then
    if nc -zv -w 3 "$host_name" "$port"; then
        print_info "SUCCESS: Successfully connected to $host_name:$port (via nc)"
        exit 0
    else
        print_error "FAILURE: Could not connect to $host_name:$port (via nc)"
        exit 1
    fi
else
    # Bash built-in /dev/tcp
    if timeout 3 bash -c "</dev/tcp/$host_name/$port" 2>/dev/null; then
        print_info "SUCCESS: Successfully connected to $host_name:$port (via /dev/tcp)"
        exit 0
    else
        print_error "FAILURE: Could not connect to $host_name:$port"
        print_warn "Note: 'nc' is recommended for better diagnostics."
        exit 1
    fi
fi
