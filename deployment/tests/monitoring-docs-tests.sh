#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DOC="$REPO_ROOT/docs/MAINTENANCE_GUIDE.md"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/monitoring-docs.XXXXXX")"
FAILURES=0

cleanup() {
    rm -rf -- "$TMP_DIR"
}
trap cleanup EXIT

fail() {
    FAILURES=$((FAILURES + 1))
    printf '[FAIL] %s\n' "$1" >&2
}

extract_compose_block() {
    local compose_name="$1"
    local output="$2"

    awk -v marker="$compose_name" '
        { sub(/\r$/, "") }
        index($0, marker) { marker_seen = 1; next }
        marker_seen && $0 == "```yaml" { in_block = 1; next }
        in_block && $0 == "```" { extracted = 1; exit }
        in_block { print }
        END { if (!extracted) exit 1 }
    ' "$DOC" >"$output"
}

extract_service_block() {
    local compose_block="$1"
    local service="$2"
    local output="$3"

    awk -v service="$service" '
        $0 == "  " service ":" { found = 1; in_service = 1; next }
        in_service && /^[^[:space:]]/ { exit }
        in_service && /^  [^[:space:]][^:]*:$/ { exit }
        in_service { print }
        END { if (!found) exit 1 }
    ' "$compose_block" >"$output"
}

extract_section() {
    local heading="$1"
    local output="$2"

    awk -v heading="$heading" '
        { sub(/\r$/, "") }
        $0 == heading { found = 1; in_section = 1; next }
        in_section && /^## / { exit }
        in_section { print }
        END { if (!found) exit 1 }
    ' "$DOC" >"$output"
}

assert_loopback_port() {
    local name="$1"
    local service_block="$2"
    local expected="$3"
    local ports

    ports="$(awk '
        /^    ports:$/ { in_ports = 1; next }
        in_ports && /^    [^[:space:]][^:]*:/ { in_ports = 0 }
        in_ports && /^      - / {
            sub(/^[[:space:]]*-[[:space:]]*/, "")
            print
        }
    ' "$service_block")"

    if [[ "$ports" != "\"$expected\"" ]]; then
        fail "$name must publish exactly one quoted loopback port mapping ($expected)"
    fi
}

assert_grafana_password_contract() {
    local name="$1"
    local grafana_block="$2"
    local expected='      GF_SECURITY_ADMIN_PASSWORD: "${GRAFANA_PASSWORD:?GRAFANA_PASSWORD must be set and non-empty before startup}"'
    local count

    count="$(grep -Ec '^[[:space:]]*GF_SECURITY_ADMIN_PASSWORD:' "$grafana_block" || true)"
    if [[ "$count" != "1" ]]; then
        fail "$name must declare exactly one Grafana administrator password value"
        return
    fi
    if ! grep -Fqx "$expected" "$grafana_block"; then
        fail "$name must use quoted non-empty GRAFANA_PASSWORD required-value interpolation"
    fi
    if grep -Eq '\$\{GRAFANA_PASSWORD(:-|-[^?]|:=|=[^?])' "$grafana_block"; then
        fail "$name must not use a GRAFANA_PASSWORD fallback or default"
    fi
}

assert_access_guidance() {
    local name="$1"
    local section="$2"
    local log_store_url="$3"

    for required in \
        '`http://127.0.0.1:3000`' \
        "$log_store_url" \
        'read -rsp "Grafana administrator password: " GRAFANA_PASSWORD' \
        'unset GRAFANA_PASSWORD' \
        '**Protected-network exposure:**' \
        'Do not replace the loopback bindings with a broad `0.0.0.0` publication.'; do
        if ! grep -Fq "$required" "$section"; then
            fail "$name access guidance is missing: $required"
        fi
    done
}

if [[ ! -f "$DOC" ]]; then
    printf '[HARNESS] maintenance guide missing: %s\n' "$DOC" >&2
    exit 2
fi

declare -a stacks=(
    'Loki/Grafana|docker-compose.logging.yml|loki|127.0.0.1:3100:3100|`http://127.0.0.1:3100`|## Loki + Grafana 設定'
    'VictoriaLogs/Grafana|docker-compose.logging-victorialogs.yml|victorialogs|127.0.0.1:9428:9428|`http://127.0.0.1:9428`|## VictoriaLogs 設定 (輕量替代方案)'
)

for stack in "${stacks[@]}"; do
    IFS='|' read -r name compose_name log_store expected_log_port log_store_url heading <<<"$stack"
    compose_block="$TMP_DIR/$compose_name"
    section="$TMP_DIR/${compose_name}.section"
    grafana_block="$TMP_DIR/${compose_name}.grafana"
    log_store_block="$TMP_DIR/${compose_name}.${log_store}"

    if ! extract_compose_block "$compose_name" "$compose_block"; then
        fail "$name Compose block is missing or malformed"
        continue
    fi
    if ! extract_service_block "$compose_block" grafana "$grafana_block"; then
        fail "$name Grafana service is missing"
        continue
    fi
    if ! extract_service_block "$compose_block" "$log_store" "$log_store_block"; then
        fail "$name log-store service is missing"
        continue
    fi
    if ! extract_section "$heading" "$section"; then
        fail "$name documentation section is missing"
        continue
    fi

    assert_loopback_port "$name Grafana" "$grafana_block" '127.0.0.1:3000:3000'
    assert_loopback_port "$name $log_store" "$log_store_block" "$expected_log_port"
    assert_grafana_password_contract "$name" "$grafana_block"
    assert_access_guidance "$name" "$section" "$log_store_url"
done

if (( FAILURES > 0 )); then
    printf '[RESULT] monitoring documentation contracts failed: %d\n' "$FAILURES" >&2
    exit 1
fi

printf '[PASS] monitoring documentation contracts passed for both stacks\n'
