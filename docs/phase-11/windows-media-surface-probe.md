# Phase 11B Windows Media Surface Mirror probe

驗證日期：2026-08-25

此紀錄只涵蓋 feasibility probe，不代表 Phase 11C production commitment。Probe 使用 documented desktop
SMTC publication；private Current Session setter 的隔離實驗另記於
[`../research/windows-media-current-session-selection.md`](../research/windows-media-current-session-selection.md)。

## Environment

- Host：Windows 11 Pro 25H2，build 26200.9168，x64。
- Target：Brave YouTube Music PWA，`Brave._crx_cinhimbnkkghhklpknlkffjgod`。
- Competing source：普通 Brave YouTube，`Brave`。
- Probe：`MediaLock.Phase11BMirrorProbe.exe`，unsigned local Release build。
- Routing：Session Lock，target 為 YouTube Music。

## Automated boundary evidence

- `GsmtcMediaAdapter` 可接受明確 excluded Source application IDs。
- Owned mirror Session 即使是 Windows Current Session，也不進入 catalog、不取得 `SessionKey`、不讀取
  metadata，且無法成為 routing target。
- `MediaLock.Windows.Tests` 40/40 passed，包含 owned-session exclusion regression。
- Probe build 與 formatting verification passed，0 warnings／0 errors。

## Host observations

| Check | Result | Evidence／limitation |
| --- | --- | --- |
| Initial catalog | Pass | 只列出普通 Brave 與 Brave YouTube Music；owned probe 未出現在可選清單 |
| Mirror publication | Pass | Windows public GSMTC 列出 mirror |
| Current Session selection | **Limit** | 啟用 mirror 後 `GetCurrentSession()` 指向 mirror，但 mirror 位於 `GetSessions()` index 2 |
| Metadata | Pass | `回不去的夏天`／`夏日入侵企画`／album 正確 |
| Playback state | Pass | Paused／Playing 狀態由 public GSMTC 讀回正確 |
| Capabilities | Pass | Play、Pause、Previous、Next、Stop、Seek 與 target 一致 |
| Timeline | Pass | position 與 4:45 duration 正確發布 |
| Windows native media card Seek | N/A | Mirror 已發布 timeline 且 public playback controls 回報 `seek=True`，但此 Windows 11 `Win+A` 卡片未顯示可點擊或拖曳的進度條，因此原生介面沒有可執行的 Seek 操作 |
| Public consumer request | Pass | 一次 `TryTogglePlayPauseAsync` 產生一筆 `SURFACE #1 Play`，Routed/LockedSession |
| Physical Play/Pause | Pass | 一次實體鍵產生一筆 `SURFACE #2 Pause`，Routed/LockedSession |
| Windows native media card | Pass | 使用者在 `Win+A` 媒體卡片按一次 Play，產生且僅產生一筆 `SURFACE #6 Play`；YouTube Music 播放，普通 YouTube 未改變 |
| Lock／unlock | Pass | 鎖定畫面顯示帶 `[Media Lock Mirror]` 標記的卡片；按一次 Pause 僅產生一筆 `SURFACE #1 Pause`，YouTube Music 暫停、普通 YouTube 未改變。解鎖期間外部 catalog 暫由 2 個 Sessions 變成 1 個再恢復為 2 個，Media Lock 的 active target 始終為 YouTube Music，無重複 route 或崩潰 |
| Current after unlock／surface command | **Limit** | 解鎖後使用者觀察 `Win+A` 卡片回到 Brave；隨後 probe 的 public `GetCurrentSession()` 亦確認為 `Brave`，同時 mirror 仍列於 Sessions、Media Lock 仍鎖定 YouTube Music。Windows 可在原生來源更新狀態後重新選擇 Current，mirror 無法保證持續顯示 |
| Sleep／resume routing lifecycle | Pass | 睡眠時 catalog 由 2 個外部 Sessions 變為 0，狀態依序進入 Recovering 與 Fallback；喚醒後恢復 2 個 Sessions 並重新 Locked 至 YouTube Music，mirror Session 仍存在，無錯誤或崩潰 |
| Current after sleep／resume | **Limit** | 睡眠前 public Current 與 `Win+A` 為帶標記 mirror；喚醒後使用者觀察卡片不含標記，probe 亦確認 Current 已改為 `Brave`。Mirror 仍在 public Sessions 中，但無法維持 Current 身分 |
| Disable／Exit teardown | Pass | `Disable and exit` 後 probe process count 為 0；重新啟動且 mirror 預設停用時，public Sessions 只有普通 Brave 與 YouTube Music，沒有舊 owned Session。再次退出後 process count 仍為 0 |
| Visual identity marker | **Limit** | 為 mirror title 加上 `[Media Lock Mirror]` 後重啟；首次 enable 時 Windows Current 仍為原生 YouTube Music。停用再重新 enable 後，Current 與 `Win+A` 實際卡片才一致顯示帶標記 mirror。鏡像可見，但前景選擇不穩定 |
| Competing ordinary YouTube | Pass | 兩次控制均未改變普通 YouTube |
| Feedback loop | Pass | catalog 維持兩個外部 Sessions；無 owned target、重複 route 或循環 |
| Reload／Session recreation | Pass | 換曲與兩次 Ctrl+R 造成多次 Recovering；active 在缺席期間為 none，每次只重新鎖定 YouTube Music successor，未選普通 Brave |
| Recovery successor control | Pass | 重新 enable mirror 後，一次 public request 產生一筆 `SURFACE #3 Pause` 並路由至新的 locked successor |
| Target change | Pass | 改鎖普通 Brave 時 mirror metadata／capabilities／timeline 隨目標更新；兩次單次 public request 分別產生 `SURFACE #4 Play` 與 `SURFACE #5 Pause`，再鎖回 YouTube Music 後鏡像恢復其 metadata 與 Paused 狀態 |
| Persistent Current／first | **Limit** | 首輪 enable 曾使 mirror 成為 Current；播放路由會讓真正的 YouTube Music 重新成為 Current。加入視覺標記並重啟後，即使 mirror 已 enable，Current 仍維持原生 YouTube Music。Public API 不保證選擇、持久性或排序 |

## Remaining matrix

- [x] YouTube Music reload／Session recreation：Recovering、successor、metadata、一次控制。
- [x] Target change between ordinary YouTube and YouTube Music。
- [x] Native media surface button interaction。
- [x] Native media surface Seek interaction：Windows 介面未提供進度條，N/A。
- [x] Lock／unlock。
- [x] Sleep／resume。
- [x] Disable／Exit teardown：owned Session、metadata 與 controls 無殘留。
- [x] Final Proceed／Limit／Reject decision。

## Decision

Final decision：**Limit**。

Documented desktop SMTC 足以建立可辨識的 Media Lock mirror，並同步 metadata、artwork、playback state、
capabilities 與 timeline。Windows 原生媒體卡片、鎖定畫面及 public GSMTC consumer 發出的控制要求，都能透過
既有 router 精確一次送到鎖定目標；owned Session 排除、recovery、target change、lock／unlock、sleep／resume
與 teardown 也成立。

但 public API 沒有設定或維持 Windows Current Session 的契約。Mirror 即使一度成為 Current，也會在原生來源
更新、控制完成、解鎖或睡眠喚醒後被 `Brave`／YouTube Music 取代；`GetSessions()` 的排序同樣不可依賴。
Private `SetCurrentSession` 實驗不適合作為 production dependency。此 Windows 11 `Win+A` 卡片亦未提供 Seek
進度條，即使 mirror 已發布 `seek=True`。

因此不得把 Phase 11C 定義為「Media Lock 永遠取代 Windows 媒體卡片」或預設啟用的可靠功能。若後續仍要
產品化，只能定位成明確標示限制、可關閉的 best-effort mirror；Media Lock 的實體媒體鍵路由仍應維持為核心
保證，不得依賴 Windows Current Session 或 mirror 的顯示順位。
