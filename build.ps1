$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $projectDir "bin\Release\net10.0-windows"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$net10Dir = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.11"
$wpf10Dir = "C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.11"
$wv2PkgDir = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.web.webview2\1.0.3719.77"

if (-not (Test-Path $net10Dir) -or -not (Test-Path $wpf10Dir)) {
    throw ".NET 10.0.11 Windows Desktop Runtime not found in C:\Program Files\dotnet\shared."
}
if (-not (Test-Path $wv2PkgDir)) {
    throw "Microsoft.Web.WebView2 1.0.3719.77 NuGet package not found in local NuGet cache."
}

# Copy WebView2 managed and native win-x64 runtime assemblies to output directory
Copy-Item (Join-Path $wv2PkgDir "lib_manual\netcoreapp3.0\Microsoft.Web.WebView2.Core.dll") $outDir -Force
Copy-Item (Join-Path $wv2PkgDir "lib_manual\netcoreapp3.0\Microsoft.Web.WebView2.Wpf.dll") $outDir -Force
Copy-Item (Join-Path $wv2PkgDir "lib_manual\net8.0-windows10.0.17763.0\Microsoft.Web.WebView2.Core.Projection.dll") $outDir -Force
Copy-Item (Join-Path $wv2PkgDir "runtimes\win-x64\native\WebView2Loader.dll") $outDir -Force

$refs = @(
    "$net10Dir\System.Private.CoreLib.dll",
    "$net10Dir\System.Runtime.dll",
    "$net10Dir\System.Console.dll",
    "$net10Dir\System.Threading.dll",
    "$net10Dir\System.Threading.Tasks.dll",
    "$net10Dir\System.IO.FileSystem.dll",
    "$net10Dir\System.Runtime.InteropServices.dll",
    "$net10Dir\System.ComponentModel.Primitives.dll",
    "$net10Dir\System.ComponentModel.dll",
    "$net10Dir\System.Private.Uri.dll",
    "$net10Dir\mscorlib.dll",
    "$net10Dir\System.ObjectModel.dll",
    "$net10Dir\System.Drawing.Primitives.dll",
    "$net10Dir\Microsoft.Win32.Primitives.dll",
    "$net10Dir\netstandard.dll",
    "$wpf10Dir\WindowsBase.dll",
    "$wpf10Dir\PresentationCore.dll",
    "$wpf10Dir\PresentationFramework.dll",
    "$wpf10Dir\System.Xaml.dll",
    "$wpf10Dir\System.Windows.Forms.Primitives.dll",
    "$outDir\Microsoft.Web.WebView2.Core.dll",
    "$outDir\Microsoft.Web.WebView2.Wpf.dll"
)

$refArgs = $refs | ForEach-Object { "/r:`"$_`"" }
$sources = @(
    (Join-Path $projectDir "OverlayMode.cs"),
    (Join-Path $projectDir "OverlaySettingsService.cs"),
    (Join-Path $projectDir "NativeMethods.cs"),
    (Join-Path $projectDir "Program.cs"),
    (Join-Path $projectDir "PocOverlayWindow.cs")
)

$outDll = Join-Path $outDir "DynamicOverlay.Poc.dll"
$manifestPath = Join-Path $projectDir "app.manifest"
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "Compiling DynamicOverlay.Poc.dll targeting .NET 10.0.11 (Microsoft.WindowsDesktop.App 10.0.11)..."
& $cscPath /nologo /nostdlib /noconfig /nowarn:1701,1702 /optimize+ /platform:x64 /target:winexe /win32manifest:"$manifestPath" /out:"$outDll" $refArgs $sources

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$runtimeConfig = @"
{
  "runtimeOptions": {
    "tfm": "net10.0-windows",
    "rollForward": "LatestMinor",
    "framework": {
      "name": "Microsoft.WindowsDesktop.App",
      "version": "10.0.11"
    }
  }
}
"@
Set-Content -Path (Join-Path $outDir "DynamicOverlay.Poc.runtimeconfig.json") -Value $runtimeConfig -Encoding UTF8

Write-Host "SUCCESS: Built $outDll"
