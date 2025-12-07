# Phase 14: Dynamic System & Security Configuration

**Status:** ✅ Completed

## Overview
This phase introduced dynamic configuration capabilities to allow administrators to adjust system behavior and security policies without redeployment.

## Features Implemented

### 1. Dynamic System Monitoring
- **Control**: Enable/Disable background monitoring service.
- **Intervals**: Configure polling intervals for Activity, Security, and Metrics stats.
- **Service**: `MonitoringBackgroundService` now respects database settings.
- **UI**: New "System" settings tab in Admin Portal.

### 2. Dynamic Security Policies
- **Policies**:
    - Password complexity (length, character classes).
    - Password history and expiration.
    - Account lockout rules (attempts, duration).
    - Abnormal login detection.
- **Service**: `SecurityPolicyService` manages policy state.
- **UI**: New "Security" app in Admin Portal (`/admin/security/policies`).

### 3. Token Lifetimes Configuration
- **Configuration**: Moved token lifetimes to `appsettings.json` (Option Pattern).
- **Settings**:
    - `TokenOptions:AccessTokenLifetimeMinutes`
    - `TokenOptions:RefreshTokenLifetimeMinutes`
    - `TokenOptions:DeviceCodeLifetimeMinutes`
- **Defaults**: Secure defaults applied (1h access, 14d refresh).

## Technical Details
- **Settings Storage**: Centralized `Settings` table accessed via `ISettingsService`.
- **Caching**: Aggressive caching with invalidation on update.
- **API**:
    - `GET/PUT /api/admin/settings` (Generic)
    - `GET/PUT /api/admin/security/policies` (Typed)

## Verification
- Unit Tests:
    - `TokenOptionsTests`: Verifies configuration binding.
    - `MonitoringBackgroundServiceTests`: Verifies service respects "Enabled" flag.
    - `SecurityPolicyControllerTests`: Verifies API endpoints.
- Manual verification via Admin UI.
