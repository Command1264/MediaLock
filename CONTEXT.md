# Media Lock

Media Lock 的領域是選擇、鎖定、恢復及控制 Windows 媒體工作階段，使實體媒體鍵具有可預期的
控制目標。

## Language

**Media Session**:
一個由支援 System Media Transport Controls 的來源應用程式公開、可觀察且可能可遠端控制的播放工作階段。
_Avoid_: Player instance, tab

**Windows Current Session**:
Windows 目前判定最適合接收媒體控制的 Media Session；它可以隨系統活動而改變。
_Avoid_: Active player, default player

**Routing Mode**:
決定 Media Lock 如何選擇控制目標的使用者策略；正式值為 Windows Auto、App Lock 與 Session Lock。
_Avoid_: Control mode, lock type

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
