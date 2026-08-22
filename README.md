# Media Lock

Media Lock 是規劃中的 Windows 桌面工具：它位於實體媒體鍵與 Windows 媒體工作階段之間，讓使用者
將播放、暫停、上一首、下一首與停止命令導向指定播放器，而不是永遠接受 Windows 的
`CurrentSession` 選擇。

## 專案狀態

目前已有可執行的 WPF 桌面殼層與 Console Probe。桌面程式可列出 GSMTC Sessions、鎖定目標、
提供媒體控制、常駐通知區域，並保存設定與執行狀態。Phase 5 已加入 App Lock 與有序的來源應用程式
Priority Rules；實機相容性仍依測試矩陣記錄。

已驗證的基礎能力包括：

1. 可列出 GSMTC Sessions。
2. 可選定並持續辨識單一 Session。
3. 可攔截並 consume 實體媒體鍵，避免 Windows 重複觸發。
4. 可將命令可靠送往鎖定目標。
5. Session 消失及重新出現時可恢復。

## 執行桌面程式

```powershell
dotnet run --project src\MediaLock.App\MediaLock.App.csproj --configuration Release
```

關閉視窗預設只會隱藏至 Windows 通知區域；從托盤選單按 `Exit` 才會結束程式。設定頁可切換
close-to-tray 與目前使用者的 Windows 登入啟動，也可保存預設 routing mode、Recovery timeout 與
Fallback Policy。設定可由主視窗右上角或托盤選單開啟為獨立視窗；短淡入／隱藏動畫會遵循
Windows 的用戶端動畫設定。`Save settings` 成功後會關閉設定視窗；儲存失敗則保留視窗與錯誤訊息。
使用者資料位於 `%LocalAppData%\MediaLock\`：`settings.json`、`state.json` 與
有界輪替的 `logs\*.jsonl`。正常診斷記錄預設不保存媒體 title 或 artist。

Recovery timeout、Fallback Policy、Priority Rules 與 Default routing mode 會在下次啟動時套用。Priority
Rules 依設定順序選擇第一個目前可用且已啟用的來源應用程式，沒有匹配時使用 Windows Current Session；
同一來源內沿用 App Lock 的確定性候選政策。App Lock 可從主視窗選取
Session 的來源應用程式，並在該應用程式的候選 Sessions 間依確定性政策切換；它不辨識瀏覽器 URL 或
保證單一 Session 連續性。Default App Lock 會從有效的 `state.json` 恢復保存的來源應用程式。
Default Session Lock
只在 `state.json` 含有有效的 Session Fingerprint，且目前 catalog 有唯一可接受候選時恢復；缺少、
損毀、過期或模糊的候選都不會被靜默鎖定。Default Windows Auto 一律忽略先前保存的鎖定。

## 預定技術基底

- C#、.NET 10 LTS。
- WPF 作為 Windows GUI。
- 展示層使用 MVVM；核心由顯式狀態機與 application services 驅動。
- Windows GSMTC 作為媒體工作階段資訊與控制邊界。
- Win32 input backend、system tray 與 startup integration 經由 adapters 隔離。
- `win-x64` self-contained single-file 為預定發布候選；須以實際相容性測試確認。

.NET 10 於 2025-11-11 發布，支援至 2028-11-14。WPF 為 Windows-only 的 .NET UI framework，
並在 .NET 10 持續獲得更新。

## 文件

- [產品規格](docs/product-spec.md)
- [架構](docs/architecture.md)
- [路線圖](docs/roadmap.md)
- [測試策略](docs/testing.md)
- [領域詞彙](CONTEXT.md)
- [架構決策](docs/adr/)

## 官方參考

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [WPF documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [Windows.Media.Control namespace](https://learn.microsoft.com/en-us/uwp/api/windows.media.control)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
