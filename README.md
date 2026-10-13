# QuickSimplify｜微軟速成轉簡（實驗版）

保留 Windows **微軟速成**原本的候選字排序及 Space 翻頁，在以數字鍵或 Enter 選字後、文字提交前轉成簡體。這是一個獨立的系統匣工具，並非新輸入法；不使用 PIME。

> **相容性提醒：**目前只在 Windows 11 x64、特定版本的 `TextInputFramework.dll` 上驗證。程式會核對該 DLL 的 CodeView 簽章，不吻合便拒絕掛鉤。這是研究性質的版本，Windows 更新後可能失效；請勿預期它在所有電腦及程式中運作。

## 下載及使用

1. 從 [Releases](../../releases) 下載 `QuickSimplify-Windows-x64-portable.zip`，解壓到固定資料夾。
2. 在 Windows 選用「微軟速成」，切到「中」模式。
3. 執行 `QuickSimplifyController.exe`。右下角系統匣會出現 **简** 金幣圖示；Windows 可能先將它放在 `^` 隱藏圖示區。
4. 左鍵點一下圖示，在簡體／繁體之間切換。簡體模式顯示 **简**，繁體模式顯示 **繁**。右鍵可查看狀態、設定「開機自動啟動」或結束。

啟動後預設為簡體模式；暫停轉簡即回到微軟速成原本的繁體輸出。關閉程式後不會留下新輸入法。若要移除，先在右鍵選單關掉「開機自動啟動」並結束程式，再刪除解壓資料夾。

## 已知限制

- 僅支援 **64 位元、與工具相同權限層級**的傳統桌面程式；32 位元、系統／受保護程式、系統管理員權限程式及 Codex 已排除。
- 目前只處理以數字鍵 `1–9` 或 Enter 確認的候選字；滑鼠點候選字和以 Space 直接提交候選字仍未驗證。
- 掛鉤 Windows 內部的 `CRange::_SetText`，依賴特定 `TextInputFramework.dll` 簽章和位址。不同 Windows 版本需要重新分析與適配。這不是受 Microsoft 支援的擴充介面。
- 工具會將 DLL 載入支援的前景程式。程式碼沒有網絡上傳或生產模式的打字內容紀錄，但請先看原始碼，才在有敏感資料的程式中使用。
- 發佈的 EXE／DLL 尚未簽署數位憑證。請核對來源和 SHA-256，並自行決定是否信任。

目前的證據是：開發機上的 Windows 原生文字框、隔離的 Chrome 普通／富文字框曾測得 `馬轟` → `马轰`，使用者亦確認實際打字會出簡體。這不是跨版本兼容性保證。

## 從原始碼編譯

需要 Windows x64、Zig **0.15.2** 和 .NET Framework 4.x C# compiler。原始碼已附帶 [MinHook 1.3.4](third_party/minhook)；其 BSD 授權在 `third_party/minhook/LICENSE.txt`。

```powershell
./scripts/Build.ps1 -ZigExe 'C:\path\to\zig.exe'
```

編譯結果放在 `dist/`。建置腳本會呼叫 `scripts/Generate-Icons.ps1` 產生中性金幣圖示。原始碼中的測試診斷分支只在明確的 `--test-profile` 參數下啟用；日常執行不寫入打字內容。

## 授權

本專案自有程式碼及中性金幣圖示採 [MIT](LICENSE)；MinHook 維持其原有 [BSD 2-Clause 授權](third_party/minhook/LICENSE.txt)。本專案與 Microsoft 無關。個人版動漫角色圖示未包含在公開版。

English: This experimental Windows x64 utility converts committed Microsoft Quick IME candidates to Simplified Chinese while keeping Microsoft's candidate ordering and Space paging. It uses a version-locked native hook, so compatibility is intentionally narrow. See the limitations above before running it.
