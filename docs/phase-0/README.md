# Phase 0：Windows 媒體控制技術探針

## 驗證問題

一般使用者權限的 .NET 10 桌面程序，是否能：

1. 透過 GSMTC 列舉並控制個別媒體 Session；
2. 以 `WH_KEYBOARD_LL` 攔截實體媒體鍵；
3. consume 按鍵後只將命令送往選定 Session，且不再由 Windows 重複分派？

這是可丟棄的技術探針，不是正式產品架構。它不保存設定、不提供 UI、不實作正式 recovery，
也不代表 Phase 1 的模組邊界。

## 執行

需求：Windows 10 1809（build 17763）以上，以及 .NET SDK `10.0.400`。

```powershell
dotnet run --project src/MediaLock.Probe/MediaLock.Probe.csproj
```

啟動媒體播放後輸入 `refresh`，用 `select <number>` 選擇目標，再以 `toggle`、`next` 等命令先確認
GSMTC 控制。輸入 `hook on` 後，只有目標 Session 宣告可處理的實體媒體鍵才會被 consume 並導向
該目標；沒有選擇或控制不可用時，按鍵會 pass through。

完整實機步驟與記錄表見 [manual-test.md](manual-test.md)。

## 已知限制

- Session 以本次列舉的物件實例識別；探針不保存選擇，也不實作正式 Session Fingerprint recovery。
- 已選 Session 短暫消失時會進入最多 2 秒的 probe-only Recovering；期間命令與實體鍵 pass through。
- 2 秒內只有一個相同 SourceAppUserModelId Session 回來時會自動重新選取；逾時後改為
  Unavailable 並保留 Locked Target，期間命令與實體鍵繼續 pass through。
- Unavailable 後若唯一同來源 Session 晚到會自動重新選取；候選不唯一時保持 Unavailable，只有
  使用者執行 clear 才真正忘記 Locked Target。
- Hook callback 僅讀取已快取的控制能力並嘗試 enqueue；不在 callback 內呼叫 WinRT 或輸出 log。
- 同一次實體按住由首個 keydown 決定 consume 或 pass-through；auto-repeat 與 keyup 沿用相同決策，
  不會在按住期間因 Recovery 或 queue 狀態改變而產生不成對的 Windows key stream。
- Input queue 上限為 128；佇列滿時該次按鍵不會被 consume，而會交回 Windows 原生處理。
- 每個 consumed input 保留 capture 當下的 Session 參考；GSMTC 非同步呼叫由單一背景 consumer 依序處理。
- GSMTC、Recovery timer、system lifecycle 與 console intent 透過同一個序列化 queue 處理；platform
  callback 只 enqueue，不直接改變 Locked Target 狀態。
- 系統 suspend 時會將 catalog 標為 unavailable 並釋放 manager 訂閱；resume 後重新取得 manager、發布
  完整 Session snapshot，再執行 Recovery。重新取得失敗會留下可操作 log，且不會終止 intent queue。
- `WH_KEYBOARD_LL` 可能被 Windows 因逾時靜默移除；本探針尚未加入 watchdog。
- 鎖定畫面、睡眠恢復、不同鍵盤韌體與媒體 App 的行為只能以實機結果判定。
- 在完成手動矩陣前，不得宣稱 capture、consume、route 已可靠成立。
