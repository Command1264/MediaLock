# Windows 安裝封裝方案研究

日期：2026-08-25
範圍：Phase 12A；為 unsigned Media Lock `0.3.x` 提供可安裝版本，同時保留 portable ZIP。
資料邊界：只採用 Microsoft、WiX／FireGiant 與 Inno Setup 專案的官方文件及官方原始碼。

## 結論

建議 Media Lock `0.3.x` 採用 **Inno Setup 的 per-user EXE installer** 作為第一個可安裝版本，並繼續把目前的
`win-x64` self-contained single-file ZIP 當作並列的 portable 下載。安裝程式應預設安裝至穩定、不含版本號的
使用者目錄，例如 `%LocalAppData%\Programs\MediaLock\`，建立目前使用者的 Start Menu 捷徑與解除安裝項目，
且不要求系統管理員權限。

這項建議不是宣稱 Inno Setup 在所有面向都勝過 MSIX 或 MSI，而是針對目前的約束做出的選擇：

- Media Lock 現階段公開檔案仍是 unsigned；Windows 要求可部署的 MSIX 必須簽章，而且憑證必須受裝置信任。
  自簽憑證只適合開發或受管理的測試環境，會替一般下載使用者增加手動信任憑證的步驟。
  [Microsoft：MSIX signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
- Inno Setup 以 `PrivilegesRequired=lowest` 明確支援非系統管理員安裝；非管理員模式會把 Start Menu、解除安裝
  資訊與 `HKA` 登錄資料導向目前使用者。
  [Inno Setup：PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)、
  [Inno Setup：Non Administrative Install Mode](https://jrsoftware.org/ishelp/topic_admininstallmode.htm)
- 它能直接包裝現有的 `MediaLock.exe`，不必先改變 WPF 執行模型、GSMTC、全域鍵盤 hook 或
  `%LocalAppData%\MediaLock\` 使用者資料邊界。
- MSI/WiX 的 transactional rollback、repair 與企業部署能力較完整，但對目前只有一個 EXE、沒有服務或驅動程式的
  per-user 應用程式，元件規則、UpgradeCode／ProductCode 與安裝情境的複雜度尚無相稱收益。
- 安裝器只能壓縮既有 payload，不能從根本消除 self-contained .NET runtime 的體積；體積最佳化應留在獨立的
  Phase 12B，不能把「換安裝格式」當成 runtime 縮小方案。Microsoft 明確說明 self-contained single-file 因包含
  runtime 與 framework libraries 而較大。
  [Microsoft：Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 決策矩陣

| 面向 | MSIX | MSI／WiX | Inno Setup EXE |
| --- | --- | --- | --- |
| unsigned 公開下載 | **不適合**。MSIX 必須簽章且憑證受信任；自簽憑證需使用者先匯入信任 | 技術上可產生 unsigned MSI，但仍受 SmartScreen、企業政策及來源信任影響 | 技術上可產生 unsigned Setup EXE；仍會有 SmartScreen／Smart App Control 風險 |
| per-user、免提權 | MSIX 一般為 per-user 安裝 | 可做，但必須正確 author `ALLUSERS`／`MSIINSTALLPERUSER` 與所有元件 | `PrivilegesRequired=lowest` 直接對應需求 |
| Start Menu／Search | manifest 的每個 `Application` entry 形成 Start Menu entry | Shortcut table／WiX Shortcut 明確建立使用者 Start Menu 捷徑 | `[Icons]` 可在 `{group}`／`{userprograms}` 建立捷徑 |
| 更新 | package identity、版本與 App Installer 可提供自動／差異更新 | Major Upgrade／patch／repair 能力完整，但 authoring 成本高 | 相同 `AppId` 與安裝目錄可覆蓋升級；自動更新需另做 |
| 解除安裝 | Windows 管理 package 與虛擬化狀態，清理最強 | Windows Installer 註冊、repair、uninstall 完整 | 內建 uninstaller 與 uninstall log，足以處理目前單一應用程式 |
| 安裝失敗 rollback | 更新具原子性與 package rollback | Windows Installer 預設建立 rollback script，能力最完整 | 安裝中會撤銷既有操作，但 uninstaller finalized 後的錯誤不再 rollback；不可等同 MSI transaction |
| 登入啟動 | 應改用 manifest `desktop:StartupTask`，不宜依賴版本化 WindowsApps 實體路徑 | 可用穩定安裝路徑搭配 HKCU Run 或 installer component | 穩定 `{app}` 路徑可沿用目前 HKCU Run；升級不改路徑 |
| 封裝大小 | 具壓縮與 differential update；仍包含相同應用程式 payload | CAB 壓縮；仍包含相同 payload，可能另有 bootstrapper | 預設 LZMA2，可顯著壓縮下載檔；安裝後 payload 大小不會消失 |
| 專案導入成本 | 高：identity、manifest、assets、簽章、startup 與 virtualized state 都需驗證 | 中高：component GUID、scope、major upgrade、repair 與 source resilience | 低：一份可 review 的 `.iss` 可包裝現有單檔輸出 |
| Phase 12A 決策 | 目前拒絕；取得可信簽章或 Store 路線時重評 | 目前拒絕；企業部署／repair 成為需求時重評 | **採用** |

## 方案一：MSIX

### 優點

MSIX 是 Windows 的現代封裝格式，提供 package identity、可靠安裝／解除安裝、差異更新與受 Windows 管理的
應用程式狀態。Microsoft 說明 package 更新會以原子方式取代 binaries，必要時可 rollback；解除安裝也能移除受
封裝管理的檔案、登錄與系統變更。
[Microsoft：What is MSIX?](https://learn.microsoft.com/en-us/windows/msix/overview)、
[Microsoft：MSIX containerization](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview)

manifest 中每個 `<Application>` entry 都會形成 Start Menu entry，因此符合「Windows Search 可以找到」的使用者
目標；`VisualElements` 同時提供顯示名稱與圖示。
[Microsoft：MSIX Start Menu entries](https://learn.microsoft.com/en-us/windows/msix/packaging-tool/create-start-group)

使用 `.appinstaller` 時，可以設定啟動時或背景更新、是否提示、是否阻止舊版啟動，亦可用
`ForceUpdateFromAnyVersion` 允許降版。MSIX 也能只下載變更的檔案區塊。
[Microsoft：App Installer update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings)、
[Microsoft：MSIX differential updates](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-update)

### 本階段的阻礙

直接散布的 MSIX **不能維持真正 unsigned**。Windows 要求 MSIX 必須以有效 code-signing certificate 簽章，且
憑證鏈必須受安裝裝置信任；自簽憑證需要測試者先匯入 Trusted People，官方將它定位為開發／測試，而非廣泛公開
散布。
[Microsoft：Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)、
[Microsoft：MSIX signing end-to-end](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)

Media Lock 現有登入啟動把完整 EXE 路徑寫入 `HKCU\...\Run`。MSIX 的安裝位置由 Windows 管理，預設位於包含
package full name 的 `C:\Program Files\WindowsApps\...`，package binaries 為唯讀；正規 packaged desktop 做法是
在 manifest 宣告 `desktop:StartupTask`。這不只是封裝檔案格式替換，而是需要新的 startup adapter、設定同步與
解除安裝測試。
[Microsoft：Packaged desktop runtime model](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)、
[Microsoft：desktop:StartupTask](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask)

MSIX 的 VFS／registry virtualization 也可能改變目前 `%LocalAppData%\MediaLock\`、HKCU Run 與 Win32 integration
的實際行為。Full-trust packaged desktop app 雖仍以標準桌面權限執行，但 GSMTC、低階鍵盤 hook、single-instance、
tray、開啟 logs、登入啟動與更新後資料位置都必須重新做完整實機矩陣，不能由 portable 結果推定。
[Microsoft：Understanding packaged desktop apps](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)

### 拒絕理由

Phase 12A 的目標是替目前 unsigned 版本降低安裝摩擦。MSIX 反而會要求公開使用者先信任自簽憑證，或要求專案先
取得 CA／Azure Artifact Signing／Microsoft Store 發布能力。這與本階段約束衝突。因此目前拒絕 MSIX；待有可信
簽章、Store 發布或真正需要 package identity API 時再重評。

## 方案二：MSI／WiX

### 優點

Windows Installer 擅長受管理的安裝、repair、component ownership 與 rollback。安裝時會同步建立 rollback
script，保留被刪除檔案；安裝失敗時預設回復原狀。
[Microsoft：Rollback Installation](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation)

Major Upgrade 能以 `UpgradeCode` 尋找相關產品，移除舊版並安裝新版；官方也要求 per-user 安裝必須以相同
per-user context 執行升級。Shortcut table 可以建立 Start Menu 捷徑，Windows Installer 也會建立 Apps／Programs
解除安裝資訊。
[Microsoft：Major Upgrades](https://learn.microsoft.com/en-us/windows/win32/msi/major-upgrades)、
[Microsoft：Shortcut Table](https://learn.microsoft.com/en-us/windows/win32/msi/shortcut-table)、
[Microsoft：Uninstall registry properties](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)

WiX 能把這些 MSI tables 以原始碼管理，並支援 `perUser` scope；其官方 schema 說明 `perUser` 不要求 elevation。
[WiX：PackageScopeType](https://docs.firegiant.com/wix/schema/wxs/packagescopetype/)

### 成本與限制

per-user MSI 不是單純把檔案位置改到 LocalAppData。套件必須一致 author 安裝 scope、components、registry、
shortcuts 與 upgrade context；`ALLUSERS=2` 與 `MSIINSTALLPERUSER=1` 才指定 Windows Installer 5 的 per-user
dual-purpose 安裝，而跨 per-user／per-machine context 不能直接 Major Upgrade。
[Microsoft：Single Package Authoring](https://learn.microsoft.com/en-us/windows/win32/msi/single-package-authoring)、
[Microsoft：Installation Context](https://learn.microsoft.com/en-us/windows/win32/msi/installation-context)

簽章對 MSI 是可用的安全能力，而不像 MSIX 是可部署的必要條件；但 unsigned MSI 仍不能解決使用者信任問題，
而且企業政策可依 signer／publisher 決定允許或拒絕安裝。
[Microsoft：Digital Signatures and Windows Installer](https://learn.microsoft.com/en-us/windows/win32/msi/digital-signatures-and-windows-installer)

WiX 本身另有版本與維護政策需要納入供應鏈決策：官方目前說明 v6 起導入 Open Source Maintenance Fee，v7
加入 EULA acceptance；達到其 revenue 條件時必須 sponsor。這不代表不能採用，但對現階段的一檔案安裝需求是
額外維護與合規成本。
[WiX：Open Source Maintenance Fee](https://docs.firegiant.com/wix/osmf/)

### 拒絕理由

Media Lock 目前沒有 system service、driver、COM registration、shared component 或企業 repair 需求。為一個
per-user EXE 引入完整 MSI component model 與 upgrade authoring，風險與維護量高於所得。若未來使用者明確需要
Intune／Group Policy、標準 MSI repair、managed deployment 或 machine-wide 安裝，再以獨立 phase 重評 WiX。

## 方案三：Inno Setup

### 為何符合 Media Lock `0.3.x`

`PrivilegesRequired=lowest` 會固定使用 non-administrative install mode，不顯示 UAC credential prompt；此模式下
`{group}` 指向目前使用者的 Start Menu，`HKA` 與解除安裝資訊指向 HKCU。`{userpf}`／`{localappdata}` 能建立穩定
per-user 安裝目錄。
[Inno Setup：PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)、
[Inno Setup：Constants](https://jrsoftware.org/ishelp/topic_consts.htm)

`[Icons]` 可直接建立 `{group}\Media Lock` 指向 `{app}\MediaLock.exe` 的 Start Menu 捷徑；Windows 桌面應用的
Start Menu 捷徑位於使用者 `%APPDATA%\Microsoft\Windows\Start Menu\Programs`，這就是 Windows app launcher
與 Search 使用的註冊表面。
[Inno Setup：Icons section](https://jrsoftware.org/ishelp/topic_iconssection.htm)、
[Microsoft：Win32 Start Menu shortcut location](https://learn.microsoft.com/en-us/windows/mixed-reality/distribute/implementing-3d-app-launchers-win32)

固定 `AppId`、安裝模式與安裝目錄時，後續版本會被視為同一應用程式；預設會沿用舊安裝目錄並把新變更附加至
同一份 uninstall log。內建 uninstaller 預設開啟。
[Inno Setup：Same Application](https://jrsoftware.org/ishelp/topic_sameappnotes.htm)、
[Inno Setup：AppId](https://jrsoftware.org/ishelp/topic_setup_appid.htm)、
[Inno Setup：Appending to uninstall logs](https://jrsoftware.org/ishelp/topic_appendnotes.htm)、
[Inno Setup：Uninstallable](https://jrsoftware.org/ishelp/topic_setup_uninstallable.htm)

預設 `lzma2/max` 能壓縮 installer payload；`SolidCompression=yes` 可再提高許多相似檔案的壓縮率，但會犧牲
隨機存取與錯誤重試效率。對目前已是 single-file 的 payload，必須量測實際 installer 大小與安裝時間，不能先假設
特定節省比例。
[Inno Setup：Compression](https://jrsoftware.org/ishelp/topic_setup_compression.htm)、
[Inno Setup：SolidCompression](https://jrsoftware.org/ishelp/topic_setup_solidcompression.htm)

Inno Setup 的 license 允許任何用途（包含 commercial applications）使用與散布；官方另請符合其 commercial
user 定義者購買 commercial license。若未來成立 Pro／商業版本，release 流程必須重新檢查當時授權條件。
[Inno Setup 官方 license](https://github.com/jrsoftware/issrc/blob/main/license.txt)、
[Inno Setup commercial licenses](https://jrsoftware.org/isorder.php)

### 必須明確揭露的限制

Inno Setup 的 uninstall log 與安裝失敗撤銷能力不等同 MSI transaction。官方安裝順序明確說明：uninstaller EXE
與 log finalized 後，使用者不能取消，後續錯誤也不會 rollback 先前已安裝內容。因此文件只能承諾「可升級與解除
安裝」，不能在沒有 fault-injection evidence 前宣稱完整 transactional rollback。
[Inno Setup：Installation Order](https://jrsoftware.org/ishelp/topic_installorder.htm)

unsigned Setup EXE 仍會面臨 SmartScreen。Microsoft 說明 unsigned 檔每一新版都必須從零累積 hash reputation，
使用者可能看到 `Windows protected your PC`，企業政策甚至可能禁止繼續；Smart App Control 也可能直接阻擋沒有
positive reputation 的 unsigned 檔案。這是所有 unsigned EXE／MSI 的公開散布限制，不是 Inno Setup 能解決的問題。
[Microsoft：SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

## 建議的 Phase 12A 封裝契約

### 產物與路徑

1. 保留現有 `MediaLock-<version>-win-x64.zip`、manifest 與 SHA-256 流程；portable ZIP 仍是完整支援的替代方案。
2. 新增 `MediaLock-Setup-<version>-win-x64.exe`，其 payload 來自與 ZIP 相同、已驗證的
   self-contained single-file `MediaLock.exe`，兩者必須記錄同一 source commit。
3. installer 預設 `PrivilegesRequired=lowest`，安裝至固定的
   `%LocalAppData%\Programs\MediaLock\`（Inno script 可用 `{localappdata}\Programs\MediaLock`）。不要把版本號放進
   `{app}`，以免每次更新破壞登入啟動的完整路徑。
4. 使用永久固定的 `AppId`；所有 `0.3.x` installer 必須維持相同 install mode、AppId 與 install directory。
5. 建立目前使用者 Start Menu 的 `Media Lock` 捷徑；桌面捷徑最多作為 unchecked opt-in，不應預設新增。
6. 建立 Apps > Installed apps 的解除安裝項目，顯示版本、publisher、support URL、updates URL 與
   `MediaLock.exe` icon。

### 登入啟動

- installer **不得預設啟用** Start with Windows；這仍由 Media Lock Settings 的使用者選擇控制。
- 穩定 `{app}\MediaLock.exe` 路徑讓既有 `"<path>" --startup` HKCU Run value 在 in-place upgrade 後保持有效。
- 安裝／升級前應請 Media Lock 正常 Exit。可使用 Inno `AppMutex`／Restart Manager 偵測使用中檔案，但不得以
  force close 作為一般流程；Inno 預設 `CloseApplications=yes` 能顯示受影響程式並請使用者決定。
  [Inno Setup：CloseApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm)
- uninstall 只可移除名為 `MediaLock` 且 value **精確等於目前 `{app}\MediaLock.exe --startup`** 的 HKCU Run
  entry；若值指向另一份 portable／其他版本，不得刪除。這個條件應有自動測試。
- 升級後啟動應驗證 Settings 的勾選狀態與 Run value 一致；如果安裝目錄由使用者更改，應由新執行檔重新同步
  exact path。

### 使用者資料與解除安裝

- `%LocalAppData%\MediaLock\settings.json`、`state.json` 與 `logs\` 維持既有產品資料位置，不搬進 `{app}`。
- 預設 uninstall 移除程式檔、Start Menu 捷徑、uninstaller registration 與只屬於該安裝的 startup value，**保留**
  `%LocalAppData%\MediaLock\`，避免無提示刪除偏好設定與診斷資料。
- 如要提供「同時移除使用者資料」，必須是 uninstall UI 中明確、預設未勾選的選項，並顯示精確目標路徑。
- portable ZIP 的移除方式保持不變；installer 與 portable 可以各自存在，但同一時間仍由 single-instance contract
  保證一個 Media Lock process。文件需警告使用者不要同時啟用兩份不同路徑的登入啟動。

### 升級與回復

- 一般升級採 same-AppId、same-directory overwrite；安裝前關閉目前程序，完成後由使用者選擇是否啟動。
- 不宣稱自動更新；`0.3.x` 先由 GitHub Release 手動下載新版 installer。
- 保留前一個 stable installer 與 `release/<minor>` branch，讓 hotfix 與人工降版仍有來源；但「執行舊 installer
  覆蓋新版」必須有獨立 downgrade smoke，不能由 AppId 相同直接推論安全。
- 若 downgrade 的 settings schema 不能安全向後讀取，文件必須要求先備份 `%LocalAppData%\MediaLock\`，或阻止
  不相容降版。

## 體積判斷

封裝方案與 runtime 體積是兩個不同問題。現有 release script 明確產生 `selfContained=true`、`singleFile=true`、
`trimmed=false` 的 `win-x64` binary；安裝器只會改變下載時的壓縮容器，不會讓安裝後免帶 .NET runtime。

Phase 12A 可以量測 Inno LZMA2 後的 Setup EXE 與現有 ZIP 大小，但不應同時啟用 trimming。Microsoft 的官方相容性
文件指出 WPF 大量使用 reflection，幾乎沒有 WPF app 能在 trimming 後正常執行，因此 SDK 目前停用 WPF trimming。
[Microsoft：Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)

Phase 12B 可獨立評估：

- `EnableCompressionInSingleFile=true` 的下載大小與冷啟動成本；Microsoft 明確要求量測兩者。
  [Microsoft：Compress assemblies in single-file apps](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps)
- framework-dependent 版本是否值得以「需要先安裝正確 .NET Desktop Runtime」交換較小下載；它不能取代目前免安裝
  runtime 的 self-contained artifact。
- 不以 WPF trimming 作為 `0.3.x` release 承諾，除非 SDK 支援狀態改變且完整 UI、WinRT、COM、tray、hook、
  suspend/resume 與 Sandbox matrix 全部重新通過。

## Windows Sandbox 驗證矩陣

Windows Sandbox 支援 `.wsb` 映射資料夾與 `LogonCommand`，可把正式 artifacts 以 read-only folder 映入全新
環境並啟動驗證腳本。
[Microsoft：Windows Sandbox sample configurations](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-sample-configuration)

正式稱為 installable 前，對 exact source commit、installer SHA-256 與 portable ZIP SHA-256 執行：

1. **冷安裝**：標準使用者啟動 Setup；確認沒有 UAC、明確顯示 unsigned／SmartScreen 實際結果、安裝成功。
2. **檔案與註冊**：確認固定 `{app}` 只有預期檔案；Start Menu/Search 可用 `Media Lock` 找到並啟動；Installed
   apps 顯示正確版本與 uninstall。
3. **runtime smoke**：冷啟動、single instance、tray restore、Settings、Edge GSMTC、實體媒體鍵、Exit。
4. **登入啟動**：預設無 Run value；在 Settings 啟用後 exact value 指向 `{app}\MediaLock.exe --startup`；重開與
   登入後啟動正常。
5. **in-place upgrade**：先裝 `N-1`、建立設定並啟用 startup，再裝 `N`；確認沒有第二份 Installed apps entry、
   Start Menu 捷徑不重複、設定保留、Run value 仍精確、版本更新。
6. **安裝取消／失敗**：在複製階段取消及以受控方式製造失敗，記錄哪些變更被撤銷；不得把結果泛化成 MSI 等級
   transaction。
7. **人工回復**：執行仍受支援的前一 stable installer，確認 version、settings compatibility、startup path 與
   media-key critical path；若不支援降版，installer 必須可操作地阻止並說明。
8. **解除安裝**：先停用／再啟用 startup 各測一次；確認 process、tray、捷徑、程式檔、uninstall entry 與匹配的
   Run value 移除，使用者資料預設保留。
9. **portable 並存**：同一 Sandbox 另解壓 portable ZIP，驗證它仍可直接啟動；兩個路徑同時啟動時只有一個
   process，不誤刪另一份 startup value 或程式檔。
10. **清潔度**：解析 settings/state/log JSON，檢查 zero Error/Critical；以檔案、registry 與 process snapshot
    比對解除安裝前後，記錄所有預期保留項。

## 重新評估條件

- **改選 MSIX**：已取得受公開裝置信任的簽章或決定進 Microsoft Store，並願意實作 packaged startup、package
  identity、virtualization 與完整 Windows integration regression。
- **改選 MSI／WiX**：企業部署、Intune／Group Policy、repair、machine-wide install、標準 MSI inventory 或
  transactional rollback 成為明確需求，且願意承擔 component／upgrade authoring 與 WiX 供應鏈政策。
- **停止 Inno Setup**：per-user 模式無法保持 global media-key hook／GSMTC 行為、upgrade 無法安全處理執行中程序
  或 startup exact path，或 Sandbox 顯示 uninstall 留下不可接受的程式狀態。

在上述重新評估條件出現前，Phase 12A 的最小且完整路線是：**Inno Setup per-user installer + 現有 portable ZIP，
兩者同源、分別雜湊、分別驗證，且持續清楚標示 unsigned。**
