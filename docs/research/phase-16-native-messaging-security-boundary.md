# Phase 16 Native Messaging security boundary

查核日期：2026-08-26

## 1. Scope and decision

本研究只處理 disposable Direct Browser Integration Prototype 的安全邊界：Chrome／Brave Extension 從
YouTube 或 YouTube Music content script 接收狀態，經由 Extension service worker 與 Native Messaging host
交換命令，再交由 Media Lock 使用。它不設計正式產品密鑰基礎設施，也不把網頁、content script、Extension
或同一使用者帳戶下的本機 process 視為天然可信。

建議的 Prototype 邊界是：

```text
Untrusted page / renderer
        |
        | validated Extension message
        v
Extension service worker (policy enforcement point)
        |
        | Chrome-owned Native Messaging stdio
        v
Minimal native host (command allowlist, no general execution)
        |
        | narrow in-process/application seam
        v
Media Lock routing target
```

**Decision**：Prototype 可以使用 Native Messaging，不需要自行替本機 `stdio` 通道加密；安全價值主要來自
固定 Extension ID、單一 `allowed_origins`、嚴格 sender／document binding、窄命令集合、協定版本與 nonce、
去重與 fail-closed fallback。正式版仍需要簽署／受保護的安裝內容與逐瀏覽器註冊驗證。

## 2. Platform facts

### 2.1 Native host admission

Extension 必須宣告 `nativeMessaging` permission。Content script 不能直接呼叫 `connectNative()` 或
`sendNativeMessage()`；只有 Extension page／service worker 可以，因此 service worker 是必要的權限邊界，
不能只當透明 relay。[Chrome Native Messaging][chrome-native]

Native host manifest 的 `allowed_origins` 是可連線 Extension origins 的精確清單，而且不允許 wildcard。
Chrome 啟動 host 時，第一個命令列參數通常是呼叫端 `chrome-extension://<extension-id>/` origin；host 應再做
exact comparison，尤其不可只檢查 prefix 或接受 manifest 中未列出的開發 ID。[Chrome Native
Messaging][chrome-native]

Extension ID 必須在開發、測試與正式分發間固定。Chrome 的 manifest `key` 文件明確把「讓 server 只接受
某一 Extension origin」列為固定 ID 的用途；Prototype 應使用預先決定的 public key／ID，不可每次 load
unpacked 後手工放寬 `allowed_origins`。[Manifest key][manifest-key]

正式分發時，Chrome 在 Windows／macOS 對一般使用者支援 Chrome Web Store 簽署的 Extension；自架分發只在
受管理的 enterprise policy 環境支援。Unpacked Extension 只適合載入可信的開發程式碼。因此 Prototype 的
developer-mode 安裝證據不能直接外推成一般使用者部署方案。[Extension distribution][extension-distribution]

### 2.2 Registration and executable trust

Windows 上，Chrome 從 `HKCU` 或 `HKLM` 的
`SOFTWARE\Google\Chrome\NativeMessagingHosts\<host-name>` 找到 manifest，registry default value 指向 manifest
路徑；manifest 再以 `path` 指定 executable。Chrome 先查 32-bit registry view，再查 64-bit view。這是一條
會決定「哪個 executable 被啟動」的信任鏈，installer／uninstaller 必須同時擁有並精確驗證 registry value、
manifest 與 executable path。[Chrome Native Messaging][chrome-native]

Chrome 文件只定義 Chrome／Chromium 的位置。Brave source 顯示它在 Linux／macOS 會把 Native Messaging
目錄映射到預期的 Chrome 位置，但這不構成 Windows Brave registry contract。[Brave source][brave-source]
因此 Prototype 必須在實機上驗證 Brave stable 的 exact registry lookup、Extension ID 與 host launch；不能因
Chrome 成功就宣稱 Brave 也成功。

Host path 應使用絕對路徑，即使 Windows 允許相對於 manifest 的路徑；不要依賴 current directory、`PATH`、
副檔名搜尋或可插入的 launcher script。Prototype 應放在專用目錄，拒絕 manifest path、executable path、
Extension ID 或 hash 與測試 fixture 不一致的環境。

正式產品可以用 `WinVerifyTrust` 驗證 PE 是否符合 Authenticode policy；Microsoft 說明其 software publisher
provider 可驗證 executable 來自受信任 publisher 且未被修改。[WinVerifyTrust][winverifytrust] 目前 Media Lock
仍是 unsigned，故 Prototype 必須把「同一 Windows 使用者可替換 HKCU registration／host files」列為明示的
本機信任限制，而不是假裝已有 publisher authenticity。

### 2.3 Transport and framing

Chrome 以獨立 child process 啟動 native host，透過 `stdin`／`stdout` 雙向傳輸。每個 message 是 UTF-8 JSON，
前置 native byte order 的 32-bit byte length。Host 到 Chrome 單一 message 上限是 1 MiB；Chrome 到 host 上限
是 64 MiB。Windows host 必須使用 binary I/O，否則文字模式可能把 LF 轉成 CRLF 而破壞 framing；診斷只能
寫 `stderr`，任何非協定 `stdout` 都是 protocol violation。[Chrome Native Messaging][chrome-native]

Chrome `connectNative()` 會讓 host 存活到 Port 關閉；`sendNativeMessage()` 每個 request 啟動一個新 process，
而且只使用 host 第一個 response。需要狀態、順序與 exactly-once correlation 的 Prototype 應使用單一長連線
Port，不應混用兩種 lifecycle。[Chrome Native Messaging][chrome-native]

Windows 文件指出 anonymous pipe 通常用於相關 parent／child processes，不能透過網路，也不能由不相關
process 直接開啟；handle 通常由 child inheritance 取得。[Windows IPC][windows-ipc] [Handle
inheritance][handle-inheritance] 因此一般網路 MITM、localhost port scan 或 DNS rebinding 不是此 `stdio`
通道的主要威脅。主要本機風險是：Extension／renderer 被利用、host registration 或 executable 被同一使用者
替換、以及 host 另外暴露不安全的 named pipe／socket。Prototype 不應加 localhost HTTP server。

### 2.4 Extension-side sender identity

Chrome 的 `MessageSender` 提供 `id`、`origin`、`url`、`tab`、`frameId`、`documentId` 與 document lifecycle。
`origin` 可能和 URL 不同或是不透明 origin；iframe 的 `url` 是 iframe 自己而不是 top-level page。
[chrome.runtime MessageSender][runtime-sender]

Chrome 自己明確要求把 content script 視為低信任來源：惡意頁面可能危及 renderer，所有輸入都要驗證與
清理；傳給 content script 的資料也可能洩漏給網頁，且 content script 可觸發的 privileged actions 必須限縮。
[Chrome messaging security][messaging-security]

因此 service worker 必須從 `MessageSender` 建立 authoritative identity，並覆寫 page message 內任何自稱的
`origin`、`tabId`、`frameId` 或 `documentId`。第一版只接受：

- `sender.id === chrome.runtime.id`；
- `frameId === 0`；
- exact HTTPS origin 為 `https://www.youtube.com` 或 `https://music.youtube.com`；
- `sender.tab.id` 存在，且 `sender.tab.url`／`sender.url` 符合相同 allowlist；
- `documentId` 與 service worker 目前登記的 top-level document 相同；
- navigation、reload、tab replacement、Port disconnect 後舊 document binding 立即失效。

不要開啟 `externally_connectable`；該 manifest key 會讓匹配的普通網頁直接連 Extension，而本 Prototype 已有
content script seam，不需要第二條入口。[Chrome message passing][messaging-security]

## 3. Threat matrix

| Threat | Boundary failure | Prototype control |
| --- | --- | --- |
| 惡意頁面偽造另一個 tab／target | service worker 信任 payload identity | identity 只取 `MessageSender`；top frame、exact origin、tab／document binding |
| iframe 觸發 native command | 只檢查 hostname 或 tab | `frameId === 0`；opaque／unexpected origin 一律拒絕 |
| Extension ID 漂移 | host 為了測試接受多個或 wildcard origins | 固定 manifest key／ID；`allowed_origins` 只放一個 exact origin |
| Confused deputy | 網頁請 service worker／host 執行超出媒體控制的操作 | 固定 command enum；禁止 arbitrary JS、shell、URL、path、file、registry 命令 |
| Replay／double dispatch | reconnect、retry 或重複 event 再執行 mutating command | connection nonce、單調 sequence、唯一 command ID、bounded dedupe cache；timeout 不自動重送 |
| Stale target | Ctrl+R／navigation 後命令落到新 document 或其他 tab | document ID generation binding；失效即 `TargetUnavailable`，重新 discovery／bind |
| Protocol downgrade | 一端默默接受較舊、較寬鬆 schema | exact supported-version intersection；不相容就斷線，不 silent downgrade |
| Oversized／deep JSON DoS | 依賴 Chrome 64 MiB inbound 上限 | app-level 64 KiB 上限、深度／陣列／字串／artwork 上限、讀取 timeout、bounded queue |
| Host substitution | HKCU registration、manifest 或 executable 被替換 | Prototype 明示 same-user trust；驗證 exact paths／fixture hash；正式版簽章、受保護安裝與 ownership check |
| Pipe／process lifecycle confusion | 多個 host、舊 response 或 port race 更新 active target | 一個 browser profile connection 一個 `connectionId`；close 後全數撤銷；response 必須 match request |
| Unsafe fallback | direct command 失敗後改控 Windows current session | fail closed；只有 routing policy 已明確指定同一 logical target 時才 GSMTC fallback |
| Sensitive log leakage | 寫下 URL query、title、完整 JSON 或使用者路徑 | 結構化最小 log；redact URL query／tokens；不記 artwork bytes 或完整 payload |

## 4. Recommended disposable protocol

### 4.1 Envelope

每個 message 只接受下列 top-level shape；unknown field 在 Prototype 應拒絕，以便暴露版本漂移：

```json
{
  "protocol": "medialock.browser-direct",
  "version": 1,
  "connectionId": "128-bit-random-base64url",
  "sequence": 1,
  "messageId": "uuid-v4",
  "kind": "hello|helloAck|bind|snapshot|command|result|error|goodbye",
  "payload": {}
}
```

規則：

1. Extension 與 host 各產生至少 128-bit random nonce；`connectionId` 由雙方 nonce 綁定後建立。
2. `hello`／`helloAck` 交換 exact protocol versions、Extension origin、browser family、adapter capabilities 與
   random nonce；沒有共同版本就關閉。
3. 每個方向各自使用從 1 開始的嚴格遞增 `sequence`；重複、倒退、跨 connection ID 一律拒絕。
4. `messageId` 用於 correlation；mutating `command` 另有 `commandId`，host 在單一 connection 維護 bounded
   LRU 去重。相同 ID 回傳原 result，不再 dispatch。
5. `command` 必須包含已 bind 的 logical target ID、tab ID、document ID、action 及 action-specific bounded
   arguments；service worker 產生 identity，page 不能指定。
6. 允許的 action 第一版只有 `play`、`pause`、`toggle`、`next`、`previous`、`stop`、`seekTo`；`seekTo`
   必須是 finite、非負且不大於已知 duration／adapter limit。
7. Host／Extension 不在 timeout 後自動重送 mutating command。先重新取得 snapshot；無法證明結果時回報
   `OutcomeUnknown`，讓 Router 決定是否安全重試。

### 4.2 Schema and resource limits

Chrome 的 64 MiB／1 MiB 是 browser safety ceiling，不是 Media Lock 合理輸入大小。Prototype 建議：

- framed JSON 最多 64 KiB；commands 最多 8 KiB；
- JSON nesting 最多 8 層、array 最多 128 個 elements；
- title／artist／album 各最多 1,024 UTF-8 bytes；URL 最多 4,096 bytes；
- artwork 不走 Native Messaging base64 payload；只傳經驗證的 page URL 或之後另設有大小／型別限制的
  fetch seam；
- queue 有固定上限，snapshot 可以 coalesce，command 不可靜默丟棄；
- parse、schema、sequence、capability 或 target mismatch 產生安全錯誤並關閉 connection；不得 catch 後繼續。

### 4.3 Authority separation

```text
Content script
  may: report observed page state, execute one fixed adapter action
  may not: choose another tab, call native host, request arbitrary privileged action

Extension service worker
  may: validate sender, bind document, choose registered adapter, call native host
  may not: forward arbitrary JS/string command, infer a different target after failure

Native host
  may: negotiate protocol, validate/dedupe commands, expose narrow Media Lock seam
  may not: execute shell, open arbitrary path/URL, edit registry, elevate, persist silently

Media Lock
  may: apply existing routing/recovery policy
  may not: treat browser-direct failure as permission to control Windows current session
```

## 5. Does asymmetric encryption help?

### 5.1 What it does not solve

Encrypting every JSON message does not meaningfully protect confidentiality from a network attacker because Native Messaging uses
local parent／child `stdio`, not a network endpoint. It also does not authenticate an Extension merely because both sides know a
public algorithm. A secret embedded in unpacked Extension JavaScript is extractable and cannot be treated as an Extension credential.

If same-user malware can replace the Extension, HKCU registration, manifest or executable, it can usually control an endpoint before
the custom crypto runs. Encryption alone therefore does not repair a compromised local installation or confused-deputy command set.

### 5.2 Narrow case where signatures can add value

An optional challenge signature could let the Extension detect that the launched host owns a pinned private key: Extension sends a
fresh nonce; host signs protocol version + both nonces; Extension pins the corresponding public key. This can detect some manifest／
executable substitution **only if** the private key is not stored beside the replaceable executable and the Extension itself is trusted.
It does not authenticate the webpage, prevent valid commands from being replayed inside an already compromised Extension, or replace
OS file protection and Authenticode.

For a disposable, unsigned Prototype，導入 key provisioning／rotation／revocation 會增加比它消除更多的失敗模式。
因此建議 **不加 encryption 或 asymmetric handshake**；使用 fresh nonce、connection ID、sequence、command ID 與
sender binding。正式版若 threat model 明確要求抵抗 local registration substitution，再獨立設計 host attestation，
而不是把它混進 Prototype 的 routing proof。

## 6. Safe fallback contract

Direct Browser Integration 失效時的預設是 `TargetUnavailable`，不是「找一個看起來相似的 Session」：

- Extension 缺失、permission 撤銷、host 未註冊、protocol 不相容：停用 direct provider，UI 明示原因。
- Port disconnect、browser crash：撤銷全部 browser target bindings，進入既有 Recovery。
- tab reload／document replacement：舊 target 失效；只有新 document 經相同 origin／adapter discovery 後才能
  reacquire。
- command timeout：回報 outcome unknown；不可立即 GSMTC 重送，避免同一媒體命令執行兩次。
- Adapter capability 缺失：按鈕 disabled；不可用 DOM 猜測或轉送任意 JavaScript。
- 使用者明確設定的 fallback policy 若允許 GSMTC，只能對已證明是相同 logical target 的 Session 使用；不能
  採 Windows current session 或 list 第一個。

## 7. Prototype security checklist

### Packaging／registration

- [ ] 固定一個 Prototype Extension ID，manifest `key` 與 `allowed_origins` 完全一致。
- [ ] `allowed_origins` 只有該 Extension；沒有 wildcard、第二個臨時 ID 或普通 web origin。
- [ ] Chrome 與 Brave 各自驗證 exact host lookup；記錄 browser version、registry view、manifest path。
- [ ] Registry、manifest、executable 都使用預期 absolute path；拒絕路徑漂移。
- [ ] Host 一般使用者權限執行；不要求 elevation，不暴露 localhost listener。
- [ ] Uninstall 只移除 installer-owned exact values；不刪除其他 browser／host registration。

### Extension boundary

- [ ] 只要求 `nativeMessaging`、`tabs` 與兩個 YouTube exact host permissions；不使用 `<all_urls>`。目前以
  declarative content script 載入固定 Adapter，因此不需要額外的 `scripting` permission。
- [ ] 不宣告 `externally_connectable`，不使用 `eval`／遠端程式碼或接收 arbitrary JS。
- [ ] Service worker 驗證 `sender.id`、exact origin、URL、top frame、tab ID、document ID。
- [ ] Page payload 不能覆寫 authoritative sender identity。
- [ ] Navigation／reload／disconnect 立即撤銷 binding；target reacquisition 是顯式 transition。

### Native protocol

- [ ] `stdin`／`stdout` binary mode；協定輸出只走 `stdout`，診斷只走 `stderr`。
- [ ] App-level 64 KiB frame limit 先於 JSON allocation；UTF-8、depth、count、string、number 全部 bounded。
- [ ] Exact schema、command enum、capability 與 version validation；unknown／downgrade fail closed。
- [ ] Fresh nonces、connection ID、monotonic sequences、command ID 與 bounded dedupe cache。
- [ ] Mutating command timeout 不自動 retry；結果含 dispatched／rejected／outcome-unknown。
- [ ] Log 不含完整 URL query、cookies、tokens、artwork bytes 或任意 page payload。

### Adversarial tests

- [ ] 非 allowlisted Extension ID、錯誤 caller origin、直接手工啟動 host 都不能取得 privileged capability。
- [ ] iframe、opaque origin、偽造 tab／document ID、舊 document message 全被拒絕。
- [ ] duplicate／out-of-order／cross-connection message 不會再次控制播放器。
- [ ] oversized、deep、invalid UTF-8、NaN／Infinity、unknown command／version 均安全終止。
- [ ] Ctrl+R、navigation、tab close、Extension reload、browser crash、host crash 後不誤控另一個 tab。
- [ ] Timeout／disconnect／fallback 不會造成 Play/Pause、Next、Previous 或 Seek 執行兩次。
- [ ] HKCU registration 指向錯誤 manifest／host 時，Prototype 明確拒絕或暴露 integrity failure。

## 8. Exit criteria

Prototype 只有在下列條件全數有證據時才可進入產品 ADR：

1. Chrome 與 Brave 的固定 Extension ID、registration、host launch 都可重現。
2. 不可信 page／iframe 無法選擇 tab、target 或 native privileged action。
3. reload／Recovery 與 reconnect 後 stale／replayed command 不會 dispatch。
4. 每個 mutating command 有 exactly-once evidence；不確定結果不被隱藏為 success。
5. Direct provider failure 保留既有 Media Lock target，不會降級到競爭來源。
6. 文件明示 unsigned／developer-mode Prototype 的 same-user local compromise 限制。

[chrome-native]: https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
[manifest-key]: https://developer.chrome.com/docs/extensions/reference/manifest/key
[extension-distribution]: https://developer.chrome.com/docs/extensions/how-to/distribute
[runtime-sender]: https://developer.chrome.com/docs/extensions/reference/api/runtime#type-MessageSender
[messaging-security]: https://developer.chrome.com/docs/extensions/develop/concepts/messaging#security-considerations
[brave-source]: https://github.com/brave/brave-core/blob/master/app/brave_main_delegate.cc
[windows-ipc]: https://learn.microsoft.com/en-us/windows/win32/ipc/interprocess-communications
[handle-inheritance]: https://learn.microsoft.com/en-us/windows/win32/procthread/inheritance
[winverifytrust]: https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust
