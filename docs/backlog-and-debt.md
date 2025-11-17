---
title: "Backlog & Technical Debt"
owner: HybridIdP Team
last-updated: 2025-11-17
percent-complete: 40
---

# Backlog & Technical Debt

說明：本文件彙整「未完成功能」「改進建議」與技術債務，供排期與責任指派使用。來源：`docs/archive/PROJECT_STATUS_FULL.md`（原始專案現況檔）

最後更新：2025-11-06

## 優先待辦（短期 - 排期內）

- Phase 5.6 Part 2: API Resource Scopes — UI: Client 選擇 API Resources 的 UI 尚未完成
- Phase 5.6 Part 3: Scope Authorization Policies — 前端完整連動（ClientForm.vue 的 UI 已實作，但仍有 UX 改善空間）
- Improve error messages when deleting scopes that have clients assigned (400 error → clearer message)
- Implement Resource table usage for consent multi-language text (Part 2)

## 改善與技術債（中期）

- Integrate API Resources into OpenIddict token issuance (aud claim population)
- Add Icon preview in Admin UI for Consent customization
- Implement Cancel/Disable logic for required scopes on consent screen
- Add OpenIddict configuration to include `aud` claim automatically (deployment checklist)

## 長期/探索性項目

- AD / LDAP self-service integration (password change, expiry notifications)
- Real-time monitoring and alerting integration with Phase 7 (pager/ops runbook)

## Test & CI Debt

- Address intermittent failing tests in `SettingsServiceTests` (investigate order-dependent state)
- Add test coverage for resource→aud pipeline once OpenIddict integration implemented

## Notes

- All backlog items include pointers to relevant files and commits in `docs/archive/PROJECT_STATUS_FULL.md`.

If you'd like, I can open PRs for selected backlog items (one PR per workstream) or create GitHub issues from each bullet.
