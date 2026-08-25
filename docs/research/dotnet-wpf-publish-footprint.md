# .NET 10／WPF 發行體積研究

日期：2026-08-25

範圍：Phase 12B；比較 Media Lock 的 self-contained single-file 發行方式與不改變功能的安全候選。

資料邊界：平台行為只採用 Microsoft、dotnet SDK／WPF／CsWinRT 與 Inno Setup 的官方文件或官方原始碼。

## 結論

Media Lock 現行約 200 MB 的 `MediaLock.exe` 主要不是產品程式碼，而是一起攜帶的 .NET 10、Windows
Desktop Runtime、WPF、WinForms、WinRT projection、native runtime libraries 與語系資源。正式 portable
套件必須繼續提供不另裝 .NET 的 self-contained single-file 版本；framework-dependent 可用於比較，但不能
無聲取代既有承諾。

目前最重要的量測結果是：

- `EnableCompressionInSingleFile=true` 把 EXE 從 `200,339,490` bytes 降至 `82,314,522` bytes，減少
  `58.91%`。
- 相同設定只把 ZIP 從 `78,687,769` bytes 降至 `76,499,762` bytes，減少 `2.78%`。
- Inno Setup 反而從 `56,041,226` bytes 增至 `76,911,021` bytes，增加 `37.24%`。原因是內層壓縮後，
  外層 LZMA2 不再能對原始 assemblies 取得相同壓縮率。
- 在 Intel Core i7-8700 的 exact clean-commit 15+15 次取樣中，壓縮版 fresh-extraction-cache median 慢
  `47.45 ms`（`2.86%`），warm-cache median 慢 `32.84 ms`（`2.04%`）。啟動成本不大，但下載用 Setup 明顯變大，
  因此不能只憑裸 EXE 尺寸決定正式設定。
- 僅保留 `zh-Hant;zh-TW` satellite resources、但不啟用 single-file 壓縮時，EXE、ZIP、Setup 分別減少
  `9.11%`、`6.98%`、`3.46%`。這是較均衡但收益較小的候選，仍須通過語言 fallback 實測。

Phase 12B 不採用 trimming、ReadyToRun、Native AOT 或關閉 native-library self extraction。使用者已接受
「安裝後 EXE 減少約 118 MB、Setup 下載增加約 21 MB」的取捨，正式候選採 single-file compression 並保留
全部語系資源；supported-locale filtering 不進入本輪正式設定。

## 現行發布契約

`win-x64.pubxml` 固定：

```xml
<PublishSelfContained>true</PublishSelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
<PublishReadyToRun>false</PublishReadyToRun>
<DebugType>embedded</DebugType>
```

Microsoft 說明 self-contained single-file 會包含 runtime 與 framework libraries，因此檔案本來就較大。
`IncludeNativeLibrariesForSelfExtract=true` 會把 native libraries 放進 bundle，Windows 執行時再解壓至
`%TEMP%\.net` 或 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 指定位置。關掉它只會把 native libraries 攤回發布
目錄，不是移除 runtime。
[Microsoft：Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

以正式 profile 只覆寫 `PublishSingleFile=false` 的展開 publish 共 `206,631,512` bytes／478 個檔案。依 PE
metadata 與具名 Windows Desktop assemblies 做互斥分類後：

| 類別 | 檔案數 | Bytes | MiB | 占比 |
| --- | ---: | ---: | ---: | ---: |
| .NET／runtime managed | 232 | 74,019,200 | 70.59 | 35.82% |
| WPF managed／resources | 168 | 46,633,368 | 44.47 | 22.57% |
| WinForms managed／resources | 48 | 33,180,200 | 31.64 | 16.06% |
| WinRT projection managed | 2 | 26,870,352 | 25.63 | 13.00% |
| .NET／runtime native | 17 | 17,279,536 | 16.48 | 8.36% |
| WPF native | 4 | 8,090,856 | 7.72 | 3.92% |
| Media Lock managed | 5 | 518,144 | 0.49 | 0.25% |
| 其他 JSON | 2 | 39,856 | 0.04 | 0.02% |

WinRT projection 類別是 `Microsoft.Windows.SDK.NET.dll` 與 `WinRT.Runtime.dll`。WPF 類別涵蓋
`Presentation*`、`WindowsBase`、XAML、Reach／Ribbon、UIAutomation、printing 與具名 WPF native libraries；
WinForms 類別涵蓋 `System.Windows.Forms*`、`System.Drawing*` 與 `Accessibility.dll`。剩餘 managed／native
檔案保守歸入 .NET runtime，不把無法可靠歸屬的 shared Windows Desktop assembly 誤報為產品程式碼。

單檔 baseline 與壓縮候選的 fresh extraction cache 都是 `8,215,400` bytes；single-file compression
沒有移除 native extraction 成本。

## i7-8700 基準

環境：

- CPU：Intel Core i7-8700，6 cores／12 logical processors，3.20 GHz。
- OS：Windows 11 Pro 25H2，build 26200.9168，x64。
- 樣本：每個變體 15 次 fresh extraction cache、1 次排除的 warm-up、15 次 warm extraction cache。
- 順序：每輪交錯 baseline／candidate，降低 Defender、OS cache 與先後順序偏差。
- 就緒定義：程序建立可用主視窗且 UI thread 進入 idle；量測後只終止該次由工具建立的 PID。

| 變體 | EXE | ZIP | Setup | Fresh median／p95 | Warm median／p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| baseline | 200,339,490 | 78,687,769 | 56,041,226 | 1,658.47／1,710.25 ms | 1,611.87／1,685.45 ms |
| single-file compressed | 82,314,522 | 76,499,762 | 76,911,021 | 1,705.92／1,919.01 ms | 1,644.71／1,735.57 ms |

這不是清除 Windows 檔案快取或重新開機後的真正 cold boot benchmark。正式採用前仍須在同一台
i7-8700 上重新開機，各執行 baseline 與 candidate，記錄從啟動到主視窗可操作的人工 smoke；自動結果只用於
同機相對比較。

## 候選比較

### Single-file compression

`EnableCompressionInSingleFile=true` 壓縮 bundle 內的 managed assemblies，執行時在記憶體中解壓。
Microsoft 明確提醒它可能增加啟動成本，必須依應用實測。
[Microsoft：Compress assemblies in single-file apps](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps)

現行 installer 使用 `Compression=lzma2/max` 與 `SolidCompression=yes`。Inno 的外層壓縮對未壓縮
assemblies 比 .NET 內層壓縮後的資料更有效，所以壓縮 bundle 會縮小安裝後檔案，卻放大下載用 Setup。
[Inno Setup：Compression](https://jrsoftware.org/ishelp/topic_setup_compression.htm)、
[Inno Setup：SolidCompression](https://jrsoftware.org/ishelp/topic_setup_solidcompression.htm)

### Satellite resource filtering

.NET SDK publish target 可用 `SatelliteResourceLanguages` 過濾 dependency 的 satellite resource assets。
Media Lock 自身以 neutral resources 提供 English，以 `zh-TW` satellite 提供繁體中文；WPF runtime 另帶
`zh-Hant` resources。因此第一個安全候選是 `zh-Hant;zh-TW`，而不是只留 `zh-TW`。
[dotnet SDK：Microsoft.NET.Publish.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Publish.targets)

| 變體 | EXE | ZIP | Setup | EXE／ZIP／Setup 變化 |
| --- | ---: | ---: | ---: | ---: |
| supported locales | 182,089,629 | 73,194,366 | 54,091,899 | -9.11%／-6.98%／-3.46% |
| locales + single-file compression | 76,236,990 | 70,431,107 | 70,959,907 | -61.95%／-10.49%／+26.64% |

正式採用語系過濾前必須驗證 English、繁體中文、Use Windows language、unsupported Windows culture 的
neutral fallback，以及 WPF／WinForms／installer／diagnostics 是否沒有 resource missing 或空字串。

### Framework-dependent

探索用 framework-dependent single-file 為 `27,491,835` bytes，ZIP 為 `7,335,929` bytes，但這只是 app
payload，沒有包含目標機另外需要的 `.NET 10 Desktop Runtime` bytes，因此不是完整使用者 footprint，也
不是 Phase 12B 的候選。基本 `.NET Runtime` 不含 WPF／WinForms。
[Microsoft：Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

它不取代預設 self-contained ZIP／Setup。若日後新增進階下載，installer 必須偵測 Windows Desktop Runtime，
缺少時提供可操作提示，並在沒有 runtime 的 Windows Sandbox 中驗證；這是新發行通道，而非 build flag 微調。

## 明確排除

### Trimming／Native AOT

.NET SDK 對 `UseWPF=true` 或 `UseWindowsForms=true` 搭配 `PublishTrimmed=true` 具有不支援 guardrail。
WPF 的 trimming 相容工作仍未完成；CsWinRT 自身的 trimming 改善不能解除頂層 WPF／WinForms 限制。
[dotnet SDK：RuntimeIdentifierInference targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.RuntimeIdentifierInference.targets)、
[dotnet/wpf #3811](https://github.com/dotnet/wpf/issues/3811)、
[CsWinRT：AOT and trimming](https://github.com/microsoft/CsWinRT/blob/master/docs/aot-trimming.md)

不得用 `_SuppressWpfTrimError`、`_SuppressWinFormsTrimError`、大量 root assemblies 或壓制 ILLink warnings
繞過限制。XAML、binding、reflection、Tray、COM／WinRT 的 runtime failure 風險不符合本專案發布門檻。

### ReadyToRun

ReadyToRun 以較大的檔案交換部分啟動速度。Microsoft 說明 R2R assembly 通常會變成原本的 2～3 倍；
Phase 12B 維持 `PublishReadyToRun=false`。若未來要改善冷啟動，應另開效能任務。
[Microsoft：ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)

## 建議決策順序

1. 保持 self-contained、single-file、native self-extract、trimming off、ReadyToRun off。
2. 用同一工具重複量測 baseline、single-file compression 與 supported-locales 候選。
3. 本輪已接受 Setup 增大，以換取安裝後 EXE 大幅下降；manifest 必須揭露 single-file compression。
4. 語系過濾維持 test-only，只有另行核准且完整 localization gate 通過後才可加入正式候選。
5. 15 次交錯取樣已通過；重開機 A/B 是偏好證據。若產品負責人明確拒絕第二次重開機，可依 Phase 12B
   計畫記錄 waiver，並以普通候選啟動 smoke 補足體感檢查，不得宣稱完成 reboot A/B。
6. Framework-dependent、多檔 publish 與移除 WinForms dependency 分別留給後續產品／架構決策。
