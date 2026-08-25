# 下載、安裝、更新與移除

Media Lock 目前以 unsigned、portable `win-x64` ZIP 發布，沒有公開安裝程式，也不需要另外安裝 .NET Runtime。
Phase 12A 的 per-user installer 已完成實作與驗證，但尚未加入公開 `0.2.0` Release；在 GitHub Release 明確
列出 Setup 與其 SHA-256 以前，不要把測試用 Setup 當作正式下載。第一個公開候選 Setup 是正在驗證的
`0.3.0-rc.1`；在正式發布以前仍不是使用者下載管道。

## 系統需求與限制

- 64 位元 Windows 11；Windows 與播放器必須提供 GSMTC。
- 一般使用者權限即可執行。
- 執行檔尚未 code signing，Windows SmartScreen 或信譽警告仍可能出現。
- Chromium 瀏覽器可能讓多個分頁共用來源識別；Media Lock 不會檢查 URL，也不保證能區分同一瀏覽器內的特定分頁。

## 下載與驗證

1. 從 [Media Lock 0.2.0 Stable Release](https://github.com/Command1264/MediaLock/releases/tag/v0.2.0) 下載
   `MediaLock-0.2.0-win-x64.zip`。後續版本請從 [Media Lock Releases](https://github.com/Command1264/MediaLock/releases)
   選擇最新 Stable／Latest 版本；不要從不明鏡像下載。
2. 在 PowerShell 將 `<version>` 換成實際版本，以實際檔名計算 SHA-256：

   ```powershell
   Get-FileHash '.\MediaLock-<version>-win-x64.zip' -Algorithm SHA256
   Get-FileHash '.\MediaLock-Setup-<version>-win-x64.exe' -Algorithm SHA256
   ```

3. 將結果與該版本 GitHub Release 說明中的 SHA-256 完整比對。大小寫不影響雜湊值，但每個十六進位字元
   都必須相同；不相同時不要執行檔案。

`0.2.0` 的 SHA-256 是
`f368421481fa0a99516618873dfd4e0422c241deae2033b105869471eab27bb0`。

## Portable ZIP 第一次執行

1. 將 ZIP 解壓縮到不會任意搬動的使用者資料夾，例如 `%LocalAppData%\Programs\MediaLock\<version>\`。
2. 執行 `MediaLock.exe`。若 Windows 顯示 unsigned publisher 警告，先確認來源與 SHA-256，再自行決定是否繼續。
3. 選擇媒體 Session 與 Routing Mode；需要實體媒體鍵路由時，確認 Settings 的全域媒體鍵攔截已啟用。
4. 關閉主視窗預設只會隱藏至通知區域；完整結束請從通知區域選單選擇 `Exit`。

若啟用 `Start with Windows`，登入啟動項會保存當下 `MediaLock.exe` 的完整路徑。因此請先確定最終放置位置，
再啟用這個選項。

## Setup 第一次安裝

只有 GitHub Release 同時列出 Setup 與其 SHA-256 時，該檔案才是正式候選下載：

1. 驗證 `MediaLock-Setup-<version>-win-x64.exe` 的 SHA-256。
2. 以目前使用者執行 Setup；它不要求系統管理員權限，安裝至
   `%LocalAppData%\Programs\MediaLock\`，並建立 Start Menu／Windows Search 項目與 Installed apps 記錄。
3. Setup 不會自動啟用 `Start with Windows`。需要時請從安裝完成後的 Media Lock Settings 啟用。
4. Setup 與內含的 `MediaLock.exe` 目前皆未簽署；安裝程式格式不會消除 SmartScreen、Smart App Control
   或信譽警告。

Installer 與 portable ZIP 使用同一個經審查的 `MediaLock.exe` payload，但兩個容器各有自己的 SHA-256。

## Portable ZIP 更新或改用 Setup

1. 保留舊版本資料夾，以便回復。
2. 下載並驗證新版本 ZIP，解壓縮到新的版本資料夾。
3. 從舊版本 Settings 停用 `Start with Windows`，再由通知區域選擇 `Exit`。
4. 執行新版本，確認既有設定與 Session 狀態可以讀取，再重新啟用 `Start with Windows`。
5. 完成實際播放器與媒體鍵 smoke test 後，才移除舊版本程式資料夾。

若從 public portable `0.2.0` 改用 Setup，仍先執行上述停用登入啟動與 Exit 步驟，再安裝新版。設定、狀態
與 logs 會繼續使用 `%LocalAppData%\MediaLock\`；確認新版可讀取後，才重新啟用登入啟動，使登錄值指向
固定的安裝路徑。

## Setup 更新與回復

新版 Setup 使用相同 AppId 與固定安裝路徑，可原地更新並保留使用者資料與正確的已安裝路徑啟動項。
同版本可 repair；舊於目前已安裝完整 release version 的 Setup 會以可操作訊息阻止降版，避免較新設定
schema 被舊程式破壞。這不是自動更新，也不宣稱具有 MSI 等級的 transactional rollback。

需要回復時不要刪除使用者資料。先保留 `%LocalAppData%\MediaLock\` 備份，使用目前版本或較新 Setup；
若舊版 Setup 被安全阻止，請先依 Release 說明確認相容方式，不要直接覆蓋固定安裝路徑。

使用者設定、狀態與 logs 位於 `%LocalAppData%\MediaLock\`，不在 portable 程式資料夾內，正常更新不需要搬移或刪除。

## 回復舊版本

1. 在目前版本停用 `Start with Windows`，並從通知區域選擇 `Exit`。
2. 執行先前已驗證且仍保留的 `MediaLock.exe`。
3. 若要登入啟動，從該舊版本重新啟用，讓登錄值指向正確路徑。

設定 schema 可能隨版本演進。調查問題時先備份 `%LocalAppData%\MediaLock\`，不要把刪除使用者資料當作例行回復步驟。

## 移除 portable ZIP

1. 在 Settings 停用 `Start with Windows`。
2. 從通知區域選單選擇 `Exit`，並確認 `MediaLock.exe` 已結束。
3. 刪除確切的 Media Lock 程式版本資料夾。
4. 若確定不再需要偏好設定、恢復狀態與診斷 logs，才另外刪除 `%LocalAppData%\MediaLock\`。

Media Lock 沒有安裝服務、驅動程式或需要系統管理員權限的系統層元件。若啟動項未能由 Settings 移除，請先依
[支援與疑難排解](../SUPPORT.md) 確認精確登錄值，不要刪除整個 Windows `Run` key。

## 解除安裝 Setup

從 Windows Installed apps 解除安裝 Media Lock。Uninstaller 會移除固定安裝路徑、Start Menu 捷徑與
Installed apps 記錄；只有完整值指向該安裝執行檔的 `MediaLock` 登入啟動項會被移除。Portable copy 擁有的
不同路徑啟動項不會被刪除。`%LocalAppData%\MediaLock\` 的設定、狀態與 logs 預設保留，除非使用者另行
確認不再需要並手動刪除。
