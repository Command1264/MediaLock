# Media Lock

Media Lock 是 Windows 桌面媒體控制路由器：它位於實體媒體鍵與 Windows 媒體工作階段之間，讓使用者
將播放、暫停、上一首、下一首與停止命令導向指定播放器，而不是永遠接受 Windows 的
`CurrentSession` 選擇。

## 專案狀態

目前已有可執行的 WPF 桌面應用程式與 Console Probe。桌面程式可列出 GSMTC Sessions、鎖定目標、
提供媒體控制、攔截全域媒體鍵、常駐通知區域，並保存設定與執行狀態。App Lock、有序的來源應用程式
Priority Rules、雙語介面、Light/Dark 主題、封面與可互動時間軸均已實作；實機相容性仍依測試矩陣記錄。
目前開發來源與公開下載均為已完成獨立驗證的 `0.2.0-rc.3`。

已驗證的基礎能力包括：

1. 可列出 GSMTC Sessions。
2. 可選定並持續辨識單一 Session。
3. 可攔截並 consume 實體媒體鍵，避免 Windows 重複觸發。
4. 可將命令可靠送往鎖定目標。
5. Session 消失及重新出現時可恢復。

## 下載與安裝

目前公開版本是 unsigned `win-x64` prerelease：

- [下載 Media Lock 0.2.0-rc.3](https://github.com/Command1264/MediaLock/releases/tag/v0.2.0-rc.3)
- SHA-256：`ee7e2174e54177c77d9edbe1233e94ed79f3613b42b782d3319c1357affa0f8a`
- [下載、驗證、更新、回復與移除說明](docs/installation.md)

ZIP 內只有 self-contained `MediaLock.exe`，不需要另外安裝 .NET Runtime。由於執行檔尚未 code signing，
Windows SmartScreen 或信譽警告仍可能出現；請只從官方 GitHub Release 下載並在執行前比對 SHA-256。

## 從原始碼執行

```powershell
dotnet run --project src\MediaLock.App\MediaLock.App.csproj --configuration Release
```

關閉視窗預設只會隱藏至 Windows 通知區域；從托盤選單按 `Exit` 才會結束程式。設定頁可切換
close-to-tray、全域媒體鍵攔截與目前使用者的 Windows 登入啟動，也可設定 Recovery timeout、Fallback Policy 與
Priority Rules，並可選擇跟隨 Windows、英文或繁體中文介面，以及跟隨 Windows、淺色或深色主題。
成功儲存後會立即切換語言與外觀。
主畫面的目前媒體目標會顯示該實際路由 Session 的可用封面、播放時間與進度；封面缺失或無法解碼時
使用中性 placeholder，不影響媒體鍵路由。支援 Seek 的 Session 可直接點擊或拖曳進度條，放開時只
提交一次跳轉。
設定可由主視窗右上角或托盤選單開啟為固定尺寸的圓角無框模態視窗；設定開啟時主視窗會停用，
短淡入／隱藏動畫會遵循 Windows 的用戶端動畫設定。`Save settings` 成功後會關閉設定視窗；
`Cancel` 或 `Esc` 會捨棄尚未儲存的修改。儲存失敗則保留視窗與錯誤訊息，設定關閉後主視窗會
直接恢復到前景。主視窗的 Windows 標題列會跟隨實際套用的淺色或深色主題。
使用者資料位於 `%LocalAppData%\MediaLock\`：`settings.json`、`state.json` 與
有界輪替的 `logs\*.jsonl`。正常診斷記錄預設不保存媒體 title 或 artist。
目前原始碼的 Settings 最下方另有「關於與診斷」卡片，可查看程式與 Windows
版本、發行／簽署狀態，複製保護隱私的診斷摘要、開啟記錄資料夾，以及前往支援或問題回報頁面。
摘要不包含媒體標題、演出者、帳戶名稱、完整路徑或完整設定；公開分享前仍應自行檢閱。

主視窗最後一次成功選擇的 Routing Mode 會成為下次啟動模式，Settings 以唯讀摘要顯示該值。
Recovery timeout、Fallback Policy 與 Priority Rules 會在下次啟動時套用。Priority
Rules 依設定順序選擇第一個目前可用且已啟用的來源應用程式，沒有匹配時使用 Windows Current Session；
同一來源內沿用 App Lock 的確定性候選政策。App Lock 可從主視窗選取
Session 的來源應用程式，並在該應用程式的候選 Sessions 間依確定性政策切換；它不辨識瀏覽器 URL 或
保證單一 Session 連續性。啟動模式為 App Lock 時，會從有效的 `state.json` 恢復保存的來源應用程式。
啟動模式為 Session Lock 時，
只在 `state.json` 含有有效的 Session Fingerprint，且目前 catalog 有唯一可接受候選時恢復；缺少、
損毀、過期或模糊的候選都不會被靜默鎖定。啟動模式為 Windows Auto 時一律忽略先前保存的鎖定。

## 技術基底

- C#、.NET 10 LTS。
- WPF 作為 Windows GUI。
- 展示層使用 MVVM；核心由顯式狀態機與 application services 驅動。
- Windows GSMTC 作為媒體工作階段資訊與控制邊界。
- Win32 input backend、system tray 與 startup integration 經由 adapters 隔離。
- 公開候選使用 `win-x64` self-contained single-file 封裝；相容性聲明以具名實測結果為準。

## 建立 Release Candidate

Phase 6 的本地封裝命令會從乾淨 Git commit 產生版本化 ZIP、manifest 與 SHA-256：

```powershell
& .\eng\Publish-ReleaseCandidate.ps1 -Version 0.2.0-rc.3
```

此命令會建立本機候選輸出，但只有經獨立驗證且正式發布的 artifact 才是官方下載。輸出位於
`artifacts\`，ZIP 內只包含 self-contained `MediaLock.exe`。正式 `0.2.0-rc.3` 已以 source commit
`10dbb5b1452fe27084a28e254388fe974ed277e6` 與 archive digest 通過主機及 Windows Sandbox gate。
目前僅支援 `win-x64` 且未經 code signing，因此仍是 unsigned prerelease。完整驗證、證據與回復流程見
[Release candidate runbook](docs/release-candidate.md)，版本內容見
[0.2.0-rc.3 release notes](docs/releases/0.2.0-rc.3.md)。目前正式候選透過
[GitHub Prerelease](https://github.com/Command1264/MediaLock/releases/tag/v0.2.0-rc.3) 公開 ZIP；Release 頁面
列出 SHA-256，manifest 與獨立 checksum 檔仍保留於受信任的本機建置輸出。

.NET 10 於 2025-11-11 發布，支援至 2028-11-14。WPF 為 Windows-only 的 .NET UI framework，
並在 .NET 10 持續獲得更新。

## 文件

- [下載、安裝、更新與移除](docs/installation.md)
- [支援與疑難排解](SUPPORT.md)
- [產品規格](docs/product-spec.md)
- [架構](docs/architecture.md)
- [路線圖](docs/roadmap.md)
- [測試策略](docs/testing.md)
- [領域詞彙](CONTEXT.md)
- [架構決策](docs/adr/)

## 回報問題與相容性結果

建立回報前請先閱讀[支援與疑難排解](SUPPORT.md)，並搜尋
[現有 Issues](https://github.com/Command1264/MediaLock/issues)。請依回報類型使用：

- [Bug report](https://github.com/Command1264/MediaLock/issues/new?template=bug-report.yml)：可重現的 Media Lock 行為缺陷。
- [Compatibility report](https://github.com/Command1264/MediaLock/issues/new?template=compatibility-report.yml)：特定媒體來源、Windows build 或輸入裝置的成功／失敗結果。

公開 Issue 前請檢查並移除 logs、設定、路徑與截圖中的私人資料。

## 官方參考

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [WPF documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [Windows.Media.Control namespace](https://learn.microsoft.com/en-us/uwp/api/windows.media.control)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
