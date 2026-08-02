#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOYMENT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOYMENT_DIR/.." && pwd)"
SENTINEL="T1_SECRET_SENTINEL"
FAILURES=0
HARNESS_ERRORS=0
declare -A FAILURE_COUNTS=()
declare -A FIRST_FAILURE=()

fail() {
    local contract="$1"
    local detail="$2"
    FAILURES=$((FAILURES + 1))
    FAILURE_COUNTS["$contract"]=$(( ${FAILURE_COUNTS["$contract"]:-0} + 1 ))
    if [[ -z "${FIRST_FAILURE["$contract"]:-}" ]]; then
        FIRST_FAILURE["$contract"]="$detail"
    fi
}

harness_error() {
    HARNESS_ERRORS=$((HARNESS_ERRORS + 1))
    printf '[HARNESS] %s\n' "$1" >&2
}

for command_name in bash docker; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        harness_error "required command unavailable: $command_name"
    fi
done
if command -v jq >/dev/null 2>&1; then
    JSON_READER="jq"
elif command -v python3 >/dev/null 2>&1; then
    JSON_READER="python3"
else
    harness_error "required JSON reader unavailable: install jq or python3"
fi
if (( HARNESS_ERRORS > 0 )); then
    exit 2
fi
if ! docker compose version >/dev/null 2>&1; then
    harness_error "docker compose v2 is unavailable"
    exit 2
fi

TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/deployment-hardening.XXXXXX")" || exit 2
cleanup() {
    rm -rf -- "$TMP_DIR"
}
trap cleanup EXIT

COMPOSE_FILES=(
    "docker-compose.internal.yml"
    "docker-compose.nginx.yml"
    "docker-compose.splithost.yml"
    "docker-compose.splithost-nginx.yml"
    "docker-compose.splithost-nginx-nodb.yml"
)
MODE_NAMES=("internal" "nginx" "splithost" "splithost-nginx" "splithost-nginx-nodb")
MODE_REQUIRED=(
    "DATABASE_PROVIDER ConnectionStrings__SqlServerConnection ConnectionStrings__PostgreSqlConnection ConnectionStrings__RedisConnection ENCRYPTION_CERT_PASSWORD SIGNING_CERT_PASSWORD MSSQL_SA_PASSWORD POSTGRES_PASSWORD OpenIddict__Issuer PUBLIC_AUTHORITY"
    "DATABASE_PROVIDER ConnectionStrings__SqlServerConnection ConnectionStrings__PostgreSqlConnection ConnectionStrings__RedisConnection ENCRYPTION_CERT_PASSWORD SIGNING_CERT_PASSWORD MSSQL_SA_PASSWORD POSTGRES_PASSWORD OpenIddict__Issuer PUBLIC_AUTHORITY"
    "DATABASE_PROVIDER ConnectionStrings__SqlServerConnection ConnectionStrings__PostgreSqlConnection ConnectionStrings__RedisConnection ENCRYPTION_CERT_PASSWORD SIGNING_CERT_PASSWORD MSSQL_SA_PASSWORD POSTGRES_PASSWORD INTERNAL_IP PROXY_HOST_IP OpenIddict__Issuer PUBLIC_AUTHORITY"
    "DATABASE_PROVIDER ConnectionStrings__SqlServerConnection ConnectionStrings__PostgreSqlConnection ConnectionStrings__RedisConnection ENCRYPTION_CERT_PASSWORD SIGNING_CERT_PASSWORD MSSQL_SA_PASSWORD POSTGRES_PASSWORD INTERNAL_IP OpenIddict__Issuer PUBLIC_AUTHORITY"
    "DATABASE_PROVIDER ConnectionStrings__SqlServerConnection ConnectionStrings__PostgreSqlConnection ENCRYPTION_CERT_PASSWORD SIGNING_CERT_PASSWORD INTERNAL_IP OpenIddict__Issuer PUBLIC_AUTHORITY"
)
MODE_VOLUMES=(
    "mssql-service:mssql-data postgres-service:postgres-data redis-service:redis-data"
    "mssql-service:mssql-data postgres-service:postgres-data redis-service:redis-data"
    "mssql-service:mssql-data postgres-service:postgres-data redis-service:redis-data"
    "mssql-service:mssql-data postgres-service:postgres-data redis-service:redis-data"
    "redis-service:redis-data idp-service:dataprotection-keys"
)
ENV_EXAMPLE_EMPTY_FIELDS=(
    "ConnectionStrings__SqlServerConnection"
    "ConnectionStrings__PostgreSqlConnection"
    "MSSQL_SA_PASSWORD"
    "POSTGRES_PASSWORD"
    "ENCRYPTION_CERT_PASSWORD"
    "SIGNING_CERT_PASSWORD"
)

write_env() {
    local output="$1"
    local target="${2:-}"
    local state="${3:-valid}"
    local provider="SqlServer"
    [[ "$target" == "ConnectionStrings__PostgreSqlConnection" ]] && provider="PostgreSql"

    local entries=(
        "DATABASE_PROVIDER=$provider"
        "ConnectionStrings__SqlServerConnection=Server=synthetic.invalid;Database=synthetic;User Id=synthetic;Password=$SENTINEL"
        "ConnectionStrings__PostgreSqlConnection=Host=synthetic.invalid;Database=synthetic;Username=synthetic;Password=$SENTINEL"
        "ConnectionStrings__RedisConnection=redis-service:6379"
        "Redis__Enabled=true"
        "ENCRYPTION_CERT_PASSWORD=$SENTINEL"
        "SIGNING_CERT_PASSWORD=$SENTINEL"
        "MSSQL_SA_PASSWORD=$SENTINEL"
        "POSTGRES_PASSWORD=$SENTINEL"
        "POSTGRES_USER=synthetic"
        "INTERNAL_IP=127.0.0.1"
        "PROXY_HOST_IP=127.0.0.2"
        "ALLOWED_PROXY_IPS=127.0.0.2"
        "OpenIddict__Issuer=https://idp.synthetic.invalid/"
        "PUBLIC_AUTHORITY=idp.synthetic.invalid"
    )

    : >"$output"
    local entry key
    for entry in "${entries[@]}"; do
        key="${entry%%=*}"
        if [[ "$key" == "$target" && "$state" == "absent" ]]; then
            continue
        elif [[ "$key" == "$target" && "$state" == "empty" ]]; then
            printf '%s=\n' "$key" >>"$output"
        else
            printf '%s\n' "$entry" >>"$output"
        fi
    done
}

database_password_policy_allows_file() {
    local source_file="$1"
    local line key value guard_pattern

    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"
        if [[ "$line" =~ ^[[:space:]]*(MSSQL_SA_PASSWORD|POSTGRES_PASSWORD)[[:space:]]*:[[:space:]]*(.*)$ ]]; then
            key="${BASH_REMATCH[1]}"
            value="${BASH_REMATCH[2]}"
        elif [[ "$line" =~ ^[[:space:]]*-[[:space:]]*(MSSQL_SA_PASSWORD|POSTGRES_PASSWORD)[[:space:]]*=(.*)$ ]]; then
            key="${BASH_REMATCH[1]}"
            value="${BASH_REMATCH[2]}"
        else
            continue
        fi

        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"
        if [[ "$value" == \"*\" || "$value" == \'*\' ]]; then
            value="${value:1:${#value}-2}"
        fi

        guard_pattern="^\\$\\{${key}(:\\?|\\?)[^}]*\\}$"
        if [[ ! "$value" =~ $guard_pattern ]]; then
            return 1
        fi
    done <"$source_file"

    return 0
}

env_example_sensitive_fields_are_empty() {
    local source_file="$1"
    local key line count

    for key in "${ENV_EXAMPLE_EMPTY_FIELDS[@]}"; do
        count=0
        while IFS= read -r line || [[ -n "$line" ]]; do
            line="${line%$'\r'}"
            if [[ "$line" == "$key="* ]]; then
                count=$((count + 1))
                if [[ "$line" != "$key=" ]]; then
                    return 1
                fi
            fi
        done <"$source_file"
        if (( count != 1 )); then
            return 1
        fi
    done

    return 0
}

write_env_example_policy_fixture() {
    local output="$1"
    local nonempty_key="${2:-}"
    local nonempty_value="${3:-}"
    local key

    : >"$output"
    for key in "${ENV_EXAMPLE_EMPTY_FIELDS[@]}"; do
        if [[ "$key" == "$nonempty_key" ]]; then
            printf '%s=%s\n' "$key" "$nonempty_value" >>"$output"
        else
            printf '%s=\n' "$key" >>"$output"
        fi
    done
}

env_example_fixture_dir="$TMP_DIR/env-example-policy"
mkdir -p "$env_example_fixture_dir"
env_example_fixture_self_check_failed=0
env_example_positive_fixture="$env_example_fixture_dir/positive.env"
write_env_example_policy_fixture "$env_example_positive_fixture"
if ! env_example_sensitive_fields_are_empty "$env_example_positive_fixture"; then
    fail "env-example-policy-self-check" "positive-fixture"
    env_example_fixture_self_check_failed=1
fi
env_example_negative_keys=(
    "ConnectionStrings__SqlServerConnection"
    "ConnectionStrings__PostgreSqlConnection"
)
env_example_negative_values=(
    "Server=sqlserver.invalid;Database=fixture;User Id=fixture;Password=fixture-sqlserver-secret"
    "Host=postgres.invalid;Database=fixture;Username=fixture;Password=fixture-postgres-secret"
)
for index in "${!env_example_negative_keys[@]}"; do
    fixture_file="$env_example_fixture_dir/negative-$index.env"
    fixture_output="$env_example_fixture_dir/negative-$index.out"
    write_env_example_policy_fixture "$fixture_file" \
        "${env_example_negative_keys[$index]}" "${env_example_negative_values[$index]}"
    if env_example_sensitive_fields_are_empty "$fixture_file" >"$fixture_output" 2>&1; then
        fail "env-example-policy-self-check" "negative-fixture-$index"
        env_example_fixture_self_check_failed=1
    elif [[ -s "$fixture_output" ]]; then
        fail "env-example-policy-self-check" "negative-fixture-output-$index"
        env_example_fixture_self_check_failed=1
    fi
done
if (( env_example_fixture_self_check_failed == 0 )); then
    printf '[CHECK] .env.example sensitive field policy fixtures passed\n'
fi

policy_fixture_dir="$TMP_DIR/database-password-policy"
mkdir -p "$policy_fixture_dir"
policy_fixtures=(
    'MSSQL_SA_PASSWORD: bounded-literal'
    'POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-bounded-default}'
    'MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD-bounded-default}'
    'POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:=bounded-default}'
    'MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD=bounded-default}'
)
policy_self_check_failed=0
for index in "${!policy_fixtures[@]}"; do
    fixture_file="$policy_fixture_dir/negative-$index.yml"
    printf 'services:\n  fixture:\n    environment:\n      %s\n' \
        "${policy_fixtures[$index]}" >"$fixture_file"
    if database_password_policy_allows_file "$fixture_file"; then
        fail "database-password-policy-self-check" "negative-fixture-$index"
        policy_self_check_failed=1
    fi
done
required_guard_fixture="$policy_fixture_dir/required-guards.yml"
printf '%s\n' \
    'services:' \
    '  fixture:' \
    '    environment:' \
    '      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?required}"' \
    '      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD?required}' \
    >"$required_guard_fixture"
if ! database_password_policy_allows_file "$required_guard_fixture"; then
    fail "database-password-policy-self-check" "required-guard-fixture"
    policy_self_check_failed=1
fi
if (( policy_self_check_failed == 0 )); then
    printf '[CHECK] database password policy fixtures passed\n'
fi

if ! env_example_sensitive_fields_are_empty "$DEPLOYMENT_DIR/.env.example"; then
    fail "example-sensitive-fields" "credential-field-not-empty-or-unique"
fi

for nginx_config in nginx/nginx.conf nginx/splithost-gateway.conf; do
    if ! grep -Fq 'proxy_set_header Host ${PUBLIC_AUTHORITY};' \
        "$DEPLOYMENT_DIR/$nginx_config"; then
        fail "fixed-proxy-host" "$nginx_config"
    fi
    if grep -Eq 'proxy_set_header Host[[:space:]]+\$host|X-Forwarded-Host[[:space:]]+\$http_x_forwarded_host' \
        "$DEPLOYMENT_DIR/$nginx_config"; then
        fail "request-derived-proxy-host" "$nginx_config"
    fi
done
if ! grep -Fq 'return 301 https://${PUBLIC_AUTHORITY}$request_uri;' \
    "$DEPLOYMENT_DIR/nginx/nginx.conf"; then
    fail "fixed-redirect-host" "nginx/nginx.conf"
fi
for nginx_compose in \
    docker-compose.nginx.yml \
    docker-compose.splithost-nginx.yml \
    docker-compose.splithost-nginx-nodb.yml; do
    if ! grep -Fq '/etc/nginx/templates/nginx.conf.template:ro' \
        "$DEPLOYMENT_DIR/$nginx_compose" ||
       ! grep -Fq 'NGINX_ENVSUBST_FILTER=PUBLIC_AUTHORITY' \
        "$DEPLOYMENT_DIR/$nginx_compose" ||
       ! grep -Fq 'NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx' \
        "$DEPLOYMENT_DIR/$nginx_compose"; then
        fail "nginx-template-contract" "$nginx_compose"
    fi
done

render_compose() {
    local compose_file="$1"
    local env_file="$2"
    local output="$3"
    shift 3
    IDP_ENV_FILE="$env_file" docker compose \
        --env-file "$env_file" \
        -f "$DEPLOYMENT_DIR/$compose_file" "$@" \
        config --format json >"$output" 2>"$TMP_DIR/render.err"
}

assert_render_contract() {
    local rendered="$1"
    local mode="$2"
    local expected_volumes="$3"
    if [[ "$JSON_READER" == "jq" ]]; then
        if ! jq -e '
            (.services // {}) as $services
            | all(["mssql-service", "postgres-service", "redis-service"][];
                (($services[.] // {}).ports // [] | length) == 0)
        ' <"$rendered" >/dev/null 2>&1; then
            fail "default-data-port" "$mode"
        fi
        if ! jq -e --arg expected "$expected_volumes" '
            (.services // {}) as $services
            | ($expected | split(" "))
            | all(.[];
                split(":") as $pair
                | ($pair[0]) as $service
                | ($pair[1]) as $volume
                | (.volumes // {} | has($volume))
                  and any(($services[$service].volumes // [])[];
                    .type == "volume"
                    and (.source == $volume or (.source | endswith("_" + $volume)))))
        ' <"$rendered" >/dev/null 2>&1; then
            fail "named-volume-retention" "$mode"
        fi
        return
    fi

    local status=0
    python3 -c '
import json, sys
expected = sys.argv[1].split()
model = json.load(sys.stdin)
services = model.get("services", {})
for name in ("mssql-service", "postgres-service", "redis-service"):
    if services.get(name, {}).get("ports"):
        raise SystemExit(10)
volumes = model.get("volumes", {})
for pair in expected:
    service, logical = pair.split(":", 1)
    if logical not in volumes:
        raise SystemExit(11)
    mounts = services.get(service, {}).get("volumes", [])
    if not any(
        mount.get("type") == "volume"
        and (mount.get("source") == logical or mount.get("source", "").endswith("_" + logical))
        for mount in mounts
    ):
        raise SystemExit(12)
' "$expected_volumes" <"$rendered" || status=$?

    if [[ "$status" == "10" ]]; then
        fail "default-data-port" "$mode"
    elif (( status != 0 )); then
        fail "named-volume-retention" "$mode"
    fi
}

for index in "${!COMPOSE_FILES[@]}"; do
    compose_file="${COMPOSE_FILES[$index]}"
    mode="${MODE_NAMES[$index]}"
    if [[ ! -f "$DEPLOYMENT_DIR/$compose_file" ]]; then
        harness_error "matrix compose file missing: $mode"
        continue
    fi

    valid_env="$TMP_DIR/$mode.valid.env"
    rendered="$TMP_DIR/$mode.json"
    write_env "$valid_env"

    direct_project="$TMP_DIR/direct-$mode"
    mkdir -p "$direct_project"
    cp "$DEPLOYMENT_DIR/$compose_file" "$direct_project/$compose_file"
    write_env "$direct_project/.env"
    if ! (
        unset IDP_ENV_FILE
        cd "$direct_project"
        docker compose --env-file .env -f "$compose_file" \
            config --format json >"$direct_project/render.json" 2>"$direct_project/render.err"
    ); then
        fail "direct-compose-compatibility" "$mode"
    fi

    if render_compose "$compose_file" "$valid_env" "$rendered"; then
        assert_render_contract "$rendered" "$mode" "${MODE_VOLUMES[$index]}"
    else
        fail "valid-render" "$mode"
    fi

    while IFS= read -r required; do
        for state in absent empty; do
            invalid_env="$TMP_DIR/$mode.$state.env"
            diagnostic="$TMP_DIR/$mode.$state.err"
            write_env "$invalid_env" "$required" "$state"
            if IDP_ENV_FILE="$invalid_env" docker compose \
                --env-file "$invalid_env" \
                -f "$DEPLOYMENT_DIR/$compose_file" \
                config --format json >"$TMP_DIR/invalid.json" 2>"$diagnostic"; then
                fail "required-input" "$mode/$required/$state"
            elif ! grep -Fq "$required" "$diagnostic"; then
                fail "required-diagnostic" "$mode/$required/$state"
            fi
            if grep -Fq "$SENTINEL" "$diagnostic"; then
                fail "diagnostic-value-disclosure" "$mode/$required/$state"
            fi
        done
    done < <(printf '%s\n' ${MODE_REQUIRED[$index]})

    if ! database_password_policy_allows_file "$DEPLOYMENT_DIR/$compose_file"; then
        fail "database-password-fallback" "$mode"
    fi
done

LOCAL_OVERRIDE="$DEPLOYMENT_DIR/docker-compose.local-ports.yml"
if [[ ! -f "$LOCAL_OVERRIDE" ]]; then
    fail "local-port-override" "file-missing"
else
    local_env="$TMP_DIR/local.env"
    local_render="$TMP_DIR/local.json"
    write_env "$local_env"
    if render_compose "docker-compose.internal.yml" "$local_env" "$local_render" -f "$LOCAL_OVERRIDE"; then
        local_status=0
        if [[ "$JSON_READER" == "jq" ]]; then
            if jq -e '
                (.services // {}) as $services
                | {"mssql-service": 1433, "postgres-service": 5432, "redis-service": 6379}
                  | to_entries
                  | all(.[];
                      ($services[.key].ports // []) as $ports
                      | ($ports | length) == 1
                        and ($ports[0].target == .value)
                        and (($ports[0].published | tonumber) == .value)
                        and ($ports[0].host_ip == "127.0.0.1"))
            ' <"$local_render" >/dev/null 2>&1; then
                :
            else
                local_status=$?
            fi
        else
            python3 -c '
import json, sys
services = json.load(sys.stdin).get("services", {})
expected = {"mssql-service": 1433, "postgres-service": 5432, "redis-service": 6379}
for name, target in expected.items():
    ports = services.get(name, {}).get("ports", [])
    if (
        len(ports) != 1
        or ports[0].get("target") != target
        or int(ports[0].get("published", 0)) != target
    ):
        raise SystemExit(1)
    if ports[0].get("host_ip") != "127.0.0.1":
        raise SystemExit(1)
' <"$local_render" || local_status=$?
        fi
        if (( local_status != 0 )); then
            fail "local-port-override" "unexpected-publication"
        fi
    else
        fail "local-port-override" "render"
    fi
fi

ghcr_env="$TMP_DIR/ghcr.env"
ghcr_render="$TMP_DIR/ghcr.json"
write_env "$ghcr_env"
if IDP_IMAGE="ghcr.invalid/hybrididp:test" render_compose \
    "docker-compose.splithost-nginx-nodb.yml" "$ghcr_env" "$ghcr_render" \
    -f "$DEPLOYMENT_DIR/docker-compose.ghcr-image.yml"; then
    ghcr_status=0
    if [[ "$JSON_READER" == "jq" ]]; then
        if jq -e '
            .services["idp-service"] as $service
            | $service.image == "ghcr.invalid/hybrididp:test"
              and $service.pull_policy == "always"
              and ($service | has("build") | not)
        ' <"$ghcr_render" >/dev/null 2>&1; then
            :
        else
            ghcr_status=$?
        fi
    else
        python3 -c '
import json, sys
service = json.load(sys.stdin)["services"]["idp-service"]
if service.get("image") != "ghcr.invalid/hybrididp:test":
    raise SystemExit(1)
if service.get("pull_policy") != "always":
    raise SystemExit(1)
if service.get("build") is not None:
    raise SystemExit(1)
' <"$ghcr_render" || ghcr_status=$?
    fi
    if (( ghcr_status != 0 )); then
        fail "ghcr-contract" "rendered-override"
    fi
else
    fail "ghcr-contract" "render"
fi
if ! grep -Fq 'IDP_IMAGE:?' "$DEPLOYMENT_DIR/docker-compose.ghcr-image.yml"; then
    fail "ghcr-contract" "required-image"
fi

STUB_BIN="$TMP_DIR/bin"
DOCKER_LOG="$TMP_DIR/docker.log"
mkdir -p "$STUB_BIN"
REAL_DOCKER="$(command -v docker)"
export DOCKER_LOG REAL_DOCKER
cat >"$STUB_BIN/docker" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$DOCKER_LOG"
if [[ " $* " == *" config "* ]]; then
    exec "$REAL_DOCKER" "$@"
fi
exit 0
STUB
chmod +x "$STUB_BIN/docker"

missing_override="$TMP_DIR/no-override.yml"
for index in "${!COMPOSE_FILES[@]}"; do
    mode="${MODE_NAMES[$index]}"
    compose_file="${COMPOSE_FILES[$index]}"
    invalid_env="$TMP_DIR/$mode.deploy-invalid.env"
    write_env "$invalid_env" "ENCRYPTION_CERT_PASSWORD" "empty"
    : >"$DOCKER_LOG"
    status=0
    PATH="$STUB_BIN:$PATH" bash "$DEPLOYMENT_DIR/deploy-idp.sh" \
        --source local --compose "$compose_file" --override "$missing_override" \
        --env-file "$invalid_env" >"$TMP_DIR/deploy.out" 2>&1 || status=$?
    if (( status == 0 )); then
        fail "deploy-preflight" "$mode/empty"
    elif ! grep -Fq "ENCRYPTION_CERT_PASSWORD" "$TMP_DIR/deploy.out"; then
        fail "deploy-preflight-diagnostic" "$mode/empty"
    fi
    if grep -Eq '(^| )(pull|build|up|create|run)( |$)' "$DOCKER_LOG"; then
        fail "deploy-preflight-order" "$mode"
    fi
    if grep -Fq "$SENTINEL" "$TMP_DIR/deploy.out"; then
        fail "diagnostic-value-disclosure" "$mode/deploy"
    fi
done

: >"$DOCKER_LOG"
status=0
PATH="$STUB_BIN:$PATH" bash "$DEPLOYMENT_DIR/deploy-idp.sh" \
    --source ghcr --image "ghcr.invalid/hybrididp:test" \
    --compose "docker-compose.splithost-nginx-nodb.yml" \
    --override "$missing_override" --env-file "$ghcr_env" \
    >"$TMP_DIR/deploy-valid.out" 2>&1 || status=$?
if (( status != 0 )); then
    fail "deploy-ghcr-order" "valid-exit"
else
    mapfile -t lifecycle < <(awk '
        { for (i = 1; i <= NF; i++) if ($i ~ /^(pull|build|up|create|run|ps)$/) print $i }
    ' "$DOCKER_LOG")
    if [[ "${lifecycle[*]}" != "pull up ps" ]]; then
        fail "deploy-ghcr-order" "action-sequence"
    elif ! grep -Eq '(^| )up -d --no-build( |$)' "$DOCKER_LOG"; then
        fail "deploy-ghcr-order" "up-flags"
    fi
fi

if (( HARNESS_ERRORS > 0 )); then
    exit 2
fi
if (( FAILURES > 0 )); then
    for contract in "${!FAILURE_COUNTS[@]}"; do
        printf '[FAIL] %s: %d case(s); first=%s\n' \
            "$contract" "${FAILURE_COUNTS[$contract]}" "${FIRST_FAILURE[$contract]}"
    done | sort
    printf '[RESULT] deployment hardening contracts failed: %d\n' "$FAILURES"
    exit 1
fi

printf '[PASS] deployment hardening contracts passed for all production modes\n'
