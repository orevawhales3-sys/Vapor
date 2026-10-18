$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $projectDir "bin\Release\net10.0-windows"
$pocDll = Join-Path $outDir "DynamicOverlay.Poc.dll"
$dataDir = Join-Path $env:LOCALAPPDATA "DynamicOverlayPoc"
$statusFile = Join-Path $dataDir "poc-status.json"
$evidenceDir = Join-Path $projectDir "evidence"
$artifactDir = "C:\Users\harshit\.gemini\antigravity\brain\a9ee4394-2173-4282-b2a8-6d84fa58d789"

New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32TestHelper {
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLOSE = 0x0010;
    public const int HOTKEY_ID_TOGGLE = 0x9001;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
}
"@

function Get-DesktopEnvironmentInfo() {
    $winsta = [Win32TestHelper]::GetProcessWindowStation()
    $desk = [Win32TestHelper]::GetThreadDesktop([Win32TestHelper]::GetCurrentThreadId())
    $sb1 = New-Object System.Text.StringBuilder(256); $len = 0
    [Win32TestHelper]::GetUserObjectInformation($winsta, 2, $sb1, 256, [ref]$len) | Out-Null
    $sb2 = New-Object System.Text.StringBuilder(256)
    [Win32TestHelper]::GetUserObjectInformation($desk, 2, $sb2, 256, [ref]$len) | Out-Null
    return [ordered]@{
        WindowStation = $sb1.ToString()
        ThreadDesktop = $sb2.ToString()
        SessionId = (Get-Process -Id $PID).SessionId
    }
}

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
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out after ${timeoutMs}ms waiting for poc-status.json condition."
}

function Capture-WindowViaPrintWindow([IntPtr]$hwnd, [int]$w, [int]$h, [string]$outPath) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $pwOk = [Win32TestHelper]::PrintWindow($hwnd, $hdc, [Win32TestHelper]::PW_RENDERFULLCONTENT)
    $pwErr = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

    # Sample pixels across header, center, and footer
    $headerPx = $bmp.GetPixel([int]($w / 2), 20)
    $centerPx = $bmp.GetPixel([int]($w / 2), [int]($h / 2))
    $bmp.Dispose()
    return [ordered]@{
        PrintWindowReturn = $pwOk
        LastError = $pwErr
        HeaderPixelRGB = "$($headerPx.R),$($headerPx.G),$($headerPx.B)"
        CenterPixelRGB = "$($centerPx.R),$($centerPx.G),$($centerPx.B)"
        IsAllBlack = ($headerPx.R -eq 0 -and $headerPx.G -eq 0 -and $headerPx.B -eq 0 -and $centerPx.R -eq 0 -and $centerPx.G -eq 0 -and $centerPx.B -eq 0)
        OutputPath = $outPath
    }
}

$results = [ordered]@{}
$results["Environment"] = Get-DesktopEnvironmentInfo

# Create an ordinary desktop reference window
$bgForm = New-Object System.Windows.Forms.Form
$bgForm.Text = "Ordinary Desktop Reference Window"
$bgForm.BackColor = [System.Drawing.Color]::FromArgb(255, 255, 0, 255)
$bgForm.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$bgForm.Size = New-Object System.Drawing.Size(1020, 680)
$bgForm.Show()
[System.Windows.Forms.Application]::DoEvents()

try {
    # =========================================================================
    # TEST 1: Launch PoC (HWND Host Mode), Verify DWM, Topmost, Hotkey, WebView2, Affinity
    # =========================================================================
    if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
    Write-Host "[Test 1] Launching DynamicOverlay.Poc (HWND Host Mode, WDA_EXCLUDEFROMCAPTURE ON)..."
    $proc1 = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru
    $status1 = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    Start-Sleep -Milliseconds 1000

    $uiSnap1 = Join-Path $dataDir "poc-ui-snapshot.png"
    $wv2Snap1 = Join-Path $dataDir "poc-webview2-preview.png"
    $testPageCompositePath = Join-Path $evidenceDir "poc_testpage_interface_composite.png"
    if ((Test-Path $uiSnap1) -and (Test-Path $wv2Snap1)) {
        $baseBmp1 = [System.Drawing.Bitmap]::FromFile($uiSnap1)
        $webBmp1 = [System.Drawing.Bitmap]::FromFile($wv2Snap1)
        $gComp1 = [System.Drawing.Graphics]::FromImage($baseBmp1)
        $gComp1.DrawImage($webBmp1, 10, 48, $baseBmp1.Width - 20, $baseBmp1.Height - 82)
        $gComp1.Dispose()
        $baseBmp1.Save($testPageCompositePath, [System.Drawing.Imaging.ImageFormat]::Png)
        $webBmp1.Dispose()
        $baseBmp1.Dispose()
        Copy-Item $testPageCompositePath (Join-Path $artifactDir "poc_testpage_interface_composite.png") -Force
    }

    $hwnd = [IntPtr][int64]$status1.hwnd
    $exStyle = [Win32TestHelper]::GetWindowLong($hwnd, [Win32TestHelper]::GWL_EXSTYLE)
    $hasTopmostFlag = ($exStyle -band [Win32TestHelper]::WS_EX_TOPMOST) -ne 0
    [uint32]$extAffinity = 0
    $extGetAffinityOk = [Win32TestHelper]::GetWindowDisplayAffinity($hwnd, [ref]$extAffinity)

    $results["Test1_InitialLaunchAndWebView2"] = [ordered]@{
        Status = if ($status1.webViewInitialized -and $hasTopmostFlag -and $status1.hotkeyRegistered -and $status1.setWindowDisplayAffinityReturn -and ($extAffinity -eq 0x11)) { "PASS" } else { "FAIL" }
        DotNetVersion = $status1.dotnetVersion
        OsVersion = $status1.osVersion
        WebView2RuntimeVersion = $status1.webView2RuntimeVersion
        HostingMode = $status1.webViewHostingMode
        Hwnd = $status1.hwnd
        WsExTopmostVerified = $hasTopmostFlag
        DwmDarkHResult = $status1.dwmDarkHResult
        DwmCornerHResult = $status1.dwmCornerHResult
        HotkeyRegistered = $status1.hotkeyRegistered
        SetWindowDisplayAffinityReturn = $status1.setWindowDisplayAffinityReturn
        ReportedDisplayAffinityHex = $status1.reportedDisplayAffinityHex
        ExternalGetWindowDisplayAffinity = ("0x{0:X8}" -f $extAffinity)
        InitialBounds = "$($status1.left),$($status1.top) $($status1.width)x$($status1.height)"
        MinBounds = "$($status1.minWidth)x$($status1.minHeight)"
    }

    # =========================================================================
    # TEST 2: Single-Instance Mutex Guard Verification
    # =========================================================================
    Write-Host "[Test 2] Verifying Single-Instance Guard..."
    $procDup = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`"" -PassThru -Wait
    $results["Test2_SingleInstanceGuard"] = [ordered]@{
        Status = if ($procDup.ExitCode -eq 2) { "PASS" } else { "FAIL" }
        DuplicateProcessExitCode = $procDup.ExitCode
        ExpectedExitCode = 2
    }

    # =========================================================================
    # TEST 3: Capture Behavior With WDA_EXCLUDEFROMCAPTURE (0x11) Active
    # =========================================================================
    Write-Host "[Test 3] Testing capture paths while WDA_EXCLUDEFROMCAPTURE (0x11) is active..."
    $copyFromScreenError = $null
    try {
        $testBmp = New-Object System.Drawing.Bitmap(100, 100)
        $testG = [System.Drawing.Graphics]::FromImage($testBmp)
        $testG.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(100, 100)))
        $testG.Dispose()
        $testBmp.Dispose()
    } catch {
        $copyFromScreenError = $_.Exception.Message
    }

    $excludedPwPath = Join-Path $evidenceDir "poc_printwindow_wda_excludefromcapture.png"
    $pwExcludedResult = Capture-WindowViaPrintWindow $hwnd ([int]$status1.width) ([int]$status1.height) $excludedPwPath
    Copy-Item $excludedPwPath (Join-Path $artifactDir "poc_printwindow_wda_excludefromcapture.png") -Force

    $results["Test3_CaptureWithExclusionActive"] = [ordered]@{
        SetWindowDisplayAffinityReturn = $status1.setWindowDisplayAffinityReturn
        GetWindowDisplayAffinityHex = ("0x{0:X8}" -f $extAffinity)
        PrintWindowCaptureResult = $pwExcludedResult
        DesktopBitBltCopyFromScreenNote = if ($copyFromScreenError) { "Unavailable in non-interactive desktop ($($results['Environment'].ThreadDesktop)): $copyFromScreenError" } else { "Succeeded" }
    }

    # =========================================================================
    # TEST 4: Repeated Show/Hide Cycles via Ctrl+Alt+Space (WM_HOTKEY)
    # =========================================================================
    Write-Host "[Test 4] Running 6 consecutive Ctrl+Alt+Space toggle cycles..."
    $toggleLog = @()
    for ($i = 1; $i -le 6; $i++) {
        [Win32TestHelper]::PostMessage($hwnd, [Win32TestHelper]::WM_HOTKEY, [IntPtr][Win32TestHelper]::HOTKEY_ID_TOGGLE, [IntPtr]0) | Out-Null
        $expectedVisible = ($i % 2 -eq 0)
        $st = Wait-ForStatus { param($s) $s.toggleCount -eq $i -and $s.isVisible -eq $expectedVisible } 5000
        $toggleLog += "Cycle ${i}: isVisible=$($st.isVisible), bounds=($($st.left),$($st.top) $($st.width)x$($st.height))"
    }
    $results["Test4_RepeatedHotkeyToggles"] = [ordered]@{
        Status = if ($st.toggleCount -eq 6 -and $st.isVisible -eq $true -and $st.webViewInitialized -eq $true) { "PASS" } else { "FAIL" }
        TotalToggles = $st.toggleCount
        FinalVisible = $st.isVisible
        StatePreserved = ($st.width -eq $status1.width -and $st.height -eq $status1.height)
        CycleDetails = $toggleLog
    }

    # =========================================================================
    # TEST 5: Window Movement & Resizing + Minimum Bounds Enforcement
    # =========================================================================
    Write-Host "[Test 5] Testing window move, resize, and minimum bounds enforcement..."
    $newX = [int]($status1.left + 30)
    $newY = [int]($status1.top + 20)
    [Win32TestHelper]::MoveWindow($hwnd, $newX, $newY, 700, 450, $true) | Out-Null
    $stResize = Wait-ForStatus { param($s) [int]$s.width -eq 700 -and [int]$s.height -eq 450 } 5000

    # Attempt resize below minimum bounds (300x200 -> should clamp to MinWidth 520 x MinHeight 380)
    [Win32TestHelper]::MoveWindow($hwnd, $newX, $newY, 300, 200, $true) | Out-Null
    $stMinClamp = Wait-ForStatus { param($s) [int]$s.width -eq 520 -and [int]$s.height -eq 380 } 5000

    # Restore 860x560
    [Win32TestHelper]::MoveWindow($hwnd, [int]$status1.left, [int]$status1.top, 860, 560, $true) | Out-Null
    $stRestored = Wait-ForStatus { param($s) [int]$s.width -eq 860 -and [int]$s.height -eq 560 } 5000

    $results["Test5_MoveResizeAndMinBounds"] = [ordered]@{
        Status = if ([int]$stResize.width -eq 700 -and [int]$stMinClamp.width -eq 520 -and [int]$stMinClamp.height -eq 380) { "PASS" } else { "FAIL" }
        ResizedBounds = "$($stResize.left),$($stResize.top) $($stResize.width)x$($stResize.height)"
        MinClampedBoundsWhenRequested300x200 = "$($stMinClamp.width)x$($stMinClamp.height)"
        RestoredBounds = "$($stRestored.left),$($stRestored.top) $($stRestored.width)x$($stRestored.height)"
    }

    # =========================================================================
    # TEST 6: URL Navigation Test (Navigate to https://example.com)
    # =========================================================================
    Write-Host "[Test 6] Closing Instance 1 and verifying clean shutdown + hotkey release..."
    [Win32TestHelper]::PostMessage($hwnd, [Win32TestHelper]::WM_CLOSE, [IntPtr]0, [IntPtr]0) | Out-Null
    $proc1.WaitForExit(5000) | Out-Null
    $stClosed = Get-Content $statusFile -Raw | ConvertFrom-Json

    $mod = [Win32TestHelper]::MOD_CONTROL -bor [Win32TestHelper]::MOD_ALT -bor [Win32TestHelper]::MOD_NOREPEAT
    $canClaimAfterExit = [Win32TestHelper]::RegisterHotKey([IntPtr]::Zero, 0x7777, $mod, [Win32TestHelper]::VK_SPACE)

    $results["Test6_CleanShutdownAndHotkeyRelease"] = [ordered]@{
        Status = if ($proc1.HasExited -and $stClosed.hotkeyUnregistered -and $canClaimAfterExit) { "PASS" } else { "FAIL" }
        ProcessExitedCleanly = $proc1.HasExited
        HotkeyUnregisteredByApp = $stClosed.hotkeyUnregistered
        HotkeyAvailableAfterExit = $canClaimAfterExit
    }

    # =========================================================================
    # TEST 7: Simulated Hotkey Conflict Detection (Pre-Claimed Ctrl+Alt+Space)
    # =========================================================================
    Write-Host "[Test 7] Launching PoC while Ctrl+Alt+Space is pre-claimed to verify conflict detection..."
    try {
        if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
        $procConflict = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`" --disable-affinity-on-start" -PassThru
        $stConflict = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
        Start-Sleep -Milliseconds 800

        $conflictImgPath = Join-Path $evidenceDir "poc_conflict_banner_visible.png"
        $pwConflict = Capture-WindowViaPrintWindow ([IntPtr][int64]$stConflict.hwnd) ([int]$stConflict.width) ([int]$stConflict.height) $conflictImgPath
        Copy-Item $conflictImgPath (Join-Path $artifactDir "poc_conflict_banner_visible.png") -Force

        $results["Test7_HotkeyConflictDetection"] = [ordered]@{
            Status = if ($stConflict.hotkeyRegistered -eq $false -and $stConflict.hotkeyLastError -eq 1409) { "PASS" } else { "FAIL" }
            HotkeyRegistered = $stConflict.hotkeyRegistered
            Win32ErrorCode = $stConflict.hotkeyLastError
            ExpectedErrorCode = 1409
            PrintWindowCapture = $pwConflict
        }

        [Win32TestHelper]::PostMessage([IntPtr][int64]$stConflict.hwnd, [Win32TestHelper]::WM_CLOSE, [IntPtr]0, [IntPtr]0) | Out-Null
        $procConflict.WaitForExit(5000) | Out-Null
    } finally {
        [Win32TestHelper]::UnregisterHotKey([IntPtr]::Zero, 0x7777) | Out-Null
    }

    # =========================================================================
    # TEST 8: Capture Visible Prototype Interface (WDA_NONE) & External URL Navigation
    # =========================================================================
    Write-Host "[Test 8] Capturing visible prototype interface (WDA_NONE) and testing URL navigation..."
    $localHtmlTest = Join-Path $evidenceDir "nav-test.html"
    Set-Content -Path $localHtmlTest -Value "<!DOCTYPE html><html><body style='background:#0f172a;color:#38bdf8;font-family:Segoe UI;padding:24px;'><h1>Navigated Local Test Page</h1><p>URL Navigation verified in Phase 01 PoC.</p></body></html>" -Encoding UTF8
    $fileUri = ([System.Uri]$localHtmlTest).AbsoluteUri

    if (Test-Path $statusFile) { Remove-Item $statusFile -Force }
    $procVis = Start-Process -FilePath "dotnet" -ArgumentList "`"$pocDll`" --disable-affinity-on-start --url `"$fileUri`"" -PassThru
    $stVis = Wait-ForStatus { param($s) $s.webViewInitialized -eq $true -and $s.lastNavigationStatus -eq "Success" } 12000
    Start-Sleep -Milliseconds 1000

    $visibleImgPath = Join-Path $evidenceDir "poc_printwindow_wda_none.png"
    $pwVisible = Capture-WindowViaPrintWindow ([IntPtr][int64]$stVis.hwnd) ([int]$stVis.width) ([int]$stVis.height) $visibleImgPath
    Copy-Item $visibleImgPath (Join-Path $artifactDir "poc_printwindow_wda_none.png") -Force

    # Also compose the WPF UI snapshot + WebView2 CapturePreviewAsync into a combined visual screenshot
    $uiSnap = Join-Path $dataDir "poc-ui-snapshot.png"
    $wv2Snap = Join-Path $dataDir "poc-webview2-preview.png"
    $combinedSnapPath = Join-Path $evidenceDir "poc_visible_interface_composite.png"
    if ((Test-Path $uiSnap) -and (Test-Path $wv2Snap)) {
        $baseBmp = [System.Drawing.Bitmap]::FromFile($uiSnap)
        $webBmp = [System.Drawing.Bitmap]::FromFile($wv2Snap)
        $gComp = [System.Drawing.Graphics]::FromImage($baseBmp)
        $gComp.DrawImage($webBmp, 10, 48, $baseBmp.Width - 20, $baseBmp.Height - 82)
        $gComp.Dispose()
        $baseBmp.Save($combinedSnapPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $webBmp.Dispose()
        $baseBmp.Dispose()
        Copy-Item $combinedSnapPath (Join-Path $artifactDir "poc_visible_interface_composite.png") -Force
    }

    [Win32TestHelper]::PostMessage([IntPtr][int64]$stVis.hwnd, [Win32TestHelper]::WM_CLOSE, [IntPtr]0, [IntPtr]0) | Out-Null
    $procVis.WaitForExit(5000) | Out-Null

    $results["Test8_VisibleInterfaceAndUrlNavigation"] = [ordered]@{
        Status = if ($stVis.webViewInitialized -and $stVis.lastNavigationStatus -eq "Success" -and (-not $pwVisible.IsAllBlack)) { "PASS" } else { "FAIL" }
        HostingMode = $stVis.webViewHostingMode
        NavigatedSource = $stVis.currentSource
        NavigationStatus = $stVis.lastNavigationStatus
        PrintWindowCaptureWdaNone = $pwVisible
        CompositeUiSnapshotCreated = (Test-Path $combinedSnapPath)
    }

} finally {
    if ($proc1 -and (-not $proc1.HasExited)) { Stop-Process -Id $proc1.Id -Force -ErrorAction SilentlyContinue }
    $bgForm.Close()
    $bgForm.Dispose()
}

$reportJsonPath = Join-Path $evidenceDir "verification-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $reportJsonPath -Encoding UTF8
Write-Host "ALL TESTS COMPLETED. Results written to $reportJsonPath"
$results | ConvertTo-Json -Depth 6
