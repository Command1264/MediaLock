# Media Lock Agent Guide

Media Lock 是 Windows 媒體控制路由器，負責將實體媒體鍵導向使用者指定的
Global System Media Transport Controls（GSMTC）Session。

## Agent skills

### Issue tracker

工作項目使用 GitHub Issues；GitHub remote 建立前不得假設遠端操作可用。詳見
`docs/agents/issue-tracker.md`。

### Triage labels

使用標準 triage roles：`needs-triage`、`needs-info`、`ready-for-agent`、
`ready-for-human`、`wontfix`。詳見 `docs/agents/triage-labels.md`。

### Domain docs

本專案採 single-context；進行領域命名或核心行為設計前讀取 `CONTEXT.md`，涉及架構決策時讀取
相關 `docs/adr/`。詳見 `docs/agents/domain.md`。

## Project references

- 修改產品行為或範圍前，讀取 `docs/product-spec.md`。
- 修改模組邊界、資料流、Windows API integration 或狀態機前，讀取
  `docs/architecture.md` 與相關 ADR。
- 規劃里程碑或版本範圍前，讀取 `docs/roadmap.md`。
- 修改核心 routing、recovery、input interception 或發布流程前，讀取
  `docs/testing.md` 並執行相稱驗證。

## Project guardrails

- MVP 的核心承諾是鎖定 GSMTC Session；瀏覽器 URL 辨識屬於後續可選整合。
- Core 不依賴 WPF；UI 經由 ViewModel 與 application services 使用核心能力。
- 媒體鍵鎖定採 capture、consume、route；完成 Phase 0 實機驗證前，不宣稱特定 input backend
  能可靠阻止 Windows 重複處理。
- 預設以一般使用者權限執行；需要系統管理員權限的設計必須先提出理由與替代方案。
- 行為變更優先建立失敗測試，再完成實作與重構。
