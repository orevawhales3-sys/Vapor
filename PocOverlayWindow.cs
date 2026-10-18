using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DynamicOverlay.Poc
{
    public sealed class PocOverlayWindow : Window
    {
        private readonly string _runtimeVersion;
        private readonly bool _useCompositionControl;
        private readonly bool _startWithAffinityDisabled;
        private readonly string _initialUrl;
        private readonly string _dataDir;
        private readonly string _statusFilePath;
        private readonly OverlaySettingsService _settingsService;
        private readonly ShortcutsService _shortcutsService;
        private readonly NotesService _notesService;
        private readonly DispatcherTimer _ultraDeactivateSettleTimer;
        private readonly DispatcherTimer _notesAutoSaveTimer;

        private IntPtr _hwnd;
        private HwndSource? _hwndSource;

        // Top Header & Navigation Controls
        private readonly TextBox _urlBox;
        private readonly Button _backButton;
        private readonly Button _forwardButton;
        private readonly Button _reloadButton;
        private readonly Button _goButton;
        private readonly Border _tabCountBadgeBorder;
        private readonly TextBlock _tabCountBadgeText;
        private readonly Button _menuButton;
        private readonly Button _minimizeButton;
        private readonly Button _closeButton;
        private readonly TextBlock _conflictBanner;
        private readonly TextBlock _statusText;

        // Visual Tab Strip Controls (Phase 4 temporary visual tab strip driving single WebView2)
        private readonly StackPanel _tabItemsStack;
        private readonly Button _newTabButton;
        private readonly List<VaporTabItem> _tabs = new List<VaporTabItem>();
        private string _activeTabId = string.Empty;

        // Content View Containers (In-window content-view swap without bitmap capture)
        private readonly Border _webViewHostBorder;
        private readonly Border _panelHostBorder;
        private readonly FrameworkElement _threeDotMenuView;
        private readonly FrameworkElement _shortcutsPanelView;
        private readonly FrameworkElement _notesPanelView;
        private readonly FrameworkElement _settingsPanelView;
        private readonly FrameworkElement _aboutPanelView;
        private VaporPanelView _activeView = VaporPanelView.Browser;

        // Dark Theme glass shell layout (presentation only)
        private const double SidebarExpandedWidth = 212;
        private const double SidebarCompactWidth = 58;
        private const double DockedPanelWidth = 296;
        private const double MinDockedBrowserWidth = 330;
        private const double CompactSidebarBreakpoint = 760;
        private readonly ColumnDefinition _sidebarColumn;
        private readonly ColumnDefinition _dockColumn;
        private readonly Grid _sidebarCard;
        private readonly Border _sidebarOmniHost;
        private readonly Border _compactOmniHost;
        private readonly Border _omniboxPill;
        private readonly Grid _browserCard;
        private readonly Grid _panelHostCard;
        private readonly List<FrameworkElement> _sidebarExpandedOnly = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _sidebarCompactOnly = new List<FrameworkElement>();
        private readonly Dictionary<VaporPanelView, Border> _sidebarNavRows = new Dictionary<VaporPanelView, Border>();
        private Button _sidebarLiteBtn = null!;
        private Button _sidebarUltraBtn = null!;
        private Border _sidebarCaptureTrack = null!;
        private Border _sidebarCaptureThumb = null!;

        // Three-Dot Menu & Settings Sync Controls
        private Button _menuLiteModeBtn = null!;
        private Button _menuUltraModeBtn = null!;
        private Border _menuCaptureToggleTrack = null!;
        private Border _menuCaptureToggleThumb = null!;
        private TextBlock _menuCaptureStatusText = null!;
        private Border _menuThemesRowBorder = null!;
        private TextBlock _menuThemesChevron = null!;
        private Border _menuThemesSubmenuBorder = null!;
        private Button _themeDarkBtn = null!;
        private Button _themeNeonBtn = null!;
        private string _selectedThemeOption = "Dark";

        private Border _settingsLiteCard = null!;
        private TextBlock _settingsLiteRadio = null!;
        private Border _settingsUltraCard = null!;
        private TextBlock _settingsUltraRadio = null!;
        private Border _settingsCaptureToggleTrack = null!;
        private Border _settingsCaptureToggleThumb = null!;
        private TextBlock _settingsCaptureStatusText = null!;

        // Saved Shortcuts Panel Controls
        private Border _addShortcutFormBorder = null!;
        private TextBox _shortcutNameInput = null!;
        private TextBox _shortcutUrlInput = null!;
        private TextBlock _shortcutValidationText = null!;
        private StackPanel _shortcutsListStack = null!;
        private string _lastShortcutValidationError = string.Empty;

        // Notes Panel Controls
        private TextBox _notesTitleBox = null!;
        private RichTextBox _notesRichTextBox = null!;
        private TextBlock _notesSaveStatusText = null!;
        private bool _isLoadingNoteContent;
        private bool _hasUnsavedNoteChanges;
        private bool _lastNoteSaveSucceeded = true;

        // WebView2 controls (one of the two is instantiated based on hosting mode)
        private WebView2? _hwndWebView;
        private WebView2CompositionControl? _compositionWebView;
        private CoreWebView2? _coreWebView;

        // Phase 2 Mode & Focus State
        private OverlayMode _currentMode;
        private OverlayMode _persistedMode;
        private bool _captureProtectionEnabled;
        private bool _isOverlayActive;
        private bool _isDraggingOrResizing;
        private DateTime _suppressAutoHideUntilUtc = DateTime.MinValue;
        private int _ultraAutoHideCount;
        private int _externalFocusTransferCount;
        private int _internalControlClickCount;
        private int _modeChangeCount;
        private string _lastHideReason = "None";

        // Phase 3 Lifecycle & Idempotent Shutdown State
        private bool _isShuttingDown;
        private bool _shutdownCleanupCompleted;
        private bool _isTransitioningState;
        private bool _webViewDisposed;
        private int _hotkeyUnregisterCount;
        private int _hideCount;
        private int _restoreCount;
        private int _minimizeCount;
        private string _lastRestoreReason = "None";
        private string _closeInitiator = "None";

        // Live WebView2 Workspace State Telemetry
        private int _webViewDomCounter;
        private string _webViewDomActiveTab = "Tab1-Workspace";
        private string _webViewDomNoteValue = "State persists across Lite switches & Ultra hide/restore!";

        // Phase 1 Telemetry & Verification State
        private bool _hotkeyRegistered;
        private int _hotkeyLastError;
        private bool _hotkeyUnregistered;
        private int _toggleCount;
        private int _dwmCornerHResult;
        private int _dwmDarkHResult;
        private bool _setAffinityReturn;
        private int _setAffinityLastError;
        private bool _getAffinityReturn;
        private uint _reportedAffinity;
        private bool _webViewInitialized;
        private string _currentSource = "about:blank";
        private string _lastNavigationStatus = "Initializing";
        private string _lastAddressInputClassification = "BuiltIn";
        private string _lastResolvedNavigationTarget = "poc://diagnostics-test-page";
        private string _lastAddressResolutionStatus = "Resolved";
        private double _savedLeft;
        private double _savedTop;
        private double _savedWidth;
        private double _savedHeight;

        public const string DefaultSearchProviderUrlTemplate = "https://www.google.com/search?q={0}";

        public PocOverlayWindow(
            string runtimeVersion,
            bool useCompositionControl,
            bool startWithAffinityDisabled,
            string initialUrl,
            OverlaySettingsService settingsService)
        {
            _runtimeVersion = runtimeVersion;
            _useCompositionControl = useCompositionControl;
            _startWithAffinityDisabled = startWithAffinityDisabled;
            _initialUrl = initialUrl;

            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DynamicOverlayPoc");
            Directory.CreateDirectory(_dataDir);
            _statusFilePath = Path.Combine(_dataDir, "poc-status.json");

            _settingsService = settingsService != null
                ? settingsService
                : new OverlaySettingsService(_dataDir);
            _shortcutsService = new ShortcutsService(_dataDir);
            _notesService = new NotesService(_dataDir);

            // Load persisted mode, capture protection, and selected theme state
            _currentMode = _settingsService.LoadMode();
            _persistedMode = _currentMode;
            _captureProtectionEnabled = _settingsService.LoadCaptureProtectionEnabled();
            _selectedThemeOption = _settingsService.LoadSelectedTheme();
            _settingsService.SaveSettings(_currentMode, _captureProtectionEnabled, _selectedThemeOption);

            InitializeDarkThemeResources();

            _ultraDeactivateSettleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(35)
            };
            _ultraDeactivateSettleTimer.Tick += OnUltraDeactivateSettleTimerTick;

            _notesAutoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _notesAutoSaveTimer.Tick += OnNotesAutoSaveTimerTick;

            Title = "VAPOR — Overlay Browser";
            Width = 880;
            Height = 580;
            MinWidth = 520;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Topmost = true;
            Background = DarkTheme.WindowBackgroundBrush; // #080A0C Near-Black

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(12),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            // Outer frame with subtle charcoal border (no neon glow)
            Border outerBorder = new Border
            {
                Background = DarkTheme.WindowBackgroundBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6)
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Vapor Top Navigation & Window Controls
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Compact Browser Tab Strip
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Conflict Banner (if any)
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 3: Content Area (Browser OR Active Panel)
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 4: Compact Telemetry Status Bar

            // =====================================================================
            // ROW 0: Dark Theme Vapor Browser Header Bar (Responsive down to 520x380)
            // =====================================================================
            Border headerBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush, // #111417
                BorderBrush = DarkTheme.BorderDividerBrush, // #22262B
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.SizeAll
            };
            headerBorder.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 0: VAPOR white wordmark
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 1: Back, Forward, Reload (borderless)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 }); // Col 2: Charcoal Address Bar Pill
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 3: Right Fixed Controls ([N], ─, •••, ✕)

            // Col 0: Crisp White VAPOR Wordmark
            TextBlock brandWordmark = CreateVaporWordmark(14.5, new Thickness(2, 0, 10, 0));
            Grid.SetColumn(brandWordmark, 0);
            headerGrid.Children.Add(brandWordmark);

            // Col 1: Borderless Back, Forward, Reload Controls sitting directly on header background
            StackPanel navCluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _backButton = CreateBorderlessIconButton(
                CreateVectorIcon("M 11,3 L 5,8 L 11,13 M 5,8 L 14,8", 13, 13, DarkTheme.TextSecondaryBrush),
                "Back", 26, 24);
            _backButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                if (_activeView != VaporPanelView.Browser)
                {
                    SetActivePanelView(VaporPanelView.Browser);
                    return;
                }
                if (_coreWebView != null && _coreWebView.CanGoBack)
                {
                    _coreWebView.GoBack();
                }
            };
            navCluster.Children.Add(_backButton);

            _forwardButton = CreateBorderlessIconButton(
                CreateVectorIcon("M 6,3 L 12,8 L 6,13 M 3,8 L 12,8", 13, 13, DarkTheme.TextSecondaryBrush),
                "Forward", 26, 24);
            _forwardButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                if (_activeView != VaporPanelView.Browser)
                {
                    SetActivePanelView(VaporPanelView.Browser);
                }
                if (_coreWebView != null && _coreWebView.CanGoForward)
                {
                    _coreWebView.GoForward();
                }
            };
            navCluster.Children.Add(_forwardButton);

            _reloadButton = CreateBorderlessIconButton(
                CreateVectorIcon("M 13,5 A 5.5,5.5 0 1 0 13.5,9.5 M 13,2 L 13,5.5 L 9.5,5.5", 13, 13, DarkTheme.TextSecondaryBrush),
                "Reload current page", 26, 24);
            _reloadButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                if (_activeView != VaporPanelView.Browser)
                {
                    SetActivePanelView(VaporPanelView.Browser);
                }
                if (_coreWebView != null)
                {
                    if (string.Equals(_currentSource, "poc://diagnostics-test-page", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadBuiltInTestPage();
                    }
                    else
                    {
                        _coreWebView.Reload();
                    }
                }
            };
            navCluster.Children.Add(_reloadButton);

            Grid.SetColumn(navCluster, 1);
            headerGrid.Children.Add(navCluster);

            // Col 2: Charcoal Pill Address Bar with Lock Icon + URL TextBox + Right Magnifying-Glass Icon
            Border urlPillBorder = new Border
            {
                Background = DarkTheme.AddressBarFillBrush, // #15181C
                BorderBrush = DarkTheme.BorderSubtleBrush,  // #292E34
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid urlPillGrid = new Grid();
            urlPillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Lock icon
            urlPillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // URL input
            urlPillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Magnifying glass icon

            UIElement lockIcon = CreateVectorIcon(
                "M 4,7 L 12,7 L 12,14 L 4,14 Z M 5.5,7 L 5.5,4.8 A 2.5,2.5 0 0 1 10.5,4.8 L 10.5,7",
                12, 13, DarkTheme.TextSecondaryBrush, 1.3);
            Border lockIconWrap = new Border
            {
                Child = lockIcon,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lockIconWrap, 0);
            urlPillGrid.Children.Add(lockIconWrap);

            _urlBox = new TextBox
            {
                Text = "poc://diagnostics-test-page",
                Background = Brushes.Transparent,
                Foreground = DarkTheme.TextPrimaryBrush,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2, 4, 2),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _urlBox.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseLeftButtonDown;
            _urlBox.KeyDown += OnUrlBoxKeyDown;
            _urlBox.GotKeyboardFocus += (s, e) => urlPillBorder.BorderBrush = CreateBrush(62, 70, 82);
            _urlBox.LostKeyboardFocus += (s, e) => urlPillBorder.BorderBrush = DarkTheme.BorderSubtleBrush;
            Grid.SetColumn(_urlBox, 1);
            urlPillGrid.Children.Add(_urlBox);

            _goButton = CreateBorderlessIconButton(
                CreateVectorIcon("M 10.5,6.5 A 4,4 0 1 1 2.5,6.5 A 4,4 0 1 1 10.5,6.5 M 9.5,9.5 L 13.5,13.5", 13, 13, DarkTheme.TextSecondaryBrush),
                "Navigate or search address", 24, 22);
            _goButton.Margin = new Thickness(2, 0, 0, 0);
            _goButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                NavigateToInputUrl(_urlBox.Text);
            };
            Grid.SetColumn(_goButton, 2);
            urlPillGrid.Children.Add(_goButton);

            urlPillBorder.Child = urlPillGrid;
            Grid.SetColumn(urlPillBorder, 2);
            headerGrid.Children.Add(urlPillBorder);

            // Col 3: Right Fixed Controls Cluster: 1. Tab Count, 2. Minimize (-), 3. Three-Dot (...), 4. Close (X)
            StackPanel rightCluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _tabCountBadgeBorder = new Border
            {
                Width = 24,
                Height = 24,
                Background = DarkTheme.AddressBarFillBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 5, 0),
                ToolTip = "Visible tab count (Click to view browser)",
                Cursor = Cursors.Hand
            };
            _tabCountBadgeText = new TextBlock
            {
                Text = "5",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _tabCountBadgeBorder.Child = _tabCountBadgeText;
            _tabCountBadgeBorder.MouseLeftButtonDown += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
                SetActivePanelView(VaporPanelView.Browser);
            };
            rightCluster.Children.Add(_tabCountBadgeBorder);

            _minimizeButton = CreateHeaderIconButton("─", "Minimize Vapor", 26, 24);
            _minimizeButton.PreviewMouseLeftButtonDown += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
            };
            _minimizeButton.PreviewMouseLeftButtonUp += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
                MinimizeOverlay("HeaderMinimizeButton");
            };
            _minimizeButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                MinimizeOverlay("HeaderMinimizeButton");
            };
            rightCluster.Children.Add(_minimizeButton);

            _menuButton = CreateHeaderIconButton("•••", "Open Vapor Menu (Shortcuts, Notes, Settings, About, Mode, Close)", 28, 24);
            _menuButton.PreviewMouseLeftButtonDown += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
            };
            _menuButton.PreviewMouseLeftButtonUp += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
                ToggleThreeDotMenu();
            };
            _menuButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                ToggleThreeDotMenu();
            };
            rightCluster.Children.Add(_menuButton);

            _closeButton = CreateBorderlessIconButton("✕", "Close Vapor (Unregister Ctrl+Alt+Space & Exit)", 26, 24);
            _closeButton.Foreground = DarkTheme.TextSecondaryBrush;
            _closeButton.Margin = new Thickness(1, 0, 0, 0);
            _closeButton.PreviewMouseLeftButtonDown += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
            };
            _closeButton.PreviewMouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                RequestApplicationClose("XButton");
            };
            _closeButton.Click += (s, e) => RequestApplicationClose("XButton");
            rightCluster.Children.Add(_closeButton);

            Grid.SetColumn(rightCluster, 3);
            headerGrid.Children.Add(rightCluster);

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);

            // =====================================================================
            // ROW 1: Compact Browser Tab Strip + New Tab (+) Button
            // =====================================================================
            Border tabStripBorder = new Border
            {
                Background = CreateBrush(11, 13, 16),
                BorderBrush = DarkTheme.BorderDividerBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 4)
            };

            Grid tabStripGrid = new Grid();
            tabStripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tabStripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            ScrollViewer tabScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _tabItemsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            tabScroll.Content = _tabItemsStack;
            Grid.SetColumn(tabScroll, 0);
            tabStripGrid.Children.Add(tabScroll);

            _newTabButton = CreateHeaderIconButton("+", "Open a new browser tab", 25, 23);
            _newTabButton.Margin = new Thickness(4, 0, 0, 0);
            _newTabButton.Click += (s, e) =>
            {
                RecordInternalInteraction();
                AddNewVisualTab("New Tab", "poc://diagnostics-test-page", true);
            };
            Grid.SetColumn(_newTabButton, 1);
            tabStripGrid.Children.Add(_newTabButton);

            tabStripBorder.Child = tabStripGrid;
            Grid.SetRow(tabStripBorder, 1);
            rootGrid.Children.Add(tabStripBorder);

            // =====================================================================
            // ROW 2: Hotkey Conflict / Diagnostic Warning Banner
            // =====================================================================
            _conflictBanner = new TextBlock
            {
                Background = CreateBrush(95, 25, 28),
                Foreground = CreateBrush(254, 226, 226),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_conflictBanner, 2);
            rootGrid.Children.Add(_conflictBanner);

            // =====================================================================
            // ROW 3: Content Region (Swaps cleanly between WebView2 Host and Panels)
            // =====================================================================
            Grid contentAreaGrid = new Grid();

            _webViewHostBorder = new Border
            {
                Background = DarkTheme.EditorSurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 4)
            };

            if (_useCompositionControl)
            {
                _compositionWebView = new WebView2CompositionControl
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 13, 16, 19)
                };
                _webViewHostBorder.Child = _compositionWebView;
            }
            else
            {
                _hwndWebView = new WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 13, 16, 19)
                };
                _webViewHostBorder.Child = _hwndWebView;
            }
            contentAreaGrid.Children.Add(_webViewHostBorder);

            _panelHostBorder = new Border
            {
                Background = DarkTheme.CreateGlassSurfaceGradient(),
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(14),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 3,
                    Opacity = 0.5
                }
            };
            _panelHostBorder.MouseLeftButtonDown += (s, e) => RecordInternalInteraction();

            Grid panelViewsGrid = new Grid();
            _threeDotMenuView = BuildThreeDotMenuView();
            _shortcutsPanelView = BuildShortcutsPanelView();
            _notesPanelView = BuildNotesPanelView();
            _settingsPanelView = BuildSettingsPanelView();
            _aboutPanelView = BuildAboutPanelView();

            panelViewsGrid.Children.Add(_threeDotMenuView);
            panelViewsGrid.Children.Add(_shortcutsPanelView);
            panelViewsGrid.Children.Add(_notesPanelView);
            panelViewsGrid.Children.Add(_settingsPanelView);
            panelViewsGrid.Children.Add(_aboutPanelView);

            _panelHostBorder.Child = panelViewsGrid;
            contentAreaGrid.Children.Add(_panelHostBorder);

            Grid.SetRow(contentAreaGrid, 3);
            rootGrid.Children.Add(contentAreaGrid);

            // =====================================================================
            // ROW 4: Live Diagnostic Telemetry Bar
            // =====================================================================
            Border footerBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderDividerBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3)
            };
            _statusText = new TextBlock
            {
                Foreground = DarkTheme.TextMutedBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = "Initializing Vapor Phase 04..."
            };
            footerBorder.Child = _statusText;
            Grid.SetRow(footerBorder, 4);
            rootGrid.Children.Add(footerBorder);

            outerBorder.Child = rootGrid;
            Content = outerBorder;

            // Initialize Starter Visual Tabs, Shortcuts UI, Notes UI, and Mode/Capture UI
            SeedInitialVisualTabs();
            RebuildShortcutsListUi();
            LoadNoteIntoUi(_notesService.CurrentNote);
            UpdateModeAndCaptureUi();

            SourceInitialized += OnWindowSourceInitialized;
            Loaded += OnWindowLoaded;
            Activated += OnWindowActivated;
            Deactivated += OnWindowDeactivated;
            StateChanged += OnWindowStateChanged;
            LocationChanged += (s, e) => OnGeometryChanged();
            SizeChanged += (s, e) => OnGeometryChanged();
            Closed += OnWindowClosed;
        }

        public OverlayMode CurrentMode
        {
            get { return _currentMode; }
        }

        public VaporPanelView ActivePanelView
        {
            get { return _activeView; }
        }

        // =========================================================================
        // UI BUILDERS FOR THREE-DOT MENU, SHORTCUTS, NOTES, SETTINGS, AND ABOUT
        // =========================================================================

        private FrameworkElement BuildThreeDotMenuView()
        {
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            Border menuCard = new Border
            {
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = DarkTheme.CreateGlassSurfaceGradient(),
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8)
            };

            StackPanel stack = new StackPanel();

            // Top row inside menu: Title + Back to Browser button
            Grid topHeader = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock menuTitle = new TextBlock
            {
                Text = "Vapor Menu",
                Foreground = DarkTheme.TextMutedBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(menuTitle, 0);
            topHeader.Children.Add(menuTitle);

            Button backToBrowserBtn = CreateCompactPillButton("← Browser", DarkTheme.SecondarySurfaceBrush, DarkTheme.TextPrimaryBrush, 8);
            backToBrowserBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetActivePanelView(VaporPanelView.Browser);
            };
            Grid.SetColumn(backToBrowserBtn, 1);
            topHeader.Children.Add(backToBrowserBtn);
            stack.Children.Add(topHeader);

            // Navigation items: Shortcuts, Notes, Settings, About, Themes (monochrome glyphs)
            stack.Children.Add(CreateMenuNavRow("☆", "Shortcuts", () => SetActivePanelView(VaporPanelView.Shortcuts)));
            stack.Children.Add(CreateMenuNavRow("☰", "Notes", () => SetActivePanelView(VaporPanelView.Notes)));
            stack.Children.Add(CreateMenuNavRow("⚙", "Settings", () => SetActivePanelView(VaporPanelView.Settings)));
            stack.Children.Add(CreateMenuNavRow("ⓘ", "About", () => SetActivePanelView(VaporPanelView.About)));

            _menuThemesRowBorder = CreateMenuNavRow("◐", "Themes", ToggleThemesSubmenu);
            if (_menuThemesRowBorder.Child is Grid themesRowGrid && themesRowGrid.Children.Count > 2 && themesRowGrid.Children[2] is TextBlock themesChev)
            {
                _menuThemesChevron = themesChev;
            }
            stack.Children.Add(_menuThemesRowBorder);

            // Themes Nested Submenu (Dark / Neon selection buttons)
            _menuThemesSubmenuBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(3),
                Margin = new Thickness(16, 2, 0, 4),
                Visibility = Visibility.Collapsed
            };

            Grid themesSubmenuGrid = new Grid();
            themesSubmenuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            themesSubmenuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _themeDarkBtn = new Button
            {
                Content = "Dark",
                Height = 26,
                Margin = new Thickness(0, 0, 2, 0),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(6),
                    DarkTheme.ElevatedSurfaceBrush,
                    DarkTheme.SecondarySurfaceBrush)
            };
            _themeDarkBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SelectThemeOption("Dark");
            };
            Grid.SetColumn(_themeDarkBtn, 0);
            themesSubmenuGrid.Children.Add(_themeDarkBtn);

            _themeNeonBtn = new Button
            {
                Content = "Neon",
                Height = 26,
                Margin = new Thickness(2, 0, 0, 0),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(6),
                    DarkTheme.ElevatedSurfaceBrush,
                    DarkTheme.SecondarySurfaceBrush)
            };
            _themeNeonBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SelectThemeOption("Neon");
            };
            Grid.SetColumn(_themeNeonBtn, 1);
            themesSubmenuGrid.Children.Add(_themeNeonBtn);

            _menuThemesSubmenuBorder.Child = themesSubmenuGrid;
            stack.Children.Add(_menuThemesSubmenuBorder);

            stack.Children.Add(CreateDivider());

            // Overlay Mode Section
            StackPanel modeHeaderRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 2, 0, 6)
            };
            modeHeaderRow.Children.Add(new TextBlock
            {
                Text = "▣",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                Width = 22,
                VerticalAlignment = VerticalAlignment.Center
            });
            modeHeaderRow.Children.Add(new TextBlock
            {
                Text = "Overlay Mode",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(modeHeaderRow);

            Border segmentedBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid segmentedGrid = new Grid();
            segmentedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            segmentedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _menuLiteModeBtn = new Button
            {
                Content = "Lite",
                Height = 27,
                Margin = new Thickness(0, 0, 2, 0),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(6),
                    DarkTheme.ElevatedSurfaceBrush,
                    DarkTheme.SecondarySurfaceBrush)
            };
            _menuLiteModeBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetOverlayMode(OverlayMode.Lite, true);
            };
            Grid.SetColumn(_menuLiteModeBtn, 0);
            segmentedGrid.Children.Add(_menuLiteModeBtn);

            _menuUltraModeBtn = new Button
            {
                Content = "Ultra",
                Height = 27,
                Margin = new Thickness(2, 0, 0, 0),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(6),
                    DarkTheme.ElevatedSurfaceBrush,
                    DarkTheme.SecondarySurfaceBrush)
            };
            _menuUltraModeBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetOverlayMode(OverlayMode.Ultra, true);
            };
            Grid.SetColumn(_menuUltraModeBtn, 1);
            segmentedGrid.Children.Add(_menuUltraModeBtn);

            segmentedBorder.Child = segmentedGrid;
            stack.Children.Add(segmentedBorder);

            stack.Children.Add(CreateDivider());

            // Capture Protection Section
            Grid cpGrid = new Grid { Margin = new Thickness(4, 2, 2, 2) };
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement cpIcon = CreateVectorIcon(
                "M 6,1 L 11,3 L 11,6.5 C 11,9.5 8.8,11.8 6,12.8 C 3.2,11.8 1,9.5 1,6.5 L 1,3 Z",
                14,
                14,
                DarkTheme.TextPrimaryBrush,
                1.3);
            Border cpIconWrap = new Border
            {
                Width = 22,
                Margin = new Thickness(0, 2, 2, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = cpIcon
            };
            Grid.SetColumn(cpIconWrap, 0);
            cpGrid.Children.Add(cpIconWrap);

            StackPanel cpTextStack = new StackPanel();
            cpTextStack.Children.Add(new TextBlock
            {
                Text = "Capture Protection",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold
            });
            cpTextStack.Children.Add(new TextBlock
            {
                Text = "Hide overlay from supported recordings and screen shares.",
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 8, 0)
            });
            _menuCaptureStatusText = new TextBlock
            {
                Foreground = DarkTheme.TextMutedBrush,
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0)
            };
            cpTextStack.Children.Add(_menuCaptureStatusText);
            Grid.SetColumn(cpTextStack, 1);
            cpGrid.Children.Add(cpTextStack);

            _menuCaptureToggleTrack = CreateToggleSwitch(out _menuCaptureToggleThumb, () =>
            {
                RecordInternalInteraction();
                ToggleDisplayAffinity(true);
            });
            Grid.SetColumn(_menuCaptureToggleTrack, 2);
            cpGrid.Children.Add(_menuCaptureToggleTrack);

            stack.Children.Add(cpGrid);
            stack.Children.Add(CreateDivider());

            // Close Action Row (Dark charcoal button, invokes centralized shutdown)
            Border closeRowBorder = new Border
            {
                Background = DarkTheme.SecondarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 2, 0, 0),
                Cursor = Cursors.Hand
            };
            closeRowBorder.MouseEnter += (s, e) => closeRowBorder.Background = DarkTheme.ElevatedSurfaceBrush;
            closeRowBorder.MouseLeave += (s, e) => closeRowBorder.Background = DarkTheme.SecondarySurfaceBrush;

            Grid closeRowGrid = new Grid();
            closeRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            closeRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            closeRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            closeRowGrid.Children.Add(new TextBlock
            {
                Text = "[→",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Width = 24,
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock closeLabel = new TextBlock
            {
                Text = "Close",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(closeLabel, 1);
            closeRowGrid.Children.Add(closeLabel);

            TextBlock closeChevron = new TextBlock
            {
                Text = "›",
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(closeChevron, 2);
            closeRowGrid.Children.Add(closeChevron);

            closeRowBorder.Child = closeRowGrid;
            closeRowBorder.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                RequestApplicationClose("MenuClose");
            };
            stack.Children.Add(closeRowBorder);

            menuCard.Child = stack;
            scroll.Content = menuCard;
            return scroll;
        }

        private FrameworkElement BuildShortcutsPanelView()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Add form
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scrollable list

            // Header Row: Borderless Back Arrow + "Shortcuts" + blue "+ Add" button
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button backBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 10,2 L 4,7 L 10,12 M 4,7 L 13,7", 13, 13, DarkTheme.TextPrimaryBrush, 1.5),
                "Back to Browser",
                28,
                26);
            backBtn.Margin = new Thickness(0, 0, 8, 0);
            backBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetActivePanelView(VaporPanelView.Browser);
            };
            Grid.SetColumn(backBtn, 0);
            header.Children.Add(backBtn);

            TextBlock title = new TextBlock
            {
                Text = "Shortcuts",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            Button addToggleBtn = CreateCompactPillButton("+ Add", DarkTheme.AccentBlueBrush, Brushes.White, 12);
            addToggleBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                _addShortcutFormBorder.Visibility = _addShortcutFormBorder.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                _shortcutValidationText.Visibility = Visibility.Collapsed;
                if (_addShortcutFormBorder.Visibility == Visibility.Visible)
                {
                    FocusTextBox(_shortcutNameInput);
                }
            };
            Grid.SetColumn(addToggleBtn, 2);
            header.Children.Add(addToggleBtn);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Add-Shortcut Inline Form
            _addShortcutFormBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };

            StackPanel formStack = new StackPanel();
            formStack.Children.Add(new TextBlock
            {
                Text = "Add Website Shortcut",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            Grid inputsGrid = new Grid();
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _shortcutNameInput = new TextBox
            {
                Background = DarkTheme.EditorSurfaceBrush,
                Foreground = DarkTheme.TextPrimaryBrush,
                CaretBrush = Brushes.White,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                ToolTip = "Website Name (e.g. GitHub)"
            };
            _shortcutNameInput.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseLeftButtonDown;
            Grid.SetColumn(_shortcutNameInput, 0);
            inputsGrid.Children.Add(_shortcutNameInput);

            _shortcutUrlInput = new TextBox
            {
                Background = DarkTheme.EditorSurfaceBrush,
                Foreground = DarkTheme.TextPrimaryBrush,
                CaretBrush = Brushes.White,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                ToolTip = "Website URL (e.g. https://github.com)"
            };
            _shortcutUrlInput.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseLeftButtonDown;
            Grid.SetColumn(_shortcutUrlInput, 1);
            inputsGrid.Children.Add(_shortcutUrlInput);

            Button saveShortcutBtn = CreateCompactPillButton("Save", DarkTheme.AccentBlueBrush, Brushes.White, 8);
            saveShortcutBtn.Margin = new Thickness(0, 0, 4, 0);
            saveShortcutBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                TryAddShortcutFromInputs(_shortcutNameInput.Text, _shortcutUrlInput.Text);
            };
            Grid.SetColumn(saveShortcutBtn, 2);
            inputsGrid.Children.Add(saveShortcutBtn);

            Button cancelShortcutBtn = CreateCompactPillButton("Cancel", DarkTheme.SecondarySurfaceBrush, DarkTheme.TextSecondaryBrush, 8);
            cancelShortcutBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                _addShortcutFormBorder.Visibility = Visibility.Collapsed;
                _shortcutValidationText.Visibility = Visibility.Collapsed;
            };
            Grid.SetColumn(cancelShortcutBtn, 3);
            inputsGrid.Children.Add(cancelShortcutBtn);

            formStack.Children.Add(inputsGrid);

            _shortcutValidationText = new TextBlock
            {
                Foreground = CreateBrush(248, 113, 113),
                FontSize = 11,
                Margin = new Thickness(2, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            formStack.Children.Add(_shortcutValidationText);

            _addShortcutFormBorder.Child = formStack;
            Grid.SetRow(_addShortcutFormBorder, 1);
            root.Children.Add(_addShortcutFormBorder);

            // Scrollable Shortcuts List inside a dark charcoal rounded card container
            Border listCardBorder = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Top
            };

            ScrollViewer listScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _shortcutsListStack = new StackPanel();
            listScroll.Content = _shortcutsListStack;
            listCardBorder.Child = listScroll;
            Grid.SetRow(listCardBorder, 2);
            root.Children.Add(listCardBorder);

            return root;
        }

        private FrameworkElement BuildNotesPanelView()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Formatting toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Unified note editor surface (title + RichTextBox)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Autosave footer

            // Row 0: Header
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button backBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 10,2 L 4,7 L 10,12 M 4,7 L 13,7", 13, 13, DarkTheme.TextPrimaryBrush, 1.5),
                "Back to Browser",
                28,
                26);
            backBtn.Margin = new Thickness(0, 0, 8, 0);
            backBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SaveCurrentNoteFromUi();
                SetActivePanelView(VaporPanelView.Browser);
            };
            Grid.SetColumn(backBtn, 0);
            header.Children.Add(backBtn);

            TextBlock title = new TextBlock
            {
                Text = "Notes",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            Button newNoteBtn = CreateHeaderIconButton("+", "Start a new note", 28, 26);
            newNoteBtn.Margin = new Thickness(0);
            newNoteBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                StartNewNoteInUi();
            };
            Grid.SetColumn(newNoteBtn, 2);
            header.Children.Add(newNoteBtn);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Row 1: Formatting Toolbar (B, I, U, Bullets, Numbering, Link - clean borderless monochrome icons)
            Border toolbarBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = DarkTheme.BorderDividerBrush,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(2, 4, 2, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            StackPanel toolbarStack = new StackPanel { Orientation = Orientation.Horizontal };

            Button boldBtn = CreateBorderlessIconButton("B", "Bold (Ctrl+B)", 26, 24);
            boldBtn.FontWeight = FontWeights.Bold;
            boldBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                EditingCommands.ToggleBold.Execute(null, _notesRichTextBox);
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(boldBtn);

            Button italicBtn = CreateBorderlessIconButton("I", "Italic (Ctrl+I)", 26, 24);
            italicBtn.FontStyle = FontStyles.Italic;
            italicBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                EditingCommands.ToggleItalic.Execute(null, _notesRichTextBox);
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(italicBtn);

            Button underlineBtn = CreateBorderlessIconButton("U", "Underline (Ctrl+U)", 26, 24);
            underlineBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                EditingCommands.ToggleUnderline.Execute(null, _notesRichTextBox);
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(underlineBtn);

            toolbarStack.Children.Add(new Border
            {
                Width = 1,
                Height = 14,
                Background = DarkTheme.BorderDividerBrush,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            Button bulletsBtn = CreateBorderlessIconButton("•≡", "Bulleted List", 30, 24);
            bulletsBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                EditingCommands.ToggleBullets.Execute(null, _notesRichTextBox);
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(bulletsBtn);

            Button numberBtn = CreateBorderlessIconButton("1≡", "Numbered List", 30, 24);
            numberBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                EditingCommands.ToggleNumbering.Execute(null, _notesRichTextBox);
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(numberBtn);

            toolbarStack.Children.Add(new Border
            {
                Width = 1,
                Height = 14,
                Background = DarkTheme.BorderDividerBrush,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            Button linkBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 5.5,8.5 L 8.5,5.5 M 4.5,6.5 L 3,8 A 2.1,2.1 0 0 0 6,11 L 7.5,9.5 M 9.5,7.5 L 11,6 A 2.1,2.1 0 0 0 8,3 L 6.5,4.5", 13, 13, DarkTheme.TextPrimaryBrush, 1.3),
                "Insert current page link",
                28,
                24);
            linkBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                string linkToInsert = string.IsNullOrWhiteSpace(_currentSource) ? "https://www.microsoft.com" : _currentSource;
                _notesRichTextBox.CaretPosition.InsertTextInRun(" " + linkToInsert + " ");
                _notesRichTextBox.Focus();
            };
            toolbarStack.Children.Add(linkBtn);

            toolbarBorder.Child = toolbarStack;
            Grid.SetRow(toolbarBorder, 1);
            root.Children.Add(toolbarBorder);

            // Row 2: Cohesive Dark Charcoal Editor Surface containing both Note Title and RichTextBox
            Border editorBorder = new Border
            {
                Background = DarkTheme.EditorSurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid editorInnerGrid = new Grid();
            editorInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editorInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editorInnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _notesTitleBox = new TextBox
            {
                Text = "Today - Notes",
                Background = Brushes.Transparent,
                Foreground = DarkTheme.TextPrimaryBrush,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 2, 2, 4),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            _notesTitleBox.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseLeftButtonDown;
            _notesTitleBox.TextChanged += OnNotesContentChanged;
            Grid.SetRow(_notesTitleBox, 0);
            editorInnerGrid.Children.Add(_notesTitleBox);

            Border titleDivider = new Border
            {
                Height = 1,
                Background = DarkTheme.BorderDividerBrush,
                Margin = new Thickness(0, 2, 0, 6)
            };
            Grid.SetRow(titleDivider, 1);
            editorInnerGrid.Children.Add(titleDivider);

            _notesRichTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                Foreground = DarkTheme.TextSecondaryBrush,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _notesRichTextBox.Document.PagePadding = new Thickness(2);
            _notesRichTextBox.PreviewMouseLeftButtonDown += OnRichTextBoxPreviewMouseLeftButtonDown;
            _notesRichTextBox.TextChanged += OnNotesContentChanged;
            Grid.SetRow(_notesRichTextBox, 2);
            editorInnerGrid.Children.Add(_notesRichTextBox);

            editorBorder.Child = editorInnerGrid;
            Grid.SetRow(editorBorder, 2);
            root.Children.Add(editorBorder);

            // Row 3: Footer (Honest Autosave Status + Borderless Vector Save Icon)
            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _notesSaveStatusText = new TextBlock
            {
                Text = _notesService.CurrentNote.LastSavedDisplay,
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_notesSaveStatusText, 0);
            footer.Children.Add(_notesSaveStatusText);

            Button manualSaveBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 2,1 L 10,1 L 13,4 L 13,13 L 2,13 Z M 4,1 L 4,5 L 9,5 L 9,1 M 4,8 L 11,8 L 11,13 L 4,13 Z", 13, 13, DarkTheme.TextSecondaryBrush, 1.3),
                "Save note now",
                28,
                24);
            manualSaveBtn.Margin = new Thickness(0);
            manualSaveBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SaveCurrentNoteFromUi();
            };
            Grid.SetColumn(manualSaveBtn, 1);
            footer.Children.Add(manualSaveBtn);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            return root;
        }

        private FrameworkElement BuildSettingsPanelView()
        {
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel root = new StackPanel();

            // Header Row
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button backBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 10,2 L 4,7 L 10,12 M 4,7 L 13,7", 13, 13, DarkTheme.TextPrimaryBrush, 1.5),
                "Back to Browser",
                28,
                26);
            backBtn.Margin = new Thickness(0, 0, 8, 0);
            backBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetActivePanelView(VaporPanelView.Browser);
            };
            Grid.SetColumn(backBtn, 0);
            header.Children.Add(backBtn);

            TextBlock title = new TextBlock
            {
                Text = "Settings",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            root.Children.Add(header);

            root.Children.Add(CreateDivider());

            root.Children.Add(new TextBlock
            {
                Text = "Overlay Mode",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 8)
            });

            // Two Selectable Side-by-Side Cards: Lite Mode vs Ultra Mode
            Grid modeCardsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            modeCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _settingsLiteCard = CreateModeSelectionCard(
                "Lite Mode",
                "Stays visible while you work on other apps.",
                out _settingsLiteRadio,
                () =>
                {
                    RecordInternalInteraction();
                    SetOverlayMode(OverlayMode.Lite, true);
                });
            _settingsLiteCard.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(_settingsLiteCard, 0);
            modeCardsGrid.Children.Add(_settingsLiteCard);

            _settingsUltraCard = CreateModeSelectionCard(
                "Ultra Mode",
                "Hides after outside click, restores with shortcut.",
                out _settingsUltraRadio,
                () =>
                {
                    RecordInternalInteraction();
                    SetOverlayMode(OverlayMode.Ultra, true);
                });
            _settingsUltraCard.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(_settingsUltraCard, 1);
            modeCardsGrid.Children.Add(_settingsUltraCard);

            root.Children.Add(modeCardsGrid);

            root.Children.Add(CreateDivider());

            // Capture Protection Row
            Border cpCard = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 8, 2, 8)
            };
            Grid cpGrid = new Grid();
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement shieldIcon = CreateVectorIcon(
                "M 6,1 L 11,3 L 11,6.5 C 11,9.5 8.8,11.8 6,12.8 C 3.2,11.8 1,9.5 1,6.5 L 1,3 Z",
                15,
                15,
                DarkTheme.TextPrimaryBrush,
                1.4);
            Border shieldWrap = new Border
            {
                Width = 24,
                Margin = new Thickness(0, 2, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = shieldIcon
            };
            Grid.SetColumn(shieldWrap, 0);
            cpGrid.Children.Add(shieldWrap);

            StackPanel cpInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            cpInfo.Children.Add(new TextBlock
            {
                Text = "Capture Protection",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold
            });
            cpInfo.Children.Add(new TextBlock
            {
                Text = "Hide overlay from supported recordings and screen shares.",
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 10, 0)
            });
            _settingsCaptureStatusText = new TextBlock
            {
                Foreground = DarkTheme.TextMutedBrush,
                FontSize = 10.5,
                Margin = new Thickness(0, 3, 0, 0)
            };
            cpInfo.Children.Add(_settingsCaptureStatusText);
            Grid.SetColumn(cpInfo, 1);
            cpGrid.Children.Add(cpInfo);

            _settingsCaptureToggleTrack = CreateToggleSwitch(out _settingsCaptureToggleThumb, () =>
            {
                RecordInternalInteraction();
                ToggleDisplayAffinity(true);
            });
            Grid.SetColumn(_settingsCaptureToggleTrack, 2);
            cpGrid.Children.Add(_settingsCaptureToggleTrack);

            cpCard.Child = cpGrid;
            root.Children.Add(cpCard);

            scroll.Content = root;
            return scroll;
        }

        private FrameworkElement BuildAboutPanelView()
        {
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel root = new StackPanel();

            // Header Row
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button backBtn = CreateBorderlessIconButton(
                CreateVectorIcon("M 10,2 L 4,7 L 10,12 M 4,7 L 13,7", 13, 13, DarkTheme.TextPrimaryBrush, 1.5),
                "Back to Browser",
                28,
                26);
            backBtn.Margin = new Thickness(0, 0, 8, 0);
            backBtn.Click += (s, e) =>
            {
                RecordInternalInteraction();
                SetActivePanelView(VaporPanelView.Browser);
            };
            Grid.SetColumn(backBtn, 0);
            header.Children.Add(backBtn);

            TextBlock title = new TextBlock
            {
                Text = "About VAPOR",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            root.Children.Add(header);

            root.Children.Add(CreateDivider());

            // Two-Column Body separated by a thin 1px vertical gray divider matching reference image
            Grid bodyGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });

            // Left Column: VAPOR Branding + Version + Privacy Description
            Border leftCard = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 16, 16, 16),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            StackPanel leftStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            TextBlock bigLogo = CreateVaporWordmark(22, new Thickness(0, 0, 0, 6));
            leftStack.Children.Add(bigLogo);

            Version? asmVersion = typeof(PocOverlayWindow).Assembly.GetName().Version;
            string versionText = asmVersion != null
                ? string.Format(CultureInfo.InvariantCulture, "Version {0}.{1}.{2}", asmVersion.Major, asmVersion.Minor, Math.Max(0, asmVersion.Build))
                : "Version 1.0.0";

            leftStack.Children.Add(new TextBlock
            {
                Text = versionText,
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            leftStack.Children.Add(new TextBlock
            {
                Text = "A privacy-friendly overlay browser for focused multitasking.",
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            leftCard.Child = leftStack;
            Grid.SetColumn(leftCard, 0);
            bodyGrid.Children.Add(leftCard);

            // Thin 1px vertical gray divider
            Border verticalDivider = new Border
            {
                Width = 1,
                Background = DarkTheme.BorderDividerBrush,
                Margin = new Thickness(4, 4, 16, 4),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(verticalDivider, 1);
            bodyGrid.Children.Add(verticalDivider);

            // Right Column: Shortcut + Features
            Border rightCard = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 4, 8, 8)
            };
            StackPanel rightStack = new StackPanel();

            rightStack.Children.Add(new TextBlock
            {
                Text = "Shortcut",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            rightStack.Children.Add(CreateAboutFeatureRow("⌨", "Ctrl + Alt + Space", "Show or hide the Vapor overlay."));

            rightStack.Children.Add(CreateDivider());

            rightStack.Children.Add(new TextBlock
            {
                Text = "Features",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 4)
            });
            rightStack.Children.Add(CreateAboutFeatureRow("☆", "Shortcuts", "Save your favourite websites and open them instantly."));
            rightStack.Children.Add(CreateAboutFeatureRow("☰", "Notes", "Quick notes with auto-save."));
            rightStack.Children.Add(CreateAboutFeatureRow("▣", "Overlay Mode", "Lite stays visible. Ultra hides on outside click."));
            rightStack.Children.Add(CreateAboutFeatureRow("⛨", "Capture Protection", "Hides Vapor from supported recordings and screen shares."));
            rightStack.Children.Add(CreateAboutFeatureRow("[→", "Close", "Fully closes the application."));

            rightCard.Child = rightStack;
            Grid.SetColumn(rightCard, 2);
            bodyGrid.Children.Add(rightCard);

            root.Children.Add(bodyGrid);
            scroll.Content = root;
            return scroll;
        }

        // =========================================================================
        // PANEL NAVIGATION, VISUAL TAB STRIP, SHORTCUTS & NOTES OPERATIONS
        // =========================================================================

        public void SetActivePanelView(VaporPanelView view)
        {
            if (_isShuttingDown)
            {
                return;
            }

            if (_activeView == VaporPanelView.Notes && view != VaporPanelView.Notes && _hasUnsavedNoteChanges)
            {
                SaveCurrentNoteFromUi();
            }

            _activeView = view;

            if (view == VaporPanelView.Browser)
            {
                _panelHostBorder.Visibility = Visibility.Collapsed;
                _webViewHostBorder.Visibility = Visibility.Visible;
                _menuButton.Background = DarkTheme.SecondarySurfaceBrush;
            }
            else
            {
                _webViewHostBorder.Visibility = Visibility.Collapsed;
                _panelHostBorder.Visibility = Visibility.Visible;
                _menuButton.Background = view == VaporPanelView.ThreeDotMenu
                    ? DarkTheme.ElevatedSurfaceBrush
                    : DarkTheme.SecondarySurfaceBrush;

                _threeDotMenuView.Visibility = view == VaporPanelView.ThreeDotMenu ? Visibility.Visible : Visibility.Collapsed;
                _shortcutsPanelView.Visibility = view == VaporPanelView.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
                _notesPanelView.Visibility = view == VaporPanelView.Notes ? Visibility.Visible : Visibility.Collapsed;
                _settingsPanelView.Visibility = view == VaporPanelView.Settings ? Visibility.Visible : Visibility.Collapsed;
                _aboutPanelView.Visibility = view == VaporPanelView.About ? Visibility.Visible : Visibility.Collapsed;

                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetFocus(_hwnd);
                }
            }

            UpdateStatusDisplay();
        }

        public void ToggleThreeDotMenu()
        {
            if (_activeView == VaporPanelView.ThreeDotMenu)
            {
                SetActivePanelView(VaporPanelView.Browser);
            }
            else
            {
                SetActivePanelView(VaporPanelView.ThreeDotMenu);
            }
        }

        private void SeedInitialVisualTabs()
        {
            _tabs.Clear();
            string startUrl = !string.IsNullOrWhiteSpace(_initialUrl) ? _initialUrl : "poc://diagnostics-test-page";
            string startTitle = string.Equals(startUrl, "poc://diagnostics-test-page", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft"
                : "Workspace";

            VaporTabItem firstTab = new VaporTabItem
            {
                Id = "tab-1",
                Title = startTitle,
                Url = startUrl,
                BadgeText = "M",
                BadgeBgHex = "#0284C7",
                BadgeFgHex = "#FFFFFF"
            };
            _tabs.Add(firstTab);
            _tabs.Add(new VaporTabItem { Id = "tab-2", Title = "YouTube", Url = "https://www.youtube.com", BadgeText = "▶", BadgeBgHex = "#DC2626", BadgeFgHex = "#FFFFFF" });
            _tabs.Add(new VaporTabItem { Id = "tab-3", Title = "GitHub", Url = "https://github.com", BadgeText = "GH", BadgeBgHex = "#24292F", BadgeFgHex = "#FFFFFF" });
            _tabs.Add(new VaporTabItem { Id = "tab-4", Title = "ChatGPT", Url = "https://chat.openai.com", BadgeText = "AI", BadgeBgHex = "#10A37F", BadgeFgHex = "#FFFFFF" });
            _tabs.Add(new VaporTabItem { Id = "tab-5", Title = "Google", Url = "https://www.google.com", BadgeText = "G", BadgeBgHex = "#2563EB", BadgeFgHex = "#FFFFFF" });

            _activeTabId = firstTab.Id;
            RebuildVisualTabStripUi();
        }

        private void RebuildVisualTabStripUi()
        {
            _tabItemsStack.Children.Clear();
            _tabCountBadgeText.Text = _tabs.Count.ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < _tabs.Count; i++)
            {
                VaporTabItem tab = _tabs[i];
                bool isActive = string.Equals(tab.Id, _activeTabId, StringComparison.OrdinalIgnoreCase);

                Border tabBorder = new Border
                {
                    Background = isActive ? CreateBrush(27, 32, 38) : DarkTheme.PrimarySurfaceBrush,
                    BorderBrush = isActive ? CreateBrush(48, 54, 61) : DarkTheme.BorderDividerBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 3, 5, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = tab.Title + " (" + tab.Url + ")"
                };

                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                Border iconBadge = new Border
                {
                    Width = 15,
                    Height = 15,
                    Background = ParseHexBrush(tab.BadgeBgHex, Color.FromRgb(48, 54, 61)),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                iconBadge.Child = new TextBlock
                {
                    Text = tab.BadgeText,
                    Foreground = Brushes.White,
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(iconBadge);

                TextBlock titleBlock = new TextBlock
                {
                    Text = tab.Title,
                    Foreground = isActive ? DarkTheme.TextPrimaryBrush : DarkTheme.TextSecondaryBrush,
                    FontSize = 11.5,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    MaxWidth = 92,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                row.Children.Add(titleBlock);

                string capturedTabId = tab.Id;
                Button closeTabBtn = CreateBorderlessIconButton("×", "Close tab", 16, 16);
                closeTabBtn.Foreground = DarkTheme.TextSecondaryBrush;
                closeTabBtn.FontSize = 11;
                closeTabBtn.Margin = new Thickness(0);
                closeTabBtn.Click += (s, e) =>
                {
                    RecordInternalInteraction();
                    e.Handled = true;
                    CloseVisualTab(capturedTabId);
                };
                row.Children.Add(closeTabBtn);

                tabBorder.Child = row;
                tabBorder.MouseLeftButtonUp += (s, e) =>
                {
                    RecordInternalInteraction();
                    SelectVisualTab(capturedTabId, true);
                };

                _tabItemsStack.Children.Add(tabBorder);
            }
        }

        private void SelectVisualTab(string tabId, bool navigateBrowser)
        {
            VaporTabItem? target = null;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, tabId, StringComparison.OrdinalIgnoreCase))
                {
                    target = _tabs[i];
                    break;
                }
            }

            if (target == null)
            {
                return;
            }

            _activeTabId = target.Id;
            RebuildVisualTabStripUi();
            SetActivePanelView(VaporPanelView.Browser);

            if (navigateBrowser && !string.IsNullOrWhiteSpace(target.Url))
            {
                if (!string.Equals(_currentSource, target.Url, StringComparison.OrdinalIgnoreCase))
                {
                    NavigateToInputUrl(target.Url);
                }
            }
            UpdateStatusDisplay();
        }

        private void AddNewVisualTab(string title, string url, bool selectAndNavigate)
        {
            VaporTabItem item = new VaporTabItem
            {
                Id = "tab-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Title = string.IsNullOrWhiteSpace(title) ? "New Tab" : title,
                Url = string.IsNullOrWhiteSpace(url) ? "poc://diagnostics-test-page" : url,
                BadgeText = ShortcutsService.DeriveBadgeText(title),
                BadgeBgHex = ShortcutsService.DeriveBadgeColor(title),
                BadgeFgHex = "#FFFFFF"
            };
            _tabs.Add(item);
            if (selectAndNavigate)
            {
                SelectVisualTab(item.Id, true);
            }
            else
            {
                RebuildVisualTabStripUi();
                UpdateStatusDisplay();
            }
        }

        private void CloseVisualTab(string tabId)
        {
            int idx = -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, tabId, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                return;
            }

            bool wasActive = string.Equals(_tabs[idx].Id, _activeTabId, StringComparison.OrdinalIgnoreCase);
            _tabs.RemoveAt(idx);

            if (_tabs.Count == 0)
            {
                AddNewVisualTab("Workspace", "poc://diagnostics-test-page", true);
                return;
            }

            if (wasActive)
            {
                int nextIdx = Math.Min(idx, _tabs.Count - 1);
                SelectVisualTab(_tabs[nextIdx].Id, true);
            }
            else
            {
                RebuildVisualTabStripUi();
                UpdateStatusDisplay();
            }
        }

        private void RebuildShortcutsListUi()
        {
            _shortcutsListStack.Children.Clear();
            IReadOnlyList<ShortcutItem> items = _shortcutsService.GetShortcuts();

            for (int i = 0; i < items.Count; i++)
            {
                ShortcutItem item = items[i];
                string capturedId = item.Id;
                string capturedUrl = item.Url;
                string capturedName = item.Name;

                Border rowBorder = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = DarkTheme.BorderDividerBrush,
                    BorderThickness = new Thickness(0, 0, 0, i < items.Count - 1 ? 1 : 0),
                    Padding = new Thickness(12, 8, 10, 8),
                    Cursor = Cursors.Hand
                };
                rowBorder.MouseEnter += (s, e) => rowBorder.Background = DarkTheme.SecondarySurfaceBrush;
                rowBorder.MouseLeave += (s, e) => rowBorder.Background = Brushes.Transparent;

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Badge
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) }); // URL
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Vertical three-dot action button

                Border badge = new Border
                {
                    Width = 22,
                    Height = 22,
                    Background = ParseHexBrush(item.BadgeBgHex, Color.FromRgb(48, 54, 61)),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = item.BadgeText,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(badge, 0);
                rowGrid.Children.Add(badge);

                TextBlock nameText = new TextBlock
                {
                    Text = item.Name,
                    Foreground = DarkTheme.TextPrimaryBrush,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameText, 1);
                rowGrid.Children.Add(nameText);

                TextBlock urlText = new TextBlock
                {
                    Text = item.Url,
                    Foreground = DarkTheme.TextSecondaryBrush,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(urlText, 2);
                rowGrid.Children.Add(urlText);

                Button actionBtn = CreateBorderlessIconButton("⋮", "Remove shortcut", 24, 22);
                actionBtn.Foreground = DarkTheme.TextSecondaryBrush;
                actionBtn.FontSize = 13;
                actionBtn.FontWeight = FontWeights.Bold;
                actionBtn.Margin = new Thickness(0);
                actionBtn.Click += (s, e) =>
                {
                    RecordInternalInteraction();
                    e.Handled = true;
                    if (_shortcutsService.RemoveShortcut(capturedId))
                    {
                        RebuildShortcutsListUi();
                        UpdateStatusDisplay();
                    }
                };
                Grid.SetColumn(actionBtn, 3);
                rowGrid.Children.Add(actionBtn);

                rowBorder.Child = rowGrid;
                rowBorder.MouseLeftButtonUp += (s, e) =>
                {
                    RecordInternalInteraction();
                    OpenShortcutItem(capturedName, capturedUrl);
                };

                _shortcutsListStack.Children.Add(rowBorder);
            }
        }

        public bool TryAddShortcutFromInputs(string rawName, string rawUrl)
        {
            ShortcutItem? added;
            string error;
            if (!_shortcutsService.AddShortcut(rawName, rawUrl, out added, out error))
            {
                _lastShortcutValidationError = error;
                _shortcutValidationText.Text = error;
                _shortcutValidationText.Visibility = Visibility.Visible;
                UpdateStatusDisplay();
                return false;
            }

            _lastShortcutValidationError = string.Empty;
            _shortcutNameInput.Text = string.Empty;
            _shortcutUrlInput.Text = string.Empty;
            _shortcutValidationText.Visibility = Visibility.Collapsed;
            _addShortcutFormBorder.Visibility = Visibility.Collapsed;
            RebuildShortcutsListUi();
            UpdateStatusDisplay();
            return true;
        }

        public void OpenShortcutItem(string name, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            // Update active visual tab metadata
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, _activeTabId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _tabs[i].Title = name;
                        _tabs[i].BadgeText = ShortcutsService.DeriveBadgeText(name);
                        _tabs[i].BadgeBgHex = ShortcutsService.DeriveBadgeColor(name);
                    }
                    _tabs[i].Url = url;
                    break;
                }
            }

            RebuildVisualTabStripUi();
            SetActivePanelView(VaporPanelView.Browser);
            NavigateToInputUrl(url);
        }

        private void LoadNoteIntoUi(NoteDocument note)
        {
            _isLoadingNoteContent = true;
            try
            {
                _notesTitleBox.Text = string.IsNullOrWhiteSpace(note.Title) ? "Today - Notes" : note.Title;
                _notesRichTextBox.Document.Blocks.Clear();

                bool loadedFormatted = false;
                if (!string.IsNullOrWhiteSpace(note.XamlBase64))
                {
                    try
                    {
                        byte[] xamlBytes = Convert.FromBase64String(note.XamlBase64);
                        using (MemoryStream ms = new MemoryStream(xamlBytes))
                        {
                            TextRange range = new TextRange(_notesRichTextBox.Document.ContentStart, _notesRichTextBox.Document.ContentEnd);
                            range.Load(ms, DataFormats.Xaml);
                            loadedFormatted = true;
                        }
                    }
                    catch
                    {
                        loadedFormatted = false;
                    }
                }

                if (!loadedFormatted)
                {
                    Paragraph p = new Paragraph(new Run(note.ContentText ?? string.Empty));
                    _notesRichTextBox.Document.Blocks.Add(p);
                }

                _notesSaveStatusText.Text = string.IsNullOrWhiteSpace(note.LastSavedDisplay) ? "Auto-saved" : note.LastSavedDisplay;
                _hasUnsavedNoteChanges = false;
                _lastNoteSaveSucceeded = true;
            }
            finally
            {
                _isLoadingNoteContent = false;
            }
        }

        private void OnNotesContentChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingNoteContent || _isShuttingDown)
            {
                return;
            }

            _hasUnsavedNoteChanges = true;
            _notesSaveStatusText.Text = "Saving...";
            _notesAutoSaveTimer.Stop();
            _notesAutoSaveTimer.Start();
        }

        private void OnNotesAutoSaveTimerTick(object? sender, EventArgs e)
        {
            _notesAutoSaveTimer.Stop();
            if (!_isShuttingDown && _hasUnsavedNoteChanges)
            {
                SaveCurrentNoteFromUi();
            }
        }

        public bool SaveCurrentNoteFromUi()
        {
            _notesAutoSaveTimer.Stop();
            string title = _notesTitleBox != null ? _notesTitleBox.Text : "Today - Notes";
            string plainText = GetRichTextBoxPlainText();
            string xamlB64 = GetRichTextBoxXamlBase64();

            string statusText;
            bool saved = _notesService.SaveNote(title, plainText, xamlB64, out statusText);
            _lastNoteSaveSucceeded = saved;
            _hasUnsavedNoteChanges = !saved;
            if (_notesSaveStatusText != null)
            {
                _notesSaveStatusText.Text = statusText;
                _notesSaveStatusText.Foreground = saved
                    ? DarkTheme.TextSecondaryBrush
                    : CreateBrush(248, 113, 113);
            }
            UpdateStatusDisplay();
            return saved;
        }

        private void StartNewNoteInUi()
        {
            _isLoadingNoteContent = true;
            try
            {
                _notesTitleBox.Text = "Note - " + DateTime.Now.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
                _notesRichTextBox.Document.Blocks.Clear();
                _notesRichTextBox.Document.Blocks.Add(new Paragraph(new Run(string.Empty)));
            }
            finally
            {
                _isLoadingNoteContent = false;
            }
            SaveCurrentNoteFromUi();
            FocusRichTextBox(_notesRichTextBox);
        }

        public void SetNoteContentProgrammatically(string title, string content)
        {
            _isLoadingNoteContent = true;
            try
            {
                _notesTitleBox.Text = string.IsNullOrWhiteSpace(title) ? "Today - Notes" : title;
                _notesRichTextBox.Document.Blocks.Clear();
                _notesRichTextBox.Document.Blocks.Add(new Paragraph(new Run(content ?? string.Empty)));
            }
            finally
            {
                _isLoadingNoteContent = false;
            }
            SaveCurrentNoteFromUi();
        }

        private string GetRichTextBoxPlainText()
        {
            if (_notesRichTextBox == null)
            {
                return string.Empty;
            }

            TextRange tr = new TextRange(_notesRichTextBox.Document.ContentStart, _notesRichTextBox.Document.ContentEnd);
            return (tr.Text ?? string.Empty).TrimEnd('\r', '\n');
        }

        private string GetRichTextBoxXamlBase64()
        {
            if (_notesRichTextBox == null)
            {
                return string.Empty;
            }

            try
            {
                TextRange tr = new TextRange(_notesRichTextBox.Document.ContentStart, _notesRichTextBox.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream())
                {
                    tr.Save(ms, DataFormats.Xaml);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        // =========================================================================
        // MODE, CAPTURE PROTECTION, AND FOCUS MANAGEMENT
        // =========================================================================

        public void SetOverlayMode(OverlayMode mode, bool persistToDisk)
        {
            _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
            _ultraDeactivateSettleTimer.Stop();

            if (_currentMode != mode)
            {
                _currentMode = mode;
                _modeChangeCount++;
            }

            if (persistToDisk)
            {
                _settingsService.SaveSettings(_currentMode, _captureProtectionEnabled, _selectedThemeOption);
                _persistedMode = _settingsService.LoadMode();
            }

            Topmost = true;
            UpdateModeAndCaptureUi();

            if (IsVisible && _currentMode == OverlayMode.Ultra && _hwnd != IntPtr.Zero)
            {
                ForceForegroundActivation();
            }

            UpdateStatusDisplay();
        }

        public void ToggleOverlayMode()
        {
            OverlayMode next = (_currentMode == OverlayMode.Lite)
                ? OverlayMode.Ultra
                : OverlayMode.Lite;
            SetOverlayMode(next, true);
        }

        private void UpdateModeAndCaptureUi()
        {
            bool isLite = _currentMode == OverlayMode.Lite;

            if (_menuLiteModeBtn != null && _menuUltraModeBtn != null)
            {
                _menuLiteModeBtn.Background = isLite ? DarkTheme.AccentBlueBrush : DarkTheme.SecondarySurfaceBrush;
                _menuLiteModeBtn.Foreground = isLite ? Brushes.White : DarkTheme.TextSecondaryBrush;

                _menuUltraModeBtn.Background = !isLite ? DarkTheme.AccentBlueBrush : DarkTheme.SecondarySurfaceBrush;
                _menuUltraModeBtn.Foreground = !isLite ? Brushes.White : DarkTheme.TextSecondaryBrush;
            }

            if (_settingsLiteCard != null && _settingsUltraCard != null)
            {
                _settingsLiteCard.Background = isLite ? DarkTheme.AccentBlueCardFillBrush : DarkTheme.PrimarySurfaceBrush;
                _settingsLiteCard.BorderBrush = isLite ? DarkTheme.AccentBlueBrush : DarkTheme.BorderSubtleBrush;
                _settingsLiteRadio.Text = isLite ? "◉" : "◯";
                _settingsLiteRadio.Foreground = isLite ? DarkTheme.AccentBlueBrush : DarkTheme.TextMutedBrush;

                _settingsUltraCard.Background = !isLite ? DarkTheme.AccentBlueCardFillBrush : DarkTheme.PrimarySurfaceBrush;
                _settingsUltraCard.BorderBrush = !isLite ? DarkTheme.AccentBlueBrush : DarkTheme.BorderSubtleBrush;
                _settingsUltraRadio.Text = !isLite ? "◉" : "◯";
                _settingsUltraRadio.Foreground = !isLite ? DarkTheme.AccentBlueBrush : DarkTheme.TextMutedBrush;
            }

            bool isCaptureActive = (_hwnd == IntPtr.Zero)
                ? _captureProtectionEnabled
                : (_reportedAffinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            UpdateToggleSwitchVisual(_menuCaptureToggleTrack, _menuCaptureToggleThumb, isCaptureActive);
            UpdateToggleSwitchVisual(_settingsCaptureToggleTrack, _settingsCaptureToggleThumb, isCaptureActive);

            string statusMsg;
            if (_hwnd != IntPtr.Zero && !_setAffinityReturn && _setAffinityLastError != 0)
            {
                statusMsg = "API Error: Win32 Code " + _setAffinityLastError.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                statusMsg = isCaptureActive
                    ? "Active (WDA_EXCLUDEFROMCAPTURE 0x11)"
                    : "Disabled (WDA_NONE 0x00)";
            }

            if (_menuCaptureStatusText != null)
            {
                _menuCaptureStatusText.Text = statusMsg;
            }
            if (_settingsCaptureStatusText != null)
            {
                _settingsCaptureStatusText.Text = statusMsg;
            }

            UpdateThemeSelectionButtonsUi();
        }

        private void ToggleThemesSubmenu()
        {
            if (_menuThemesSubmenuBorder == null)
            {
                return;
            }

            bool willBeVisible = _menuThemesSubmenuBorder.Visibility != Visibility.Visible;
            _menuThemesSubmenuBorder.Visibility = willBeVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_menuThemesChevron != null)
            {
                _menuThemesChevron.Text = willBeVisible ? "▾" : "›";
            }

            UpdateThemeSelectionButtonsUi();
            UpdateStatusDisplay();
        }

        private void SelectThemeOption(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return;
            }

            _selectedThemeOption = string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase)
                ? "Dark"
                : "Neon";
            _settingsService.SaveSettings(_currentMode, _captureProtectionEnabled, _selectedThemeOption);
            UpdateThemeSelectionButtonsUi();
            UpdateStatusDisplay();
        }

        private void UpdateThemeSelectionButtonsUi()
        {
            if (_themeDarkBtn == null || _themeNeonBtn == null)
            {
                return;
            }

            bool isDark = string.Equals(_selectedThemeOption, "Dark", StringComparison.OrdinalIgnoreCase);

            _themeDarkBtn.Background = isDark ? DarkTheme.AccentBlueBrush : DarkTheme.SecondarySurfaceBrush;
            _themeDarkBtn.Foreground = isDark ? Brushes.White : DarkTheme.TextSecondaryBrush;

            _themeNeonBtn.Background = !isDark ? DarkTheme.AccentBlueBrush : DarkTheme.SecondarySurfaceBrush;
            _themeNeonBtn.Foreground = !isDark ? Brushes.White : DarkTheme.TextSecondaryBrush;
        }

        private void RecordInternalInteraction()
        {
            _internalControlClickCount++;
            _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
            _ultraDeactivateSettleTimer.Stop();
        }

        private void OnTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            RecordInternalInteraction();
            TextBox? box = sender as TextBox;
            if (box != null)
            {
                FocusTextBox(box);
            }
            UpdateStatusDisplay();
        }

        private void FocusTextBox(TextBox box)
        {
            if (_hwnd != IntPtr.Zero)
            {
                IntPtr currentFocus = NativeMethods.GetFocus();
                if (currentFocus != _hwnd)
                {
                    NativeMethods.SetFocus(_hwnd);
                }
            }
            box.Focus();
            Keyboard.Focus(box);
        }

        private void OnRichTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            RecordInternalInteraction();
            FocusRichTextBox(_notesRichTextBox);
            UpdateStatusDisplay();
        }

        private void FocusRichTextBox(RichTextBox rtb)
        {
            if (_hwnd != IntPtr.Zero)
            {
                IntPtr currentFocus = NativeMethods.GetFocus();
                if (currentFocus != _hwnd)
                {
                    NativeMethods.SetFocus(_hwnd);
                }
            }
            rtb.Focus();
            Keyboard.Focus(rtb);
        }

        private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                RecordInternalInteraction();
                _isDraggingOrResizing = true;
                try
                {
                    DragMove();
                }
                finally
                {
                    _isDraggingOrResizing = false;
                    SaveCurrentGeometry();
                    UpdateStatusDisplay();
                }
            }
        }

        private void OnUrlBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RecordInternalInteraction();
                NavigateToInputUrl(_urlBox.Text);
                e.Handled = true;
            }
            else
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(UpdateStatusDisplay));
            }
        }

        private IntPtr _lastDeactivateTargetHwnd = IntPtr.Zero;

        private bool IsOverlayMinimized()
        {
            if (WindowState == WindowState.Minimized)
            {
                return true;
            }

            if (_hwnd != IntPtr.Zero && NativeMethods.IsIconic(_hwnd))
            {
                return true;
            }

            return false;
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_isShuttingDown)
            {
                return;
            }

            if (IsOverlayMinimized())
            {
                _ultraDeactivateSettleTimer.Stop();
                _isOverlayActive = false;
                UpdateStatusDisplay();
                return;
            }

            if (WindowState == WindowState.Normal)
            {
                SaveCurrentGeometry();
            }

            UpdateStatusDisplay();
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            if (_isShuttingDown)
            {
                return;
            }

            _ultraDeactivateSettleTimer.Stop();
            _isOverlayActive = !IsOverlayMinimized();
            _lastDeactivateTargetHwnd = _hwnd;
            UpdateStatusDisplay();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_isShuttingDown)
            {
                return;
            }

            HandleTopLevelWindowDeactivated(IntPtr.Zero);
        }

        private void HandleTopLevelWindowDeactivated(IntPtr activatedHwndParam)
        {
            if (_isShuttingDown || !IsVisible || IsOverlayMinimized())
            {
                return;
            }

            if (activatedHwndParam != IntPtr.Zero && !IsWindowPartOfOverlay(activatedHwndParam))
            {
                _lastDeactivateTargetHwnd = activatedHwndParam;
            }

            if (DateTime.UtcNow < _suppressAutoHideUntilUtc || _isDraggingOrResizing)
            {
                return;
            }

            if (_currentMode == OverlayMode.Lite)
            {
                _isOverlayActive = false;
                _externalFocusTransferCount++;
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
                UpdateStatusDisplay();
                return;
            }

            _ultraDeactivateSettleTimer.Stop();
            _ultraDeactivateSettleTimer.Start();
        }

        private void OnUltraDeactivateSettleTimerTick(object? sender, EventArgs e)
        {
            _ultraDeactivateSettleTimer.Stop();

            if (_isShuttingDown || !IsVisible || IsOverlayMinimized() || _currentMode != OverlayMode.Ultra || _isDraggingOrResizing)
            {
                return;
            }

            if (DateTime.UtcNow < _suppressAutoHideUntilUtc)
            {
                return;
            }

            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero)
            {
                fgHwnd = _lastDeactivateTargetHwnd;
            }

            IntPtr focusHwnd = NativeMethods.GetFocus();

            if (IsWindowPartOfOverlay(fgHwnd) || IsWindowPartOfOverlay(focusHwnd))
            {
                _isOverlayActive = true;
                UpdateStatusDisplay();
                return;
            }

            HideOverlay("UltraOutsideClick");
        }

        private bool IsWindowPartOfOverlay(IntPtr targetHwnd)
        {
            if (targetHwnd == IntPtr.Zero || _hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (targetHwnd == _hwnd)
            {
                return true;
            }

            if (NativeMethods.IsChild(_hwnd, targetHwnd))
            {
                return true;
            }

            IntPtr owner = NativeMethods.GetWindow(targetHwnd, NativeMethods.GW_OWNER);
            if (owner == _hwnd)
            {
                return true;
            }

            IntPtr root = NativeMethods.GetAncestor(targetHwnd, NativeMethods.GA_ROOT);
            if (root == _hwnd)
            {
                return true;
            }

            IntPtr rootOwner = NativeMethods.GetAncestor(targetHwnd, NativeMethods.GA_ROOTOWNER);
            if (rootOwner == _hwnd)
            {
                return true;
            }

            uint targetPid;
            NativeMethods.GetWindowThreadProcessId(targetHwnd, out targetPid);
            if (targetPid == (uint)Environment.ProcessId)
            {
                return true;
            }

            return false;
        }

        // =========================================================================
        // WIN32 INITIALIZATION, WEBVIEW2 HOSTING & NAVIGATION
        // =========================================================================

        private void OnWindowSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
            }

            // 1. Apply Windows 11 DWM Dark Mode and Rounded Corners (DWMWCP_ROUND = 2)
            int darkVal = 1;
            _dwmDarkHResult = NativeMethods.DwmSetWindowAttribute(
                _hwnd,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkVal,
                sizeof(int));

            int cornerPref = NativeMethods.DWMWCP_ROUND;
            _dwmCornerHResult = NativeMethods.DwmSetWindowAttribute(
                _hwnd,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref cornerPref,
                sizeof(int));

            // 2. Apply Screen-Sharing Exclusion based on CLI override or persisted setting
            uint initialAffinity = _startWithAffinityDisabled
                ? NativeMethods.WDA_NONE
                : (_captureProtectionEnabled ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
            ApplyDisplayAffinity(initialAffinity, false);

            // 3. Register Global Shortcut: Ctrl + Alt + Space
            uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
            _hotkeyRegistered = NativeMethods.RegisterHotKey(
                _hwnd,
                NativeMethods.HOTKEY_ID_TOGGLE,
                modifiers,
                NativeMethods.VK_SPACE);
            _hotkeyLastError = _hotkeyRegistered ? 0 : Marshal.GetLastWin32Error();

            if (!_hotkeyRegistered)
            {
                string reason = _hotkeyLastError == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "ERROR_HOTKEY_ALREADY_REGISTERED (1409) — Another process currently holds Ctrl + Alt + Space."
                    : "Win32 Error Code " + _hotkeyLastError.ToString(CultureInfo.InvariantCulture);

                _conflictBanner.Text = "Shortcut Conflict Detected: Could not register Ctrl + Alt + Space. " + reason;
                _conflictBanner.Visibility = Visibility.Visible;
            }

            _isOverlayActive = true;
            SaveCurrentGeometry();
            UpdateStatusDisplay();
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (_isShuttingDown)
            {
                return;
            }

            try
            {
                string udfPath = Path.Combine(_dataDir, "WebView2Data");
                Directory.CreateDirectory(udfPath);

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: udfPath);

                if (_isShuttingDown)
                {
                    return;
                }

                if (_useCompositionControl && _compositionWebView != null)
                {
                    await _compositionWebView.EnsureCoreWebView2Async(env);
                    _coreWebView = _compositionWebView.CoreWebView2;
                }
                else if (_hwndWebView != null)
                {
                    await _hwndWebView.EnsureCoreWebView2Async(env);
                    _coreWebView = _hwndWebView.CoreWebView2;
                }

                if (_isShuttingDown)
                {
                    return;
                }

                if (_coreWebView != null)
                {
                    _coreWebView.Settings.AreHostObjectsAllowed = false;
                    _coreWebView.Settings.IsWebMessageEnabled = false;

                    _coreWebView.SourceChanged += OnCoreWebViewSourceChanged;
                    _coreWebView.NavigationCompleted += OnCoreWebViewNavigationCompleted;

                    _webViewInitialized = true;

                    if (!string.IsNullOrWhiteSpace(_initialUrl))
                    {
                        NavigateToInputUrl(_initialUrl);
                    }
                    else
                    {
                        LoadBuiltInTestPage();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_isShuttingDown)
                {
                    _lastNavigationStatus = "InitError: " + ex.Message;
                    UpdateStatusDisplay();
                }
            }
        }

        private void OnCoreWebViewSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs args)
        {
            if (_isShuttingDown || _coreWebView == null)
            {
                return;
            }

            _currentSource = _coreWebView.Source;
            if (!_currentSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                _urlBox.Text = _currentSource;
            }
            UpdateStatusDisplay();
        }

        private async void OnCoreWebViewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_isShuttingDown || _coreWebView == null)
            {
                return;
            }

            _lastNavigationStatus = args.IsSuccess
                ? "Success"
                : "Failed (" + args.WebErrorStatus.ToString() + ")";

            // Sync active visual tab title when navigating to external pages
            try
            {
                if (!_currentSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_currentSource, "poc://diagnostics-test-page", StringComparison.OrdinalIgnoreCase))
                {
                    string docTitle = _coreWebView.DocumentTitle;
                    if (!string.IsNullOrWhiteSpace(docTitle))
                    {
                        for (int i = 0; i < _tabs.Count; i++)
                        {
                            if (string.Equals(_tabs[i].Id, _activeTabId, StringComparison.OrdinalIgnoreCase))
                            {
                                _tabs[i].Title = docTitle;
                                _tabs[i].Url = _currentSource;
                                RebuildVisualTabStripUi();
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            await SyncWebViewDomStateAsync();
            if (!_isShuttingDown)
            {
                UpdateStatusDisplay();
                await SaveVisualSnapshotAsync();
            }
        }

        public void ApplyDisplayAffinity(uint targetAffinity, bool persistToSettings = true)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            _setAffinityReturn = NativeMethods.SetWindowDisplayAffinity(_hwnd, targetAffinity);
            _setAffinityLastError = _setAffinityReturn ? 0 : Marshal.GetLastWin32Error();
            _getAffinityReturn = NativeMethods.GetWindowDisplayAffinity(_hwnd, out _reportedAffinity);

            bool isExcluded = _reportedAffinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE;
            _captureProtectionEnabled = isExcluded;

            if (persistToSettings)
            {
                _settingsService.SaveSettings(_currentMode, _captureProtectionEnabled, _selectedThemeOption);
            }

            UpdateModeAndCaptureUi();
            UpdateStatusDisplay();
        }

        public void ToggleDisplayAffinity(bool persistToSettings = true)
        {
            uint next = (_reportedAffinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE)
                ? NativeMethods.WDA_NONE
                : NativeMethods.WDA_EXCLUDEFROMCAPTURE;
            ApplyDisplayAffinity(next, persistToSettings);
        }

        public void NavigateToInputUrl(string rawInput)
        {
            if (_coreWebView == null)
            {
                return;
            }

            if (_activeView != VaporPanelView.Browser)
            {
                SetActivePanelView(VaporPanelView.Browser);
            }

            string resolvedTarget;
            string classification;
            string resolutionStatus;
            if (!TryClassifyAndResolveAddressInput(
                rawInput,
                out resolvedTarget,
                out classification,
                out resolutionStatus))
            {
                _lastAddressInputClassification = "Invalid";
                _lastResolvedNavigationTarget = string.Empty;
                _lastAddressResolutionStatus = resolutionStatus;
                _urlBox.ToolTip = resolutionStatus;
                UpdateStatusDisplay();
                return;
            }

            _lastAddressInputClassification = classification;
            _lastResolvedNavigationTarget = resolvedTarget;
            _lastAddressResolutionStatus = resolutionStatus;
            _urlBox.ToolTip = "Navigate to address";

            if (string.Equals(classification, "BuiltIn", StringComparison.Ordinal))
            {
                _lastNavigationStatus = "Navigating";
                UpdateStatusDisplay();
                LoadBuiltInTestPage();
                return;
            }

            try
            {
                _urlBox.Text = resolvedTarget;
                _currentSource = resolvedTarget;
                _lastNavigationStatus = "Navigating";
                for (int i = 0; i < _tabs.Count; i++)
                {
                    if (string.Equals(_tabs[i].Id, _activeTabId, StringComparison.OrdinalIgnoreCase))
                    {
                        _tabs[i].Url = resolvedTarget;
                        break;
                    }
                }
                UpdateStatusDisplay();
                _coreWebView.Navigate(resolvedTarget);
            }
            catch (Exception ex)
            {
                _lastNavigationStatus = "NavException: " + ex.Message;
                UpdateStatusDisplay();
            }
        }

        public static bool TryClassifyAndResolveAddressInput(
            string rawInput,
            out string resolvedUrl,
            out string classification,
            out string resolutionStatus)
        {
            resolvedUrl = string.Empty;
            classification = "Invalid";
            resolutionStatus = string.Empty;

            if (string.IsNullOrWhiteSpace(rawInput))
            {
                resolutionStatus = "Address or search query cannot be empty.";
                return false;
            }

            string trimmed = rawInput.Trim();
            if (string.Equals(trimmed, "poc://diagnostics-test-page", StringComparison.OrdinalIgnoreCase))
            {
                resolvedUrl = "poc://diagnostics-test-page";
                classification = "BuiltIn";
                resolutionStatus = "Resolved";
                return true;
            }

            bool hasWhitespace = trimmed.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' }) >= 0;

            // 1. Explicit http:// or https:// URLs (preserve scheme, host, port, path, query, fragment)
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (hasWhitespace)
                {
                    resolutionStatus = "Malformed URL: URLs cannot contain spaces.";
                    return false;
                }

                Uri? explicitUri;
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out explicitUri) ||
                    explicitUri == null ||
                    !IsValidExplicitHttpUri(explicitUri, trimmed))
                {
                    resolutionStatus = "Malformed URL: Please enter a valid host or domain.";
                    return false;
                }

                resolvedUrl = FormatNormalizedHttpUri(explicitUri, trimmed);
                classification = "Url";
                resolutionStatus = "Resolved";
                return true;
            }

            // 2. Explicit file:// URLs
            if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (hasWhitespace)
                {
                    resolutionStatus = "Malformed file URL.";
                    return false;
                }

                Uri? fileUri;
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out fileUri) ||
                    fileUri == null ||
                    !string.Equals(fileUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                {
                    resolutionStatus = "Malformed file URL.";
                    return false;
                }

                resolvedUrl = fileUri.AbsoluteUri;
                classification = "Url";
                resolutionStatus = "Resolved";
                return true;
            }

            // 3. Reject unsupported schemes or broken scheme syntax (e.g. ftp://, javascript:, http:/, ://)
            if (trimmed.Contains("://") ||
                trimmed.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                resolutionStatus = "Unsupported or malformed URL scheme.";
                return false;
            }

            // 4. Multi-word input without a scheme is a search query
            if (hasWhitespace)
            {
                if (!TryBuildGoogleSearchUrl(trimmed, out resolvedUrl))
                {
                    resolutionStatus = "Failed to construct search URL.";
                    return false;
                }
                classification = "Search";
                resolutionStatus = "Resolved";
                return true;
            }

            // 5. Single-token input without a scheme: inspect candidate authority (host[:port])
            int slashIdx = trimmed.IndexOfAny(new char[] { '/', '?', '#' });
            string authority = slashIdx >= 0 ? trimmed.Substring(0, slashIdx) : trimmed;

            if (string.IsNullOrEmpty(authority) ||
                authority.StartsWith(".", StringComparison.Ordinal) ||
                authority.EndsWith(".", StringComparison.Ordinal) ||
                authority.Contains(".."))
            {
                resolutionStatus = "Malformed domain or web address.";
                return false;
            }

            string hostCandidate = authority;
            string? portPart = null;

            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                int closeBracket = authority.IndexOf(']');
                if (closeBracket <= 1)
                {
                    resolutionStatus = "Malformed IPv6 address.";
                    return false;
                }

                hostCandidate = authority.Substring(0, closeBracket + 1);
                if (closeBracket + 1 < authority.Length)
                {
                    if (authority[closeBracket + 1] != ':')
                    {
                        resolutionStatus = "Malformed IPv6 authority.";
                        return false;
                    }
                    // Extract only the port digits after "]:"
                    portPart = authority.Substring(closeBracket + 2);
                }
            }
            else
            {
                int firstColon = authority.IndexOf(':');
                int lastColon = authority.LastIndexOf(':');
                if (firstColon >= 0)
                {
                    if (firstColon != lastColon)
                    {
                        resolutionStatus = "Malformed host or unbracketed IPv6 address.";
                        return false;
                    }
                    hostCandidate = authority.Substring(0, lastColon);
                    portPart = authority.Substring(lastColon + 1);
                }
            }

            if (portPart != null)
            {
                int port;
                if (string.IsNullOrEmpty(hostCandidate) ||
                    string.IsNullOrEmpty(portPart) ||
                    !int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                    port < 1 ||
                    port > 65535)
                {
                    resolutionStatus = "Malformed URL: Invalid port number.";
                    return false;
                }
            }

            bool isLoopbackHost;
            if (IsConfidentUnschemedDomainOrHost(hostCandidate, out isLoopbackHost))
            {
                // Prefer http:// for unschemed localhost/loopback development addresses; https:// for domains & public IPs
                string defaultScheme = isLoopbackHost ? "http://" : "https://";
                string candidateUrl = defaultScheme + trimmed;
                Uri? resolvedUri;
                if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out resolvedUri) ||
                    resolvedUri == null ||
                    !IsValidExplicitHttpUri(resolvedUri, candidateUrl))
                {
                    resolutionStatus = "Malformed web address.";
                    return false;
                }

                resolvedUrl = FormatNormalizedHttpUri(resolvedUri, candidateUrl);
                classification = "Url";
                resolutionStatus = "Resolved";
                return true;
            }

            // If the user typed a dotted, bracketed, or port-qualified token that failed host validation, reject as malformed
            if (authority.IndexOf('.') >= 0 || portPart != null || authority.StartsWith("[", StringComparison.Ordinal))
            {
                resolutionStatus = "Malformed domain or web address.";
                return false;
            }

            // Otherwise (e.g. single word "youtube"), classify as a search query
            if (!TryBuildGoogleSearchUrl(trimmed, out resolvedUrl))
            {
                resolutionStatus = "Failed to construct search URL.";
                return false;
            }
            classification = "Search";
            resolutionStatus = "Resolved";
            return true;
        }

        private static bool IsValidExplicitHttpUri(Uri uri, string rawInputWithScheme)
        {
            if (uri == null || !uri.IsAbsoluteUri)
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Authority) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                uri.Port < 1 ||
                uri.Port > 65535)
            {
                return false;
            }

            int schemeSep = rawInputWithScheme.IndexOf("://", StringComparison.Ordinal);
            if (schemeSep <= 0)
            {
                return false;
            }
            string afterScheme = rawInputWithScheme.Substring(schemeSep + 3);
            int pathStart = afterScheme.IndexOfAny(new char[] { '/', '?', '#' });
            string rawAuthority = pathStart >= 0 ? afterScheme.Substring(0, pathStart) : afterScheme;
            if (string.IsNullOrWhiteSpace(rawAuthority) ||
                rawAuthority.EndsWith(":", StringComparison.Ordinal))
            {
                return false;
            }

            string host = uri.Host;
            if (uri.HostNameType == UriHostNameType.IPv6)
            {
                if (!uri.Authority.StartsWith("[", StringComparison.Ordinal) ||
                    uri.Authority.IndexOf(']') <= 1)
                {
                    return false;
                }

                System.Net.IPAddress? ipv6;
                return (System.Net.IPAddress.TryParse(host, out ipv6) ||
                        System.Net.IPAddress.TryParse(uri.DnsSafeHost, out ipv6)) &&
                       ipv6 != null &&
                       ipv6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            }

            if (uri.HostNameType == UriHostNameType.IPv4)
            {
                string[] octets = host.Split('.');
                if (octets.Length != 4)
                {
                    return false;
                }
                System.Net.IPAddress? ipv4;
                return System.Net.IPAddress.TryParse(host, out ipv4) &&
                       ipv4 != null &&
                       ipv4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
            }

            if (uri.HostNameType == UriHostNameType.Dns)
            {
                if (host.StartsWith(".", StringComparison.Ordinal) ||
                    host.EndsWith(".", StringComparison.Ordinal) ||
                    host.Contains(".."))
                {
                    return false;
                }

                string[] labels = host.Split('.');
                for (int i = 0; i < labels.Length; i++)
                {
                    if (!IsValidDnsLabel(labels[i]))
                    {
                        return false;
                    }
                }

                if (labels.Length > 1 && IsAllDigits(labels[labels.Length - 1]))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool IsConfidentUnschemedDomainOrHost(string host, out bool isLoopback)
        {
            isLoopback = false;
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                isLoopback = true;
                return true;
            }

            if (host.StartsWith("[", StringComparison.Ordinal) &&
                host.EndsWith("]", StringComparison.Ordinal) &&
                host.Length > 2)
            {
                string innerIpv6 = host.Substring(1, host.Length - 2);
                System.Net.IPAddress? ipv6;
                if (System.Net.IPAddress.TryParse(innerIpv6, out ipv6) &&
                    ipv6 != null &&
                    ipv6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    isLoopback = System.Net.IPAddress.IsLoopback(ipv6);
                    return true;
                }
                return false;
            }

            if (host.IndexOf('.') >= 0)
            {
                string[] parts = host.Split('.');
                if (parts.Length == 4 && IsAllDigits(parts[3]))
                {
                    System.Net.IPAddress? ipv4;
                    if (System.Net.IPAddress.TryParse(host, out ipv4) &&
                        ipv4 != null &&
                        ipv4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        isLoopback = System.Net.IPAddress.IsLoopback(ipv4);
                        return true;
                    }
                    return false;
                }
            }

            if (host.IndexOf('.') <= 0 ||
                host.StartsWith(".", StringComparison.Ordinal) ||
                host.EndsWith(".", StringComparison.Ordinal) ||
                host.Contains(".."))
            {
                return false;
            }

            string[] labels = host.Split('.');
            if (labels.Length < 2)
            {
                return false;
            }

            for (int i = 0; i < labels.Length; i++)
            {
                if (!IsValidDnsLabel(labels[i]))
                {
                    return false;
                }
            }

            string tld = labels[labels.Length - 1];
            if (tld.Length < 2)
            {
                return false;
            }

            if (tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
            {
                return tld.Length > 4;
            }

            for (int c = 0; c < tld.Length; c++)
            {
                char ch = tld[c];
                bool isAlpha = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
                if (!isAlpha)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidDnsLabel(string label)
        {
            if (string.IsNullOrEmpty(label) ||
                label.StartsWith("-", StringComparison.Ordinal) ||
                label.EndsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            for (int c = 0; c < label.Length; c++)
            {
                char ch = label[c];
                bool isAlphaNum = (ch >= 'a' && ch <= 'z') ||
                                  (ch >= 'A' && ch <= 'Z') ||
                                  (ch >= '0' && ch <= '9');
                if (!isAlphaNum && ch != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAllDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static string FormatNormalizedHttpUri(Uri uri, string originalInputWithScheme)
        {
            string abs = uri.AbsoluteUri;
            if (abs.EndsWith("/", StringComparison.Ordinal) &&
                uri.PathAndQuery == "/" &&
                string.IsNullOrEmpty(uri.Fragment) &&
                !originalInputWithScheme.EndsWith("/", StringComparison.Ordinal))
            {
                return abs.Substring(0, abs.Length - 1);
            }
            return abs;
        }

        private static bool TryBuildGoogleSearchUrl(string query, out string searchUrl)
        {
            searchUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(query) ||
                !DefaultSearchProviderUrlTemplate.Contains("{0}"))
            {
                return false;
            }

            string candidate = string.Format(
                CultureInfo.InvariantCulture,
                DefaultSearchProviderUrlTemplate,
                Uri.EscapeDataString(query));

            Uri? validatedUri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out validatedUri) ||
                validatedUri == null ||
                !string.Equals(validatedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            searchUrl = validatedUri.AbsoluteUri;
            return true;
        }

        private void LoadBuiltInTestPage()
        {
            if (_coreWebView == null)
            {
                return;
            }

            _urlBox.Text = "poc://diagnostics-test-page";
            _currentSource = "poc://diagnostics-test-page";

            string modeName = _useCompositionControl ? "WebView2CompositionControl" : "WebView2 (HWND Host)";
            string html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<title>Vapor Overlay Workspace</title>
<style>
  * { box-sizing: border-box; }
  body {
    margin: 0;
    padding: 18px;
    background: linear-gradient(160deg, #0b0d10 0%, #14181d 55%, #0b0d10 100%);
    color: #f4f5f7;
    font-family: 'Segoe UI', system-ui, sans-serif;
    min-height: 1200px;
  }
  .hero {
    background: linear-gradient(135deg, rgba(22, 26, 31, 0.95), rgba(14, 17, 20, 0.98));
    border: 1px solid #292e34;
    border-radius: 10px;
    padding: 18px 20px;
    margin-bottom: 14px;
  }
  h2 { margin: 0 0 6px 0; font-size: 18px; color: #f4f5f7; }
  p { margin: 4px 0; font-size: 13px; color: #a5adb7; }
  .row { display: flex; gap: 10px; align-items: center; margin-top: 12px; flex-wrap: wrap; }
  button {
    background: #1677ff; color: #fff; border: none; border-radius: 6px;
    padding: 6px 12px; font-weight: 600; cursor: pointer;
  }
  button.tab-btn { background: #191d21; border: 1px solid #292e34; color: #a5adb7; }
  button.tab-btn.active { background: #1677ff; border: 1px solid #1677ff; color: #ffffff; }
  input {
    background: #0d1013; color: #f4f5f7; border: 1px solid #292e34;
    border-radius: 6px; padding: 6px 10px; width: 320px;
  }
  .badge {
    display: inline-block; background: #191d21; color: #f4f5f7;
    border: 1px solid #292e34;
    padding: 2px 8px; border-radius: 4px; font-size: 11.5px; font-weight: 600;
  }
</style>
</head>
<body>
  <div class='hero'>
    <h2>Maximize your potential with Vapor</h2>
    <p>Host: <span class='badge'>" + modeName + @"</span> | Runtime: <span class='badge'>" + _runtimeVersion + @"</span></p>
    <p id='viewportInfo'>Viewport: measuring...</p>
    <div class='row'>
      <button id='tabBtn1' class='tab-btn active' onclick='switchTab(""Tab1-Workspace"")'>Tab 1: Workspace</button>
      <button id='tabBtn2' class='tab-btn' onclick='switchTab(""Tab2-Docs"")'>Tab 2: Docs</button>
      <button id='tabBtn3' class='tab-btn' onclick='switchTab(""Tab3-Notes"")'>Tab 3: Notes</button>
      <span>Active: <strong id='activeTabLabel'>Tab1-Workspace</strong></span>
    </div>
    <div class='row'>
      <button id='incBtn' onclick='incCounter()'>Increment State Counter: <span id='cnt'>0</span></button>
      <input id='noteInput' type='text' value='State persists across Lite switches & Ultra hide/restore!' />
    </div>
  </div>
  <script>
    window.count = 0;
    window.activeTabName = 'Tab1-Workspace';
    function incCounter() {
      window.count++;
      document.getElementById('cnt').textContent = window.count;
      return window.count;
    }
    function switchTab(name) {
      window.activeTabName = name;
      document.getElementById('activeTabLabel').textContent = name;
      var btns = document.querySelectorAll('.tab-btn');
      for (var i = 0; i < btns.length; i++) btns[i].classList.remove('active');
      if (name === 'Tab1-Workspace') document.getElementById('tabBtn1').classList.add('active');
      if (name === 'Tab2-Docs') document.getElementById('tabBtn2').classList.add('active');
      if (name === 'Tab3-Notes') document.getElementById('tabBtn3').classList.add('active');
      return window.activeTabName;
    }
    function updateMetrics() {
      var el = document.getElementById('viewportInfo');
      if (el) {
        el.textContent = 'Viewport: ' + window.innerWidth + 'x' + window.innerHeight + ' | DPR: ' + window.devicePixelRatio.toFixed(2);
      }
    }
    window.addEventListener('resize', updateMetrics);
    updateMetrics();
  </script>
</body>
</html>";

            _coreWebView.NavigateToString(html);
        }

        private async System.Threading.Tasks.Task SyncWebViewDomStateAsync()
        {
            if (_coreWebView == null || !_webViewInitialized)
            {
                return;
            }

            try
            {
                string cntRaw = await _coreWebView.ExecuteScriptAsync("window.count !== undefined ? window.count : 0");
                int parsedCnt;
                if (int.TryParse(cntRaw.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedCnt))
                {
                    _webViewDomCounter = parsedCnt;
                }

                string tabRaw = await _coreWebView.ExecuteScriptAsync("window.activeTabName || 'Tab1-Workspace'");
                if (!string.IsNullOrWhiteSpace(tabRaw) && tabRaw != "null")
                {
                    _webViewDomActiveTab = tabRaw.Trim('"');
                }

                string noteRaw = await _coreWebView.ExecuteScriptAsync(
                    "document.getElementById('noteInput') ? document.getElementById('noteInput').value : ''");
                if (noteRaw != null && noteRaw != "null")
                {
                    _webViewDomNoteValue = noteRaw.Trim('"');
                }
            }
            catch
            {
            }
        }

        // =========================================================================
        // WIN32 MESSAGE HOOK (WNDPROC)
        // =========================================================================

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_isShuttingDown)
            {
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == NativeMethods.HOTKEY_ID_TOGGLE)
            {
                _toggleCount++;
                ToggleOverlayVisibility();
                handled = true;
            }
            else if (msg == NativeMethods.WM_CLOSE)
            {
                ExecuteIdempotentShutdownCleanup("WM_CLOSE");
            }
            else if (msg == NativeMethods.WM_SIZE)
            {
                int sizeType = (int)(wParam.ToInt64() & 0xFFFF);
                if (sizeType == NativeMethods.SIZE_MINIMIZED)
                {
                    _ultraDeactivateSettleTimer.Stop();
                    _isOverlayActive = false;
                    UpdateStatusDisplay();
                }
            }
            else if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    _getAffinityReturn = NativeMethods.GetWindowDisplayAffinity(_hwnd, out _reportedAffinity);
                    UpdateStatusDisplay();
                }
            }
            else if (msg == NativeMethods.WM_ENTERSIZEMOVE)
            {
                _isDraggingOrResizing = true;
            }
            else if (msg == NativeMethods.WM_EXITSIZEMOVE)
            {
                _isDraggingOrResizing = false;
                SaveCurrentGeometry();
                UpdateStatusDisplay();
            }
            else if (msg == NativeMethods.WM_PARENTNOTIFY)
            {
                int eventId = (int)(wParam.ToInt64() & 0xFFFF);
                if (eventId == NativeMethods.WM_LBUTTONDOWN ||
                    eventId == NativeMethods.WM_RBUTTONDOWN ||
                    eventId == NativeMethods.WM_MBUTTONDOWN)
                {
                    RecordInternalInteraction();
                    _isOverlayActive = true;
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(async () =>
                        {
                            if (_isShuttingDown)
                            {
                                return;
                            }
                            await System.Threading.Tasks.Task.Delay(80);
                            if (!_isShuttingDown)
                            {
                                await SyncWebViewDomStateAsync();
                                UpdateStatusDisplay();
                            }
                        }));
                }
            }
            else if (msg == NativeMethods.WM_ACTIVATE)
            {
                int activeState = (int)(wParam.ToInt64() & 0xFFFF);
                if (activeState == NativeMethods.WA_INACTIVE)
                {
                    HandleTopLevelWindowDeactivated(lParam);
                }
                else
                {
                    _ultraDeactivateSettleTimer.Stop();
                    _isOverlayActive = !IsOverlayMinimized();
                    _lastDeactivateTargetHwnd = _hwnd;
                    UpdateStatusDisplay();
                }
            }
            else if (msg == NativeMethods.WM_LBUTTONUP)
            {
                if (IsVisible && !IsOverlayMinimized() && !_isShuttingDown)
                {
                    try
                    {
                        int clientX = (short)(lParam.ToInt64() & 0xFFFF);
                        int clientY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        Point rawPt = new Point(clientX, clientY);
                        Point dipPt = rawPt;
                        PresentationSource? src = PresentationSource.FromVisual(this);
                        if (src != null && src.CompositionTarget != null)
                        {
                            dipPt = src.CompositionTarget.TransformFromDevice.Transform(rawPt);
                        }

                        if (IsPointInsideElement(_closeButton, dipPt, rawPt))
                        {
                            RecordInternalInteraction();
                            handled = true;
                            Dispatcher.BeginInvoke(
                                DispatcherPriority.Normal,
                                new Action(() => RequestApplicationClose("XButton")));
                        }
                        else if (IsPointInsideElement(_minimizeButton, dipPt, rawPt))
                        {
                            RecordInternalInteraction();
                            handled = true;
                            Dispatcher.BeginInvoke(
                                DispatcherPriority.Normal,
                                new Action(() => MinimizeOverlay("HeaderMinimizeButton")));
                        }
                        else if (IsPointInsideElement(_menuButton, dipPt, rawPt))
                        {
                            RecordInternalInteraction();
                            handled = true;
                            Dispatcher.BeginInvoke(
                                DispatcherPriority.Normal,
                                new Action(ToggleThreeDotMenu));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else if (msg == NativeMethods.WM_APP_SET_MODE)
            {
                OverlayMode requestedMode = (wParam.ToInt32() == 1)
                    ? OverlayMode.Ultra
                    : OverlayMode.Lite;
                SetOverlayMode(requestedMode, true);
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_SYNC_DOM)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(async () =>
                    {
                        if (_isShuttingDown)
                        {
                            return;
                        }
                        await SyncWebViewDomStateAsync();
                        await SaveVisualSnapshotAsync();
                        UpdateStatusDisplay();
                    }));
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_INCREMENT_COUNTER)
            {
                RecordInternalInteraction();
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(async () =>
                    {
                        if (_isShuttingDown)
                        {
                            return;
                        }
                        if (_coreWebView != null && _webViewInitialized)
                        {
                            await _coreWebView.ExecuteScriptAsync("if (window.incCounter) { window.incCounter(); }");
                            await SyncWebViewDomStateAsync();
                        }
                        UpdateStatusDisplay();
                    }));
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_SWITCH_TAB)
            {
                int tabIndex = wParam.ToInt32();
                string tabName = tabIndex == 2 ? "Tab2-Docs" : (tabIndex == 3 ? "Tab3-Notes" : "Tab1-Workspace");
                RecordInternalInteraction();
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(async () =>
                    {
                        if (_isShuttingDown)
                        {
                            return;
                        }
                        if (_coreWebView != null && _webViewInitialized)
                        {
                            await _coreWebView.ExecuteScriptAsync("if (window.switchTab) { window.switchTab('" + tabName + "'); }");
                            await SyncWebViewDomStateAsync();
                        }
                        UpdateStatusDisplay();
                    }));
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_MINIMIZE_WINDOW)
            {
                MinimizeOverlay("DiagnosticMinimize");
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_RESTORE_WINDOW)
            {
                RestoreOverlay("DiagnosticRestore");
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_HIDE_WINDOW)
            {
                HideOverlay("DiagnosticHide");
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_REQUEST_CLOSE)
            {
                string reason = (wParam.ToInt32() == 1) ? "MenuClose" : "XButton";
                RequestApplicationClose(reason);
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_OPEN_VIEW)
            {
                RecordInternalInteraction();
                int viewCode = wParam.ToInt32();
                VaporPanelView targetView = VaporPanelView.Browser;
                if (viewCode >= 0 && viewCode <= 5)
                {
                    targetView = (VaporPanelView)viewCode;
                }
                SetActivePanelView(targetView);
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_TOGGLE_CAPTURE_PROTECTION)
            {
                RecordInternalInteraction();
                int action = wParam.ToInt32();
                if (action == 1)
                {
                    ApplyDisplayAffinity(NativeMethods.WDA_EXCLUDEFROMCAPTURE, true);
                }
                else if (action == 2)
                {
                    ApplyDisplayAffinity(NativeMethods.WDA_NONE, true);
                }
                else
                {
                    ToggleDisplayAffinity(true);
                }
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_ADD_SHORTCUT)
            {
                RecordInternalInteraction();
                int mode = wParam.ToInt32();
                if (mode == 1)
                {
                    // Test invalid URL rejection
                    TryAddShortcutFromInputs("InvalidSite", "not a valid url with spaces");
                }
                else
                {
                    // Test valid shortcut addition
                    TryAddShortcutFromInputs("MDN Web Docs", "developer.mozilla.org");
                }
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_REMOVE_SHORTCUT)
            {
                RecordInternalInteraction();
                _shortcutsService.RemoveShortcut("MDN Web Docs");
                RebuildShortcutsListUi();
                UpdateStatusDisplay();
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_OPEN_SHORTCUT)
            {
                RecordInternalInteraction();
                OpenShortcutItem("BuiltInTest", "poc://diagnostics-test-page");
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_UPDATE_NOTE)
            {
                RecordInternalInteraction();
                SetNoteContentProgrammatically(
                    "Phase 4 Persistent Note",
                    "• Verified note persistence across hide, restore, and app restart.\r\n• Saved atomically to notes.json.");
                handled = true;
            }
            else if (msg == NativeMethods.WM_APP_VISUAL_TAB_ACTION)
            {
                RecordInternalInteraction();
                int action = wParam.ToInt32();
                int param = lParam.ToInt32();
                if (action == 1)
                {
                    AddNewVisualTab("Docs Tab", "poc://diagnostics-test-page", true);
                }
                else if (action == 2 && _tabs.Count > 1)
                {
                    CloseVisualTab(_tabs[_tabs.Count - 1].Id);
                }
                else if (action == 0 && param >= 0 && param < _tabs.Count)
                {
                    SelectVisualTab(_tabs[param].Id, false);
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        private bool IsPointInsideElement(FrameworkElement? element, Point dipPt, Point rawPt)
        {
            if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }

            GeneralTransform transform = element.TransformToAncestor(this);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Contains(dipPt) || bounds.Contains(rawPt);
        }

        // =========================================================================
        // DISTINCT WINDOW LIFECYCLE OPERATIONS (HIDE, MINIMIZE, RESTORE, CLOSE)
        // =========================================================================

        public void HideOverlay(string reason)
        {
            if (_isShuttingDown || _isTransitioningState || !IsVisible)
            {
                return;
            }

            _isTransitioningState = true;
            try
            {
                _ultraDeactivateSettleTimer.Stop();

                if (_hasUnsavedNoteChanges)
                {
                    SaveCurrentNoteFromUi();
                }

                if (!IsOverlayMinimized() && WindowState == WindowState.Normal)
                {
                    SaveCurrentGeometry();
                }

                if (string.Equals(reason, "UltraOutsideClick", StringComparison.Ordinal))
                {
                    _ultraAutoHideCount++;
                }

                _hideCount++;
                _isOverlayActive = false;
                _lastHideReason = string.IsNullOrWhiteSpace(reason) ? "HideCommand" : reason;
                Hide();
                UpdateStatusDisplay();
            }
            finally
            {
                _isTransitioningState = false;
            }
        }

        public void MinimizeOverlay(string reason)
        {
            if (_isShuttingDown || _isTransitioningState)
            {
                return;
            }

            _isTransitioningState = true;
            try
            {
                _ultraDeactivateSettleTimer.Stop();
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(350);

                if (_hasUnsavedNoteChanges)
                {
                    SaveCurrentNoteFromUi();
                }

                if (IsVisible && !IsOverlayMinimized() && WindowState == WindowState.Normal)
                {
                    SaveCurrentGeometry();
                }

                if (!IsVisible)
                {
                    Show();
                }

                _minimizeCount++;
                _isOverlayActive = false;
                WindowState = WindowState.Minimized;
                if (_hwnd != IntPtr.Zero && !NativeMethods.IsIconic(_hwnd))
                {
                    NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWMINIMIZED);
                }

                UpdateStatusDisplay();
            }
            finally
            {
                _isTransitioningState = false;
            }
        }

        public void RestoreOverlay(string reason)
        {
            if (_isShuttingDown || _isTransitioningState)
            {
                return;
            }

            _isTransitioningState = true;
            try
            {
                _ultraDeactivateSettleTimer.Stop();
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(300);

                if (!IsVisible)
                {
                    Show();
                }

                if (_hwnd != IntPtr.Zero && (NativeMethods.IsIconic(_hwnd) || WindowState == WindowState.Minimized))
                {
                    NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);
                }

                if (WindowState != WindowState.Normal)
                {
                    WindowState = WindowState.Normal;
                }

                if (_savedWidth >= MinWidth && _savedHeight >= MinHeight)
                {
                    Left = _savedLeft;
                    Top = _savedTop;
                    Width = _savedWidth;
                    Height = _savedHeight;
                }

                Topmost = true;
                _restoreCount++;
                _isOverlayActive = true;
                _lastRestoreReason = string.IsNullOrWhiteSpace(reason) ? "RestoreCommand" : reason;

                ForceForegroundActivation();
                UpdateStatusDisplay();
            }
            finally
            {
                _isTransitioningState = false;
            }
        }

        private void ToggleOverlayVisibility()
        {
            if (_isShuttingDown || _isTransitioningState)
            {
                return;
            }

            if (IsOverlayMinimized())
            {
                RestoreOverlay("ShortcutRestoreFromMinimized");
                return;
            }

            if (!IsVisible)
            {
                RestoreOverlay("ShortcutRestoreFromHidden");
                return;
            }

            HideOverlay("ShortcutToggle");
        }

        public void RequestApplicationClose(string initiator = "MenuClose")
        {
            if (!string.IsNullOrWhiteSpace(initiator) && string.Equals(_closeInitiator, "None", StringComparison.Ordinal))
            {
                _closeInitiator = initiator;
            }

            if (_isShuttingDown)
            {
                return;
            }

            ExecuteIdempotentShutdownCleanup(_closeInitiator);

            try
            {
                Close();
            }
            catch
            {
            }

            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown(0);
                }
            }
            catch
            {
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            ExecuteIdempotentShutdownCleanup("WindowClosed");

            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown(0);
                }
            }
            catch
            {
            }
        }

        private void ExecuteIdempotentShutdownCleanup(string initiator)
        {
            _isShuttingDown = true;

            if (_shutdownCleanupCompleted)
            {
                return;
            }

            if (string.Equals(_closeInitiator, "None", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(initiator))
            {
                _closeInitiator = initiator;
            }

            // Flush any pending note edits before shutting down
            try
            {
                if (_notesAutoSaveTimer != null)
                {
                    _notesAutoSaveTimer.Stop();
                    _notesAutoSaveTimer.Tick -= OnNotesAutoSaveTimerTick;
                }
                if (_hasUnsavedNoteChanges)
                {
                    SaveCurrentNoteFromUi();
                }
            }
            catch
            {
            }

            try
            {
                if (_ultraDeactivateSettleTimer != null)
                {
                    _ultraDeactivateSettleTimer.Stop();
                    _ultraDeactivateSettleTimer.Tick -= OnUltraDeactivateSettleTimerTick;
                }
            }
            catch
            {
            }

            try
            {
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
            }
            catch
            {
            }

            try
            {
                if (_hwnd != IntPtr.Zero && _hotkeyRegistered && !_hotkeyUnregistered)
                {
                    _hotkeyUnregistered = NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_TOGGLE);
                    _hotkeyRegistered = false;
                    _hotkeyUnregisterCount++;
                }
            }
            catch
            {
            }

            try
            {
                if (_hwndSource != null)
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_coreWebView != null)
                {
                    _coreWebView.SourceChanged -= OnCoreWebViewSourceChanged;
                    _coreWebView.NavigationCompleted -= OnCoreWebViewNavigationCompleted;
                    _coreWebView = null;
                }
                _webViewInitialized = false;
            }
            catch
            {
            }

            try
            {
                if (_webViewHostBorder != null)
                {
                    _webViewHostBorder.Child = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_hwndWebView != null)
                {
                    _hwndWebView.Dispose();
                    _hwndWebView = null;
                    _webViewDisposed = true;
                }
            }
            catch
            {
                _webViewDisposed = true;
            }

            try
            {
                if (_compositionWebView != null)
                {
                    _compositionWebView.Dispose();
                    _compositionWebView = null;
                    _webViewDisposed = true;
                }
            }
            catch
            {
                _webViewDisposed = true;
            }

            try
            {
                if (_settingsService != null)
                {
                    _settingsService.SaveSettings(_currentMode, _captureProtectionEnabled, _selectedThemeOption);
                    _persistedMode = _currentMode;
                }
            }
            catch
            {
            }

            try
            {
                Program.ReleaseSingleInstanceMutexSafely();
            }
            catch
            {
            }

            _shutdownCleanupCompleted = true;
            WriteDiagnosticJson("Closed");
        }

        private void ForceForegroundActivation()
        {
            if (_hwnd == IntPtr.Zero || _isShuttingDown)
            {
                return;
            }

            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            uint currentThreadId = NativeMethods.GetCurrentThreadId();
            uint fgPid;
            uint fgThreadId = fgHwnd != IntPtr.Zero
                ? NativeMethods.GetWindowThreadProcessId(fgHwnd, out fgPid)
                : 0;

            bool attached = false;
            try
            {
                if (fgThreadId != 0 && fgThreadId != currentThreadId)
                {
                    attached = NativeMethods.AttachThreadInput(currentThreadId, fgThreadId, true);
                }

                Activate();
                NativeMethods.BringWindowToTop(_hwnd);
                NativeMethods.SetForegroundWindow(_hwnd);
                NativeMethods.SetFocus(_hwnd);
                _lastDeactivateTargetHwnd = _hwnd;
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, fgThreadId, false);
                }
            }
        }

        private void OnGeometryChanged()
        {
            if (_isShuttingDown)
            {
                return;
            }

            if (IsVisible && WindowState == WindowState.Normal && !IsOverlayMinimized())
            {
                SaveCurrentGeometry();
                UpdateStatusDisplay();
            }
        }

        private void SaveCurrentGeometry()
        {
            if (WindowState != WindowState.Normal || IsOverlayMinimized())
            {
                return;
            }

            if (!double.IsNaN(Left) && !double.IsNaN(Top) &&
                Left > -10000 && Top > -10000 &&
                Width >= MinWidth && Height >= MinHeight)
            {
                _savedLeft = Left;
                _savedTop = Top;
                _savedWidth = Width;
                _savedHeight = Height;
            }
        }

        private void UpdateStatusDisplay()
        {
            if (_isShuttingDown)
            {
                return;
            }

            string modeStatus = "Mode=" + OverlayModeExtensions.ToIdentifier(_currentMode) +
                " | View=" + _activeView.ToString();

            string hkStatus = _hotkeyRegistered
                ? "Ctrl+Alt+Space: OK (Toggles=" + _toggleCount.ToString(CultureInfo.InvariantCulture) + ")"
                : "Ctrl+Alt+Space: CONFLICT (Err=" + _hotkeyLastError.ToString(CultureInfo.InvariantCulture) + ")";

            string wdaStatus = string.Format(
                CultureInfo.InvariantCulture,
                "WDA:0x{0:X2}",
                _reportedAffinity);

            string geomStatus = string.Format(
                CultureInfo.InvariantCulture,
                "Bounds=({0:F0},{1:F0} {2:F0}x{3:F0})",
                _savedLeft,
                _savedTop,
                _savedWidth,
                _savedHeight);

            string navDisplay = string.Equals(_lastAddressInputClassification, "Invalid", StringComparison.Ordinal)
                ? _lastNavigationStatus + " | AddrErr=" + _lastAddressResolutionStatus
                : _lastNavigationStatus;

            _statusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | WV2={3} | {4}",
                modeStatus,
                hkStatus,
                wdaStatus,
                navDisplay,
                geomStatus);

            bool isMinimized = IsOverlayMinimized();
            string stateLabel;
            if (!IsVisible)
            {
                stateLabel = (_lastHideReason == "UltraOutsideClick")
                    ? "HiddenUltraOutsideClick"
                    : "Hidden";
            }
            else if (isMinimized)
            {
                stateLabel = "Minimized";
            }
            else
            {
                stateLabel = "Visible";
            }

            WriteDiagnosticJson(stateLabel);
        }

        private void WriteDiagnosticJson(string lifecycleState)
        {
            try
            {
                IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                if (fgHwnd == IntPtr.Zero)
                {
                    fgHwnd = _isOverlayActive ? _hwnd : _lastDeactivateTargetHwnd;
                }
                uint fgPid = 0;
                if (fgHwnd != IntPtr.Zero)
                {
                    NativeMethods.GetWindowThreadProcessId(fgHwnd, out fgPid);
                }

                bool isMinimized = !_shutdownCleanupCompleted && IsOverlayMinimized();
                string windowStateName = _shutdownCleanupCompleted
                    ? "Closed"
                    : (isMinimized ? "Minimized" : WindowState.ToString());

                int closeBtnX = 0, closeBtnY = 0;
                int minBtnX = 0, minBtnY = 0;
                int menuBtnX = 0, menuBtnY = 0;
                bool headerControlsUnclipped = false;

                try
                {
                    if (!_shutdownCleanupCompleted && IsVisible && !isMinimized && ActualWidth > 0)
                    {
                        GetElementLogicalScreenCenter(_closeButton, out closeBtnX, out closeBtnY);
                        GetElementLogicalScreenCenter(_minimizeButton, out minBtnX, out minBtnY);
                        GetElementLogicalScreenCenter(_menuButton, out menuBtnX, out menuBtnY);

                        headerControlsUnclipped =
                            IsElementWithinWindowBounds(_tabCountBadgeBorder) &&
                            IsElementWithinWindowBounds(_menuButton) &&
                            IsElementWithinWindowBounds(_minimizeButton) &&
                            IsElementWithinWindowBounds(_closeButton);
                    }
                }
                catch
                {
                }

                string activeVisualTabTitle = string.Empty;
                for (int i = 0; i < _tabs.Count; i++)
                {
                    if (string.Equals(_tabs[i].Id, _activeTabId, StringComparison.OrdinalIgnoreCase))
                    {
                        activeVisualTabTitle = _tabs[i].Title;
                        break;
                    }
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"timestampUtc\": \"{0:O}\",\n", DateTime.UtcNow);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"processId\": {0},\n", Environment.ProcessId);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"dotnetVersion\": \"{0}\",\n", Environment.Version);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"osVersion\": \"{0}\",\n", Environment.OSVersion.VersionString);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webView2RuntimeVersion\": \"{0}\",\n", EscapeJson(_runtimeVersion));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewHostingMode\": \"{0}\",\n", _useCompositionControl ? "WebView2CompositionControl" : "WebView2HwndHost");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"currentMode\": \"{0}\",\n", OverlayModeExtensions.ToIdentifier(_currentMode));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"persistedMode\": \"{0}\",\n", OverlayModeExtensions.ToIdentifier(_persistedMode));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"captureProtectionEnabled\": {0},\n", _captureProtectionEnabled ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"captureProtectionPersisted\": {0},\n", _settingsService.LoadCaptureProtectionEnabled() ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"settingsFilePath\": \"{0}\",\n", EscapeJson(_settingsService.SettingsFilePath));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"modeChangeCount\": {0},\n", _modeChangeCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"activePanelView\": \"{0}\",\n", _activeView.ToString());
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lifecycleState\": \"{0}\",\n", EscapeJson(lifecycleState));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"windowState\": \"{0}\",\n", EscapeJson(windowStateName));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isMinimized\": {0},\n", isMinimized ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastHideReason\": \"{0}\",\n", EscapeJson(_lastHideReason));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastRestoreReason\": \"{0}\",\n", EscapeJson(_lastRestoreReason));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"closeInitiator\": \"{0}\",\n", EscapeJson(_closeInitiator));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isShuttingDown\": {0},\n", _isShuttingDown ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"shutdownCleanupCompleted\": {0},\n", _shutdownCleanupCompleted ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewDisposed\": {0},\n", _webViewDisposed ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"singleInstanceMutexReleased\": {0},\n", Program.SingleInstanceMutexReleased ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isVisible\": {0},\n", (!_shutdownCleanupCompleted && IsVisible) ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isOverlayActive\": {0},\n", (!_shutdownCleanupCompleted && _isOverlayActive) ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"topmost\": {0},\n", Topmost ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hwnd\": {0},\n", _hwnd.ToInt64());
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"foregroundHwnd\": {0},\n", fgHwnd.ToInt64());
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"foregroundPid\": {0},\n", fgPid);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hideCount\": {0},\n", _hideCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"restoreCount\": {0},\n", _restoreCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"minimizeCount\": {0},\n", _minimizeCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"ultraAutoHideCount\": {0},\n", _ultraAutoHideCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"externalFocusTransferCount\": {0},\n", _externalFocusTransferCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"internalControlClickCount\": {0},\n", _internalControlClickCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"headerControlsUnclipped\": {0},\n", headerControlsUnclipped ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"menuButtonScreenX\": {0},\n", menuBtnX);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"menuButtonScreenY\": {0},\n", menuBtnY);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"minimizeButtonScreenX\": {0},\n", minBtnX);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"minimizeButtonScreenY\": {0},\n", minBtnY);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"closeButtonScreenX\": {0},\n", closeBtnX);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"closeButtonScreenY\": {0},\n", closeBtnY);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"left\": {0:F1},\n", _savedLeft);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"top\": {0:F1},\n", _savedTop);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"width\": {0:F1},\n", _savedWidth);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"height\": {0:F1},\n", _savedHeight);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"minWidth\": {0:F1},\n", MinWidth);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"minHeight\": {0:F1},\n", MinHeight);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"dwmDarkHResult\": {0},\n", _dwmDarkHResult);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"dwmCornerHResult\": {0},\n", _dwmCornerHResult);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hotkeyRegistered\": {0},\n", _hotkeyRegistered ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hotkeyLastError\": {0},\n", _hotkeyLastError);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hotkeyUnregistered\": {0},\n", _hotkeyUnregistered ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hotkeyUnregisterCount\": {0},\n", _hotkeyUnregisterCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"toggleCount\": {0},\n", _toggleCount);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"setWindowDisplayAffinityReturn\": {0},\n", _setAffinityReturn ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"setWindowDisplayAffinityError\": {0},\n", _setAffinityLastError);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"getWindowDisplayAffinityReturn\": {0},\n", _getAffinityReturn ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"reportedDisplayAffinity\": {0},\n", _reportedAffinity);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"reportedDisplayAffinityHex\": \"0x{0:X8}\",\n", _reportedAffinity);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"visualTabCount\": {0},\n", _tabs.Count);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"activeVisualTabTitle\": \"{0}\",\n", EscapeJson(activeVisualTabTitle));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"shortcutsCount\": {0},\n", _shortcutsService.GetShortcuts().Count);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastShortcutValidationError\": \"{0}\",\n", EscapeJson(_lastShortcutValidationError));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"noteTitle\": \"{0}\",\n", EscapeJson(_notesService.CurrentNote.Title));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"notePlainText\": \"{0}\",\n", EscapeJson(_notesService.CurrentNote.ContentText));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"noteLastSavedStatus\": \"{0}\",\n", EscapeJson(_notesSaveStatusText != null ? _notesSaveStatusText.Text : string.Empty));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"noteSaveSucceeded\": {0},\n", _lastNoteSaveSucceeded ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewInitialized\": {0},\n", _webViewInitialized ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"currentSource\": \"{0}\",\n", EscapeJson(_currentSource));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"urlBoxText\": \"{0}\",\n", EscapeJson(_urlBox != null ? _urlBox.Text : string.Empty));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastAddressClassification\": \"{0}\",\n", EscapeJson(_lastAddressInputClassification));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastResolvedAddressTarget\": \"{0}\",\n", EscapeJson(_lastResolvedNavigationTarget));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastAddressResolutionStatus\": \"{0}\",\n", EscapeJson(_lastAddressResolutionStatus));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewDomCounter\": {0},\n", _webViewDomCounter);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewDomActiveTab\": \"{0}\",\n", EscapeJson(_webViewDomActiveTab));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"webViewDomNoteValue\": \"{0}\",\n", EscapeJson(_webViewDomNoteValue));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastNavigationStatus\": \"{0}\"\n", EscapeJson(_lastNavigationStatus));
                sb.AppendLine("}");

                string tmp = _statusFilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Copy(tmp, _statusFilePath, true);
            }
            catch
            {
            }
        }

        private void GetElementLogicalScreenCenter(FrameworkElement? element, out int screenX, out int screenY)
        {
            screenX = 0;
            screenY = 0;
            if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return;
            }

            GeneralTransform transform = element.TransformToAncestor(this);
            Point centerDip = transform.Transform(new Point(element.ActualWidth / 2.0, element.ActualHeight / 2.0));
            screenX = (int)Math.Round(_savedLeft + centerDip.X);
            screenY = (int)Math.Round(_savedTop + centerDip.Y);
        }

        private bool IsElementWithinWindowBounds(FrameworkElement? element)
        {
            if (element == null || element.Visibility != Visibility.Visible || element.ActualWidth < 18 || element.ActualHeight < 18)
            {
                return false;
            }

            GeneralTransform transform = element.TransformToAncestor(this);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= ActualWidth + 1.0 && bounds.Bottom <= ActualHeight + 1.0;
        }

        public async System.Threading.Tasks.Task SaveVisualSnapshotAsync(string? customFileName = null)
        {
            try
            {
                if (!IsVisible || ActualWidth < 100 || ActualHeight < 100)
                {
                    return;
                }

                string wv2PreviewPath = Path.Combine(_dataDir, "poc-webview2-preview.png");
                if (_coreWebView != null && _activeView == VaporPanelView.Browser)
                {
                    using (FileStream fs = new FileStream(wv2PreviewPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await _coreWebView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                    }
                }

                UpdateLayout();
                int width = (int)Math.Max(1, ActualWidth);
                int height = (int)Math.Max(1, ActualHeight);
                RenderTargetBitmap rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(this);

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                string uiSnapPath = Path.Combine(_dataDir, string.IsNullOrWhiteSpace(customFileName) ? "poc-ui-snapshot.png" : customFileName);
                using (FileStream fsUi = new FileStream(uiSnapPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    encoder.Save(fsUi);
                }
            }
            catch
            {
            }
        }

        // =========================================================================
        // CENTRALIZED DARK THEME RESOURCES & UI STYLING HELPERS
        // =========================================================================

        private static class DarkTheme
        {
            public static readonly SolidColorBrush WindowBackgroundBrush = CreateBrush(8, 10, 12);         // #080A0C
            public static readonly SolidColorBrush PrimarySurfaceBrush = CreateBrush(17, 20, 23);          // #111417
            public static readonly SolidColorBrush SecondarySurfaceBrush = CreateBrush(25, 29, 33);        // #191D21
            public static readonly SolidColorBrush ElevatedSurfaceBrush = CreateBrush(31, 36, 42);         // #1F242A
            public static readonly SolidColorBrush EditorSurfaceBrush = CreateBrush(13, 16, 19);           // #0D1013
            public static readonly SolidColorBrush AddressBarFillBrush = CreateBrush(21, 24, 28);          // #15181C
            public static readonly SolidColorBrush BorderSubtleBrush = CreateBrush(41, 46, 52);            // #292E34
            public static readonly SolidColorBrush BorderDividerBrush = CreateBrush(34, 38, 43);           // #22262B
            public static readonly SolidColorBrush TextPrimaryBrush = CreateBrush(244, 245, 247);          // #F4F5F7
            public static readonly SolidColorBrush TextSecondaryBrush = CreateBrush(165, 173, 183);        // #A5ADB7
            public static readonly SolidColorBrush TextMutedBrush = CreateBrush(142, 150, 160);            // #8E96A0
            public static readonly SolidColorBrush AccentBlueBrush = CreateBrush(22, 119, 255);            // #1677FF
            public static readonly SolidColorBrush AccentBlueCardFillBrush = CreateBrush(19, 28, 43);      // #131C2B
            public static readonly SolidColorBrush AccentBlueCardBorderBrush = CreateBrush(29, 99, 216);   // #1D63D8
            public static readonly SolidColorBrush ToggleTrackOffBrush = CreateBrush(43, 48, 54);          // #2B3036

            public static LinearGradientBrush CreateGlassSurfaceGradient()
            {
                LinearGradientBrush grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(22, 26, 31), 0.0)); // #161A1F
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(14, 17, 20), 1.0)); // #0E1114
                return grad;
            }
        }

        private void InitializeDarkThemeResources()
        {
            Resources["Vapor.WindowBackgroundBrush"] = DarkTheme.WindowBackgroundBrush;
            Resources["Vapor.PrimarySurfaceBrush"] = DarkTheme.PrimarySurfaceBrush;
            Resources["Vapor.SecondarySurfaceBrush"] = DarkTheme.SecondarySurfaceBrush;
            Resources["Vapor.ElevatedSurfaceBrush"] = DarkTheme.ElevatedSurfaceBrush;
            Resources["Vapor.EditorSurfaceBrush"] = DarkTheme.EditorSurfaceBrush;
            Resources["Vapor.AddressBarFillBrush"] = DarkTheme.AddressBarFillBrush;
            Resources["Vapor.BorderSubtleBrush"] = DarkTheme.BorderSubtleBrush;
            Resources["Vapor.BorderDividerBrush"] = DarkTheme.BorderDividerBrush;
            Resources["Vapor.TextPrimaryBrush"] = DarkTheme.TextPrimaryBrush;
            Resources["Vapor.TextSecondaryBrush"] = DarkTheme.TextSecondaryBrush;
            Resources["Vapor.TextMutedBrush"] = DarkTheme.TextMutedBrush;
            Resources["Vapor.AccentBlueBrush"] = DarkTheme.AccentBlueBrush;
            Resources["Vapor.AccentBlueCardFillBrush"] = DarkTheme.AccentBlueCardFillBrush;
            Resources["Vapor.AccentBlueCardBorderBrush"] = DarkTheme.AccentBlueCardBorderBrush;
            Resources["Vapor.GlassSurfaceGradientBrush"] = DarkTheme.CreateGlassSurfaceGradient();
        }

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush ParseHexBrush(string hex, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                string clean = hex.Trim().TrimStart('#');
                if (clean.Length == 6)
                {
                    byte r, g, b;
                    if (byte.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                        byte.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                        byte.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    {
                        return new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                }
            }
            return new SolidColorBrush(fallback);
        }

        private static TextBlock CreateVaporWordmark(double fontSize, Thickness margin)
        {
            return new TextBlock
            {
                Text = "V A P O R",
                Foreground = DarkTheme.TextPrimaryBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin
            };
        }

        private static ControlTemplate CreateRoundedButtonTemplate(CornerRadius cornerRadius, Brush hoverBackground, Brush pressedBackground)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border), "PART_Border");
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, cornerRadius);
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground, "PART_Border"));
            template.Triggers.Add(hoverTrigger);

            Trigger pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressedBackground, "PART_Border"));
            template.Triggers.Add(pressedTrigger);

            return template;
        }

        private static Button CreateBorderlessIconButton(object content, string tooltip, double width, double height)
        {
            return new Button
            {
                Content = content,
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                Foreground = DarkTheme.TextPrimaryBrush,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 2, 0),
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(5),
                    CreateBrush(30, 35, 42),
                    CreateBrush(38, 44, 52))
            };
        }

        private static Button CreateHeaderIconButton(string glyph, string tooltip, double width, double height)
        {
            return new Button
            {
                Content = glyph,
                Width = width,
                Height = height,
                Background = DarkTheme.SecondarySurfaceBrush,
                Foreground = DarkTheme.TextPrimaryBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(6),
                    DarkTheme.ElevatedSurfaceBrush,
                    CreateBrush(40, 46, 54))
            };
        }

        private static Button CreateCompactPillButton(string text, Brush bg, Brush fg, double cornerRadius = 11)
        {
            return new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(
                    new CornerRadius(cornerRadius),
                    CreateBrush(38, 132, 255),
                    CreateBrush(18, 102, 224))
            };
        }

        private static UIElement CreateVectorIcon(string pathData, double width, double height, Brush strokeBrush, double strokeThickness = 1.4)
        {
            return new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(pathData),
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = width,
                Height = height,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private Border CreateMenuNavRow(string icon, string label, Action onClick)
        {
            Border rowBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand
            };
            rowBorder.MouseEnter += (s, e) => rowBorder.Background = DarkTheme.SecondarySurfaceBrush;
            rowBorder.MouseLeave += (s, e) => rowBorder.Background = Brushes.Transparent;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = icon,
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 13,
                Width = 24,
                VerticalAlignment = VerticalAlignment.Center
            });

            TextBlock lbl = new TextBlock
            {
                Text = label,
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 1);
            grid.Children.Add(lbl);

            TextBlock chev = new TextBlock
            {
                Text = "›",
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chev, 2);
            grid.Children.Add(chev);

            rowBorder.Child = grid;
            rowBorder.MouseLeftButtonUp += (s, e) =>
            {
                RecordInternalInteraction();
                e.Handled = true;
                onClick();
            };
            return rowBorder;
        }

        private Border CreateModeSelectionCard(string title, string description, out TextBlock radioGlyph, Action onSelect)
        {
            Border card = new Border
            {
                Background = DarkTheme.PrimarySurfaceBrush,
                BorderBrush = DarkTheme.BorderSubtleBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            radioGlyph = new TextBlock
            {
                Text = "◉",
                Foreground = DarkTheme.AccentBlueBrush,
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(radioGlyph, 0);
            grid.Children.Add(radioGlyph);

            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            card.Child = grid;
            card.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                onSelect();
            };
            return card;
        }

        private static Border CreateToggleSwitch(out Border thumb, Action onToggle)
        {
            Border track = new Border
            {
                Width = 40,
                Height = 20,
                Background = DarkTheme.AccentBlueBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            thumb = new Border
            {
                Width = 16,
                Height = 16,
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            track.Child = thumb;
            track.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                onToggle();
            };
            return track;
        }

        private static void UpdateToggleSwitchVisual(Border? track, Border? thumb, bool isOn)
        {
            if (track == null || thumb == null)
            {
                return;
            }

            track.Background = isOn ? DarkTheme.AccentBlueBrush : DarkTheme.ToggleTrackOffBrush;
            thumb.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private static Border CreateDivider()
        {
            return new Border
            {
                Height = 1,
                Background = DarkTheme.BorderDividerBrush,
                Margin = new Thickness(0, 4, 0, 4)
            };
        }

        private static FrameworkElement CreateAboutFeatureRow(string icon, string title, string subtitle)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 3, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = icon,
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 13,
                Width = 24,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            });

            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = DarkTheme.TextPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = DarkTheme.TextSecondaryBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            return grid;
        }

        private static string EscapeJson(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
