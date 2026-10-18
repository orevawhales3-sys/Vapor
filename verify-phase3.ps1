$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $projectDir "bin\Release\net10.0-windows"
$pocDll = Join-Path $outDir "DynamicOverlay.Poc.dll"
$dataDir = Join-Path $env:LOCALAPPDATA "DynamicOverlayPoc"
$statusFile = Join-Path $dataDir "poc-status.json"
$settingsFile = Join-Path $dataDir "settings.json"
$dupLogFile = Join-Path $dataDir "poc-duplicate-instance.json"
$evidenceDir = Join-Path $projectDir "evidence"

New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32Phase3Helper {
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLOSE = 0x0010;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_MINIMIZE = 0xF020;
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

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
                # Ignore transient read/parse race
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

function Get-RunningOverlayProcesses() {
    $procs = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'DynamicOverlay.Poc.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*DynamicOverlay.Poc*" }
    return @($procs)
}

# Ensure ExternalTargetApp.exe exists (built in Phase 2 or compile now)
$extAppSrc = Join-Path $evidenceDir "ExternalTargetApp.cs"
$extAppExe = Join-Path $evidenceDir "ExternalTargetApp.exe"
if (-not (Test-Path $extAppExe)) {
    & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:"$extAppExe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$extAppSrc"
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile ExternalTargetApp.exe" }
}

$results = [ordered]@{}
$extStatusA = Join-Path $evidenceDir "ext-app-phase3.json"
if (Test-Path $extStatusA) { Remove-Item $extStatusA -Force }
if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }
if (Test-Path $dupLogFile) { Remove-Item $dupLogFile -Force }

$procOverlay = $null
$procExtA = $null

try {
    # Launch External Target App for Lite & Ultra regression tests
    Write-Host "[Setup] Launching ExternalTargetApp.exe outside overlay bounds..."
    $procExtA = Start-Process -FilePath $extAppExe -ArgumentList "`"ExternalEditorPhase3`" `"$extStatusA`" 820 60 440 360" -PassThru
    $stExtA = Wait-ForJsonFile $extStatusA { param($j) $j.hwnd -ne 0 } 8000

    # =========================================================================
    # TEST 1 & 15: Startup & Browser Regression
    # =========================================================================
    Write-Host "[Test 1 & 15] Launching primary Vapor overlay instance and verifying startup + WebView2 initialization..."
    $procOverlay = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $st1 = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000

    $hwndOverlay = [IntPtr][int64]$st1.hwnd
    [Win32Phase3Helper]::LastActiveRootHwnd = $hwndOverlay
    [Win32Phase3Helper]::MoveWindow($hwndOverlay, 40, 60, 700, 500, $true) | Out-Null
    $st1 = Wait-ForStatus { param($s) [int]$s.left -eq 40 -and [int]$s.width -eq 700 } 5000

    $runningOverlayProcs1 = @(Get-RunningOverlayProcesses)

    $results["T01_Startup"] = [ordered]@{
        Status = if ($st1.isVisible -and $st1.windowState -eq "Normal" -and (-not $st1.isMinimized) -and $runningOverlayProcs1.Count -eq 1) { "PASS" } else { "FAIL" }
        ProcessId = $st1.processId
        Hwnd = $st1.hwnd
        IsVisible = $st1.isVisible
        WindowState = $st1.windowState
        IsMinimized = $st1.isMinimized
        PrimaryOverlayProcessCount = $runningOverlayProcs1.Count
    }

    $results["T15_BrowserRegression"] = [ordered]@{
        Status = if ($st1.webViewInitialized -and $st1.lastNavigationStatus -eq "Success" -and $st1.currentSource -eq "poc://diagnostics-test-page") { "PASS" } else { "FAIL" }
        WebViewInitialized = $st1.webViewInitialized
        WebView2RuntimeVersion = $st1.webView2RuntimeVersion
        HostingMode = $st1.webViewHostingMode
        CurrentSource = $st1.currentSource
        LastNavigationStatus = $st1.lastNavigationStatus
    }

    # =========================================================================
    # TEST 2: Duplicate Launch Protection
    # =========================================================================
    Write-Host "[Test 2] Launching second instance while primary instance is running..."
    $procDup = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru -Wait
    $dupExitCode = $procDup.ExitCode
    $dupLog = if (Test-Path $dupLogFile) { Get-Content $dupLogFile -Raw | ConvertFrom-Json } else { $null }
    $stAfterDup = Get-Content $statusFile -Raw | ConvertFrom-Json
    $runningAfterDup = @(Get-RunningOverlayProcesses)

    $results["T02_DuplicateLaunch"] = [ordered]@{
        Status = if ($dupExitCode -eq 2 -and $dupLog -ne $null -and $dupLog.duplicateInstanceDetected -eq $true -and $runningAfterDup.Count -eq 1 -and $stAfterDup.hotkeyRegistered -eq $true) { "PASS" } else { "FAIL" }
        DuplicateExitCode = $dupExitCode
        DuplicateInstanceDetected = if ($dupLog) { $dupLog.duplicateInstanceDetected } else { $false }
        DuplicateAttemptPid = if ($dupLog) { $dupLog.processId } else { 0 }
        RunningInstanceCountAfterDuplicateAttempt = $runningAfterDup.Count
        PrimaryHotkeyStillRegistered = $stAfterDup.hotkeyRegistered
    }

    # =========================================================================
    # Modify Workspace State & Geometry for State Preservation Verification
    # =========================================================================
    Write-Host "[Setup State] Setting custom geometry (65, 75, 720x500), incrementing DOM counter to 3, and switching to Tab2-Docs..."
    [Win32Phase3Helper]::MoveWindow($hwndOverlay, 65, 75, 720, 500, $true) | Out-Null
    $stMoved = Wait-ForStatus { param($s) [int]$s.left -eq 65 -and [int]$s.top -eq 75 -and [int]$s.width -eq 720 -and [int]$s.height -eq 500 } 5000
    for ($i = 1; $i -le 3; $i++) {
        [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_INCREMENT_COUNTER, [IntPtr]0, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 120
    }
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_SWITCH_TAB, [IntPtr]2, [IntPtr]0) | Out-Null
    $stCustomState = Wait-ForStatus { param($s) $s.webViewDomCounter -eq 3 -and $s.webViewDomActiveTab -eq "Tab2-Docs" } 5000

    # =========================================================================
    # TEST 3: Shortcut Hide (Ctrl + Alt + Space while visible in normal state)
    # =========================================================================
    Write-Host "[Test 3] Pressing Ctrl + Alt + Space while visible to hide overlay..."
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stHidden = Wait-ForStatus { param($s) $s.isVisible -eq $false -and $s.hideCount -eq 1 } 5000
    $win32VisibleWhileHidden = [Win32Phase3Helper]::IsWindowVisible($hwndOverlay)

    $results["T03_ShortcutHide"] = [ordered]@{
        Status = if ((-not $stHidden.isVisible) -and (-not $win32VisibleWhileHidden) -and $stHidden.lifecycleState -eq "Hidden" -and $stHidden.lastHideReason -eq "ShortcutToggle" -and (-not $procOverlay.HasExited)) { "PASS" } else { "FAIL" }
        IsVisible = $stHidden.isVisible
        Win32IsWindowVisible = $win32VisibleWhileHidden
        LifecycleState = $stHidden.lifecycleState
        LastHideReason = $stHidden.lastHideReason
        HideCount = $stHidden.hideCount
        ProcessStillRunning = (-not $procOverlay.HasExited)
    }

    # =========================================================================
    # TEST 4: Shortcut Restore (Ctrl + Alt + Space while hidden)
    # =========================================================================
    Write-Host "[Test 4] Pressing Ctrl + Alt + Space while hidden to restore existing window..."
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stRestoredFromHidden = Wait-ForStatus { param($s) $s.isVisible -eq $true -and $s.restoreCount -eq 1 } 5000
    [Win32Phase3Helper]::LastActiveRootHwnd = $hwndOverlay

    $results["T04_ShortcutRestore"] = [ordered]@{
        Status = if ($stRestoredFromHidden.isVisible -and $stRestoredFromHidden.hwnd -eq $st1.hwnd -and $stRestoredFromHidden.windowState -eq "Normal" -and $stRestoredFromHidden.lastRestoreReason -eq "ShortcutRestoreFromHidden") { "PASS" } else { "FAIL" }
        IsVisible = $stRestoredFromHidden.isVisible
        SameHwnd = ($stRestoredFromHidden.hwnd -eq $st1.hwnd)
        WindowState = $stRestoredFromHidden.windowState
        LastRestoreReason = $stRestoredFromHidden.lastRestoreReason
        RestoreCount = $stRestoredFromHidden.restoreCount
        RestoredGeometry = "$($stRestoredFromHidden.left),$($stRestoredFromHidden.top) $($stRestoredFromHidden.width)x$($stRestoredFromHidden.height)"
    }

    # =========================================================================
    # TEST 5: Minimize (Standard Windows Minimized State, Distinct from Hide & Close)
    # =========================================================================
    Write-Host "[Test 5] Minimizing the overlay window and verifying it enters Minimized state while staying alive..."
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_MINIMIZE_WINDOW, [IntPtr]0, [IntPtr]0) | Out-Null
    $stMinimized = Wait-ForStatus { param($s) $s.isMinimized -eq $true -and $s.windowState -eq "Minimized" } 5000
    $isIconicNative = [Win32Phase3Helper]::IsIconic($hwndOverlay)

    $results["T05_Minimize"] = [ordered]@{
        Status = if ($stMinimized.isMinimized -and $stMinimized.windowState -eq "Minimized" -and $stMinimized.lifecycleState -eq "Minimized" -and $isIconicNative -and (-not $procOverlay.HasExited) -and [int]$stMinimized.left -eq 65) { "PASS" } else { "FAIL" }
        IsMinimized = $stMinimized.isMinimized
        WindowState = $stMinimized.windowState
        LifecycleState = $stMinimized.lifecycleState
        Win32IsIconic = $isIconicNative
        MinimizeCount = $stMinimized.minimizeCount
        SavedNormalGeometryPreserved = "$($stMinimized.left),$($stMinimized.top) $($stMinimized.width)x$($stMinimized.height)"
        ProcessStillRunning = (-not $procOverlay.HasExited)
    }

    # =========================================================================
    # TEST 6: Shortcut After Minimize (Ctrl + Alt + Space restores minimized window)
    # =========================================================================
    Write-Host "[Test 6] Pressing Ctrl + Alt + Space while minimized to verify it restores to Normal visible state..."
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stRestoredFromMin = Wait-ForStatus { param($s) $s.isMinimized -eq $false -and $s.windowState -eq "Normal" -and $s.isVisible -eq $true -and $s.restoreCount -eq 2 } 5000
    $isIconicAfterRestore = [Win32Phase3Helper]::IsIconic($hwndOverlay)
    [Win32Phase3Helper]::LastActiveRootHwnd = $hwndOverlay

    # Sync DOM state to verify browser state after minimize->restore
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_SYNC_DOM, [IntPtr]0, [IntPtr]0) | Out-Null
    Start-Sleep -Milliseconds 250
    $stRestoredFromMin = Get-Content $statusFile -Raw | ConvertFrom-Json

    $results["T06_ShortcutAfterMinimize"] = [ordered]@{
        Status = if ($stRestoredFromMin.isVisible -and (-not $stRestoredFromMin.isMinimized) -and (-not $isIconicAfterRestore) -and $stRestoredFromMin.windowState -eq "Normal" -and $stRestoredFromMin.lastRestoreReason -eq "ShortcutRestoreFromMinimized" -and $stRestoredFromMin.hwnd -eq $st1.hwnd) { "PASS" } else { "FAIL" }
        IsVisible = $stRestoredFromMin.isVisible
        IsMinimized = $stRestoredFromMin.isMinimized
        Win32IsIconic = $isIconicAfterRestore
        WindowState = $stRestoredFromMin.windowState
        LastRestoreReason = $stRestoredFromMin.lastRestoreReason
        SameHwnd = ($stRestoredFromMin.hwnd -eq $st1.hwnd)
        RestoredGeometry = "$($stRestoredFromMin.left),$($stRestoredFromMin.top) $($stRestoredFromMin.width)x$($stRestoredFromMin.height)"
    }

    # =========================================================================
    # TEST 13: State Preservation Across Hide/Restore and Minimize/Restore
    # =========================================================================
    $geomPreserved = ([int]$stRestoredFromMin.left -eq 65 -and [int]$stRestoredFromMin.top -eq 75 -and [int]$stRestoredFromMin.width -eq 720 -and [int]$stRestoredFromMin.height -eq 500)
    $domPreserved = ($stRestoredFromMin.webViewDomCounter -eq 3 -and $stRestoredFromMin.webViewDomActiveTab -eq "Tab2-Docs" -and $stRestoredFromMin.webViewDomNoteValue -eq "State persists across Lite switches & Ultra hide/restore!")
    $results["T13_StatePreservation"] = [ordered]@{
        Status = if ($geomPreserved -and $domPreserved -and $stRestoredFromMin.currentMode -eq "Lite" -and $stRestoredFromMin.currentSource -eq "poc://diagnostics-test-page") { "PASS" } else { "FAIL" }
        ModePreserved = $stRestoredFromMin.currentMode
        GeometryPreserved = "$($stRestoredFromMin.left),$($stRestoredFromMin.top) $($stRestoredFromMin.width)x$($stRestoredFromMin.height)"
        WebViewDomCounter = $stRestoredFromMin.webViewDomCounter
        WebViewDomActiveTab = $stRestoredFromMin.webViewDomActiveTab
        WebViewDomNoteValue = $stRestoredFromMin.webViewDomNoteValue
        CurrentSource = $stRestoredFromMin.currentSource
    }

    # =========================================================================
    # TEST 7: Rapid Transitions (Repeated Hide -> Restore -> Minimize -> Restore Cycles)
    # =========================================================================
    Write-Host "[Test 7] Executing 5 rapid Hide -> Shortcut Restore -> Minimize -> Shortcut Restore cycles..."
    $baseHideCount = [int]$stRestoredFromMin.hideCount
    $baseMinCount = [int]$stRestoredFromMin.minimizeCount
    $baseRestoreCount = [int]$stRestoredFromMin.restoreCount

    for ($cycle = 1; $cycle -le 5; $cycle++) {
        $targetHide = $baseHideCount + $cycle
        $targetRestore1 = $baseRestoreCount + (($cycle - 1) * 2) + 1
        $targetMin = $baseMinCount + $cycle
        $targetRestore2 = $baseRestoreCount + ($cycle * 2)

        # 1. Shortcut Hide
        [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
        Wait-ForStatus { param($s) $s.hideCount -eq $targetHide -and $s.isVisible -eq $false } 5000 | Out-Null

        # 2. Shortcut Restore from Hidden
        [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
        Wait-ForStatus { param($s) $s.restoreCount -eq $targetRestore1 -and $s.isVisible -eq $true } 5000 | Out-Null

        # 3. Minimize
        [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_MINIMIZE_WINDOW, [IntPtr]0, [IntPtr]0) | Out-Null
        Wait-ForStatus { param($s) $s.minimizeCount -eq $targetMin -and $s.isMinimized -eq $true } 5000 | Out-Null

        # 4. Shortcut Restore from Minimized
        [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
        Wait-ForStatus { param($s) $s.restoreCount -eq $targetRestore2 -and $s.isMinimized -eq $false -and $s.isVisible -eq $true } 5000 | Out-Null
    }

    $stAfterRapid = Get-Content $statusFile -Raw | ConvertFrom-Json
    $procsAfterRapid = @(Get-RunningOverlayProcesses)

    $results["T07_RapidTransitions"] = [ordered]@{
        Status = if ($stAfterRapid.isVisible -and (-not $stAfterRapid.isMinimized) -and $stAfterRapid.windowState -eq "Normal" -and $stAfterRapid.hwnd -eq $st1.hwnd -and $procsAfterRapid.Count -eq 1 -and [int]$stAfterRapid.left -eq 65) { "PASS" } else { "FAIL" }
        TotalHideCount = $stAfterRapid.hideCount
        TotalMinimizeCount = $stAfterRapid.minimizeCount
        TotalRestoreCount = $stAfterRapid.restoreCount
        SameHwndPreserved = ($stAfterRapid.hwnd -eq $st1.hwnd)
        RunningOverlayProcessCount = $procsAfterRapid.Count
        FinalWindowState = $stAfterRapid.windowState
        FinalGeometry = "$($stAfterRapid.left),$($stAfterRapid.top) $($stAfterRapid.width)x$($stAfterRapid.height)"
    }

    # =========================================================================
    # TEST 8: Lite Mode Regression
    # =========================================================================
    Write-Host "[Test 8] Verifying Lite Mode regression: External app button clicks and typing while overlay stays visible..."
    $stExtA = Get-Content $extStatusA -Raw | ConvertFrom-Json
    [Win32Phase3Helper]::ClickScreenPoint([int]$stExtA.actionBtnScreenX, [int]$stExtA.actionBtnScreenY) | Out-Null
    [Win32Phase3Helper]::ClickScreenPoint([int]$stExtA.menuBtnScreenX, [int]$stExtA.menuBtnScreenY) | Out-Null
    [Win32Phase3Helper]::ClickScreenPoint([int]$stExtA.editorScreenX, [int]$stExtA.editorScreenY) | Out-Null
    [Win32Phase3Helper]::TypeAsciiString("phase3 lite regression ok") | Out-Null
    $stExtAAfter = Wait-ForJsonFile $extStatusA { param($j) $j.buttonClicks -ge 1 -and $j.menuClicks -ge 1 -and ($j.editorText -like "*phase3 lite regression ok*") } 5000
    $stOverlayDuringLite = Get-Content $statusFile -Raw | ConvertFrom-Json

    $results["T08_LiteModeRegression"] = [ordered]@{
        Status = if ($stOverlayDuringLite.isVisible -and $stOverlayDuringLite.currentMode -eq "Lite" -and $stExtAAfter.buttonClicks -ge 1 -and $stExtAAfter.menuClicks -ge 1 -and ($stExtAAfter.editorText -like "*phase3 lite regression ok*")) { "PASS" } else { "FAIL" }
        OverlayRemainedVisible = $stOverlayDuringLite.isVisible
        ExternalButtonClicks = $stExtAAfter.buttonClicks
        ExternalMenuClicks = $stExtAAfter.menuClicks
        ExternalEditorText = $stExtAAfter.editorText
        NoKeyboardLeakIntoOverlayUrlBox = ($stOverlayDuringLite.urlBoxText -eq "poc://diagnostics-test-page")
    }

    # =========================================================================
    # TEST 9: Ultra Mode Regression (Including Minimize vs Outside-Click Distinction)
    # =========================================================================
    Write-Host "[Test 9] Switching to Ultra Mode and verifying outside-click hide, shortcut restore, and minimize distinction..."
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_SET_MODE, [IntPtr]1, [IntPtr]0) | Out-Null
    $stUltra = Wait-ForStatus { param($s) $s.currentMode -eq "Ultra" -and $s.persistedMode -eq "Ultra" } 5000
    [Win32Phase3Helper]::ClickScreenPoint(250, 220) | Out-Null
    Start-Sleep -Milliseconds 200

    # Verify minimizing in Ultra Mode keeps window Minimized (does NOT trigger UltraOutsideClick hide!)
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_MINIMIZE_WINDOW, [IntPtr]0, [IntPtr]0) | Out-Null
    $stUltraMin = Wait-ForStatus { param($s) $s.isMinimized -eq $true -and $s.windowState -eq "Minimized" } 5000
    Start-Sleep -Milliseconds 250
    $stUltraMinAfterSettle = Get-Content $statusFile -Raw | ConvertFrom-Json
    $ultraMinDidNotHide = ($stUltraMinAfterSettle.isMinimized -eq $true -and $stUltraMinAfterSettle.ultraAutoHideCount -eq 0)

    # Restore from minimized in Ultra Mode via Ctrl + Alt + Space
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    Wait-ForStatus { param($s) $s.isMinimized -eq $false -and $s.isVisible -eq $true } 5000 | Out-Null
    [Win32Phase3Helper]::LastActiveRootHwnd = $hwndOverlay
    Start-Sleep -Milliseconds 250

    # Now click outside into External App A to trigger Ultra Mode outside-click hide
    [Win32Phase3Helper]::ClickScreenPoint([int]$stExtA.editorScreenX, [int]$stExtA.editorScreenY) | Out-Null
    $stUltraHidden = Wait-ForStatus { param($s) $s.isVisible -eq $false -and $s.ultraAutoHideCount -eq 1 -and $s.lastHideReason -eq "UltraOutsideClick" } 5000

    # Restore via Ctrl + Alt + Space and sync DOM
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_HOTKEY, [IntPtr][Win32Phase3Helper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
    $stUltraRestored = Wait-ForStatus { param($s) $s.isVisible -eq $true } 5000
    [Win32Phase3Helper]::LastActiveRootHwnd = $hwndOverlay
    [Win32Phase3Helper]::PostMessage($hwndOverlay, [Win32Phase3Helper]::WM_APP_SYNC_DOM, [IntPtr]0, [IntPtr]0) | Out-Null
    Start-Sleep -Milliseconds 250
    $stUltraRestored = Get-Content $statusFile -Raw | ConvertFrom-Json

    $results["T09_UltraModeRegression"] = [ordered]@{
        Status = if ($ultraMinDidNotHide -and (-not $stUltraHidden.isVisible) -and $stUltraHidden.lastHideReason -eq "UltraOutsideClick" -and $stUltraRestored.isVisible -and $stUltraRestored.webViewDomCounter -eq 3 -and $stUltraRestored.webViewDomActiveTab -eq "Tab2-Docs") { "PASS" } else { "FAIL" }
        MinimizeInUltraModeRemainedMinimizedNotHidden = $ultraMinDidNotHide
        OutsideClickHidden = (-not $stUltraHidden.isVisible)
        LastHideReason = $stUltraHidden.lastHideReason
        RestoredViaShortcut = $stUltraRestored.isVisible
        WorkspaceCounterPreserved = $stUltraRestored.webViewDomCounter
        WorkspaceTabPreserved = $stUltraRestored.webViewDomActiveTab
    }

    # =========================================================================
    # TEST 10 & 11: X Control Close & Shutdown Cleanup
    # =========================================================================
    Write-Host "[Test 10 & 11] Clicking top-right X button to close Vapor and verifying full shutdown cleanup..."
    $rect = New-Object Win32Phase3Helper+RECT
    [Win32Phase3Helper]::GetWindowRect($hwndOverlay, [ref]$rect) | Out-Null
    $xBtnScreenX = if ([int]$stUltraRestored.closeButtonScreenX -gt 0) { [int]$stUltraRestored.closeButtonScreenX } else { $rect.Right - 22 }
    $xBtnScreenY = if ([int]$stUltraRestored.closeButtonScreenY -gt 0) { [int]$stUltraRestored.closeButtonScreenY } else { $rect.Top + 22 }
    Write-Host "  [Diag] Clicking X button at screen=($xBtnScreenX,$xBtnScreenY), rect=($($rect.Left),$($rect.Top)..$($rect.Right),$($rect.Bottom)), hwndOverlay=$hwndOverlay"
    $hitXHwnd = [Win32Phase3Helper]::ClickScreenPoint($xBtnScreenX, $xBtnScreenY)

    $exitedOnX = $procOverlay.WaitForExit(8000)
    $xExitCode = if ($exitedOnX) { $procOverlay.ExitCode } else { -1 }
    $stClosedByX = Get-Content $statusFile -Raw | ConvertFrom-Json
    Write-Host "  [Diag] hitXHwnd=$hitXHwnd, exitedOnX=$exitedOnX, exitCode=$xExitCode, lifecycleState=$($stClosedByX.lifecycleState), closeInitiator=$($stClosedByX.closeInitiator)"

    $results["T10_XControlClose"] = [ordered]@{
        Status = if ($exitedOnX -and $xExitCode -eq 0 -and $stClosedByX.closeInitiator -eq "XButton" -and $stClosedByX.lifecycleState -eq "Closed") { "PASS" } else { "FAIL" }
        ClickedScreenCoordinates = "${xBtnScreenX},${xBtnScreenY}"
        ProcessExitedWithinTimeout = $exitedOnX
        ExitCode = $xExitCode
        CloseInitiator = $stClosedByX.closeInitiator
        FinalLifecycleState = $stClosedByX.lifecycleState
    }

    $results["T11_ShutdownCleanup"] = [ordered]@{
        Status = if ($stClosedByX.isShuttingDown -and $stClosedByX.shutdownCleanupCompleted -and (-not $stClosedByX.hotkeyRegistered) -and $stClosedByX.hotkeyUnregistered -and $stClosedByX.hotkeyUnregisterCount -eq 1 -and $stClosedByX.webViewDisposed -and $stClosedByX.singleInstanceMutexReleased) { "PASS" } else { "FAIL" }
        IsShuttingDown = $stClosedByX.isShuttingDown
        ShutdownCleanupCompleted = $stClosedByX.shutdownCleanupCompleted
        HotkeyRegistered = $stClosedByX.hotkeyRegistered
        HotkeyUnregistered = $stClosedByX.hotkeyUnregistered
        HotkeyUnregisterCount = $stClosedByX.hotkeyUnregisterCount
        WebViewDisposed = $stClosedByX.webViewDisposed
        SingleInstanceMutexReleased = $stClosedByX.singleInstanceMutexReleased
    }

    # =========================================================================
    # TEST 14 & MenuClose Path + TEST 12: Settings Persistence, Reusable MenuClose Path, & No Orphan Process
    # =========================================================================
    Write-Host "[Test 14 & MenuClose] Relaunching Vapor to verify Ultra Mode persistence, reusable MenuClose shutdown path, and zero orphan processes..."
    if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
    $procOverlay = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $stRestartUltra = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    $hwndOverlay2 = [IntPtr][int64]$stRestartUltra.hwnd

    # Switch back to Lite Mode so default Lite Mode is persisted on close, then trigger WM_APP_REQUEST_CLOSE with wParam=1 (which invokes RequestApplicationClose("MenuClose"))
    [Win32Phase3Helper]::PostMessage($hwndOverlay2, [Win32Phase3Helper]::WM_APP_SET_MODE, [IntPtr]0, [IntPtr]0) | Out-Null
    Wait-ForStatus { param($s) $s.currentMode -eq "Lite" -and $s.persistedMode -eq "Lite" } 5000 | Out-Null

    [Win32Phase3Helper]::PostMessage($hwndOverlay2, [Win32Phase3Helper]::WM_APP_REQUEST_CLOSE, [IntPtr]1, [IntPtr]0) | Out-Null
    $exitedOnMenuClose = $procOverlay.WaitForExit(8000)
    $menuCloseExitCode = if ($exitedOnMenuClose) { $procOverlay.ExitCode } else { -1 }
    $stClosedByMenu = Get-Content $statusFile -Raw | ConvertFrom-Json

    # Relaunch once more to verify Lite Mode persisted across restart, then close via X button
    if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
    $procOverlay = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $stRestartLite = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    $hwndOverlay3 = [IntPtr][int64]$stRestartLite.hwnd

    [Win32Phase3Helper]::PostMessage($hwndOverlay3, [Win32Phase3Helper]::WM_CLOSE, [IntPtr]0, [IntPtr]0) | Out-Null
    $procOverlay.WaitForExit(8000) | Out-Null

    Start-Sleep -Milliseconds 600
    $remainingOverlayProcs = @(Get-RunningOverlayProcesses)

    $results["T14_SettingsPersistence"] = [ordered]@{
        Status = if ($stRestartUltra.currentMode -eq "Ultra" -and $stRestartUltra.hotkeyRegistered -and $stRestartLite.currentMode -eq "Lite" -and $stRestartLite.hotkeyRegistered -and $exitedOnMenuClose -and $stClosedByMenu.closeInitiator -eq "MenuClose") { "PASS" } else { "FAIL" }
        ModeRestoredAfterUltraShutdown = $stRestartUltra.currentMode
        HotkeyRegisteredOnRelaunch = $stRestartUltra.hotkeyRegistered
        ModeRestoredAfterLiteShutdown = $stRestartLite.currentMode
        ReusableMenuCloseInitiatorVerified = $stClosedByMenu.closeInitiator
        ReusableMenuCloseExitCode = $menuCloseExitCode
    }

    $results["T12_NoOrphanProcess"] = [ordered]@{
        Status = if ($remainingOverlayProcs.Count -eq 0) { "PASS" } else { "FAIL" }
        RemainingOverlayProcessCount = $remainingOverlayProcs.Count
    }

} finally {
    if ($procOverlay -ne $null -and (-not $procOverlay.HasExited)) {
        Stop-Process -Id $procOverlay.Id -Force -ErrorAction SilentlyContinue
    }
    if ($procExtA -ne $null -and (-not $procExtA.HasExited)) {
        Stop-Process -Id $procExtA.Id -Force -ErrorAction SilentlyContinue
    }
    $resultsPath = Join-Path $evidenceDir "phase3-verification-results.json"
    $resultsJson = $results | ConvertTo-Json -Depth 6
    Set-Content -Path $resultsPath -Value $resultsJson -Encoding UTF8
}

Write-Host ""
Write-Host "============================================================="
Write-Host "PHASE 03 VERIFICATION RESULTS SUMMARY"
Write-Host "============================================================="
$allPassed = $true
foreach ($key in $results.Keys) {
    $st = $results[$key].Status
    Write-Host ("{0,-35} : {1}" -f $key, $st)
    if ($st -ne "PASS") {
        $allPassed = $false
    }
}
Write-Host "============================================================="
Write-Host "Saved full evidence to: $resultsPath"
if (-not $allPassed) {
    exit 1
}
exit 0
