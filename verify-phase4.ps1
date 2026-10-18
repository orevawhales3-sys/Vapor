$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $projectDir "bin\Release\net10.0-windows"
$pocDll = Join-Path $outDir "DynamicOverlay.Poc.dll"
$dataDir = Join-Path $env:LOCALAPPDATA "DynamicOverlayPoc"
$statusFile = Join-Path $dataDir "poc-status.json"
$settingsFile = Join-Path $dataDir "settings.json"
$shortcutsFile = Join-Path $dataDir "shortcuts.json"
$notesFile = Join-Path $dataDir "notes.json"
$dupLogFile = Join-Path $dataDir "poc-duplicate-instance.json"
$evidenceDir = Join-Path $projectDir "evidence"
$artifactDir = "C:\Users\harshit\.gemini\antigravity\brain\a9ee4394-2173-4282-b2a8-6d84fa58d789"

New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class Win32Phase4Helper {
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLOSE = 0x0010;
    public const int WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;
    public const int HOTKEY_ID_TOGGLE = 0x9001;

    public const int WM_APP_SET_MODE = 0x8001;
    public const int WM_APP_SYNC_DOM = 0x8002;
    public const int WM_APP_INCREMENT_COUNTER = 0x8003;
    public const int WM_APP_SWITCH_TAB = 0x8004;
    public const int WM_APP_MINIMIZE_WINDOW = 0x8005;
    public const int WM_APP_RESTORE_WINDOW = 0x8006;
    public const int WM_APP_HIDE_WINDOW = 0x8007;
    public const int WM_APP_REQUEST_CLOSE = 0x8008;
    public const int WM_APP_OPEN_VIEW = 0x8009;
    public const int WM_APP_TOGGLE_MAXIMIZE = 0x800A;
    public const int WM_APP_TOGGLE_CAPTURE_PROTECTION = 0x800B;
    public const int WM_APP_ADD_SHORTCUT = 0x800C;
    public const int WM_APP_REMOVE_SHORTCUT = 0x800D;
    public const int WM_APP_OPEN_SHORTCUT = 0x800E;
    public const int WM_APP_UPDATE_NOTE = 0x800F;
    public const int WM_APP_VISUAL_TAB_ACTION = 0x8010;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_CHAR = 0x0102;
    public const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public static IntPtr LastActiveRootHwnd = IntPtr.Zero;
    public static IntPtr LastFocusedHwnd = IntPtr.Zero;

    public static IntPtr ClickScreenPoint(int x, int y) {
        SetCursorPos(x, y);
        POINT pt = new POINT { X = x, Y = y };
        IntPtr hitHwnd = WindowFromPoint(pt);
        if (hitHwnd == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        IntPtr rootHwnd = GetAncestor(hitHwnd, GA_ROOT);
        if (rootHwnd == IntPtr.Zero) {
            rootHwnd = hitHwnd;
        }

        IntPtr prevFg = GetForegroundWindow();
        if (prevFg == IntPtr.Zero) {
            prevFg = LastActiveRootHwnd;
        }

        uint curTid = GetCurrentThreadId();
        uint prevPid = 0;
        uint prevTid = prevFg != IntPtr.Zero ? GetWindowThreadProcessId(prevFg, out prevPid) : 0;
        uint targetPid = 0;
        uint targetTid = GetWindowThreadProcessId(rootHwnd, out targetPid);

        bool attachedPrev = false;
        bool attachedTarget = false;
        try {
            if (prevTid != 0 && prevTid != curTid) {
                attachedPrev = AttachThreadInput(curTid, prevTid, true);
            }
            if (targetTid != 0 && targetTid != curTid && targetTid != prevTid) {
                attachedTarget = AttachThreadInput(curTid, targetTid, true);
            }

            BringWindowToTop(rootHwnd);
            SetForegroundWindow(rootHwnd);
            SetActiveWindow(rootHwnd);
            SetFocus(hitHwnd);
        } finally {
            if (attachedTarget) {
                AttachThreadInput(curTid, targetTid, false);
            }
            if (attachedPrev) {
                AttachThreadInput(curTid, prevTid, false);
            }
        }

        if (prevFg != IntPtr.Zero && prevFg != rootHwnd) {
            PostMessage(prevFg, WM_ACTIVATE, (IntPtr)WA_INACTIVE, rootHwnd);
        }
        PostMessage(rootHwnd, WM_ACTIVATE, (IntPtr)2, prevFg);

        LastActiveRootHwnd = rootHwnd;
        LastFocusedHwnd = hitHwnd;

        POINT clientPt = new POINT { X = x, Y = y };
        ScreenToClient(hitHwnd, ref clientPt);
        IntPtr lParam = (IntPtr)((clientPt.Y << 16) | (clientPt.X & 0xFFFF));
        PostMessage(hitHwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        System.Threading.Thread.Sleep(25);
        PostMessage(hitHwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
        System.Threading.Thread.Sleep(30);
        PostMessage(hitHwnd, WM_LBUTTONUP, (IntPtr)0, lParam);
        System.Threading.Thread.Sleep(80);
        return hitHwnd;
    }

    public static IntPtr TypeAsciiString(string text) {
        IntPtr fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) {
            fgHwnd = LastActiveRootHwnd;
        }
        if (fgHwnd == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        uint fgPid = 0;
        uint fgTid = GetWindowThreadProcessId(fgHwnd, out fgPid);
        GUITHREADINFO gti = new GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        IntPtr targetFocus = (LastFocusedHwnd != IntPtr.Zero) ? LastFocusedHwnd : fgHwnd;
        if (GetGUIThreadInfo(fgTid, ref gti) && gti.hwndFocus != IntPtr.Zero) {
            targetFocus = gti.hwndFocus;
        }

        foreach (char c in text) {
            SendMessage(targetFocus, WM_CHAR, (IntPtr)(int)c, IntPtr.Zero);
            System.Threading.Thread.Sleep(10);
        }
        System.Threading.Thread.Sleep(60);
        return targetFocus;
    }
}
"@

function Wait-ForStatus([scriptblock]$predicate, [int]$timeoutMs = 12000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path $statusFile) {
            try {
                $raw = Get-Content $statusFile -Raw -ErrorAction Stop
                $json = $raw | ConvertFrom-Json -ErrorAction Stop
                if (& $predicate $json) {
                    return $json
                }
            } catch {
            }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out after ${timeoutMs}ms waiting for poc-status.json condition."
}

function Wait-ForJsonFile([string]$path, [scriptblock]$predicate, [int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path $path) {
            try {
                $raw = Get-Content $path -Raw -ErrorAction Stop
                $json = $raw | ConvertFrom-Json -ErrorAction Stop
                if (& $predicate $json) {
                    return $json
                }
            } catch {
            }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out after ${timeoutMs}ms waiting for $path condition."
}

function Save-ViewSnapshot([IntPtr]$hwnd, [string]$fileName, [bool]$compositeWebView = $false) {
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_SYNC_DOM, [IntPtr]0, [IntPtr]0) | Out-Null
    Start-Sleep -Milliseconds 450
    $uiSnap = Join-Path $dataDir "poc-ui-snapshot.png"
    $wv2Snap = Join-Path $dataDir "poc-webview2-preview.png"
    $destEvidence = Join-Path $evidenceDir $fileName
    $destArtifact = Join-Path $artifactDir $fileName

    if (Test-Path $uiSnap) {
        if ($compositeWebView -and (Test-Path $wv2Snap)) {
            $baseBmp = [System.Drawing.Bitmap]::FromFile($uiSnap)
            $webBmp = [System.Drawing.Bitmap]::FromFile($wv2Snap)
            $gComp = [System.Drawing.Graphics]::FromImage($baseBmp)
            $gComp.DrawImage($webBmp, 10, 80, $baseBmp.Width - 20, $baseBmp.Height - 108)
            $gComp.Dispose()
            $baseBmp.Save($destEvidence, [System.Drawing.Imaging.ImageFormat]::Png)
            $webBmp.Dispose()
            $baseBmp.Dispose()
        } else {
            Copy-Item $uiSnap $destEvidence -Force
        }
        if (Test-Path $artifactDir) {
            Copy-Item $destEvidence $destArtifact -Force
        }
    }
}

# Clean up any leftover processes
Get-Process -Name "DynamicOverlay.Poc" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "ExternalTargetApp" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$extAppExe = Join-Path $evidenceDir "ExternalTargetApp.exe"
$extAppSrc = Join-Path $evidenceDir "ExternalTargetApp.cs"
if (-not (Test-Path $extAppExe) -and (Test-Path $extAppSrc)) {
    & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:"$extAppExe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$extAppSrc"
}

$extStatusA = Join-Path $evidenceDir "ext-app-a.json"
if (Test-Path $extStatusA) { Remove-Item $extStatusA -Force }
if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }
if (Test-Path $shortcutsFile) { Remove-Item $shortcutsFile -Force }
if (Test-Path $notesFile) { Remove-Item $notesFile -Force }
if (Test-Path $dupLogFile) { Remove-Item $dupLogFile -Force }

$results = [ordered]@{}
$procOverlay = $null
$procExtA = $null

try {
    # Launch External Target App outside overlay
    Write-Host "[Setup] Launching ExternalTargetApp.exe at (780, 60, 440x360)..."
    $procExtA = Start-Process -FilePath $extAppExe -ArgumentList "`"ExternalEditorApp`" `"$extStatusA`" 780 60 440 360" -PassThru
    $stExtA = Wait-ForJsonFile $extStatusA { param($j) $j.hwnd -ne 0 } 8000

    # =========================================================================
    # TEST 1: Main Browser Window, Redesigned Header Bar & Initial WebView2 Host
    # =========================================================================
    Write-Host "[Test 1] Launching Vapor (DynamicOverlay.Poc) and verifying redesigned Browser View..."
    $procOverlay = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $st1 = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    $hwnd = [IntPtr][int64]$st1.hwnd
    [Win32Phase4Helper]::LastActiveRootHwnd = $hwnd

    [Win32Phase4Helper]::MoveWindow($hwnd, 30, 50, 720, 520, $true) | Out-Null
    $st1 = Wait-ForStatus { param($s) [int]$s.left -eq 30 -and [int]$s.width -eq 720 } 5000
    Save-ViewSnapshot $hwnd "phase4_browser_view.png" $true

    $results["T01_MainBrowserWindowAndHeader"] = [ordered]@{
        Status = if ($st1.isVisible -and $st1.activePanelView -eq "Browser" -and $st1.webViewInitialized -and $st1.headerControlsUnclipped -and $st1.currentMode -eq "Lite") { "PASS" } else { "FAIL" }
        ActivePanelView = $st1.activePanelView
        WebViewHostingMode = $st1.webViewHostingMode
        CurrentMode = $st1.currentMode
        HeaderControlsUnclipped = $st1.headerControlsUnclipped
        VisualTabCount = $st1.visualTabCount
        ActiveVisualTabTitle = $st1.activeVisualTabTitle
        Screenshot = "evidence\phase4_browser_view.png"
    }

    # =========================================================================
    # TEST 2: Phase 4 Visual Tab Strip Operations
    # =========================================================================
    Write-Host "[Test 2] Verifying Phase 4 Visual Tab Strip (+ Add, Select, x Close)..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_VISUAL_TAB_ACTION, [IntPtr]1, [IntPtr]0) | Out-Null
    $stTabAdded = Wait-ForStatus { param($s) [int]$s.visualTabCount -eq 6 -and $s.activeVisualTabTitle -eq "Docs Tab" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_VISUAL_TAB_ACTION, [IntPtr]0, [IntPtr]0) | Out-Null
    $stTabSelected = Wait-ForStatus { param($s) $s.activeVisualTabTitle -eq "Microsoft" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_VISUAL_TAB_ACTION, [IntPtr]2, [IntPtr]0) | Out-Null
    $stTabClosed = Wait-ForStatus { param($s) [int]$s.visualTabCount -eq 5 } 5000

    $results["T02_Phase4VisualTabStrip"] = [ordered]@{
        Status = if ($stTabAdded.visualTabCount -eq 6 -and $stTabSelected.activeVisualTabTitle -eq "Microsoft" -and $stTabClosed.visualTabCount -eq 5) { "PASS" } else { "FAIL" }
        InitialTabCount = $st1.visualTabCount
        TabCountAfterAdd = $stTabAdded.visualTabCount
        ActiveTitleAfterAdd = $stTabAdded.activeVisualTabTitle
        ActiveTitleAfterSelectFirst = $stTabSelected.activeVisualTabTitle
        TabCountAfterClose = $stTabClosed.visualTabCount
    }

    # =========================================================================
    # TEST 3: Minimum Window Size (520 x 380) & Unclipped Header Controls
    # =========================================================================
    Write-Host "[Test 3] Resizing below minimum bounds (300x200 -> 520x380) and verifying unclipped header controls..."
    [Win32Phase4Helper]::MoveWindow($hwnd, 30, 50, 300, 200, $true) | Out-Null
    $stMinSize = Wait-ForStatus { param($s) [int]$s.width -eq 520 -and [int]$s.height -eq 380 } 5000
    Save-ViewSnapshot $hwnd "phase4_minimum_size_520x380.png" $true

    # Restore 720x520
    [Win32Phase4Helper]::MoveWindow($hwnd, 30, 50, 720, 520, $true) | Out-Null
    $stRestoredSize = Wait-ForStatus { param($s) [int]$s.width -eq 720 -and [int]$s.height -eq 520 } 5000

    $results["T03_MinimumSize520x380HeaderUnclipped"] = [ordered]@{
        Status = if ([int]$stMinSize.width -eq 520 -and [int]$stMinSize.height -eq 380 -and $stMinSize.headerControlsUnclipped -eq $true) { "PASS" } else { "FAIL" }
        ClampedWidth = $stMinSize.width
        ClampedHeight = $stMinSize.height
        HeaderControlsUnclippedAt520x380 = $stMinSize.headerControlsUnclipped
        CloseButtonCoordsAt520x380 = "$($stMinSize.closeButtonScreenX),$($stMinSize.closeButtonScreenY)"
        MinimizeButtonCoordsAt520x380 = "$($stMinSize.minimizeButtonScreenX),$($stMinSize.minimizeButtonScreenY)"
        MaximizeButtonCoordsAt520x380 = "$($stMinSize.maximizeButtonScreenX),$($stMinSize.maximizeButtonScreenY)"
        MenuButtonCoordsAt520x380 = "$($stMinSize.menuButtonScreenX),$($stMinSize.menuButtonScreenY)"
        Screenshot = "evidence\phase4_minimum_size_520x380.png"
    }

    # =========================================================================
    # TEST 4: Window Controls — Minimize, Restore, Maximize, Restore
    # =========================================================================
    Write-Host "[Test 4] Testing Header Minimize (-), Restore (Ctrl+Alt+Space), Maximize ([]), and Restore from Maximized..."
    [Win32Phase4Helper]::ClickScreenPoint([int]$stRestoredSize.minimizeButtonScreenX, [int]$stRestoredSize.minimizeButtonScreenY) | Out-Null
    $stMinimized = Wait-ForStatus { param($s) $s.isMinimized -eq $true -and $s.windowState -eq "Minimized" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_HOTKEY, [IntPtr][Win32Phase4Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stRestoredFromMin = Wait-ForStatus { param($s) $s.isMinimized -eq $false -and $s.windowState -eq "Normal" -and $s.isVisible -eq $true } 5000

    [Win32Phase4Helper]::ClickScreenPoint([int]$stRestoredFromMin.maximizeButtonScreenX, [int]$stRestoredFromMin.maximizeButtonScreenY) | Out-Null
    $stMaximized = Wait-ForStatus { param($s) $s.windowState -eq "Maximized" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_TOGGLE_MAXIMIZE, [IntPtr]0, [IntPtr]0) | Out-Null
    $stRestoredFromMax = Wait-ForStatus { param($s) $s.windowState -eq "Normal" -and [int]$s.width -eq 720 } 5000

    $results["T04_WindowControlsMinimizeMaximizeRestore"] = [ordered]@{
        Status = if ($stMinimized.isMinimized -and $stRestoredFromMin.windowState -eq "Normal" -and $stMaximized.windowState -eq "Maximized" -and $stRestoredFromMax.windowState -eq "Normal") { "PASS" } else { "FAIL" }
        MinimizedState = $stMinimized.windowState
        RestoredFromMinimizeState = $stRestoredFromMin.windowState
        MaximizedState = $stMaximized.windowState
        RestoredFromMaximizeState = $stRestoredFromMax.windowState
        RestoredBounds = "$($stRestoredFromMax.left),$($stRestoredFromMax.top) $($stRestoredFromMax.width)x$($stRestoredFromMax.height)"
    }

    # =========================================================================
    # TEST 5: Three-Dot Menu Panel in Ultra Mode & Content-View Swapping
    # =========================================================================
    Write-Host "[Test 5] Switching to Ultra Mode, modifying WebView2 DOM state, and opening Three-Dot Menu via header button..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_SET_MODE, [IntPtr]1, [IntPtr]0) | Out-Null
    $stUltra = Wait-ForStatus { param($s) $s.currentMode -eq "Ultra" } 5000

    for ($i = 0; $i -lt 3; $i++) {
        [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_INCREMENT_COUNTER, [IntPtr]0, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 120
    }
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_SWITCH_TAB, [IntPtr]2, [IntPtr]0) | Out-Null
    $stDomReady = Wait-ForStatus { param($s) [int]$s.webViewDomCounter -eq 3 -and $s.webViewDomActiveTab -eq "Tab2-Docs" } 5000

    [Win32Phase4Helper]::ClickScreenPoint([int]$stDomReady.menuButtonScreenX, [int]$stDomReady.menuButtonScreenY) | Out-Null
    $stMenu = Wait-ForStatus { param($s) $s.activePanelView -eq "ThreeDotMenu" } 5000
    Start-Sleep -Milliseconds 350
    Save-ViewSnapshot $hwnd "phase4_three_dot_menu.png" $false

    $results["T05_ThreeDotMenuPanelInUltraMode"] = [ordered]@{
        Status = if ($stMenu.activePanelView -eq "ThreeDotMenu" -and $stMenu.isVisible -and $stMenu.ultraAutoHideCount -eq 0) { "PASS" } else { "FAIL" }
        ActivePanelView = $stMenu.activePanelView
        ModeDuringMenuOpen = $stMenu.currentMode
        RemainedVisibleWithoutSpuriousAutoHide = ($stMenu.isVisible -and $stMenu.ultraAutoHideCount -eq 0)
        Screenshot = "evidence\phase4_three_dot_menu.png"
    }

    # =========================================================================
    # TEST 6: Saved Shortcuts Panel — Validation, Add, Open, Remove & Persistence
    # =========================================================================
    Write-Host "[Test 6] Opening Saved Shortcuts panel and testing URL validation, Add, Open, and Remove..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_VIEW, [IntPtr]2, [IntPtr]0) | Out-Null
    $stShortcutsInit = Wait-ForStatus { param($s) $s.activePanelView -eq "Shortcuts" } 5000

    # Test invalid URL rejection
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_ADD_SHORTCUT, [IntPtr]1, [IntPtr]0) | Out-Null
    $stInvalidUrl = Wait-ForStatus { param($s) $s.lastShortcutValidationError -ne "" } 5000

    # Test valid shortcut addition (MDN Web Docs -> https://developer.mozilla.org)
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_ADD_SHORTCUT, [IntPtr]0, [IntPtr]0) | Out-Null
    $stAddedShortcut = Wait-ForStatus { param($s) [int]$s.shortcutsCount -eq 8 -and $s.lastShortcutValidationError -eq "" } 5000
    Save-ViewSnapshot $hwnd "phase4_shortcuts_panel.png" $false

    # Test opening a shortcut switches back to Browser view and loads in WebView2
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_SHORTCUT, [IntPtr]0, [IntPtr]0) | Out-Null
    $stOpenedShortcut = Wait-ForStatus { param($s) $s.activePanelView -eq "Browser" -and $s.lastNavigationStatus -eq "Success" } 5000

    # Test removing the shortcut and re-adding it for restart persistence check
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_REMOVE_SHORTCUT, [IntPtr]0, [IntPtr]0) | Out-Null
    $stRemovedShortcut = Wait-ForStatus { param($s) [int]$s.shortcutsCount -eq 7 } 5000
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_ADD_SHORTCUT, [IntPtr]0, [IntPtr]0) | Out-Null
    $stReAddedShortcut = Wait-ForStatus { param($s) [int]$s.shortcutsCount -eq 8 } 5000

    $results["T06_SavedShortcutsPanelCrudAndValidation"] = [ordered]@{
        Status = if ($stShortcutsInit.shortcutsCount -eq 7 -and $stInvalidUrl.shortcutsCount -eq 7 -and $stInvalidUrl.lastShortcutValidationError -ne "" -and $stAddedShortcut.shortcutsCount -eq 8 -and $stOpenedShortcut.activePanelView -eq "Browser" -and $stRemovedShortcut.shortcutsCount -eq 7) { "PASS" } else { "FAIL" }
        DefaultShortcutsCount = $stShortcutsInit.shortcutsCount
        InvalidUrlRejectedError = $stInvalidUrl.lastShortcutValidationError
        CountAfterValidAdd = $stAddedShortcut.shortcutsCount
        CountAfterRemove = $stRemovedShortcut.shortcutsCount
        ReturnedToBrowserOnOpen = ($stOpenedShortcut.activePanelView -eq "Browser")
        Screenshot = "evidence\phase4_shortcuts_panel.png"
    }

    # =========================================================================
    # TEST 7: Notes Panel — Rich Text, Formatting, and Honest Disk Persistence
    # =========================================================================
    Write-Host "[Test 7] Opening Notes panel, updating note content, and verifying honest save status..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_VIEW, [IntPtr]3, [IntPtr]0) | Out-Null
    $stNotesOpen = Wait-ForStatus { param($s) $s.activePanelView -eq "Notes" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_UPDATE_NOTE, [IntPtr]0, [IntPtr]0) | Out-Null
    $stNoteUpdated = Wait-ForStatus { param($s) $s.noteTitle -eq "Phase 4 Persistent Note" -and $s.noteSaveSucceeded -eq $true -and $s.noteLastSavedStatus -like "Auto-saved*" } 5000
    Save-ViewSnapshot $hwnd "phase4_notes_panel.png" $false

    $notesDiskJson = Get-Content $notesFile -Raw | ConvertFrom-Json

    $results["T07_NotesPanelAndHonestPersistence"] = [ordered]@{
        Status = if ($stNoteUpdated.noteSaveSucceeded -and $stNoteUpdated.noteLastSavedStatus -like "Auto-saved*" -and $notesDiskJson.title -eq "Phase 4 Persistent Note") { "PASS" } else { "FAIL" }
        NoteTitle = $stNoteUpdated.noteTitle
        NoteLastSavedStatus = $stNoteUpdated.noteLastSavedStatus
        NoteSaveSucceeded = $stNoteUpdated.noteSaveSucceeded
        NotesJsonExistsOnDisk = (Test-Path $notesFile)
        NotesJsonTitleOnDisk = $notesDiskJson.title
        Screenshot = "evidence\phase4_notes_panel.png"
    }

    # =========================================================================
    # TEST 8: Settings Panel & Capture Protection Toggle (WDA_NONE / WDA_EXCLUDEFROMCAPTURE)
    # =========================================================================
    Write-Host "[Test 8] Opening Settings panel and testing Capture Protection toggle & persistence..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_VIEW, [IntPtr]4, [IntPtr]0) | Out-Null
    $stSettingsOpen = Wait-ForStatus { param($s) $s.activePanelView -eq "Settings" } 5000
    Save-ViewSnapshot $hwnd "phase4_settings_panel.png" $false

    # Toggle Capture Protection OFF (WDA_NONE = 0x00)
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_TOGGLE_CAPTURE_PROTECTION, [IntPtr]2, [IntPtr]0) | Out-Null
    $stCapOff = Wait-ForStatus { param($s) $s.captureProtectionEnabled -eq $false -and $s.reportedDisplayAffinityHex -eq "0x00000000" } 5000
    [uint32]$extAffOff = 99
    [Win32Phase4Helper]::GetWindowDisplayAffinity($hwnd, [ref]$extAffOff) | Out-Null

    # Toggle Capture Protection ON (WDA_EXCLUDEFROMCAPTURE = 0x11)
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_TOGGLE_CAPTURE_PROTECTION, [IntPtr]1, [IntPtr]0) | Out-Null
    $stCapOn = Wait-ForStatus { param($s) $s.captureProtectionEnabled -eq $true -and $s.reportedDisplayAffinityHex -eq "0x00000011" } 5000
    [uint32]$extAffOn = 0
    [Win32Phase4Helper]::GetWindowDisplayAffinity($hwnd, [ref]$extAffOn) | Out-Null

    $results["T08_SettingsPanelAndCaptureProtection"] = [ordered]@{
        Status = if ($stSettingsOpen.activePanelView -eq "Settings" -and $extAffOff -eq 0x00 -and $extAffOn -eq 0x11 -and $stCapOn.captureProtectionPersisted -eq $true) { "PASS" } else { "FAIL" }
        AffinityWhenDisabledHex = ("0x{0:X8}" -f $extAffOff)
        AffinityWhenEnabledHex = ("0x{0:X8}" -f $extAffOn)
        CaptureProtectionPersistedInSettingsJson = $stCapOn.captureProtectionPersisted
        Screenshot = "evidence\phase4_settings_panel.png"
    }

    # =========================================================================
    # TEST 9: About VAPOR Panel & Return to Browser View State Recovery
    # =========================================================================
    Write-Host "[Test 9] Opening About VAPOR panel and returning to Browser view to verify WebView2 recovery..."
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_VIEW, [IntPtr]5, [IntPtr]0) | Out-Null
    $stAboutOpen = Wait-ForStatus { param($s) $s.activePanelView -eq "About" } 5000
    Save-ViewSnapshot $hwnd "phase4_about_panel.png" $false

    # Return to Browser view and verify WebView2 is alive and interactive
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_OPEN_VIEW, [IntPtr]0, [IntPtr]0) | Out-Null
    $stReturnedBrowser = Wait-ForStatus { param($s) $s.activePanelView -eq "Browser" } 5000
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_INCREMENT_COUNTER, [IntPtr]0, [IntPtr]0) | Out-Null
    $stBrowserInteractive = Wait-ForStatus { param($s) [int]$s.webViewDomCounter -ge 1 } 5000

    $results["T09_AboutPanelAndBrowserStateRecovery"] = [ordered]@{
        Status = if ($stAboutOpen.activePanelView -eq "About" -and $stReturnedBrowser.activePanelView -eq "Browser" -and $stBrowserInteractive.webViewInitialized -and [int]$stBrowserInteractive.webViewDomCounter -ge 1) { "PASS" } else { "FAIL" }
        AboutPanelOpened = ($stAboutOpen.activePanelView -eq "About")
        ReturnedToBrowserView = ($stReturnedBrowser.activePanelView -eq "Browser")
        WebViewDomCounterAfterReturn = $stBrowserInteractive.webViewDomCounter
        Screenshot = "evidence\phase4_about_panel.png"
    }

    # =========================================================================
    # TEST 10: Phase 1-3 Regressions — Ultra Outside-Click Hide, Ctrl+Alt+Space Restore, Lite Mode External Use
    # =========================================================================
    Write-Host "[Test 10] Verifying Ultra Mode outside-click auto-hide, Ctrl+Alt+Space restore, and Lite Mode external app use..."
    Start-Sleep -Milliseconds 350
    $stExtA = Get-Content $extStatusA -Raw | ConvertFrom-Json
    [Win32Phase4Helper]::ClickScreenPoint([int]$stExtA.actionBtnScreenX, [int]$stExtA.actionBtnScreenY) | Out-Null
    $stUltraHidden = Wait-ForStatus { param($s) $s.isVisible -eq $false -and $s.lastHideReason -eq "UltraOutsideClick" } 5000

    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_HOTKEY, [IntPtr][Win32Phase4Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stUltraRestored = Wait-ForStatus { param($s) $s.isVisible -eq $true } 5000

    # Switch to Lite Mode and interact with ExternalTargetApp outside overlay
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_SET_MODE, [IntPtr]0, [IntPtr]0) | Out-Null
    $stLite = Wait-ForStatus { param($s) $s.currentMode -eq "Lite" } 5000

    [Win32Phase4Helper]::ClickScreenPoint([int]$stExtA.editorScreenX, [int]$stExtA.editorScreenY) | Out-Null
    [Win32Phase4Helper]::TypeAsciiString("phase4 lite external test") | Out-Null
    $stExtAAfterLite = Wait-ForJsonFile $extStatusA { param($j) $j.editorText -like "*phase4 lite external test*" } 5000
    $stOverlayDuringLite = Get-Content $statusFile -Raw | ConvertFrom-Json

    $results["T10_LiteAndUltraModeRegressions"] = [ordered]@{
        Status = if (-not $stUltraHidden.isVisible -and $stUltraRestored.isVisible -and $stOverlayDuringLite.isVisible -and $stExtAAfterLite.editorText -like "*phase4 lite external test*") { "PASS" } else { "FAIL" }
        UltraOutsideClickAutoHidden = (-not $stUltraHidden.isVisible)
        RestoredViaCtrlAltSpace = $stUltraRestored.isVisible
        LiteModeOverlayRemainedVisible = $stOverlayDuringLite.isVisible
        ExternalAppTypedTextInLiteMode = $stExtAAfterLite.editorText
    }

    # =========================================================================
    # TEST 11: Persistence Across Restart & Clean Shutdown via X Button and Menu Close
    # =========================================================================
    Write-Host "[Test 11] Closing via Header X button, restarting to verify persistence, testing Single-Instance guard, and closing via Menu Close..."
    $stBeforeClose = Get-Content $statusFile -Raw | ConvertFrom-Json
    [Win32Phase4Helper]::ClickScreenPoint([int]$stBeforeClose.closeButtonScreenX, [int]$stBeforeClose.closeButtonScreenY) | Out-Null
    $exitedX = $procOverlay.WaitForExit(6000)
    $stClosedX = Get-Content $statusFile -Raw | ConvertFrom-Json

    # Relaunch without CLI flags
    if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
    $procOverlay = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $stRestarted = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    $hwnd = [IntPtr][int64]$stRestarted.hwnd

    # Test Single-Instance guard while running
    $procDup = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru -Wait

    # Close via Three-Dot Menu Close (WM_APP_REQUEST_CLOSE wParam=1 -> "MenuClose")
    [Win32Phase4Helper]::PostMessage($hwnd, [Win32Phase4Helper]::WM_APP_REQUEST_CLOSE, [IntPtr]1, [IntPtr]0) | Out-Null
    $exitedMenu = $procOverlay.WaitForExit(6000)
    $stClosedMenu = Get-Content $statusFile -Raw | ConvertFrom-Json

    $remainingProcs = @(Get-Process -Name "DynamicOverlay.Poc" -ErrorAction SilentlyContinue).Count

    $results["T11_PersistenceAcrossRestartAndCleanShutdown"] = [ordered]@{
        Status = if ($exitedX -and $stClosedX.closeInitiator -eq "XButton" -and $stRestarted.currentMode -eq "Lite" -and $stRestarted.captureProtectionEnabled -eq $true -and [int]$stRestarted.shortcutsCount -eq 8 -and $stRestarted.noteTitle -eq "Phase 4 Persistent Note" -and $procDup.ExitCode -eq 2 -and $exitedMenu -and $stClosedMenu.closeInitiator -eq "MenuClose" -and $remainingProcs -eq 0) { "PASS" } else { "FAIL" }
        XButtonCleanExit = $exitedX
        XButtonCloseInitiator = $stClosedX.closeInitiator
        PersistedModeAfterRestart = $stRestarted.currentMode
        PersistedCaptureProtectionAfterRestart = $stRestarted.captureProtectionEnabled
        PersistedShortcutsCountAfterRestart = $stRestarted.shortcutsCount
        PersistedNoteTitleAfterRestart = $stRestarted.noteTitle
        DuplicateInstanceExitCode = $procDup.ExitCode
        MenuCloseCleanExit = $exitedMenu
        MenuCloseInitiator = $stClosedMenu.closeInitiator
        RemainingOverlayProcesses = $remainingProcs
    }

} finally {
    if ($procOverlay -and (-not $procOverlay.HasExited)) { Stop-Process -Id $procOverlay.Id -Force -ErrorAction SilentlyContinue }
    if ($procExtA -and (-not $procExtA.HasExited)) { Stop-Process -Id $procExtA.Id -Force -ErrorAction SilentlyContinue }
}

$outJsonPath = Join-Path $evidenceDir "phase4-verification-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $outJsonPath -Encoding UTF8
Write-Host "PHASE 4 VERIFICATION COMPLETED. Results saved to $outJsonPath"
$results | ConvertTo-Json -Depth 6
