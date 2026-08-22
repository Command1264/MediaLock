# Open-source media routing landscape

查核日期：2026-08-22

## 1. 目的與判讀方式

本文件只把外部專案當成 Media Lock 的功能、UX 與實作參考，不評估改用第三方 dependency、fork
上游或替換現有 GSMTC adapter。Media Lock 已經有通過自動測試與 Phase 0 實機驗證的
`capture → consume → route`、Recovery 與 lifecycle 邊界；是否改變 dependency 是另一項需要獨立
成本、相容性與授權評估的決策，不由本次研究推導。

以下標記用來避免混淆來源層級：

- **Verified fact**：可由上游 repository、source、release、license、NuGet 或 Microsoft／Chromium
  官方文件直接確認。
- **Media Lock local evidence**：本 repository 已保存的實機記錄，只能代表記錄中的版本與環境。
- **Inference**：根據已查證資料做出的有限推論，不視為平台保證。
- **Recommendation**：針對 Media Lock roadmap 或設計的建議，不代表已核准的產品範圍。

這不是對 GitHub 的窮盡搜尋，因此不能證明「市場上不存在完整成熟工具」。可以確認的是：本文核對的
四個專案都沒有同時實作並驗證 Media Lock 所需的實體媒體鍵攔截與 consume、Locked Target、
Session Recovery、Fallback Policy 與持久化 routing。

## 2. 結論摘要

| 專案 | 已查證定位 | 最值得參考 | Media Lock 缺少的核心 | 本次結論 |
| --- | --- | --- | --- | --- |
| [WindowsMediaController][wmc-repo] | C#/.NET GSMTC wrapper，附 CLI 與 WPF sample | 精簡 API surface、基本 Session 事件與 artwork sample | 實體鍵 consume、持久鎖定、Recovery／fallback；同來源多 Session 模型也較受限 | Reference only |
| [Media Controls for Command Palette][mce-repo] | PowerToys Command Palette extension；列出、觀察及控制多 Session | Session lifetime、短暫消失 retention、artwork、能力導向命令、rich session UX | 全域實體鍵 consume、持久 Locked Target 與 routing policy | **主要 implementation／UX 參考** |
| [Native Taskbar Media Controller][ntmc-repo] | 注入 Windows 11 taskbar 的 Windhawk mod | compact now-playing、progress interpolation、crossfade、session-count affordance | 持久鎖定與 Recovery；並採 Explorer/taskbar injection | **只參考視覺與互動** |
| [Descolada `Media.ahk`][ahk-media] | AutoHotkey v2 的 GSMTC COM/WinRT wrapper | 快速 script prototype 的 API coverage | 本檔沒有 GUI、hotkey interception、routing state machine 或 Recovery | Reference only；不回頭重做 prototype |

**Recommendation**：保留 Media Lock 現有架構與實作。功能面優先借鑑 Media Controls Extension 的
capability-aware presentation、artwork lifetime 與 Session recreation 防護，以及 Native Taskbar Media
Controller 的 compact media card 與非干擾動畫。不要把任何一個上游的「選擇 Session」等同於
Media Lock 的 Locked Target。

## 3. Chromium Session 粒度：原說法需要限縮

### 3.1 上游原文與官方實作說明

**Verified fact**：Native Taskbar Media Controller README 原文聲稱 Chromium browsers 是「one SMTC
session per browser process; all open tabs share it」，並把它描述成 protocol-level limitation。
[該文字只出現在其 compatibility 文件][ntmc-chromium-claim]；引入該敘述的 commit 沒有附 Chromium
或 Windows 規格來源。[commit history][ntmc-claim-commit]

**Verified fact**：Chromium 自己的文件提供了更精確、而且能解釋例外的模型：

- 一般瀏覽器分頁與視窗通常共用一個 OS-facing System Media Controls singleton；它跟隨最近 active 的
  非 PWA media，而不是讓每個普通分頁各自成為 OS Session。
- Desktop PWA（dPWA）可以擁有獨立的 instanced System Media Controls；Windows 與 macOS 自 Chromium
  M130 起啟用此路徑。
- Chromium 內部仍可讓每個 `WebContents` 有自己的 media session；這不代表每個 tab 都會成為獨立
  Windows GSMTC Session。OS-facing SMC aggregation 與 Chromium 內部 media-session identity 是兩個
  不同層級。

以上可直接見 Chromium 的 [System Media Controls overview][chromium-smc-overview]、
[media playback architecture][chromium-media-playback] 與 [`MediaSession` interface 註解][chromium-media-session].

### 3.2 與 Media Lock 實機證據的對照

**Media Lock local evidence**：Phase 0 在 Windows 11 build 26200、Brave 151.1.93.137 上，同時列出普通
Brave YouTube 與獨立安裝的 Brave YouTube Music PWA；後來連同 Chrome YouTube 共列出三個 GSMTC
Session。[Phase 0 manual evidence][phase0-manual] 其中 redacted log 也明確記錄三個 Session 來自兩個
source application，並保留 Brave PWA 的獨立 source marker。[Chrome evidence][phase0-chrome-log]

**Inference**：這不是與 Chromium 官方模型衝突，而是「普通 browser singleton + dPWA instanced
SMC」的預期結果。Native Taskbar Media Controller 的原句若只用來描述普通 browser tabs，大致抓到
OS-facing aggregation；若泛化到 PWA、所有 Chromium surface 或固定的 browser-process identity，就不正確。

**Recommendation**：文件與 UI 應繼續使用 Media Session、source application、App Lock 與 Session
Lock，不使用「browser tab lock」或「每個 browser process 必定一個 Session」作為產品承諾：

- 同一個普通 browser surface 的多個 tabs 仍可能只對 Windows 暴露一個聚合 Session。
- 安裝式 PWA 可能另外暴露獨立 Session 與 `SourceAppUserModelId`。
- Browser、profile、PWA、版本、feature rollout 與網站 Media Session API 行為都可能改變結果。
- 只靠 GSMTC 仍無法保證 `music.youtube.com` URL 或特定 tab identity；若未來要做到 URL/tab precision，
  仍需 Phase v0.3 所列的 browser adapter，且必須另做技術驗證。

## 4. 個別專案查核

### 4.1 DubyaDude/WindowsMediaController

**Verified fact — 功能**：README 與 source 顯示它包裝
`GlobalSystemMediaTransportControlsSessionManager`，提供 Session enumeration、focused Session、open／close、
playback、media properties 與 timeline events，並把 raw
`GlobalSystemMediaTransportControlsSession` 暴露給 caller 執行控制；repository 附有 CLI 與 WPF sample，
sample 顯示 title、artist、thumbnail 與 previous/play-pause/next。
[README][wmc-readme] [manager source][wmc-main] [WPF sample][wmc-sample]

**Verified fact — 邊界**：`CurrentMediaSessions` 以 `SourceAppUserModelId` 字串作 dictionary key；同一
source identity 同時出現多個 Session 時，這個資料模型不會保留多個獨立 entry。library 也直接暴露
raw WinRT Session，因此它是方便 wrapper，不是 Locked Target／Recovery abstraction。
[session dictionary implementation][wmc-dictionary]

**Verified fact — 維護與授權**：MIT；NuGet 最新版為
[`Dubya.WindowsMediaController` 2.5.6][wmc-nuget]。2.5.6 release 發布於 2025-12-08，latest commit
為 2025-12-09 的 .NET 10 pruning fix；截至查核日 repository 未 archived。repository 沒有發現
automated test project，且仍有 SourceAppUserModelId 變動與事件停止觸發的 open issues；這些 issue 是
維護訊號，不是已驗證的 Windows 平台規則。
[2.5.6 release][wmc-release] [latest commit][wmc-latest-commit] [MIT license][wmc-license]
[issue 18][wmc-issue-18] [issue 6][wmc-issue-6]

**Recommendation**：只把 API 命名、基本事件訂閱與 sample 的 artwork flow 當參考。目前 Media Lock
已有更深的 catalog/controller seam、序列化 queue、lifecycle reacquisition 與多候選政策；本次不涉及
package adoption，也不應因 wrapper 較短就推論其能取代這些行為。

### 4.2 jiripolasek/MediaControlsExtension

**Verified fact — 功能與 UX**：extension 會列出所有可見 GSMTC Session，依 live capability 提供
play、pause、stop、next、previous、shuffle、repeat、metadata 與 artwork；另有 Command Palette/Dock
surface、Session 切換、可選擇在播放某 Session 時暫停其他 Session，以及將 source application 帶到
前景。它也提供 default Windows playback endpoint 的 system volume 控制。
[README][mce-readme] [compatibility guide][mce-compat] [session commands][mce-commands]
[source-focus implementation][mce-focus] [system-volume implementation][mce-volume]

**Verified fact — 重要實作模式**：

- GSMTC command 先依 session capability 顯示／執行；shuffle、repeat 明確讀取當前狀態再呼叫
  `TryChange*Async`。[command implementation][mce-commands]
- Artwork 使用 versioned key、延後讀取、32 MiB 上限與 content-type detection，降低 stale artwork 與
  無界輸入風險。[artwork implementation][mce-artwork]
- Session reconciliation 不只用 application identity：它保留 internal ID/generation，在 candidate
  unambiguous 時才 rebound；對短暫消失先用 400 ms probe grace，觀察到 recreation 後才學習較長
  grace，且上限 3 秒與 evidence expiry 都有界。[session reconciliation][mce-reconcile]
  [adaptive retention policy][mce-retention]
- repository 有針對 concurrency、observation gate、native lifetime 與 adaptive retention 的 tests。
  [media tests][mce-tests]

**Verified fact — 維護與授權**：Apache-2.0；v0.22.0 與 latest commit 都在 2026-08-17，repository
截至查核日未 archived。它仍有「old media players don't disappear」與 sleeping Edge/PWA 不回應命令的
open issues，說明即使是近期且有測試的 implementation，Session lifetime 與 browser interoperability
仍需實機證據。[v0.22.0 release][mce-release] [latest commit][mce-latest-commit]
[Apache-2.0 license][mce-license] [issue 10][mce-issue-10] [issue 5][mce-issue-5]

**Inference**：這是四個專案中最接近 Media Lock Windows adapter 問題域的 implementation reference，
尤其是 native object lifetime、bounded refresh 與 recreation retention；但它的目標是讓使用者在
Command Palette 選擇並操作 Session，不是讓實體媒體鍵永久附著 Locked Target。

**Recommendation**：未來修改 Media Lock artwork 或 Windows Session lifetime 時，可重新檢視其
bounded artwork read、generation binding 與 adaptive grace 的測試案例，重新以 Media Lock 的 ports 與
state machine 實作；不要複製其 Command Palette-specific surface 或把 transient retention 當成 durable
Session identity。

### 4.3 StarlightDaemon/Native-Taskbar-Media-Controller

**Verified fact — 功能與 UX**：這是一個需要 Windhawk、直接把 XAML widget 注入 Windows 11 taskbar
tree 的 mod。它顯示 artwork、title、artist、play/pause、previous/next、progress 與 duration；有 hover
flyout、marquee、track-change crossfade、fade animation、session-count chip，以及 double-click bring
source window to front。[README][ntmc-readme] [widget source][ntmc-source]

**Verified fact — Session 行為**：session chip 只是把 in-memory index 加一；每次重新 enumeration
會清除舊 entries，然後把 active index 重設成第一個 playing Session，否則為第一個 Session。這不是
Lock、持久選擇或 Recovery。[cycle implementation][ntmc-cycle] [refresh implementation][ntmc-refresh]

**Verified fact — 維護與授權**：MIT；repository 建立於 2026-05，v1.5.0 tag 指向 2026-06-24
commit，latest branch commit 是 2026-07-15；截至查核日未 archived。目前只有一位 repository
contributor、沒有 issue 或 automated tests，這表示專案仍新，不能只由版本號推定成熟度。
[v1.5.0 tag][ntmc-tag] [latest commit][ntmc-latest-commit] [MIT license][ntmc-license]

**Recommendation**：只參考 compact layout、session-count affordance、播放中 timeline interpolation 與
尊重狀態變化的 subtle animation。Windhawk／Explorer injection、固定大小 Session array、每次 refresh
重置 active index，以及 taskbar 私有 XAML tree coupling 都不適合 Media Lock 的一般使用者權限 WPF
桌面架構。

### 4.4 Descolada/AHK-v2-libraries `Media.ahk`

**Verified fact — 功能**：`Media.ahk` 以 AutoHotkey v2 的 COM/WinRT interop 直接呼叫 GSMTC，提供
current/all Sessions、manager/Session events、media/timeline/playback properties，以及 play、pause、
toggle、stop、previous/next、seek、playback rate、shuffle 與 repeat 等操作。
[Media.ahk source][ahk-media]

**Verified fact — 邊界**：這個單檔 library 本身沒有 GUI，也沒有宣告 hotkeys、全域 keyboard hook、
press-cycle consume 決策、routing queue、Locked Target 或 Recovery。其 async helper 以 10 ms sleep loop
同步等待 WinRT operation；這適合小型 script 的簡便性，不是 Media Lock 已建立之非同步 application
boundary 的參考模式。[async wait implementation][ahk-wait]

**Verified fact — 維護與授權**：整個 collection 是 MIT 且 repository 截至查核日仍有近期其他 library
commit；但 `Lib/Media.ahk` 本身最後修改於 2024-09-05，只有 2024-01-22 與 2024-09-05 兩筆 path
commits，也沒有發現針對 Media library 的 tests 或 releases。
[path history][ahk-history] [MIT license][ahk-license]

**Recommendation**：它仍可作一次性 GSMTC script 教材，但 Phase 0 已用正式 .NET probe 驗證實體鍵
capture/consume/route，現在退回 AHK prototype 不會降低風險，反而會分裂 lifetime、testing 與
packaging 路徑。不要把「AutoHotkey 能宣告 media hotkey」推論成 `Media.ahk` 已驗證可靠 consume。

## 5. 功能候選分類

### 5.1 可直接納入後續 roadmap 候選

這一類表示方向與既有產品定位相容，仍須另開 feature scope、驗收標準與測試，不表示立即實作。

| 候選 | 可參考的上游做法 | Media Lock 建議邊界 |
| --- | --- | --- |
| Artwork | WindowsMediaController WPF sample；Media Controls 的 versioned lazy read、size bound、content sniff | 只作 presentation data；失敗不影響 routing；防止 stale image 與過大 stream |
| Timeline／progress | Native Taskbar 的 position/duration 與播放中 wall-clock interpolation；Media Controls 的 immutable timeline snapshot | 顯示與 routing state 分離；paused/stopped/recreation 時重設；不把估算位置寫回 identity |
| Shuffle／repeat | Media Controls 只在 capability available 時顯示，並由目前狀態計算下一值 | 擴充 `MediaCommand` 前先定義 unsupported/failure semantics；每個 player 實測 |
| Multi-session UX | Media Controls 的明確 Session list；Native Taskbar 的 session-count indicator | 保留「目前 route target」與「Windows Current Session」的視覺差異；不要只用循環 index |
| Subtle animations | Native Taskbar 的 track-change crossfade、marquee、fade；Media Lock 已有依 Windows animation setting 控制的 opacity transition | 動畫只表達狀態改變；respect accessibility/system animation；不得延遲或遮蔽 routing 結果 |

### 5.2 需另行技術驗證或產品決策

| 候選 | 為何不能直接採用 | 最小驗證問題 |
| --- | --- | --- |
| Seek | Microsoft GSMTC 有 `TryChangePlaybackPositionAsync`，`Media.ahk` 也包裝它；但 Media Controls 目前只讀 timeline，沒有可佐證的 seek UX | target 是否宣告 seek capability、單位／bounds、drag throttling、Session recreation、YouTube/YTM 實際接受率 |
| Volume | Media Controls 控制的是 default Windows playback endpoint，不是 Locked Target 的 GSMTC Session | 產品要的是 system volume、process/session volume，還是 target app volume？跨 PWA/browser source 如何解析與避免改到競爭 App？ |
| Source focus | Media Controls 與 Native Taskbar 都以 AUMID/window enumeration heuristics 帶 app 到前景 | 多視窗、PWA、一般 browser tabs、foreground restrictions 下能否選對 window；失敗必須不改變 lock |
| Custom hotkeys | Media Controls 的 chords 是 Command Palette 內 requested shortcuts；`Media.ahk` 沒有提供已驗證的 physical-key consume backend | binding 衝突、press/repeat semantics、global capture、可取消設定、保留既有 media-key可靠度與普通權限 |
| Browser URL／tab correlation | Chromium 普通 tabs 聚合、dPWA 可獨立；GSMTC 不提供 URL identity | browser extension／native messaging 的權限、privacy、lifecycle、profile/PWA mapping 與 fallback |
| Adaptive recovery grace | Media Controls 會依短暫 recreation evidence 學習 bounded grace；Media Lock 現有 Recovery 是正式 routing state | 學習是否可測、是否跨 process 保存、ambiguous same-app candidate 如何拒絕、是否改善 YTM 而不延長錯誤 lock |

Microsoft 官方列出的 GSMTC Session 方法確實包含 seek、shuffle、repeat 與 playback-rate request，但
`Try*Async` 只表示「嘗試要求」；實際 capability 與結果仍由來源 Session 決定。
[Microsoft GSMTC Session API][ms-session-api]

### 5.3 不適合 Media Lock

- **Windhawk／Explorer taskbar injection**：增加 private taskbar tree coupling 與 host-process lifecycle
  風險，不符合 Media Lock 的 WPF shell、tray surface 與 replaceable Windows adapters。
- **每次 refresh 依 first-playing Session 重設 target**：會破壞 Locked Target 與 Recovery 語意。
- **用 `SourceAppUserModelId` 當唯一 live Session dictionary key**：App Lock 可以用 source identity，
  Session Lock 與同 app 多候選仍需要獨立 ephemeral handle 與 fingerprint policy。
- **同步 busy-wait WinRT async operation**：不適用 Media Lock 的 serialized async routing queue。
- **把 system volume 混成 media routing command**：除非先定義清楚 target 與 failure semantics，否則會
  讓使用者以為音量只作用於 Locked Target。
- **從 track metadata 或 browser title 猜 durable identity**：metadata 會正常變動，也不能證明 URL/tab。

## 6. 對原始推薦內容的修正

| 原始推薦 | 查核後判斷 |
| --- | --- |
| WindowsMediaController 已做掉 Media Lock 大部分底層 | 它確實包裝基本 GSMTC，但沒有 Media Lock 的 deeper adapter lifetime、serialized routing、Recovery 或同來源多候選政策；可參考，不宜量化成「大部分」。 |
| Media Controls Extension 可列出並切換所有 Session | 在來源有發布 GSMTC 的範圍內成立；controls/metadata 取決於 live capability。它的 switch/select 不是 durable lock。 |
| Native Taskbar 是 GUI 最佳基底 | compact UX 值得參考；Windhawk injection 與單一大型 C++ mod 不適合作為 Media Lock WPF/application-core 基底。 |
| Chromium 一個 browser process 只有一個 Session | 對普通 tabs 應改寫成「通常聚合到 browser singleton」；dPWA 在 Windows 自 M130 起可以各有 instanced SMC，不能泛化。 |
| `Media.ahk` 可直接快速驗證鎖定 | 它提供 GSMTC wrapper，但沒有 lock/recovery，也沒有在該檔內驗證 hotkey consume；Media Lock Phase 0 已有更直接的硬體證據。 |
| 找不到成熟完整成品，因此 Media Lock 沒有重造輪子 | 四個專案的功能缺口支持「本文範圍內沒有相同完整核心」；不能由有限查核證明整個市場不存在其他工具。Media Lock 的差異仍應以可驗證的 locked routing 與 recovery 行為表述。 |

## 7. 研究限制

- 只查核指定四個 repository；沒有進行市場完整性證明。
- 維護狀態是 2026-08-22 的 snapshot；stars、issues、commit frequency 不能單獨證明品質。
- 本次閱讀 source、history、release、issues 與 license，但沒有 build、安裝或執行上游程式。
- License 標示只記錄上游條款，不構成法律意見；若未來要複製實作而不只是參考概念，必須另做
  provenance、notice 與相容性 review。
- 上游 README 的相容性矩陣是其作者聲明；Media Lock support claim 仍只能由自己的 named
  environment 實機矩陣建立。

## 8. Primary sources

[ms-session-api]: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession?view=winrt-26100
[chromium-smc-overview]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/content/browser/media/system_media_controls/README.md
[chromium-media-playback]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/services/media_session/controlling_media_playback.md
[chromium-media-session]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/content/public/browser/media_session.h

[wmc-repo]: https://github.com/DubyaDude/WindowsMediaController
[wmc-readme]: https://github.com/DubyaDude/WindowsMediaController/blob/4933f150fe3dde9447e4efeda1ab6681d65cce03/README.md
[wmc-main]: https://github.com/DubyaDude/WindowsMediaController/blob/4933f150fe3dde9447e4efeda1ab6681d65cce03/WindowsMediaController/Main.cs
[wmc-dictionary]: https://github.com/DubyaDude/WindowsMediaController/blob/4933f150fe3dde9447e4efeda1ab6681d65cce03/WindowsMediaController/Main.cs#L47-L61
[wmc-sample]: https://github.com/DubyaDude/WindowsMediaController/blob/4933f150fe3dde9447e4efeda1ab6681d65cce03/Sample.UI/MainWindow.xaml.cs
[wmc-nuget]: https://www.nuget.org/packages/Dubya.WindowsMediaController/2.5.6
[wmc-release]: https://github.com/DubyaDude/WindowsMediaController/releases/tag/2.5.6
[wmc-latest-commit]: https://github.com/DubyaDude/WindowsMediaController/commit/4933f150fe3dde9447e4efeda1ab6681d65cce03
[wmc-license]: https://github.com/DubyaDude/WindowsMediaController/blob/4933f150fe3dde9447e4efeda1ab6681d65cce03/LICENSE
[wmc-issue-18]: https://github.com/DubyaDude/WindowsMediaController/issues/18
[wmc-issue-6]: https://github.com/DubyaDude/WindowsMediaController/issues/6

[mce-repo]: https://github.com/jiripolasek/MediaControlsExtension
[mce-readme]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/readme.md
[mce-compat]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/docs/user/GSMTC-Compatibility.md
[mce-commands]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension.Media/Infrastructure/Gsmtc/GsmtcBackend.cs#L892-L951
[mce-artwork]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension.Media/Infrastructure/Gsmtc/GsmtcBackend.cs#L387-L418
[mce-reconcile]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension.Media/Infrastructure/Gsmtc/GsmtcBackend.cs#L1031-L1227
[mce-retention]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension.Media/Infrastructure/Gsmtc/AdaptiveSessionRetentionPolicy.cs
[mce-focus]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension/Commands/BringAssociatedAppToFrontCommand.cs
[mce-volume]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/src/MediaControlsExtension/Services/SystemVolumeService.cs
[mce-tests]: https://github.com/jiripolasek/MediaControlsExtension/tree/28ae8f540436572f2fd6e48c062d9c06e788f9e8/tests/MediaControlsExtension.Media.Tests
[mce-release]: https://github.com/jiripolasek/MediaControlsExtension/releases/tag/v0.22.0
[mce-latest-commit]: https://github.com/jiripolasek/MediaControlsExtension/commit/28ae8f540436572f2fd6e48c062d9c06e788f9e8
[mce-license]: https://github.com/jiripolasek/MediaControlsExtension/blob/28ae8f540436572f2fd6e48c062d9c06e788f9e8/LICENSE.txt
[mce-issue-10]: https://github.com/jiripolasek/MediaControlsExtension/issues/10
[mce-issue-5]: https://github.com/jiripolasek/MediaControlsExtension/issues/5

[ntmc-repo]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller
[ntmc-readme]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/README.md
[ntmc-chromium-claim]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/README.md#compatibility
[ntmc-claim-commit]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/commit/4ede546bc6aa8224f160861046e6d95b863ccda2
[ntmc-source]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/native-taskbar-media-controller.wh.cpp
[ntmc-cycle]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/native-taskbar-media-controller.wh.cpp#L1353-L1372
[ntmc-refresh]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/native-taskbar-media-controller.wh.cpp#L2581-L2645
[ntmc-tag]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/tree/v1.5.0
[ntmc-latest-commit]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/commit/f24c4a4041a01251b9021a08847ae08749b4224d
[ntmc-license]: https://github.com/StarlightDaemon/Native-Taskbar-Media-Controller/blob/f24c4a4041a01251b9021a08847ae08749b4224d/LICENSE

[ahk-media]: https://github.com/Descolada/AHK-v2-libraries/blob/b969d5152541fd47d598293330a2fced6d21fc8d/Lib/Media.ahk
[ahk-wait]: https://github.com/Descolada/AHK-v2-libraries/blob/b969d5152541fd47d598293330a2fced6d21fc8d/Lib/Media.ahk#L539-L553
[ahk-history]: https://github.com/Descolada/AHK-v2-libraries/commits/main/Lib/Media.ahk
[ahk-license]: https://github.com/Descolada/AHK-v2-libraries/blob/b969d5152541fd47d598293330a2fced6d21fc8d/LICENSE

[phase0-manual]: ../phase-0/manual-test.md
[phase0-chrome-log]: ../phase-0/evidence/2026-08-22-chrome-redacted.log
