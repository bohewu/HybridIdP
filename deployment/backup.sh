#!/bin/bash
# HybridIdP Backup Script
# Backs up: certificates, .env, nginx config, and docker logs
#
# Usage: ./backup.sh [backup_dir]
# Example: ./backup.sh /backups/hybrididp

set -e

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUP_BASE="${1:-/backups/hybrididp}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_DIR="${BACKUP_BASE}/${TIMESTAMP}"
RETENTION_DAYS=30

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Create backup directory
mkdir -p "${BACKUP_DIR}"
log_info "Backup directory: ${BACKUP_DIR}"

# 1. Backup certificates (CRITICAL)
log_info "Backing up certificates..."
if [ -d "${SCRIPT_DIR}/certs" ]; then
    cp -r "${SCRIPT_DIR}/certs" "${BACKUP_DIR}/certs"
    log_info "  ✓ Certificates backed up"
else
    log_warn "  ⚠ No certs directory found"
fi

# 2. Backup .env file (contains passwords)
log_info "Backing up .env file..."
if [ -f "${SCRIPT_DIR}/.env" ]; then
    cp "${SCRIPT_DIR}/.env" "${BACKUP_DIR}/.env"
    chmod 600 "${BACKUP_DIR}/.env"
    log_info "  ✓ .env backed up (permissions set to 600)"
else
    log_warn "  ⚠ No .env file found"
fi

# 3. Backup nginx configuration
log_info "Backing up nginx configuration..."
if [ -d "${SCRIPT_DIR}/nginx" ]; then
    cp -r "${SCRIPT_DIR}/nginx" "${BACKUP_DIR}/nginx"
    log_info "  ✓ Nginx config backed up"
else
    log_warn "  ⚠ No nginx directory found"
fi

# 4. Export Docker container logs
log_info "Backing up Docker logs..."
mkdir -p "${BACKUP_DIR}/logs"

# Get logs from all hybrididp related containers
for container in $(docker ps -a --format '{{.Names}}' | grep -E '(idp|nginx-gateway|redis)' 2>/dev/null || true); do
    if [ -n "$container" ]; then
        docker logs "${container}" > "${BACKUP_DIR}/logs/${container}.log" 2>&1 || true
        log_info "  ✓ ${container} logs exported"
    fi
done

# 5. Create compressed archive
log_info "Creating compressed archive..."
ARCHIVE_NAME="hybrididp_backup_${TIMESTAMP}.tar.gz"
cd "${BACKUP_BASE}"
tar -czf "${ARCHIVE_NAME}" "${TIMESTAMP}"
rm -rf "${TIMESTAMP}"
log_info "  ✓ Archive created: ${BACKUP_BASE}/${ARCHIVE_NAME}"

# 6. Cleanup old backups
log_info "Cleaning up old backups (older than ${RETENTION_DAYS} days)..."
find "${BACKUP_BASE}" -name "hybrididp_backup_*.tar.gz" -mtime +${RETENTION_DAYS} -delete 2>/dev/null || true
log_info "  ✓ Cleanup complete"

# 7. Summary
echo ""
log_info "========================================="
log_info "Backup completed successfully!"
log_info "Location: ${BACKUP_BASE}/${ARCHIVE_NAME}"
log_info "========================================="

# List current backups
echo ""
log_info "Current backups:"
ls -lh "${BACKUP_BASE}"/hybrididp_backup_*.tar.gz 2>/dev/null || log_warn "No backups found"
