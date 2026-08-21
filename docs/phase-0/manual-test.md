# Phase 0 手動驗證程序

## 測試環境記錄

| 欄位 | 結果 |
| --- | --- |
| Windows 版本／build | Windows 11 專業版，10.0.26200（build 26200） |
| .NET SDK | 10.0.400 |
| 權限（一般／系統管理員） | 一般使用者，Medium integrity（S-1-16-8192） |
| 鍵盤／媒體鍵來源 | ASUS 華碩 ROG STRIX FLARE 機械式鍵盤的實體媒體鍵 |
| Brave 與網站版本 | Brave 151.1.93.137；網站版本待實機矩陣記錄 |
| Spotify 版本 | 待測 |

## 2026-08-21 本機 smoke test

- `dotnet format MediaLock.sln --verify-no-changes --no-restore` 通過。
- Debug 與 Release build 均為 0 warning、0 error。
- `--help` 正常輸出並以 exit code 0 結束。
- GSMTC manager 在一般使用者權限下初始化成功，列出 2 個 Brave Media Session，並取得 media、
  playback status、timeline 與 controls。
- 未選擇 Session 與選擇一個 Brave Session 時，`hook on`、`status`、`hook off` 均正常；
  已選目標的 playback status 與 controls 可讀取，`clear` 可解除選擇，`exit` 可正常卸載；
  全程未出現 hook lifecycle 錯誤。

這組 smoke test 當時沒有按下實體媒體鍵，也沒有執行 control command，因此本身不證明 selected Session
routing、consume 或無重複 Windows dispatch；後續實體矩陣結果記錄如下。

## 2026-08-21 YouTube Music Session recreation

- YouTube Music PWA 每次真正執行 Next／Previous 時，會暫時從 GSMTC 清單消失，再以相同
  `SourceAppUserModelId` 建立新 Session；觀察到的 gap 約為 0.3–0.6 秒。
- 加入 2 秒 probe-only Recovering 後，只選取一次目標即可連續執行 Next 10 次：10/10 accepted、
  10/10 temporary loss、10/10 recovered、0 skipped，最後仍保持選取。
- 新歌剛開始後立即 Previous，Next 與 Previous 兩次 Session recreation 都成功恢復；這排除了
  YouTube Music 在播放超過門檻後將目前歌曲歸零、而非返回上一首的不同語意。
- Recovering gap 內的第二個命令會明確 skipped，不會送往 stale Session；`clear` 會取消 recovery，
  Session 回來後保持未選取。

以上證明 GSMTC command 與短暫 Session recreation 路徑；後續再以真實硬體輸入驗證 consume 與無重複
Windows dispatch。

## 2026-08-21 YouTube Music 實體媒體鍵矩陣

- 測試時同時保留普通 Brave YouTube Session（Paused）與 YouTube Music PWA Session，僅選取後者，
  並啟用 keyboard hook。
- Play/Pause 實際執行 24 次：24/24 INPUT consumed、24/24 ROUTE accepted；播放狀態逐次切換，
  沒有同一實體輸入產生重複 route。
- 播放清單尾端的探索操作證實：YouTube Music 仍可能回報 Next accepted，但內容不再切換；將清單
  回到開頭後另行執行正式 Next 10 次，10/10 consumed、10/10 accepted、10/10 temporary loss、
  10/10 recovered、0 selection lost。
- Previous 在每首歌剛開始時正式執行 10 次，避免「播放超過門檻後只將目前歌曲歸零」：10/10
  consumed、10/10 accepted、10/10 temporary loss、10/10 recovered、0 selection lost。
- 實體 Stop 1/1 consumed 且 accepted；YouTube Music 隨後關閉 GSMTC Session。當時版本在 2 秒後
  清除 selection；此結果促成後續 Unavailable Locked Target 行為。Stop 因會終止 Session，列為
  獨立終止情境並待新行為回歸。
- 全部正式操作後，非目標普通 YouTube 仍為 Paused；未觀察到 Windows 重複分派。hook off 與 exit
  均正常，沒有殘留 hook。
- 在普通 YouTube 與 Codex 分別位於 foreground 時，共 6 次實體 Play/Pause（2 次探索、4 次正式）
  全部只導向已選取的 YouTube Music；每次各有一個 consumed 與一個 accepted route，非目標普通
  YouTube 的前後狀態均為 Playing。
- 執行 clear 後保持 hook on，再於普通 YouTube foreground 按 Play/Pause 2 次：2/2 log 僅為
  pass-through（no selected session），沒有 consumed 或 route；Windows 原生處理成功讓普通 YouTube
  暫停後恢復播放。
- 實體 Play/Pause 快速短按 6 次（間隔約 0.4–0.5 秒）：6/6 consumed、6/6 accepted，route 順序
  正確，沒有 skipped、queue full 或 Session loss，最後依偶數次切換回到 Paused。
- 實體 Play/Pause 長按約 3 秒只產生 1 個 input、1 個 consumed 與 1 個 accepted route；鍵盤沒有
  auto-repeat，放開後也沒有延遲事件。非目標普通 YouTube 的最終 Paused 狀態由使用者手動操作造成。
- Windows 鎖定約 10 秒後解鎖：鎖定期間 YouTube Music Session 先 replaced，接著消失超過 2 秒，
  recovery 逾時並清除 selection；解鎖後兩個 Session 均返回且 hook 仍為 on，但實體 Play/Pause 因
  no selected session 而 pass through，由 Windows 原生處理 YouTube Music。結論為 selection recovery
  失敗、hook lifecycle 通過；此為修復前基線。
- Unavailable 修復後重跑鎖定約 10 秒／解鎖：2 秒 recovery window 到期後 Locked Target 保持
  unavailable 約 16 秒；YouTube Music Session 返回後自動 reselected，解鎖後實體 Play/Pause 為
  consumed 且 accepted，沒有 pass-through，hook 全程存活。
- 睡眠至少 20 秒後喚醒：YouTube Music Session 消失並轉為 Unavailable，約 5.6 秒後自動 reselected；
  喚醒後實體 Play/Pause 為 consumed 且 accepted，hook 全程存活。普通 YouTube 在睡眠後為 Paused，
  沒有收到喚醒後的 routed key；YouTube Music 是唯一被路由並變為 Playing 的目標。

上述睡眠結果來自 manager lifecycle hardening 前的版本，只證明該次既有 manager 在本機恢復後仍可使用；
它不證明 manager 已被釋放與重新取得。2026-08-22 加入自動 suspend／resume manager reacquisition、統一
intent queue 與 press-cycle consume 決策後，必須依下方回歸流程重新測試才能更新結論。

## 2026-08-22 hardening 自動驗證

- 同一次按住的 consumed 與 pass-through 決策各有回歸測試，涵蓋 `keydown → repeat keydown → keyup`；
  handler 每次 press cycle 只執行一次，三個訊號使用同一決策。
- 序列化 intent queue 測試確認 callback 依提交順序執行，maximum concurrency 為 1。
- system lifecycle 測試確認 suspend 後舊 manager 解除訂閱並釋放，resume 後取得新 manager 且只保留
  一份 SessionsChanged 訂閱；舊 manager 的事件不再進入 queue。
- manager reacquisition 失敗測試確認錯誤可觀察，而且 intent queue 仍可繼續處理後續工作。
- 實體回歸使用 ASUS 華碩 ROG STRIX FLARE 機械式鍵盤。Stop 共 11 次：11/11 INPUT consumed、
  11/11 ROUTE accepted、0 rejected；普通 Brave YouTube 維持 Paused，沒有 competing action。
- hook off 的 Next baseline 長按約 2 秒已切換超過 10 次，證明此裝置會產生 auto-repeat。hook on 後
  transcript 記錄兩次獨立 Next press cycle，各只有 1 個 INPUT consumed 與 1 個 ROUTE accepted；
  使用者確認長按測試只切換一次，放開後沒有延遲切歌。
- hardening 後睡眠／喚醒記錄到一次 `Reacquiring GSMTC manager after system resume.`；YouTube Music
  Media Session 在喚醒過程多次消失、替換及 Recovery，最後 Locked Target 成功 reselected。其後實體
  Play/Pause 為 1 INPUT consumed、1 ROUTE accepted，普通 YouTube 沒有改變。
- 完整 transcript 中 `Serialized intent failed` 為 0、rejected route 為 0。原始 transcript 含媒體標題，
  repository 只保存去識別化的計數與結果。

## 基本 GSMTC

1. 以一般使用者權限執行探針。
2. 在 Brave 開啟 YouTube Music 並播放；另開 Spotify 並播放後暫停其中一方。
3. 執行 `refresh`，確認兩個來源及 title、artist、status、timeline、controls 可辨識。
4. 分別 `select` 每個 Session，執行 `play`、`pause`、`toggle`、`next`、`previous`、`stop`。
5. 記錄 `accepted`／`rejected` 與實際 App 行為是否一致。

## Capture、consume、route

1. 同時保留至少兩個可控制 Session，選擇其中一個後執行 `hook on`。
2. 對 Play/Pause、Next、Previous 各按至少 10 次（App 不支援的控制標記 N/A）；Stop 因可能終止
   Session，另以可重新建立 Session 的獨立流程驗證。
3. 每次確認 log 同時出現 `INPUT ... consumed` 與 `ROUTE ... accepted`。
4. 確認只有選定 Session 改變；非選定 Session 不得收到 Windows 的重複分派。
5. 執行 `clear`，再按媒體鍵，確認 log 為 `pass-through` 且 Windows 維持原生處理。
6. 重新選擇 Session，關閉該 App，確認先進入最多 2 秒 Recovering，期間按鍵必須 pass through；
   Session 未返回時再確認轉為 Unavailable 且保留 Locked Target，日後唯一同來源 Session 返回時
   自動重新選取；只有 clear 才真正忘記目標。

| 情境 | 次數 | INPUT | ROUTE | 僅目標改變 | 無重複分派 | 結果 |
| --- | ---: | --- | --- | --- | --- | --- |
| YTM PWA Play/Pause | 24 | 24 consumed | 24 accepted | 是 | 是 | 通過 |
| YTM PWA Next | 10 | 10 consumed | 10 accepted | 是 | 是 | 通過 |
| YTM PWA Previous | 10 | 10 consumed | 10 accepted | 是 | 是 | 通過 |
| YTM PWA Stop（hardening 後） | 11 | 11 consumed | 11 accepted | 是 | 是 | 通過 |
| 普通 Brave YouTube（非目標） | 44 次正式路由期間 | N/A | N/A | 維持 Paused | 是 | 通過 |
| Spotify Play/Pause | 10 | 待測 | 待測 | 待測 | 待測 | 待測 |
| Spotify Next | 10 | 待測 | 待測 | 待測 | 待測 | 待測 |
| Spotify Previous | 10 | 待測 | 待測 | 待測 | 待測 | 待測 |

## 邊界情境

- 通過：切換普通 Brave YouTube 與 Codex 的 foreground focus 後，實體鍵仍只控制已選目標。
- 通過：clear 後實體 Play/Pause 2/2 pass through，由 Windows 原生處理普通 YouTube。
- 通過：實體 Play/Pause 快速連按 6 次，事件數與 route 數一致且順序正確。
- 通過：hook off 長按 Next 約 2 秒會切換超過 10 次；hook on 後同一次長按只產生 1 個 INPUT、
  1 個 ROUTE 與一次切換，press-cycle consume 決策保持一致。
- 待測：加入 Spotify 後切換 Spotify foreground，再確認仍只控制已選目標。
- 通過：Unavailable 修復後，Windows 鎖定／解鎖保留 Locked Target，Session 返回後自動 reselected，
  hook 與實體路由正常。
- 通過：hardening 後睡眠／喚醒明確記錄一次 `Reacquiring GSMTC manager after system resume.`；
  Locked Target 最後成功 reselected，喚醒後實體 Play/Pause 只產生一次 consumed 與 accepted route。
- 執行 `hook off` 與正常結束程式，確認媒體鍵恢復 Windows 原生行為。

## 通過門檻與結論

Phase 0 只有在支援的控制 10/10 次皆只影響選定 Session、沒有可觀察的重複 Windows 分派，且
focus／解鎖／睡眠恢復結果可接受時，才能判定此 backend 適合進入 Phase 1。任何失敗都要附上可重現
步驟與 log，再決定修正、加入 recovery，或更換 input backend。

**目前結論：YouTube Music PWA 的 capture、consume、route、pass-through、foreground focus、快速
連按、短暫 Session Recovery、Stop 11 次、實體 auto-repeat、Windows 鎖定／解鎖，以及 hardening 後
睡眠／喚醒均已通過。自動測試亦涵蓋 press-cycle、事件序列化及 manager reacquisition。因此可判定
`WH_KEYBOARD_LL` backend 在本機 Windows 11 build 26200、ASUS ROG STRIX FLARE、Brave＋YouTube
Music PWA 範圍通過；Spotify 與其他 compatibility matrix 項目仍待測，不得擴張為完整 MVP 相容性
聲明。**
