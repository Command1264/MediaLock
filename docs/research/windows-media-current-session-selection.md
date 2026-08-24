# Windows media Current Session selection and ordering

查核日期：2026-08-25

## 1. 問題與來源分級

本研究回答三個不同問題：

1. Media Lock 能否用受支援 API 建立一個完整的自有 SMTC Session？
2. Media Lock 能否要求 Windows 把該 Session 設為 `Current Session`，或保證它出現在原生媒體 UI 的
   第一個位置？
3. 若公開 API 做不到，是否存在 private／undocumented 介面，以及其風險是否可接受？

以下標記避免把實作細節誤當成平台契約：

- **Supported contract**：Microsoft Learn、Windows SDK header／metadata 明確公開的能力。
- **Observable but unspecified**：可以量測，但 Microsoft 沒有承諾排序或因果關係。
- **Private／undocumented**：Windows 內部介面或逆向結果，不在公開 SDK contract。
- **Inference**：由來源得出的有限推論，仍須在指定 Windows build 實測。

## 2. 結論摘要

| 問題 | 結論 | 信心／限制 |
| --- | --- | --- |
| 建立 Media Lock-owned Session | **可以**。Desktop app 可透過 `ISystemMediaTransportControlsInterop.GetForWindow` 取得 SMTC，發布 metadata、artwork、狀態、timeline、capabilities，並接收 button／seek 等 request。 | Supported contract |
| 公開 API 指定 Current Session | **不可以**。GSMTC Manager 只有 `GetCurrentSession`、`GetSessions`、`RequestAsync` 與兩個 change events；沒有 setter 或 priority。 | Supported contract，Windows SDK 26100 metadata 亦一致 |
| `GetSessions()[0]` 是否等於原生 UI 第一個／Current Session | **沒有契約**。`GetSessions` 只承諾「所有可用 Sessions」；順序、穩定性及與 Current Session 的關係均未定義。 | Observable but unspecified |
| 讓 Windows 永遠選 Media Lock | **受支援 API 無法保證**。完整、正確的 SMTC publication 可能影響 Windows 的內部選擇，但任何「永遠第一」結論都只能是 build-specific evidence。 | 必須由 Phase 11B probe 量測 |
| Windows 內部是否有 setter | **有逆向證據**。NPSMLib 的 private `INowPlayingSessionManager` 宣告包含 `SetCurrentSession`、`SetCurrentNextSession`，`AddSession` 也有 `fMarkAsCurrentSession`。 | Private／undocumented，不建議產品採用 |
| Windows 11 24H2 build 26100 | 公開 GSMTC API 明確列為適用；private NPSM **沒有 26100 相容保證**。現有 wrapper 最後註明驗證到 build 20279，repository 最後 commit 是 2022 年。 | 公開路徑可 probe；private 路徑只能另做 Sandbox 實驗 |

**Recommendation**：Phase 11B 仍應先用 documented SMTC mirror 完成完整度與選擇行為量測；判定依據是
`GetCurrentSession()` 與原生 UI 的實際結果，而不是 `GetSessions()` index。不要把 NPSMLib 或 private COM
帶進 production dependency。若產品真的要求強制 Current Session，應另開一個明確標為不受支援、只在
Windows Sandbox 執行的可拋棄實驗，再決定是否直接記錄為平台限制。

## 3. 公開、受支援的能力

### 3.1 GSMTC Manager 只能觀察 Current Session

Microsoft 把 `GetCurrentSession()` 定義為取得「系統認為使用者最可能想控制」的 Session；
`CurrentSessionChanged` 只通知該判斷已改變。Manager 的公開方法只有 `RequestAsync`、
`GetCurrentSession`、`GetSessions`，沒有 `SetCurrentSession`、priority 或 reorder。
[Manager API][manager-api] [GetCurrentSession source][get-current-source]

Windows SDK 10.0.26100.0 的本機 metadata 也只投影上述三個方法與兩個 events；desktop interop header
`SystemMediaTransportControlsInterop.h` 則只有 `GetForWindow(HWND, REFIID, void**)`。這是針對 build
26100 的直接 SDK 檢查，不只是文件搜尋。[desktop interop][desktop-interop]

Microsoft 的 Raymond Chen 進一步說明：Current Session 是硬體媒體控制按鍵會套用的 app。這個說法證明
Current Session 的系統意義，但同樣只展示讀取與控制，沒有指定它的方法。
[The Old New Thing][old-new-thing]

### 3.2 `GetSessions` 沒有順序契約

`GetSessions()` 的全部描述是「取得所有可用 Sessions」，回傳 `IVectorView`／`IReadOnlyList`；沒有任何
first、priority、current-first、playing-first、recently-used 或 stable-order 語意。
[GetSessions][get-sessions]

因此：

- `GetSessions()[0] == GetCurrentSession()` 偶爾成立也只是觀察，不可成為產品邏輯。
- list index 在 Session connect／disconnect、browser/PWA recreation、lock/unlock 或 sleep/resume 後都必須
  視為可變。
- Phase 11B 應同時記錄 enumeration order 與 Current Session identity，但兩者要分欄判讀。

### 3.3 可以做出多完整的 mirror

公開 SMTC 足以支援本 probe 所需的「完整鏡像」：

- `IsEnabled` 決定 app 的 controls 是否顯示。
- `PlaybackStatus` 發布 Playing／Paused／Stopped／Closed／Changing。
- `DisplayUpdater.Update()` 發布 title、artist、album、media type 與 thumbnail。
- `UpdateTimelineProperties` 發布 start、end、position、seek range；Microsoft 建議播放時約每五秒及狀態
  改變時更新。
- button capability properties 配合 `ButtonPressed` 接收 Play／Pause／Next／Previous／Stop。
- `PlaybackPositionChangeRequested`、repeat、shuffle、playback-rate request 可以接收對應使用者操作。

以上皆可由 Microsoft 的 manual SMTC guide 與 API surface 確認；它們描述的是 publication 與 interaction，
沒有任何一項宣稱提高 Current Session priority。[manual SMTC][manual-smtc]

Chromium 的 Windows implementation 也是相同模式：用 `GetForWindow` 取得 controls，設定 `IsEnabled`、
capabilities、PlaybackStatus、metadata、thumbnail 與 timeline，再把 button／seek callback 回送 browser；
沒有額外的公開 ranking API。Chromium 的 ordinary browser singleton 跟隨 browser 內最近 active media，
而 dPWA 自 M130 起可擁有獨立 instance；這是 Chromium 自己的 aggregation，不是 Windows 全域 Current
Session 規則。[Chromium Windows source][chromium-win] [Chromium SMC overview][chromium-overview]

Firefox 的實作另記錄了 SMTC cleanup 的 Windows 行為問題：若 metadata/button 修改與 disable 沒在同一
main-thread task 依序完成，殘留 Session 可能只顯示 executable name。這支持 Phase 11B 必須驗證 teardown，
但不提供 Current Session setter 或排序規則。[Firefox source][firefox-source]

## 4. 哪些 activity 可能影響 Current Session

Microsoft 沒有公開 selection algorithm。下表將「必要 publication」與「可能被內部 heuristic 使用」分開：

| Activity | 公開契約 | 對 Current Session 的可接受結論 |
| --- | --- | --- |
| `IsEnabled = true` | 讓該 app 的 SMTC 顯示／可用 | **必要 eligibility**；不保證成為 Current |
| `PlaybackStatus = Playing` | 告知 system 目前播放狀態 | **最值得量測的候選 signal**；仍無 priority 契約 |
| metadata／artwork `Update()` | 更新原生 UI 顯示資料 | presentation only；不可宣稱提高 priority |
| timeline update | 更新 position、duration、seek range | presentation／interaction；不可宣稱提高 priority |
| 啟用 buttons、註冊 handlers | 決定可見控制與接收事件 | capability only；不可宣稱提高 priority |
| 真實 audio playback | manual SMTC 可代表一或多個外部／受控 player，API 沒要求 SMTC owner 自己輸出 audio | 不可為了搶 priority 播放 silent audio；沒有保證且會增加 audio-session 副作用 |
| foreground／active HWND | Desktop interop 只把 SMTC 綁到指定 window | 應量測，但沒有 foreground-wins 契約 |
| 反覆切換 status、刷新 metadata | 不是 documented selection API | 不應作為「喚醒」hack；可能閃動、洗掉使用者選擇並隨 build 失效 |
| 使用者在 native carousel 選另一 Session | Windows 可改變 Current Session | Media Lock 應觀察並記錄，不應假設 mirror 能永久覆蓋使用者意圖 |

**Inference**：Playing、最近 activity、foreground 或 user interaction 很可能參與 Windows 的內部 heuristic，
但現有 primary sources 無法證明權重、優先順序或持續時間。只有 controlled experiment 能回答 build 26100
上的實際行為，且該結果不會自動成為 Windows 10／11 全版本承諾。

## 5. Private `INowPlayingSessionManager`

### 5.1 找到的能力

ModernFlyouts 的作者說明其早期 GSMTC 路徑遇到 Windows bugs 後，改用 Windows 內部
`INowPlayingSessionManager`（INPSM）；該專案明確稱它為 private API、指出 Windows 曾在不同 build
改動介面，並引用 ADeltaX 的 NPSMLib wrapper。[ModernFlyouts release notes][modernflyouts-release]

NPSMLib source 提供更直接的逆向證據：

- `CLSID_NowPlayingSessionManager = BCBB9860-C012-4AD7-A938-6E337AE6ABA5`。
- 19041+ interface IID 為 `3b6a7908-ce07-4ba9-878c-6e4a15db5e5b`。
- vtable 包含 `SetCurrentSession(INowPlayingSessionInfo)`、`SetCurrentNextSession()`。
- `AddSession(...)` 有 `fMarkAsCurrentSession` 參數。
- wrapper 公開 `SetCurrentSession` 與 `SetNextCurrentSession`。

[NPSMLib manager][npsm-manager] [NPSMLib interop][npsm-interop]

這表示「Windows 內部完全沒有設定 Current Session 的能力」是不正確的；正確說法是：

> Windows 公開、受支援的 GSMTC contract 沒有 setter；逆向出的 private NPSM 有 setter。

### 5.2 為何仍不建議產品使用

- private CLSID／IID／vtable 不在 Windows SDK metadata 或公開 headers，Microsoft 可在任何 servicing／feature
  update 改變或移除。
- NPSMLib source 只註明驗證到 build 20279；沒有 22000、22621 或 26100 的相容證據。
- repository 最後 commit 是 2022-12-30，且仍有 QueryInterface GUID unavailable 的 open crash issue。
  [latest commit][npsm-latest] [NPSMLib issue 3][npsm-issue-3]
- `SetCurrentSession` 會改變 system-wide user state，可能與 native carousel、其他 players 或 Windows 自身
  heuristic 競爭；持續重設會破壞使用者選擇。
- COM vtable 猜錯不是一般的 feature failure：可能造成 access violation、process crash 或 OS update 後
  啟動失敗；需要每個 architecture/build 的相容矩陣與 fail-closed isolation。
- 即使 HRESULT 成功，也必須另外確認原生 Windows 11 UI 已選中目標；private call success 仍不等於完整 UX
  成功。

### 5.3 License

NPSMLib 是 MIT；Chromium Windows SMTC source 是 BSD-style；Firefox 是 MPL-2.0；ModernFlyouts 是 MIT。
本研究只引用行為與介面，不複製程式碼。若未來採用任何 source，仍須逐檔保留 attribution／license 並做
dependency 與 notice review。[NPSMLib license][npsm-license] [Chromium source header][chromium-win]

License 寬鬆不會把 private Windows API 變成 supported API，也不會提供 build 相容保證。

## 6. Windows 11 24H2 build 26100 的判讀

公開 `Windows.Media.Control` 文件明確把 build 26100 列在適用版本中；本機安裝的 Windows SDK
10.0.26100.0 header／metadata 也包含既有 manager、Session、SMTC 與 desktop interop surface。
[Manager API][manager-api]

但 UBR（例如 26100.9168）是 servicing revision，不代表 undocumented COM vtable 維持不變。對 private
NPSM，目前找到的來源不足以把「19041+」標註提升為 26100 guarantee。若另做實驗，報告必須精確記錄：

- `DisplayVersion`、`CurrentBuild.UBR`、architecture。
- `CoCreateInstance` 與 QueryInterface HRESULT。
- read-only enumeration 是否成功。
- `SetCurrentSession` HRESULT、`GetCurrentSession` identity 與原生 UI identity 三者是否一致。
- lock/unlock、sleep/resume、Explorer restart、Session recreation 後是否仍一致。

## 7. Phase 11B 建議驗證矩陣

### 7.0 Sandbox-only private compatibility result

2026-08-25 在 Windows Sandbox 24H2 build 26100.9168 x64 執行獨立、unsigned、self-contained probe；
private dependency 只存在於 `experiments/Phase11B.PrivateCurrentSessionProbe`，沒有加入正式 solution 或產品。

| Observation | Result |
| --- | --- |
| Documented desktop SMTC publication | 成功；public GSMTC 列出 probe |
| Private NPSM enumeration | 成功；辨識 probe PID 7028、HWND `0x10238` |
| Precondition | private Session PID 與目前 process PID 相同，才開放 setter |
| `SetCurrentSession` calls | **恰好一次** |
| Private return value | `True` |
| Public current after call | `MediaLock.PrivateCurrentSessionProbe.exe` |
| Private current after call | PID 7028，仍為 probe |
| Crash／exception | 無 |
| Cleanup | 由 probe 設為 Closed、清除 display、停用 SMTC 後退出 |

此結果把 build 26100 的 private ABI 判定由「未知」提升為「單一 Sandbox build 上可呼叫」，但**沒有證明競爭
選擇能力**：當時只有 probe 一個媒體 Session，所以它在 setter 前已自然是 Current。也沒有證明原生媒體 UI
排序、lock screen、sleep/resume 或其他 servicing build 的相容性。因此產品決策維持不採用 private API；若要
驗證競爭切換，必須另行核准第二個、包含 competing Session 的一次性 Sandbox 實驗。

### 7.1 Documented mirror（本階段建議執行）

1. 建立 Media Lock-owned SMTC，正確發布 metadata、artwork、status、timeline、capabilities。
2. 在每個 observation point 同時記錄：
   - `GetCurrentSession().SourceAppUserModelId`；
   - `GetSessions()` 完整順序；
   - mirror index；
   - Windows 原生 media surface 顯示中的選取 Session。
3. 依序測試 mirror enable、Paused、Playing、metadata change、timeline update、foreground/background、
   competing source start/pause、native carousel manual switch。
4. 重複測試 target change、YouTube Music reload／Recovery、lock/unlock、sleep/resume、Explorer restart、Exit。
5. 每個 native button／seek event 捕捉當下 routed target，只 dispatch 一次；mirror 必須在 catalog boundary
   被排除，避免自我選取與 feedback loop。
6. 判定：
   - **Proceed**：完整鏡像與 interaction 可靠；Current selection 在聲明的 build matrix 內有可接受行為。
   - **Limit**：鏡像完整但 Current／first selection 不可靠；產品明示限制，不宣稱永遠第一。
   - **Reject**：顯示、控制、teardown 或 self-exclusion 不可靠。

### 7.2 Private NPSM（不屬於正常 Phase 11B）

只有在使用者另行核准「unsupported Sandbox-only mutation experiment」後才考慮：

- 不加入 production project 或 dependency。
- 先做 read-only activation／enumeration，再單次 `SetCurrentSession`；不得 polling 或持續搶回。
- 在 disposable Windows Sandbox 測試，process crash／HRESULT failure 必須 fail closed。
- 結束時回復原 Current Session 或直接關閉 Sandbox，不把結果當成公開 API 承諾。
- 即使 26100 成功，預設產品決策仍應是 **不採用**，除非另有完整風險接受與逐 build 維護方案。

## 8. 最終建議

Phase 11B 的目標應保持為「完整、可安全卸載的 supported SMTC mirror + 可重現的 selection evidence」，而非
「用 update churn 猜 Windows heuristic」或「直接採 private setter」。完整 mirror 對 Windows 原生 UI 仍有
價值，即使它不能永遠成為第一個選項；Media Lock 已有自己的實體媒體鍵 capture／consume／route，因此
Current Session 不可靠主要限制的是 Windows 原生 surface，而不是 Media Lock 的核心 routing。

研究確實找到 private `SetCurrentSession`，所以後續文件不應再籠統說「沒有任何 API」；應精確寫成：

> 沒有受支援 API；存在逆向的 Windows private COM，但目前不符合 Media Lock 的可靠性與維護要求。

[manager-api]: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager?view=winrt-26100
[get-current-source]: https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.media.control/globalsystemmediatransportcontrolssessionmanager_getcurrentsession_374874497.md
[get-sessions]: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager.getsessions?view=winrt-26100
[desktop-interop]: https://learn.microsoft.com/en-us/windows/win32/api/systemmediatransportcontrolsinterop/nf-systemmediatransportcontrolsinterop-isystemmediatransportcontrolsinterop-getforwindow
[old-new-thing]: https://devblogs.microsoft.com/oldnewthing/20231108-00/?p=108980
[manual-smtc]: https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/system-media-transport-controls
[chromium-win]: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/components/system_media_controls/win/system_media_controls_win.cc
[chromium-overview]: https://chromium.googlesource.com/chromium/src.git/+/refs/heads/main/content/browser/media/system_media_controls/README.md
[firefox-source]: https://searchfox.org/firefox-main/source/widget/windows/WindowsSMTCProvider.cpp
[modernflyouts-release]: https://github.com/ModernFlyouts-Community/ModernFlyouts/releases/tag/v0.9.0
[npsm-manager]: https://github.com/ADeltaX/NPSMLib/blob/22616b82f9b6ffd43ecf863f89455766edf63c76/src/NPSMLib/NowPlayingSessionManager.cs
[npsm-interop]: https://github.com/ADeltaX/NPSMLib/blob/22616b82f9b6ffd43ecf863f89455766edf63c76/src/NPSMLib/Interop/COMInterop.cs
[npsm-latest]: https://github.com/ADeltaX/NPSMLib/commit/22616b82f9b6ffd43ecf863f89455766edf63c76
[npsm-issue-3]: https://github.com/ADeltaX/NPSMLib/issues/3
[npsm-license]: https://github.com/ADeltaX/NPSMLib/blob/22616b82f9b6ffd43ecf863f89455766edf63c76/LICENSE
