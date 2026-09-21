#!/usr/bin/env python3
"""Run the existing credential-migration validation tests without exposing config values."""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import Sequence


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
COMPOSE_FILE = REPOSITORY_ROOT / "docker-compose.dev.yml"
MSSQL_SERVICE = "mssql-service"
MSSQL_READY_TIMEOUT_SECONDS = 120
TEST_LOGGER = "console;verbosity=minimal"

FOCUSED_TESTS = (
    (
        "Stage 1 binding migration tests",
        "Tests.Infrastructure.IntegrationTests/Tests.Infrastructure.IntegrationTests.csproj",
        "FullyQualifiedName~Stage1BindingMigrationTests",
    ),
    (
        "Stage 2 credential migration tests",
        "Tests.Application.UnitTests/Tests.Application.UnitTests.csproj",
        "FullyQualifiedName~Stage2CredentialMigrationTests",
    ),
    (
        "Stage 2 directory credential tests",
        "Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj",
        "FullyQualifiedName~Stage2DirectoryCredentialTests",
    ),
    (
        "Migration ceremony and issuance guard tests",
        "Tests.Web.IdP.UnitTests/Tests.Web.IdP.UnitTests.csproj",
        "FullyQualifiedName~CredentialMigrationCeremonyTests|FullyQualifiedName~MigrationIssuanceGuardTests",
    ),
)

ACTIVE_CHILD: subprocess.Popen[str] | None = None


class RunnerError(RuntimeError):
    """A safe, user-facing validation failure."""


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate the existing Stage 1 and Stage 2 credential migration tests."
    )
    parser.add_argument(
        "--full-suite",
        action="store_true",
        help="Run the complete solution test suite after starting only mssql-service when needed.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the selected commands without starting services or running tests.",
    )
    return parser.parse_args()


def print_phase(number: int, total: int, message: str) -> None:
    print(f"[{number}/{total}] {message}", flush=True)


def safe_environment() -> dict[str, str]:
    environment = dict(os.environ)
    environment.update(
        {
            "CredentialMigration__Enabled": "false",
            "CredentialMigration__EmailOtpPolicyFloor": "Disabled",
            "CredentialMigration__OperatorCutoff": "false",
            "DirectoryIntegration__Enabled": "false",
            "DirectoryIntegration__AuthenticationEnabled": "false",
        }
    )
    return environment


def terminate_active_child() -> None:
    global ACTIVE_CHILD

    child = ACTIVE_CHILD
    if child is None or child.poll() is not None:
        return

    child.terminate()
    try:
        child.wait(timeout=10)
    except subprocess.TimeoutExpired:
        child.kill()
        child.wait(timeout=10)


def run_child(command: Sequence[str], *, environment: dict[str, str] | None = None) -> tuple[int, str]:
    global ACTIVE_CHILD

    try:
        ACTIVE_CHILD = subprocess.Popen(
            list(command),
            cwd=REPOSITORY_ROOT,
            env=environment,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        output, _ = ACTIVE_CHILD.communicate()
        return ACTIVE_CHILD.returncode, output
    except KeyboardInterrupt:
        terminate_active_child()
        raise
    except OSError:
        return 127, ""
    finally:
        ACTIVE_CHILD = None


def require_success(command: Sequence[str], message: str, *, environment: dict[str, str] | None = None) -> str:
    return_code, output = run_child(command, environment=environment)
    if return_code != 0:
        raise RunnerError(message)
    return output


def require_docker() -> None:
    require_success(
        ["docker", "version", "--format", "{{.Server.Version}}"],
        "Docker is unavailable. Credential migration validation requires Docker for Testcontainers.",
    )


def compose_command(*arguments: str) -> list[str]:
    return ["docker", "compose", "-f", str(COMPOSE_FILE), *arguments]


def service_is_running() -> bool:
    output = require_success(
        compose_command("ps", "-q", MSSQL_SERVICE),
        "Unable to inspect mssql-service through Docker Compose.",
    )
    return bool(output.strip())


def start_mssql_service() -> None:
    require_success(
        compose_command("up", "-d", MSSQL_SERVICE),
        "Unable to start mssql-service through Docker Compose.",
    )


def wait_for_mssql_service() -> None:
    deadline = time.monotonic() + MSSQL_READY_TIMEOUT_SECONDS
    readiness_command = compose_command(
        "exec",
        "-T",
        MSSQL_SERVICE,
        "/bin/sh",
        "-c",
        'test -x /opt/mssql-tools18/bin/sqlcmd && /opt/mssql-tools18/bin/sqlcmd '
        '-S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" >/dev/null',
    )

    while time.monotonic() < deadline:
        return_code, _ = run_child(readiness_command)
        if return_code == 0:
            return
        time.sleep(2)

    raise RunnerError(
        f"mssql-service did not become ready within {MSSQL_READY_TIMEOUT_SECONDS} seconds."
    )


def stop_mssql_service() -> None:
    require_success(
        compose_command("stop", MSSQL_SERVICE),
        "Unable to stop the mssql-service instance started by this runner.",
    )


def test_count(output: str) -> int | None:
    output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", output)
    for expression in (
        r"Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*\d+,\s*Total:\s*(\d+)",
        r"Total tests:\s*(\d+)",
        r"Tests run:\s*(\d+)",
        r"Total:\s*(\d+)",
        r"測試數總計:\s*(\d+)",
        r"總計:\s*(\d+)",
    ):
        matches = re.findall(expression, output, flags=re.IGNORECASE)
        if matches:
            return sum(int(match) for match in matches)
    return None


def run_test(label: str, project: str, test_filter: str) -> int:
    return_code, output = run_child(
        ["dotnet", "test", project, "--filter", test_filter, "--logger", TEST_LOGGER],
        environment=safe_environment(),
    )
    count = test_count(output)
    if return_code != 0:
        completed = f" after {count} reported tests" if count is not None else ""
        raise RunnerError(f"{label} failed{completed} (exit code {return_code}).")
    if count is None:
        print(f"    {label}: completed.", flush=True)
        return 0
    print(f"    {label}: {count} tests passed.", flush=True)
    return count


def run_focused_tests() -> int:
    total = 0
    for label, project, test_filter in FOCUSED_TESTS:
        total += run_test(label, project, test_filter)
    return total


def run_full_suite() -> int:
    return_code, output = run_child(
        ["dotnet", "test", "HybridAuthIdP.sln", "--logger", TEST_LOGGER],
        environment=safe_environment(),
    )
    count = test_count(output)
    if return_code != 0:
        completed = f" after {count} reported tests" if count is not None else ""
        raise RunnerError(f"Full solution test suite failed{completed} (exit code {return_code}).")
    if count is None:
        print("    Full solution test suite: completed.", flush=True)
        return 0
    print(f"    Full solution test suite: {count} tests passed.", flush=True)
    return count


def print_dry_run(full_suite: bool) -> None:
    print("[1/1] Dry run: no services will start and no tests will run.")
    print("    Child feature switches: CredentialMigration=false, DirectoryIntegration=false.")
    if full_suite:
        print("    docker compose -f docker-compose.dev.yml up -d mssql-service (only when absent)")
        print("    dotnet test HybridAuthIdP.sln --logger console;verbosity=minimal")
        print("    docker compose -f docker-compose.dev.yml stop mssql-service (only if started here)")
        return

    for _, project, test_filter in FOCUSED_TESTS:
        print(f"    dotnet test {project} --filter {test_filter} --logger console;verbosity=minimal")


def validate_repository() -> None:
    required_paths = [REPOSITORY_ROOT / project for _, project, _ in FOCUSED_TESTS]
    required_paths.append(REPOSITORY_ROOT / "HybridAuthIdP.sln")
    if not all(path.is_file() for path in required_paths):
        raise RunnerError("Run this script from an intact HybridIdP checkout with its existing test projects.")


def main() -> int:
    arguments = parse_arguments()
    if arguments.dry_run:
        print_dry_run(arguments.full_suite)
        return 0

    validate_repository()
    phase_count = 4 if arguments.full_suite else 3
    started_mssql_service = False
    total: int | None = None
    exit_code = 1

    try:
        print_phase(1, phase_count, "Checking Docker and keeping migration feature switches disabled.")
        require_docker()

        if arguments.full_suite:
            print_phase(2, phase_count, "Checking mssql-service for the full suite.")
            if not service_is_running():
                started_mssql_service = True
                start_mssql_service()
                print("    Started mssql-service for this run.", flush=True)
            else:
                print("    Reusing the existing mssql-service instance.", flush=True)
            wait_for_mssql_service()
            print("    mssql-service is ready.", flush=True)

            print_phase(3, phase_count, "Running the full solution test suite.")
            total = run_full_suite()
        else:
            print_phase(2, phase_count, "Running focused Stage 1 and Stage 2 migration tests.")
            total = run_focused_tests()

        exit_code = 0
    except KeyboardInterrupt:
        print("Interrupted. Active child terminated; cleanup will restore only runner-started services.", flush=True)
        exit_code = 130
    except RunnerError as error:
        print(f"Validation failed: {error}", file=sys.stderr, flush=True)
        exit_code = 1
    finally:
        print_phase(phase_count, phase_count, "Completing validation cleanup.")
        if started_mssql_service:
            try:
                stop_mssql_service()
                print("Cleanup completed: stopped the mssql-service instance started by this runner.", flush=True)
            except RunnerError as error:
                print(f"Cleanup failed: {error}", file=sys.stderr, flush=True)
                if exit_code != 130:
                    exit_code = 1

    if exit_code == 0:
        print(f"Validation completed: {total} tests passed.", flush=True)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
