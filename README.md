# Media Lock

Media Lock 是規劃中的 Windows 桌面工具：它位於實體媒體鍵與 Windows 媒體工作階段之間，讓使用者
將播放、暫停、上一首、下一首與停止命令導向指定播放器，而不是永遠接受 Windows 的
`CurrentSession` 選擇。

## 專案狀態

目前為文件與技術驗證準備階段，尚未建立可執行程式。第一個里程碑是 Console Prototype，用來證明：

1. 可列出 GSMTC Sessions。
2. 可選定並持續辨識單一 Session。
3. 可攔截並 consume 實體媒體鍵，避免 Windows 重複觸發。
4. 可將命令可靠送往鎖定目標。
5. Session 消失及重新出現時可恢復。

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
