using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DynamicOverlay.Poc
{
    public static class Program
    {
        private const string MutexName = @"Local\DynamicOverlay_Poc_SingleInstance_Mutex";
        private static Mutex _singleInstanceMutex;
        private static bool _ownsSingleInstanceMutex;
        private static bool _singleInstanceMutexReleased;

        public static bool SingleInstanceMutexReleased
        {
            get { return _singleInstanceMutexReleased; }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool createdNew = false;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
                _ownsSingleInstanceMutex = createdNew;

                if (!createdNew)
                {
                    string duplicateMsg = string.Format(
                        "[{0:O}] Duplicate Vapor instance detected; exiting cleanly without spawning a second window.",
                        DateTime.UtcNow);
                    WriteBootstrapLog(duplicateMsg);
                    WriteDuplicateInstanceJson();
                    Console.WriteLine(duplicateMsg);
                    return 2; // Distinct exit code indicating duplicate instance prevented
                }

                // Verify WebView2 Evergreen Runtime availability before launching UI
                string runtimeVersion = string.Empty;
                try
                {
                    runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Microsoft Edge WebView2 Runtime is not available: " + ex.Message,
                        "Vapor — Missing Prerequisite",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return 3;
                }

                bool useCompositionControl = false;
                bool startWithAffinityDisabled = false;
                string initialUrl = string.Empty;
                bool hasCliModeOverride = false;
                OverlayMode cliModeOverride = OverlayMode.Lite;

                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--use-composition-control", StringComparison.OrdinalIgnoreCase))
                        {
                            useCompositionControl = true;
                        }
                        else if (string.Equals(args[i], "--disable-affinity-on-start", StringComparison.OrdinalIgnoreCase))
                        {
                            startWithAffinityDisabled = true;
                        }
                        else if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        {
                            initialUrl = args[i + 1];
                            i++;
                        }
                        else if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        {
                            OverlayMode parsedMode;
                            if (OverlayModeExtensions.TryParse(args[i + 1], out parsedMode))
                            {
                                hasCliModeOverride = true;
                                cliModeOverride = parsedMode;
                            }
                            i++;
                        }
                    }
                }

                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DynamicOverlayPoc");
                OverlaySettingsService settingsService = new OverlaySettingsService(dataDir);

                if (hasCliModeOverride)
                {
                    settingsService.SaveMode(cliModeOverride);
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                PocOverlayWindow window = new PocOverlayWindow(
                    runtimeVersion,
                    useCompositionControl,
                    startWithAffinityDisabled,
                    initialUrl,
                    settingsService);

                app.Run(window);
                return 0;
            }
            finally
            {
                ReleaseSingleInstanceMutexSafely();
            }
        }

        /// <summary>
        /// Idempotently releases and disposes the process-lifetime single-instance mutex
        /// if owned by the current thread. Safe to call during centralized shutdown and Main cleanup.
        /// </summary>
        public static void ReleaseSingleInstanceMutexSafely()
        {
            if (_singleInstanceMutex == null)
            {
                return;
            }

            try
            {
                if (_ownsSingleInstanceMutex && !_singleInstanceMutexReleased)
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _ownsSingleInstanceMutex = false;
                    _singleInstanceMutexReleased = true;
                }
            }
            catch
            {
                // Ignore if already released on this thread
                _singleInstanceMutexReleased = true;
            }

            try
            {
                _singleInstanceMutex.Dispose();
            }
            catch
            {
                // Ignore disposal exceptions
            }
            finally
            {
                _singleInstanceMutex = null;
            }
        }

        private static void WriteBootstrapLog(string line)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DynamicOverlayPoc");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "bootstrap.log"), line + Environment.NewLine);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private static void WriteDuplicateInstanceJson()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DynamicOverlayPoc");
                Directory.CreateDirectory(dir);
                string json = string.Format(
                    "{{\n  \"timestampUtc\": \"{0:O}\",\n  \"processId\": {1},\n  \"duplicateInstanceDetected\": true,\n  \"exitCode\": 2\n}}\n",
                    DateTime.UtcNow,
                    Environment.ProcessId);
                File.WriteAllText(Path.Combine(dir, "poc-duplicate-instance.json"), json);
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}
