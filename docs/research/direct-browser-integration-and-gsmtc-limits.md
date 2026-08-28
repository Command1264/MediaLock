# Direct browser integration and GSMTC replacement limits

查核日期：2026-08-26

## 1. 問題

本研究區分三個容易混在一起的產品目標：

1. Media Lock 能否不使用來源的 GSMTC Session，直接控制指定 YouTube／YouTube Music？
2. Media Lock 能否阻止其他應用程式向 Windows 發布 GSMTC Session？
3. Media Lock 能否發布統一的 Windows 媒體卡片，並保證 Windows 只顯示或永遠選擇該卡片？

結論是：第一項可用瀏覽器 Extension + Native Messaging 驗證；第二項沒有受支援的跨程式 Windows
能力；第三項可以發布完整 mirror，但公開 API 無法保證 Current Session 或唯一顯示。

## 2. 能力矩陣

| 目標 | 判定 | 支援邊界 |
| --- | --- | --- |
| 直接控制指定 YouTube／YouTube Music | **Proceed to prototype** | Extension 對已授權站點執行腳本，Native Messaging 連接 Media Lock |
| 取得 URL／tab／PWA 精確身分 | **Proceed to prototype** | 比 GSMTC source identity 精確；Brave PWA 必須列入實機矩陣 |
| Media Lock 發布完整 SMTC mirror | **Supported but limited** | metadata、status、timeline、button／seek request 可支援；Phase 11B 已判定 Limit |
| 從 Media Lock 關閉其他 process 的 SMTC | **Not supported** | 公開 Windows API 沒有 hide、remove 或 disable-other-session |
| 強制 Media Lock 成為 Windows Current Session | **Not supported** | GSMTC Manager 只有 getter、enumeration 與 change events，沒有 public setter |
| 受控 Chromium 看起來只剩 Media Lock | **Experimental** | 可能依賴 browser-wide feature flag、重啟及個別版本實測；不是 Extension contract |

## 3. 直接瀏覽器控制

Chrome Extension 的 content script 可以在匹配的網站讀取或修改頁面，`chrome.scripting` 可在取得
`scripting` 與 host permission 後注入程式。Extension service worker／page 可透過
`runtime.connectNative()` 或 `runtime.sendNativeMessage()` 與本機程式交換訊息；content script 必須先把
訊息轉送給 Extension context。[Content scripts][content-scripts] [Scripting][scripting]
[Native Messaging][native-messaging]

建議的第一個可拋棄 Prototype：

```text
YouTube／YouTube Music page
        ↕ validated messages
Browser Extension
        ↕ Native Messaging JSON
Media Lock native host
        ↕ neutral target adapter
Existing Router
```

Play、Pause、Seek 與狀態觀察應優先使用標準 `HTMLMediaElement` 能力。`play()` 回傳 Promise，可能因
autoplay／user-activation policy 或其他錯誤而拒絕；Adapter 必須等待結果，不能只因呼叫成功就回報命令
accepted。[HTMLMediaElement play Promise][play-promise]

Next／Previous 沒有通用 HTML media element 命令，且網頁沒有標準方法讀取或呼叫網站已註冊的
`MediaSession` action handler。因此 YouTube 與 YouTube Music 需要各自的網站 Adapter。任何 DOM selector
或網站私有 JavaScript 介面都應視為不穩定 Implementation：必須做版本／能力偵測、明確錯誤與
GSMTC fallback，不可在失效時猜測另一個按鈕或分頁。

Chrome DevTools Protocol 不是建議的預設 Seam。`chrome.debugger` 能附加 tab 並以 CDP
`Runtime.evaluate` 執行頁面程式，但其權限廣、可能與 DevTools／其他 debugger 衝突；CDP `Media`
domain 本身偏向觀察而非提供完整 transport control。[chrome.debugger][chrome-debugger]
[CDP Runtime][cdp-runtime] [CDP Media][cdp-media]

## 4. 為何不能攔截其他應用程式的 GSMTC publication

`GlobalSystemMediaTransportControlsSessionManager` 的公開 surface 提供 `RequestAsync`、
`GetCurrentSession`、`GetSessions`、`CurrentSessionChanged` 與 `SessionsChanged`。它能觀察及遙控已發布的
Sessions，但沒有 hide、remove、disable 或 priority mutation。[GSMTC Manager][gsmtc-manager]

`SystemMediaTransportControls.IsEnabled` 控制的是呼叫端自己的 SMTC instance，不是另一個 process 已發布
的 Session。[SMTC IsEnabled][smtc-enabled]

因此 Media Lock 可以：

- 攔截、consume 並重新 route 支援的實體媒體鍵；
- 列舉與控制其他應用程式自願發布的 GSMTC Session；
- 發布及停用自己的 SMTC mirror。

Media Lock 不可以透過受支援 API：

- 阻止 Brave、Chrome、Spotify、VLC 或其他 process 建立 Session；
- 從 Windows 的 Session 集合刪除另一個 process；
- 把另一個 process 的 `IsEnabled` 設為 `false`。

Process injection、Runtime／COM hook、Windows shell replacement 或 private Current Session setter 都不是此
方向的 production 選項。它們會擴張權限與維護矩陣，也不會形成 Microsoft 支援的契約。

## 5. Chromium controlled-browser option

Chromium source 定義預設啟用的 `HardwareMediaKeyHandling` feature；Chromium 的 Windows
`SystemMediaControlsWin` 實作會建立自己的 SMTC、發布 metadata／status／timeline，並把 Windows button
與 seek request 通知 observers。[Chromium media switches][chromium-switches]
[Chromium Windows controls][chromium-windows-controls]

由此可提出但尚未驗證的 **Inference**：以 Chromium `--disable-features=HardwareMediaKeyHandling` 啟動
特定 browser process，可能使該 process 不再參與原生硬體媒體鍵／SMTC 整合，然後由 Extension 直接控制
網頁、Media Lock 發布 mirror。這不能直接升格成產品承諾，因為：

- feature flag 不是 Chrome Extension API 或穩定使用者設定；
- 需要重啟並影響整個 browser process，不是單一 tab；
- Chrome／Brave／Edge 版本可能有不同結果；
- Media Lock 不能安全改寫所有既有瀏覽器捷徑與啟動路徑；
- 它無法停用其他桌面程式的 Session。

只有在指定 browser/version 上驗證啟動、Session absence、direct commands、browser update、rollback 與
使用者可逆操作後，才能提供明確標示的 controlled-browser experimental option。

## 6. Windows projection remains separate

Desktop application 可以透過 `ISystemMediaTransportControlsInterop.GetForWindow()` 取得自己的 SMTC，並
發布 controls、metadata、playback status、timeline，以及接收 buttons 與 playback-position requests。
[Desktop SMTC interop][desktop-interop] [Manual SMTC integration][manual-smtc]

這個 mirror 是 Windows **呈現 Adapter**，不是 routing identity 或 direct-control transport。Windows 公開
GSMTC Manager 沒有 `SetCurrentSession`，也沒有 Session ordering contract。Phase 11B 已實測 mirror 可被
Windows 顯示並正確路由部分操作，但 Win+A、lock/unlock 與 sleep/resume 無法可靠保留它為 Current
Session。因此後續直接瀏覽器控制成功也不會推翻 Phase 11B 的 Limit 判定。

## 7. 建議的模組 Seam

直接瀏覽器 Adapter 與 GSMTC Adapter 應是同一個中立目標 Interface 後的 peer Implementations；不應把
Extension、URL 或 DOM 規則放入 GSMTC Adapter。Windows SMTC mirror 則位於獨立的 projection Seam：

```text
Target discovery/control
├─ GSMTC Adapter
└─ Browser Direct Adapter
   ├─ YouTube Adapter
   ├─ YouTube Music Adapter
   └─ Generic HTML Media Adapter (capability-limited)

Routing domain
└─ target identity, capabilities, Recovery, expected-target and exactly-once command semantics

Windows projection
└─ optional Media Lock-owned SMTC mirror
```

此設計的 Leverage 是讓 Router、Priority Rules、Recovery、Playback State Lock 與實體媒體鍵繼續使用同一套
語意；browser/site churn 保持在一個可替換 Adapter 內。正式 Interface 與 identity migration 必須在
Prototype 證明可行後另做 ADR，不能預先把現有 `Media Session`／`Session Fingerprint` 偷換成 URL identity。

## 8. Prototype evidence gates

### Gate A — Direct control

- Chrome ordinary tab、Brave ordinary tab、Brave installed YouTube Music PWA 各自辨識。
- Play、Pause、Seek、Next、Previous、metadata、artwork、status、timeline。
- 前景競爭來源、navigation、Ctrl+R、換曲、browser restart、Extension reload、native-host disconnect。
- 每個 command exactly once；失效時不誤控其他 tab，並有可觀察 fallback。

### Gate B — Source-native SMTC suppression

- Extension-only configuration 是否仍發布 Chromium Session。
- Controlled-browser flag 是否真的移除指定 process Session。
- browser restart、update、shortcut path、multiple profiles／windows 與 rollback。
- 只記錄指定 browser/version 結果，不外推到其他 application。

### Gate C — Media Lock mirror

- 使用 Phase 11B 既有矩陣分開記錄 mirror existence、Current Session 與原生 UI selection。
- metadata、artwork、status、timeline、button／seek、Recovery、lock/unlock、sleep/resume、Exit cleanup。
- 不以 mirror 可見或 action 可路由推論 Windows 只顯示 Media Lock。

Gate A 可單獨產生有價值的 browser-direct routing。Gate B 或 C 失敗不得阻止 GSMTC-only fallback，也不得
被文案隱藏為「已取代 GSMTC」。

## 9. Recommendation

將方向命名為 **Direct Browser Integration**，而不是 **Replace GSMTC**：

- Media Lock 成為使用者的控制中心；
- 支援網站可繞過來源 GSMTC，直接路由到精確 tab／PWA；
- GSMTC 保留為桌面 application 與未安裝 Extension 時的 universal fallback；
- Media Lock-owned SMTC mirror 只作為 best-effort Windows projection；
- 「Windows 只剩 Media Lock 一張卡片」只有在每個來源自行停用 publication 且指定環境通過 Gate B／C
  時才可描述，不能成為全域產品保證。

[content-scripts]: https://developer.chrome.com/docs/extensions/develop/concepts/content-scripts
[scripting]: https://developer.chrome.com/docs/extensions/reference/api/scripting
[native-messaging]: https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
[play-promise]: https://developer.chrome.com/blog/play-returns-promise
[chrome-debugger]: https://developer.chrome.com/docs/extensions/reference/api/debugger
[cdp-runtime]: https://chromedevtools.github.io/devtools-protocol/tot/Runtime/
[cdp-media]: https://chromedevtools.github.io/devtools-protocol/tot/Media/
[gsmtc-manager]: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager?view=winrt-26100
[smtc-enabled]: https://learn.microsoft.com/en-us/uwp/api/windows.media.systemmediatransportcontrols.isenabled?view=winrt-26100
[chromium-switches]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/media/base/media_switches.cc
[chromium-windows-controls]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/components/system_media_controls/win/system_media_controls_win.cc
[desktop-interop]: https://learn.microsoft.com/en-us/windows/win32/api/systemmediatransportcontrolsinterop/nf-systemmediatransportcontrolsinterop-isystemmediatransportcontrolsinterop-getforwindow
[manual-smtc]: https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/system-media-transport-controls
