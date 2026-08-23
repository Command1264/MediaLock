# 支援與疑難排解

Media Lock 目前是 unsigned prerelease，維護者不承諾回覆時間或個別環境一定相容。建立 Issue 前，請先使用
[最新 GitHub Release](https://github.com/Command1264/MediaLock/releases)、確認下載雜湊，並查看下列常見情況。

## 常見情況

### 看不到媒體 Session

- 先在播放器開始播放或暫停一次，確認它已向 Windows 發布 GSMTC Session。
- 重新整理瀏覽器分頁或重新開啟播放器，再觀察 Media Lock 是否進入 Recovering 並恢復。
- 記錄 Media Lock 顯示的 Source application。瀏覽器品牌名稱、PWA 名稱與 Windows 提供的來源識別可能不同。
- 某個應用程式沒有發布 GSMTC 時屬於相容性結果，不一定是 Media Lock 缺陷。

### 媒體鍵控制錯誤播放器

- 確認主畫面的 Routing Mode 及其選取標記符合預期。
- Session Lock 與 App Lock 必須先選取清單中的目標；Priority Rules 依 Settings 的已啟用順序決定。
- 確認 Settings 的全域媒體鍵攔截已啟用。停用、目標不可用、命令不受支援或輸入佇列忙碌時，按鍵會安全交還 Windows。
- 分別記錄目標與競爭媒體來源在按鍵前後的狀態；不要只依目前前景視窗判斷路由結果。

### 顯示 Recovering 或 Unavailable

- Session 在重新整理、換曲或播放器重建時可能短暫消失；Recovery 期間不應靜默改鎖不安全的目標。
- 等候設定的 Recovery timeout。若原目標回來，確認選取與路由目標都正確恢復。
- 逾時後的結果由 Fallback Policy 決定。回報時請附上 timeout、policy 與完整重現順序。

### 登入啟動不正確

`Start with Windows` 是目前使用者的 Windows `Run` 登錄值，內容應是加上引號的執行檔完整路徑及
`--startup`，例如：

```text
"C:\Users\name\AppData\Local\Programs\MediaLock\<version>\MediaLock.exe" --startup
```

搬動 `MediaLock.exe` 後，請從舊位置停用登入啟動，於新位置啟動後再重新啟用。不要把僅比較執行檔路徑、
卻忽略 `--startup` 的檢查結果當成程式錯誤。

### Windows 顯示安全或信譽警告

目前公開執行檔未 code signing。只從官方 Release 下載，並依
[下載與驗證](docs/installation.md#下載與驗證) 比對 SHA-256。雜湊不符時不要執行，也不要略過警告。

## 診斷資料與隱私

使用者資料位於 `%LocalAppData%\MediaLock\`：

- `settings.json`：偏好設定。
- `state.json`：Routing Mode 與恢復所需狀態。
- `logs\*.jsonl`：有界輪替的結構化診斷記錄。

正常 logs 預設不保存 media title 或 artist，但上傳前仍須逐項檢查。請只附與問題時間相符的最小片段，並移除
帳號、路徑中的私人名稱、媒體名稱、artist、秘密、token、完整設定與無關活動。公開 Issue 不適合放置敏感資料。

## 建立回報

- 可重現的 Media Lock 行為缺陷：使用 [Bug report](https://github.com/Command1264/MediaLock/issues/new?template=bug-report.yml)。
- 特定播放器、瀏覽器、Windows build 或鍵盤的成功／失敗結果：使用
  [Compatibility report](https://github.com/Command1264/MediaLock/issues/new?template=compatibility-report.yml)。

請提供版本、Windows edition／DisplayVersion／完整 build／architecture、媒體來源及版本、Source application、
Routing Mode、逐步操作、預期與實際結果，以及是否影響競爭媒體來源。實體媒體鍵問題另附鍵盤型號與按鍵；
Recovery 問題另附 timeout、Fallback Policy 與 Session 重建情境。

先搜尋 [現有 Issues](https://github.com/Command1264/MediaLock/issues)，避免重複回報。Issue 依
`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human` 或 `wontfix` 狀態進行分類；分類不是修復時程承諾。
