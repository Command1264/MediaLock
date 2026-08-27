# Media Lock

Media Lock 的領域是選擇、鎖定、恢復及控制 Windows 媒體工作階段，使實體媒體鍵具有可預期的
控制目標。

## Language

**Media Session**:
一個由支援 System Media Transport Controls 的來源應用程式公開、可觀察且可能可遠端控制的播放工作階段。
_Avoid_: Player instance, tab

**Media Target**:
Router 在單次 Route Decision 中可解析並控制的 provider-qualified 目標；目前可由 Media Session 或
Browser Media Target 提供。它不是需要跨失效狀態保存的 Locked Target。
_Avoid_: Player, Locked Target, generic Media Session

**Authoritative Media Target Correlation**:
provider 對一個 Browser Media Target 與一個 GSMTC Media Target 是同一播放來源所提出的精確證據。
只有此證據可以在可見 target projection 中隱藏該 GSMTC duplicate；瀏覽器 executable、title、URL、
origin 相似、tab order 與 track metadata 都不是 correlation evidence。
_Avoid_: title match, browser deduplication, heuristic correlation

**Browser Media Target**:
使用者明確授權、可由 Browser Adapter 解析成特定網頁播放端點的頁面級媒體目標；它不等於瀏覽器程序、
網站名稱或當前分頁標題。
_Avoid_: Browser Session, Chrome target, website player

**Page Binding**:
Browser Media Target 跨文件重建時保持的邏輯頁面身分；只有同一 binding 的後繼文件可以延續鎖定或規則。
_Avoid_: Tab ID, URL match, page title

**Browser Media Endpoint**:
Browser Media Target 在目前文件與 frame 中實際接收命令的短生命週期播放端點；重新整理或導覽會使它失效。
_Avoid_: Browser Media Target, saved tab

**Browser Application Scope**:
由特定瀏覽器設定檔與網站／已安裝 Web App 身分共同界定的應用程式範圍；它不能只由 Chrome、Brave 等
瀏覽器程序名稱表示。
_Avoid_: Browser executable, all browser tabs

**Windows Current Session**:
Windows 目前判定最適合接收媒體控制的 Media Session；它可以隨系統活動而改變。
_Avoid_: Active player, default player

**Routing Mode**:
決定 Media Lock 如何選擇控制目標的使用者策略；正式值為 Windows Auto、Priority Rules、App Lock 與 Session Lock。
_Avoid_: Control mode, lock type

**Priority Rule**:
Priority Rules 中一條可啟用、具順序的來源應用程式偏好；第一條目前可解析的偏好具有最高優先權。
_Avoid_: Filter, automatic lock

**Priority Rules**:
依 Priority Rule 順序選擇控制目標的 Routing Mode；沒有規則可解析時使用 Windows Current Session。
_Avoid_: Smart mode, rule engine

**App Lock**:
將來源應用程式身分保存為 Locked Target，並在該應用程式目前可用的 Media Sessions 中依明確候選政策選擇控制目標；它不承諾歌曲、瀏覽器 URL 或單一 Session 的連續性。
_Avoid_: Browser lock, tab lock

**Locked Target**:
在 App Lock 或 Session Lock 下，Media Lock 承諾優先控制的目標描述。
_Avoid_: Selected player, current player

**Session Fingerprint**:
用於在 Media Session 物件失效後辨識其可能後繼者的一組穩定與輔助特徵；它不是歌曲名稱或物件參考。
_Avoid_: Session ID, track identity

**Recovery**:
Locked Target 暫時無法解析成現存 Media Session 時，觀察候選者並嘗試重新建立鎖定的過程。
_Avoid_: Retry, reconnect loop

**Fallback Policy**:
Recovery 未立即成功時，決定等待、採用同應用程式、使用 Windows Current Session 或停用路由的策略。
_Avoid_: Error handling, backup player

**Route Decision**:
對一個媒體命令及當前狀態計算出的單次結果，包含目標、理由或明確不處理。
_Avoid_: Selection, dispatch

**Media Command**:
使用者意圖執行的播放、暫停、切換播放狀態、上一首、下一首、停止或未來支援的定位操作。
_Avoid_: Key press, hotkey

**Media Command Outcome**:
Adapter 對單次 Media Command dispatch 回報的 one-shot 結果；正式值為 Succeeded、Unsupported、
Rejected、Failed 與 Outcome Unknown。Outcome Unknown 表示命令可能已跨越 provider boundary，不得重送。
_Avoid_: retry status, transport response

**Playback State Lock**:
針對當前控制目標提供單向播放保護的執行期政策；正式值為 Off 與 Keep Playing。Keep Playing 只能在
目標已播放時啟用，並以明確 Play 修正外部造成的 Paused；它不維持 Paused，也不重啟 Stopped 或 Closed。
Windows 鎖定畫面上的 Pause 或 Stop 是明確的人為覆寫，會關閉 Keep Playing，而不是被自動復原。
可設定的 Repeated Pause Override 只計算 Armed Playback Target 明確的 Playing → Paused 轉換；在指定時間內
達到次數門檻時會關閉 Keep Playing、保留暫停並提示使用者。Changing、Recovery、目標變更與重複的
Paused 通知不是新的暫停意圖。
真正的 Windows Power Suspend 是安全邊界：它會關閉 Keep Playing，喚醒後不自動重新播放。單純的
Workstation Lock／Unlock 不會關閉保護，除非鎖定畫面觀察到明確 Pause、Stop 或 Closed。
_Avoid_: Playback mode, forced toggle, autoplay

**Armed Playback Target**:
Keep Playing 啟用時捕捉的控制目標。只有同一目標或 Router 接受的鎖定目標後繼者可以接收修正；競爭
Session、fallback 目標與單純的 UI 選取不是 Armed Playback Target。在 Windows Auto 或 Priority Rules
下，Armed Playback Target 暫時從 catalog 消失時保護進入 Suspended；只有唯一、fingerprint 可接受且
重新成為 Router Active Target 的後繼者可恢復保護。原目標仍存在時的 Active Target 變更則關閉保護。
_Avoid_: Selected Session, Windows Current Session

**Windows Media Surface Mirror**:
由 Media Lock 公開、鏡像當前控制目標資料與控制能力的自有 SMTC Media Session。它可讓 Windows 媒體
表面顯示及操作 Media Lock 的路由目標，但不代表 Media Lock 能指定 Windows Current Session。
_Avoid_: Windows Current Session override, native panel takeover
