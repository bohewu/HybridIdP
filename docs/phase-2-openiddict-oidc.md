---
title: "Phase 2: OpenIddict Integration & OIDC Flow"
owner: HybridIdP Team
last-updated: 2025-11-16
percent-complete: 100
---

# Phase 2: OpenIddict Integration & OIDC Flow

簡短摘要：Phase 2 已完成，整合 OpenIddict 以支援標準 OIDC 授權流程（Authorization Code + PKCE）。

- 完成內容：OpenIddict 6.x 整合、Authorization Code Flow with PKCE、ASP.NET Core Identity 整合
- 實作：TestClient (MVC)、Custom Claims Factory、JIT Provisioning Service
- 主要 Endpoints：`/connect/authorize`, `/connect/token`, `/connect/userinfo`

驗證結果：完整 OIDC 登入流程、Consent 頁面、Claims 正確傳遞。

詳細資訊請參見 `docs/PROJECT_PROGRESS.md` 或本檔案相應連結。
