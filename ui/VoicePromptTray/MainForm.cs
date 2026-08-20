using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace VoicePromptTray;

internal sealed class MainForm : Form
{
    private const string OverviewPage = "overview";
    private const string DictationPage = "dictation";
    private const string AudioPage = "audio";
    private const string IntelligencePage = "intelligence";
    private const string HistoryPage = "history";
    private const string AdvancedPage = "advanced";
    private const int PreferencesMaxBytes = 64 * 1024;

    private readonly DaemonManager _daemon;
    private readonly ConfigManager _config;
    private readonly AppPaths _paths;
    private readonly TranscriptHistoryStore _historyStore;
    private readonly PersonalDictionaryStore _dictionaryStore;
    private readonly TextSnippetStore _snippetStore;
    private readonly AppProfileStore _appProfileStore;
    private readonly UpdateChecker _updateChecker = new();
    private readonly UpdateInstaller _updateInstaller = new();
    private readonly Dictionary<string, Panel> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlowLayoutPanel> _pageBodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavigationButton> _navigation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Title, string Description)> _pageCopy = new(StringComparer.Ordinal)
    {
        [OverviewPage] = ("Overview", "Your local dictation workspace at a glance."),
        [DictationPage] = ("Dictation", "Control the shortcut, writing behavior, languages, and vocabulary."),
        [AudioPage] = ("Audio", "Choose your microphone and tune speech detection with live feedback."),
        [IntelligencePage] = ("Engine & AI", "Balance local recognition speed, accuracy, and optional text refinement."),
        [HistoryPage] = ("Recovery", "Review and recover recent transcripts stored only on this computer."),
        [AdvancedPage] = ("System", "Appearance, performance, updates, maintenance, and diagnostics."),
    };

    private Panel _pageHost = null!;
    private Label _pageTitle = null!;
    private Label _pageDescription = null!;
    private StatusPill _headerStatus = null!;
    private StatusPill _sidebarStatus = null!;
    private Label _sidebarStatusDetail = null!;
    private Label _footerMessage = null!;
    private ActionButton _saveButton = null!;
    private ActionButton _discardButton = null!;

    private ActionButton _daemonToggleButton = null!;
    private ActionButton _daemonRestartButton = null!;
    private StatusPill _overviewStatus = null!;
    private Label _overviewStatusText = null!;
    private Label _hotkeySummary = null!;
    private Label _languageSummary = null!;
    private Label _microphoneSummary = null!;
    private Label _engineSummary = null!;
    private Label _runtimeCheck = null!;
    private Label _hotkeyCheck = null!;
    private Label _audioCheck = null!;

    private HotkeyRecorder _hotkeyRecorder = null!;
    private ChoiceStrip _activationChoice = null!;
    private Label _activationHint = null!;
    private ChoiceStrip _outputChoice = null!;
    private Label _outputHint = null!;
    private ToggleSwitch _voiceCommandsToggle = null!;
    private ToggleSwitch _smartFormattingToggle = null!;
    private ToggleSwitch _contextAwarenessToggle = null!;
    private ToggleSwitch _autoStartToggle = null!;
    private ChoiceStrip _languageChoice = null!;
    private ComboBox _additionalLanguageCombo = null!;
    private Label _languageHint = null!;
    private TextBox _promptText = null!;
    private TextBox _correctionsText = null!;
    private TextBox _snippetsText = null!;

    private ComboBox _microphoneCombo = null!;
    private InputLevelMeter _inputLevelMeter = null!;
    private ChoiceStrip _sampleRateChoice = null!;
    private NumericUpDown _threshold = null!;
    private NumericUpDown _silenceMs = null!;
    private NumericUpDown _minimumSpeechMs = null!;
    private NumericUpDown _maximumSpeechSeconds = null!;

    private ChoiceStrip _aiModeChoice = null!;
    private TextBox _aiEndpointText = null!;
    private TextBox _aiModelText = null!;
    private NumericUpDown _aiTimeoutMs = null!;
    private TextBox _aiKeyText = null!;
    private TextBox _appProfilesText = null!;
    private ComboBox _runningAppCombo = null!;
    private ActionButton _aiClearKeyButton = null!;
    private ActionButton _aiTestButton = null!;
    private Label _aiResult = null!;
    private AiSettings _aiSettings = new();

    private ToggleSwitch _historyEnabled = null!;
    private NumericUpDown _historyLimit = null!;
    private ListBox _historyList = null!;
    private TextBox _historyResultPreview = null!;
    private TextBox _historyOriginalPreview = null!;
    private ActionButton _historyCopyOriginalButton = null!;
    private Label _historyComparisonStatus = null!;
    private Label _historyStatus = null!;

    private ComboBox _recognitionModelCombo = null!;
    private ChoiceStrip _recognitionEngineChoice = null!;
    private TextBox _recognitionServerUrl = null!;
    private NumericUpDown _recognitionServerTimeout = null!;
    private ActionButton _recognitionServerTestButton = null!;
    private Label _recognitionServerResult = null!;
    private ChoiceStrip _computeChoice = null!;
    private ChoiceStrip _processorChoice = null!;
    private NumericUpDown _temperature = null!;
    private TextBox _hotwordsText = null!;
    private ToggleSwitch _bufferedTranscriptionToggle = null!;

    private Label _diagnosticDaemon = null!;
    private Label _diagnosticRuntime = null!;
    private Label _diagnosticConfig = null!;
    private Label _diagnosticVersion = null!;
    private Label _performanceLatest = null!;
    private Label _performanceTypical = null!;
    private Label _performanceMicrophone = null!;
    private Label _performanceRecovery = null!;
    private Label _updateStatus = null!;
    private ChoiceStrip _updateChannelChoice = null!;
    private ActionButton _updateButton = null!;
    private ThemePicker _themePicker = null!;
    private OverlayStylePicker _overlayStylePicker = null!;
    private string _updateReleaseUrl = "";
    private UpdateResult? _availableUpdate;

    private bool _loading = true;
    private bool _dirty;
    private bool _busy;
    private bool _hotkeyWasMigrated;
    private string _selectedPage = OverviewPage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string SelectedPage => _selectedPage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool HasUnsavedChanges => _dirty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string OverlayStyle => _overlayStylePicker.SelectedValue;

    public event Action? DaemonRestarted;
    public event Action? UpdateInstallerLaunched;
    public event Action<string>? OverlayStyleChanged;

    public MainForm(DaemonManager daemon, AppPaths paths)
    {
        _daemon = daemon;
        _paths = paths;
        _config = new ConfigManager(paths.ConfigPath);
        _historyStore = new TranscriptHistoryStore(paths.HistoryPath, paths.HistorySettingsPath);
        _dictionaryStore = new PersonalDictionaryStore(paths.CorrectionsPath);
        _snippetStore = new TextSnippetStore(paths.SnippetsPath);
        _appProfileStore = new AppProfileStore(paths.AppProfilesPath);

        Theme.Use(ReadThemePreference(Path.Combine(paths.AppDataDir, "prefs.json")));

        Text = "VoicePrompt";
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.Font();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 700);
        Size = new Size(1180, 820);
        DoubleBuffered = true;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        BuildWindow();
        BuildAllPages();
        AttachChangeTracking();
        LoadPreferences();
        LoadConfiguration();
        LoadLocalTextSettings();
        LoadAiConfiguration();
        LoadAutoStartState();
        _loading = false;
        SetDirty(false);
        ShowPage(_selectedPage, persist: false);
        _ = RefreshMicrophonesAsync();
        UpdateStatus(_daemon.Refresh(true));
        UpdateOverview();
        if (_hotkeyWasMigrated)
            ShowFooter("A reserved or invalid shortcut was reset to F1. Review it in Dictation settings.", Theme.Warn);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeWindowStyle.Apply(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeWindowStyle.Apply(this);
    }

    private void BuildWindow()
    {
        var sidebar = BuildSidebar();
        var workspace = BuildWorkspace();
        Controls.Add(workspace);
        Controls.Add(sidebar);
    }

    private Panel BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 248,
            BackColor = Theme.Sidebar,
            Padding = new Padding(16, 18, 16, 18),
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, sidebar.ClientSize.Width - 1, 0, sidebar.ClientSize.Width - 1, sidebar.ClientSize.Height);
        };

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = Theme.Sidebar,
        };
        var logo = new BrandMark
        {
            Bounds = new Rectangle(4, 5, 46, 46),
            BackColor = Theme.Sidebar,
        };
        brand.Controls.Add(logo);

        var title = Theme.Label("VoicePrompt", Theme.Text, 13f, FontStyle.Bold, Theme.Sidebar);
        title.Location = new Point(62, 7);
        brand.Controls.Add(title);
        var caption = Theme.Label("Private voice to text", Theme.Muted, 8.5f, FontStyle.Regular, Theme.Sidebar);
        caption.Location = new Point(62, 35);
        brand.Controls.Add(caption);
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 336,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Sidebar,
            Padding = new Padding(0, 0, 0, 0),
        };
        var navLabel = Theme.Label("WORKSPACE", Theme.Muted, 7.6f, FontStyle.Bold, Theme.Sidebar);
        navLabel.AutoSize = false;
        navLabel.Size = new Size(214, 24);
        navLabel.Padding = new Padding(8, 2, 0, 0);
        navLabel.Margin = new Padding(0, 0, 0, 4);
        nav.Controls.Add(navLabel);
        AddNavigation(nav, OverviewPage, "Overview", NavigationGlyph.Overview);
        AddNavigation(nav, DictationPage, "Dictation", NavigationGlyph.Dictation);
        AddNavigation(nav, AudioPage, "Audio", NavigationGlyph.Audio);
        AddNavigation(nav, IntelligencePage, "Engine & AI", NavigationGlyph.Intelligence);
        AddNavigation(nav, HistoryPage, "Recovery", NavigationGlyph.History);
        AddNavigation(nav, AdvancedPage, "System", NavigationGlyph.Advanced);
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(brand);

        var sidebarBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 168,
            BackColor = Theme.Sidebar,
        };

        _sidebarStatus = new StatusPill
        {
            Location = new Point(6, 8),
            BackColor = Theme.Sidebar,
        };
        sidebarBottom.Controls.Add(_sidebarStatus);
        _sidebarStatusDetail = Theme.Label("Checking runtime…", Theme.Muted, 8.4f, FontStyle.Regular, Theme.Sidebar);
        _sidebarStatusDetail.AutoSize = false;
        _sidebarStatusDetail.Bounds = new Rectangle(6, 43, 198, 38);
        sidebarBottom.Controls.Add(_sidebarStatusDetail);

        var hideButton = new ActionButton("Hide to tray", ActionButtonStyle.Secondary)
        {
            Bounds = new Rectangle(4, 92, 212, 40),
            BackColor = Theme.Sidebar,
        };
        hideButton.Click += (_, _) => Hide();
        sidebarBottom.Controls.Add(hideButton);

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion;
        var versionLabel = Theme.Label($"Version {version}", Theme.Muted, 8f, FontStyle.Regular, Theme.Sidebar);
        versionLabel.Location = new Point(6, 146);
        sidebarBottom.Controls.Add(versionLabel);
        sidebar.Controls.Add(sidebarBottom);
        return sidebar;
    }

    private void AddNavigation(FlowLayoutPanel parent, string key, string text, NavigationGlyph glyph)
    {
        var button = new NavigationButton(key, text, glyph)
        {
            Width = 216,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Theme.Sidebar,
        };
        button.Click += (_, _) => ShowPage(key);
        parent.Controls.Add(button);
        _navigation[key] = button;
    }

    private Panel BuildWorkspace()
    {
        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Theme.Canvas,
            Padding = new Padding(38, 24, 38, 12),
        };
        _pageTitle = Theme.Label("Overview", Theme.Text, 20.5f, FontStyle.Bold, Theme.Canvas);
        _pageTitle.Location = new Point(38, 20);
        header.Controls.Add(_pageTitle);
        _pageDescription = Theme.Label("", Theme.TextSecondary, 9.5f, FontStyle.Regular, Theme.Canvas);
        _pageDescription.Location = new Point(39, 61);
        header.Controls.Add(_pageDescription);
        _headerStatus = new StatusPill { BackColor = Theme.Canvas, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Controls.Add(_headerStatus);
        header.SizeChanged += (_, _) => _headerStatus.Location = new Point(header.ClientSize.Width - _headerStatus.Width - 38, 31);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = Theme.Sidebar,
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _footerMessage = Theme.Label("All changes saved", Theme.Muted, 8.8f, FontStyle.Regular, Theme.Sidebar);
        _footerMessage.AutoSize = false;
        _footerMessage.Bounds = new Rectangle(38, 24, 470, 24);
        footer.Controls.Add(_footerMessage);

        _discardButton = new ActionButton("Discard", ActionButtonStyle.Secondary)
        {
            BackColor = Theme.Sidebar,
        };
        _discardButton.Click += (_, _) => DiscardChanges();
        footer.Controls.Add(_discardButton);
        _saveButton = new ActionButton("Save & restart", ActionButtonStyle.Primary)
        {
            Width = 142,
            BackColor = Theme.Sidebar,
        };
        _saveButton.Click += async (_, _) => await SaveAsync();
        footer.Controls.Add(_saveButton);
        footer.SizeChanged += (_, _) =>
        {
            _saveButton.Location = new Point(footer.ClientSize.Width - _saveButton.Width - 38, 15);
            _discardButton.Location = new Point(_saveButton.Left - _discardButton.Width - 10, 15);
        };

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
        };

        workspace.Controls.Add(_pageHost);
        workspace.Controls.Add(footer);
        workspace.Controls.Add(header);
        return workspace;
    }

    private void BuildAllPages()
    {
        BuildOverviewPage();
        BuildDictationPage();
        BuildAudioPage();
        BuildIntelligencePage();
        BuildHistoryPage();
        BuildAdvancedPage();
    }

    private FlowLayoutPanel CreatePage(string key)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            AutoScroll = true,
            Visible = false,
            AccessibleName = _pageCopy[key].Title + " settings page",
        };
        page.HandleCreated += (_, _) => NativeWindowStyle.ApplyToTree(page);
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(38, 8, 38, 48),
            Margin = Padding.Empty,
        };
        page.Controls.Add(body);
        page.SizeChanged += (_, _) => ResizePageBody(page, body);
        _pageHost.Controls.Add(page);
        _pages[key] = page;
        _pageBodies[key] = body;
        return body;
    }

    private static void ResizePageBody(Panel page, FlowLayoutPanel body)
    {
        int availableWidth = Math.Max(650, page.ClientSize.Width - (page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        body.Width = availableWidth;
        int cardWidth = Math.Max(580, availableWidth - body.Padding.Horizontal);
        foreach (Control child in body.Controls)
            child.Width = cardWidth;
    }

    private static void AddPageItem(FlowLayoutPanel body, Control control)
    {
        control.Margin = new Padding(0, 0, 0, 16);
        body.Controls.Add(control);
    }

    private void BuildOverviewPage()
    {
        var body = CreatePage(OverviewPage);
        AddPageItem(body, BuildReadinessHero());
        AddPageItem(body, BuildSetupSummary());
        AddPageItem(body, BuildReadinessChecklist());
        AddPageItem(body, BuildQuickActions());
    }

    private SurfacePanel BuildReadinessHero()
    {
        var card = new SurfacePanel
        {
            Height = 170,
            SurfaceColor = Theme.SurfaceRaised,
            BorderColor = Theme.BorderStrong,
        };
        var eyebrow = Theme.Label("VOICEPROMPT STATUS", Theme.Accent, 8.2f, FontStyle.Bold, Theme.SurfaceRaised);
        eyebrow.Location = new Point(26, 24);
        card.Controls.Add(eyebrow);
        _overviewStatusText = Theme.Label("Getting things ready", Theme.Text, 17f, FontStyle.Bold, Theme.SurfaceRaised);
        _overviewStatusText.Location = new Point(26, 50);
        card.Controls.Add(_overviewStatusText);
        var copy = Theme.Label(
            "Your microphone stays local. Hold the configured shortcut, speak, and release to type into the focused app.",
            Theme.TextSecondary,
            9.25f,
            FontStyle.Regular,
            Theme.SurfaceRaised);
        copy.AutoSize = false;
        copy.Bounds = new Rectangle(27, 83, 480, 44);
        card.Controls.Add(copy);
        _overviewStatus = new StatusPill { BackColor = Theme.SurfaceRaised, Location = new Point(27, 128) };
        card.Controls.Add(_overviewStatus);

        _daemonToggleButton = new ActionButton("Start runtime", ActionButtonStyle.Primary)
        {
            Width = 124,
            BackColor = Theme.SurfaceRaised,
        };
        _daemonToggleButton.Click += async (_, _) => await ToggleDaemonAsync();
        card.Controls.Add(_daemonToggleButton);
        _daemonRestartButton = new ActionButton("Restart", ActionButtonStyle.Secondary)
        {
            Width = 92,
            BackColor = Theme.SurfaceRaised,
        };
        _daemonRestartButton.Click += async (_, _) => await RestartDaemonAsync();
        card.Controls.Add(_daemonRestartButton);
        card.SizeChanged += (_, _) =>
        {
            _daemonRestartButton.Location = new Point(card.ClientSize.Width - _daemonRestartButton.Width - 26, 109);
            _daemonToggleButton.Location = new Point(_daemonRestartButton.Left - _daemonToggleButton.Width - 10, 109);
            copy.Width = Math.Max(320, card.ClientSize.Width - 310);
        };
        return card;
    }

    private SurfacePanel BuildSetupSummary()
    {
        var card = new SurfacePanel { Height = 278 };
        AddCardHeading(card, "Your setup", "The essentials at a glance. Changes update here before you save.");
        var grid = new TableLayoutPanel
        {
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Location = new Point(24, 76),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Size = new Size(card.Width - 48, 178),
            Margin = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(BuildSummaryTile("Hotkey", out _hotkeySummary), 0, 0);
        grid.Controls.Add(BuildSummaryTile("Language", out _languageSummary), 1, 0);
        grid.Controls.Add(BuildSummaryTile("Microphone", out _microphoneSummary), 0, 1);
        grid.Controls.Add(BuildSummaryTile("Recognition", out _engineSummary), 1, 1);
        card.Controls.Add(grid);
        card.SizeChanged += (_, _) => grid.Size = new Size(card.ClientSize.Width - 48, 178);
        return card;
    }

    private static SurfacePanel BuildSummaryTile(string title, out Label value)
    {
        var tile = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(16),
            Radius = 9,
            SurfaceColor = Theme.SurfaceRaised,
        };
        var heading = Theme.Label(title.ToUpperInvariant(), Theme.Muted, 7.8f, FontStyle.Bold, Theme.SurfaceRaised);
        heading.Location = new Point(16, 13);
        tile.Controls.Add(heading);
        var valueLabel = Theme.Label("—", Theme.Text, 10.25f, FontStyle.Bold, Theme.SurfaceRaised);
        valueLabel.AutoSize = false;
        valueLabel.Bounds = new Rectangle(16, 38, 160, 26);
        tile.SizeChanged += (_, _) => valueLabel.Width = Math.Max(40, tile.ClientSize.Width - 32);
        tile.Controls.Add(valueLabel);
        value = valueLabel;
        return tile;
    }

    private SurfacePanel BuildReadinessChecklist()
    {
        var card = new SurfacePanel { Height = 238 };
        AddCardHeading(card, "Setup checklist", "Four clear signals tell you whether dictation is ready.");
        _runtimeCheck = AddChecklistRow(card, 82, "Runtime", "Local speech engine installed and reachable");
        _hotkeyCheck = AddChecklistRow(card, 128, "Shortcut", "A global shortcut is configured");
        _audioCheck = AddChecklistRow(card, 174, "Audio", "A Windows microphone route is selected");
        return card;
    }

    private static Label AddChecklistRow(SurfacePanel card, int top, string title, string description)
    {
        var dot = new Panel
        {
            Bounds = new Rectangle(26, top + 7, 10, 10),
            BackColor = Theme.Muted,
            Tag = "check-dot",
        };
        card.Controls.Add(dot);
        var titleLabel = Theme.Label(title, Theme.Text, 9.5f, FontStyle.Bold, Theme.Surface);
        titleLabel.Location = new Point(50, top);
        card.Controls.Add(titleLabel);
        var descriptionLabel = Theme.Label(description, Theme.Muted, 8.5f, FontStyle.Regular, Theme.Surface);
        descriptionLabel.Location = new Point(156, top + 1);
        card.Controls.Add(descriptionLabel);
        return descriptionLabel;
    }

    private SurfacePanel BuildQuickActions()
    {
        var card = new SurfacePanel { Height = 126 };
        AddCardHeading(card, "Quick actions", "Useful tools without digging through folders.");
        var openLog = new ActionButton("Open log", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        openLog.Click += (_, _) => OpenLog();
        var copy = new ActionButton("Copy diagnostics", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        copy.Click += (_, _) => CopyDiagnostics();
        var sound = new ActionButton("Windows sound", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        sound.Click += (_, _) => OpenExternal("ms-settings:sound");
        var actions = new FlowLayoutPanel
        {
            BackColor = Theme.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Location = new Point(24, 74),
            Size = new Size(520, 40),
        };
        actions.Controls.Add(openLog);
        actions.Controls.Add(copy);
        actions.Controls.Add(sound);
        card.Controls.Add(actions);
        return card;
    }

    private void BuildDictationPage()
    {
        var body = CreatePage(DictationPage);
        _hotkeyRecorder = new HotkeyRecorder { Dock = DockStyle.Fill };
        _activationChoice = new ChoiceStrip(new[] { "Hold to talk", "Toggle" }, new[] { "hold", "toggle" }) { Dock = DockStyle.Fill };
        _activationChoice.AccessibleName = "Recording activation mode";
        _activationChoice.SelectedChanged += (_, _) => UpdateActivationHint();
        _activationHint = BuildInlineHint(Theme.Surface);
        _outputChoice = new ChoiceStrip(new[] { "Paste into app", "Copy only" }, new[] { "paste", "clipboard" }) { Dock = DockStyle.Fill };
        _outputChoice.AccessibleName = "Transcript output mode";
        _outputChoice.SelectedChanged += (_, _) => UpdateOutputHint();
        _outputHint = BuildInlineHint(Theme.Surface);
        _voiceCommandsToggle = new ToggleSwitch("Enable spoken commands") { Dock = DockStyle.Left };
        _smartFormattingToggle = new ToggleSwitch("Format dictation automatically") { Dock = DockStyle.Left };
        _contextAwarenessToggle = new ToggleSwitch("Match the active app and nearby text") { Dock = DockStyle.Left };
        _autoStartToggle = new ToggleSwitch("Launch VoicePrompt when I sign in") { Dock = DockStyle.Left };

        var shortcut = new SectionBuilder("Shortcut & behavior", "A global key works from browsers, editors, chat apps, and games.");
        shortcut.Add("Global hotkey", "Click the field, press a key or combination, then press Enter.", _hotkeyRecorder, 76);
        shortcut.Add("Activation", "Hold mode is fastest and avoids accidental long recordings.", StackControl(_activationChoice, _activationHint, 64), 82);
        shortcut.Add("Output", "Copy only is useful when an app blocks synthetic paste or you want manual placement.", StackControl(_outputChoice, _outputHint, 64), 82);
        shortcut.Add("Voice commands", "Exact commands and snippets, plus “Command …” / “Ukaz …” for selected text when AI is enabled.", _voiceCommandsToggle, 62);
        shortcut.Add("Smart formatting", "Local punctuation, casing, paragraph commands, and safe filler cleanup; no AI required.", _smartFormattingToggle, 62);
        shortcut.Add("App context", "Uses the active app type and bounded nearby text when Windows exposes it; password fields are ignored.", _contextAwarenessToggle, 62);
        shortcut.Add("Windows startup", "Start silently in the tray so dictation is always available.", _autoStartToggle, 62);
        AddPageItem(body, shortcut.Build());

        _languageChoice = new ChoiceStrip(
            new[] { "Auto", "Slovenian", "Slang", "English", "More" },
            new[] { "", "sl", "sl-slang", "en", "other" })
        { Dock = DockStyle.Fill };
        _languageChoice.AccessibleName = "Spoken language";
        _languageChoice.SelectedChanged += (_, _) =>
        {
            UpdateLanguageControls();
            UpdateOverview();
        };
        _languageHint = BuildInlineHint(Theme.Surface);
        _additionalLanguageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Dock = DockStyle.Fill,
            MaxDropDownItems = 14,
        };
        _additionalLanguageCombo.AccessibleName = "Additional spoken language";
        Theme.StyleCombo(_additionalLanguageCombo);
        foreach (LanguageOption option in LanguageCatalog.All.Where(option => option.Code is not "en" and not "sl"))
            _additionalLanguageCombo.Items.Add(option);
        _additionalLanguageCombo.SelectedIndex = -1;
        _promptText = new TextBox
        {
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Optional context, product names, code terms, or vocabulary…",
        };
        _promptText.AccessibleName = "Recognition context";
        var promptFrame = new TextFieldFrame(_promptText, 116, multiline: true) { Dock = DockStyle.Fill };
        _correctionsText = new TextBox
        {
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "polly market => Polymarket\ncodecs => Codex",
        };
        _correctionsText.AccessibleName = "Personal corrections";
        var correctionsFrame = new TextFieldFrame(_correctionsText, 116, multiline: true) { Dock = DockStyle.Fill };

        var language = new SectionBuilder("Language & vocabulary", "English + Slovenian Auto stays the fast default; other languages are optional.");
        language.Add("Spoken language", "Choose Auto for mixed English and Slovenian, or pin one language.", StackControl(_languageChoice, _languageHint, 64), 84);
        language.Add("Additional language", "Search the 98 other languages already built into the installed model; no model download needed.", _additionalLanguageCombo, 64);
        language.Add("Recognition context", "Give Whisper examples of names and technical terms; this is not sent to an AI service.", promptFrame, 136);
        language.Add("Personal corrections", "One local replacement per line: misheard => intended. Applied before optional AI cleanup.", correctionsFrame, 136);
        var importProfile = new ActionButton("Import profile", ActionButtonStyle.Secondary) { Width = 112, BackColor = Theme.Surface };
        importProfile.Click += (_, _) => ImportLanguageProfile();
        var exportProfile = new ActionButton("Export profile", ActionButtonStyle.Secondary) { Width = 112, BackColor = Theme.Surface };
        exportProfile.Click += (_, _) => ExportLanguageProfile();
        var profileActions = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = Theme.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            Size = new Size(240, 40),
            WrapContents = false,
        };
        importProfile.Margin = new Padding(0, 0, 8, 0);
        exportProfile.Margin = Padding.Empty;
        profileActions.Controls.Add(importProfile);
        profileActions.Controls.Add(exportProfile);
        language.Add("Language profile", "Share language, context, hotwords, and corrections without sharing secrets or device settings.", LeftControl(profileActions), 64);
        AddPageItem(body, language.Build());

        _snippetsText = new TextBox
        {
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "signature => Kind regards,\\nYour name",
        };
        _snippetsText.AccessibleName = "Reusable text snippets";
        var snippetsFrame = new TextFieldFrame(_snippetsText, 116, multiline: true) { Dock = DockStyle.Fill };
        var reusable = new SectionBuilder(
            "Reusable text",
            "With Voice commands enabled, say “Insert snippet name” or “Vstavi predlogo name.”");
        reusable.Add("Saved snippets", "One per line: name => content. Write \\n for a line break.", snippetsFrame, 136);
        AddPageItem(body, reusable.Build());
    }

    private void BuildAudioPage()
    {
        var body = CreatePage(AudioPage);
        _microphoneCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _microphoneCombo.AccessibleName = "Microphone";
        Theme.StyleCombo(_microphoneCombo);
        var refresh = new ActionButton("Refresh", ActionButtonStyle.Secondary) { Width = 88, BackColor = Theme.Surface };
        refresh.Click += async (_, _) => await RefreshMicrophonesAsync();
        var sound = new ActionButton("Sound settings", ActionButtonStyle.Quiet) { Width = 112, BackColor = Theme.Surface };
        sound.Click += (_, _) => OpenExternal("ms-settings:sound");
        var microphoneRow = HorizontalControl(_microphoneCombo, refresh, sound);

        _sampleRateChoice = new ChoiceStrip(
            new[] { "8 kHz", "16 kHz", "22 kHz", "44.1 kHz", "48 kHz" },
            new[] { "8000", "16000", "22050", "44100", "48000" })
        { Dock = DockStyle.Fill };
        _sampleRateChoice.AccessibleName = "Audio sample rate";

        var input = new SectionBuilder("Input device", "VoicePrompt records mono audio and keeps it on this computer.");
        input.Add("Microphone", "System default follows the current Windows input device.", microphoneRow, 68);
        input.Add("Sample rate", "16 kHz is Whisper's native rate and the recommended setting.", _sampleRateChoice, 64);
        _inputLevelMeter = new InputLevelMeter { Dock = DockStyle.Fill };
        input.Add("Input test", "Hold your configured hotkey and speak. This reuses the live recording stream and never opens a second microphone capture.", _inputLevelMeter, 66);
        AddPageItem(body, input.Build());

        _threshold = MakeNumber(0m, 1m, 0.05m, 2, 148);
        _silenceMs = MakeNumber(50, 5000, 50, 0, 148);
        _minimumSpeechMs = MakeNumber(50, 5000, 50, 0, 148);
        _maximumSpeechSeconds = MakeNumber(1, 600, 5, 1, 148);
        _threshold.AccessibleName = "Speech sensitivity";
        _silenceMs.AccessibleName = "End-of-speech pause in milliseconds";
        _minimumSpeechMs.AccessibleName = "Minimum speech duration in milliseconds";
        _maximumSpeechSeconds.AccessibleName = "Recognition segment duration in seconds";
        var resetVad = new ActionButton("Use recommended values", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        resetVad.Click += (_, _) =>
        {
            _threshold.Value = 0.60m;
            _silenceMs.Value = 250;
            _minimumSpeechMs.Value = 250;
            _maximumSpeechSeconds.Value = 180;
            ShowFooter("Recommended voice detection values applied. Save to activate them.", Theme.Accent);
        };

        var detection = new SectionBuilder("Voice detection", "These controls decide what counts as speech and when an utterance ends.");
        detection.Add("Sensitivity", "Higher rejects more background noise; 0.60 is a balanced starting point.", LeftControl(_threshold), 62);
        detection.Add("End-of-speech pause", "Milliseconds of silence before transcription begins.", LeftControl(_silenceMs), 62);
        detection.Add("Minimum speech", "Very short sounds below this duration are ignored.", LeftControl(_minimumSpeechMs), 62);
        detection.Add("Recognition segment", "Internal VAD segment size. Held recordings stay complete even when they run longer.", LeftControl(_maximumSpeechSeconds), 62);
        detection.Add("Recommended preset", "Restore the tested values without changing your microphone.", LeftControl(resetVad), 62);
        AddPageItem(body, detection.Build());
    }

    private void BuildIntelligencePage()
    {
        var body = CreatePage(IntelligencePage);
        _recognitionEngineChoice = new ChoiceStrip(
            new[] { "Local on this PC", "Compatible server" },
            new[] { "local", "server" }) { Dock = DockStyle.Fill };
        _recognitionEngineChoice.AccessibleName = "Recognition engine location";
        _recognitionEngineChoice.SelectedChanged += (_, _) => UpdateRecognitionEngineAvailability();
        _recognitionServerUrl = new TextBox { PlaceholderText = "http://localhost:8000" };
        _recognitionServerUrl.AccessibleName = "Recognition server base URL";
        _recognitionServerUrl.TextChanged += (_, _) => UpdateRecognitionEngineAvailability();
        _recognitionServerTimeout = MakeNumber(
            RecognitionServer.MinimumTimeoutSeconds,
            RecognitionServer.MaximumTimeoutSeconds,
            5,
            0,
            148);
        _recognitionServerTimeout.AccessibleName = "Recognition server maximum wait in seconds";
        _recognitionServerTestButton = new ActionButton("Test server", ActionButtonStyle.Secondary)
        {
            Width = 104,
            BackColor = Theme.Surface,
        };
        _recognitionServerTestButton.Click += async (_, _) => await TestRecognitionServerAsync();
        _recognitionServerResult = BuildInlineHint(Theme.Surface);
        var recognitionServerRow = HorizontalControl(
            new TextFieldFrame(_recognitionServerUrl) { Dock = DockStyle.Fill },
            _recognitionServerTestButton);
        _recognitionModelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        _recognitionModelCombo.AccessibleName = "Recognition model";
        Theme.StyleCombo(_recognitionModelCombo);
        _recognitionModelCombo.Items.AddRange(new object[]
        {
            "Systran/faster-whisper-large-v3",
            "Systran/faster-whisper-large-v3-turbo",
        });
        _processorChoice = new ChoiceStrip(new[] { "Automatic", "NVIDIA GPU", "CPU" }, new[] { "auto", "cuda", "cpu" }) { Dock = DockStyle.Fill };
        _computeChoice = new ChoiceStrip(new[] { "Automatic", "FP16", "INT8" }, new[] { "auto", "float16", "int8" }) { Dock = DockStyle.Fill };
        _processorChoice.AccessibleName = "Recognition processor";
        _computeChoice.AccessibleName = "Recognition precision";
        _temperature = MakeNumber(0m, 1m, 0.1m, 1, 148);
        _hotwordsText = new TextBox { PlaceholderText = "Comma-separated terms: OpenAI, Codex, Ljubljana…" };
        _temperature.AccessibleName = "Recognition temperature";
        _hotwordsText.AccessibleName = "Recognition hotwords";
        _bufferedTranscriptionToggle = new ToggleSwitch("Pre-transcribe long recordings while I speak") { Dock = DockStyle.Left };
        _bufferedTranscriptionToggle.AccessibleName = "Fast long recordings";
        var hotwordsFrame = new TextFieldFrame(_hotwordsText) { Dock = DockStyle.Fill };

        var engine = new SectionBuilder(
            "Recognition engine",
            "Local remains private and recommended. A compatible server is an explicit opt-in for shared or separate hardware.");
        engine.Add("Location", "Local runs on this PC. Server uses the upstream OpenAI-compatible transcription API.", _recognitionEngineChoice, 64);
        engine.Add("Server address", "Used only in Server mode. Test checks /health and never sends microphone audio.", StackControl(recognitionServerRow, _recognitionServerResult, 64), 84);
        engine.Add("Server wait", "Maximum time after release for a server transcription; longer recordings may need more time.", LeftControl(_recognitionServerTimeout), 62);
        engine.Add("Model", "large-v3 gives the best Slovenian accuracy; Turbo trades some accuracy for speed.", _recognitionModelCombo, 64);
        engine.Add("Processor", "Use NVIDIA GPU for the fastest local transcription.", _processorChoice, 64);
        engine.Add("Precision", "FP16 is recommended on modern NVIDIA GPUs; INT8 is useful on CPU.", _computeChoice, 64);
        engine.Add("Temperature", "0 uses deterministic decoding with automatic fallback only when quality checks fail.", LeftControl(_temperature), 62);
        engine.Add("Hotwords", "Extra words to boost. Built-in English/Slovenian vocabulary is added automatically in Auto.", hotwordsFrame, 64);
        engine.Add("Fast long recordings", "Decodes complete speech blocks in the background, pastes once on release, and keeps full audio for automatic fallback.", _bufferedTranscriptionToggle, 64);
        AddPageItem(body, engine.Build());
        UpdateRecognitionEngineAvailability();

        _aiModeChoice = new ChoiceStrip(
            new[] { "Verbatim", "Clean", "Polish", "Prompt" },
            new[] { "off", "clean", "grammar", "prompt" }) { Dock = DockStyle.Fill };
        _aiModeChoice.AccessibleName = "AI cleanup mode";
        _aiModeChoice.SelectedChanged += (_, _) => UpdateAiAvailability();
        _aiEndpointText = new TextBox { PlaceholderText = "http://127.0.0.1:11434/v1/chat/completions" };
        _aiEndpointText.TextChanged += (_, _) => UpdateAiAvailability();
        _aiModelText = new TextBox { PlaceholderText = "qwen2.5:3b" };
        _aiTimeoutMs = MakeNumber(400, 3000, 100, 0, 148);
        _aiKeyText = new TextBox { UseSystemPasswordChar = true, PlaceholderText = "Optional for local providers" };
        _aiEndpointText.AccessibleName = "AI provider endpoint";
        _aiModelText.AccessibleName = "AI provider model";
        _aiTimeoutMs.AccessibleName = "AI maximum wait in milliseconds";
        _aiKeyText.AccessibleName = "AI provider API key";
        _aiClearKeyButton = new ActionButton("Clear key", ActionButtonStyle.Quiet) { Width = 88, BackColor = Theme.Surface };
        _aiClearKeyButton.Click += (_, _) =>
        {
            _aiSettings.ApiKeyProtected = "";
            _aiKeyText.Clear();
            _aiKeyText.Modified = true;
            _aiResult.Text = "Saved key removed from this form. Save changes to apply.";
            _aiResult.ForeColor = Theme.Warn;
            MarkDirty();
            UpdateAiAvailability();
        };
        _aiTestButton = new ActionButton("Test provider", ActionButtonStyle.Secondary) { Width = 112, BackColor = Theme.Surface };
        _aiTestButton.Click += async (_, _) => await TestAiAsync();
        var keyRow = HorizontalControl(new TextFieldFrame(_aiKeyText) { Dock = DockStyle.Fill }, _aiClearKeyButton, _aiTestButton);
        _aiResult = BuildInlineHint(Theme.Surface);
        _aiResult.Text = "Verbatim adds zero delay and sends no transcript anywhere.";

        var ai = new SectionBuilder("Optional AI cleanup", "Disabled by default. When enabled, only completed text is sent to the configured provider.");
        ai.Add("Writing mode", "Clean removes speech clutter; Polish repairs broken sentences; Prompt restructures without translating.", StackControl(_aiModeChoice, _aiResult, 64), 84);
        ai.Add("Endpoint", "Any OpenAI-compatible chat completions endpoint, local or cloud.", new TextFieldFrame(_aiEndpointText) { Dock = DockStyle.Fill }, 64);
        ai.Add("Model", "The model name expected by your provider.", new TextFieldFrame(_aiModelText) { Dock = DockStyle.Fill }, 64);
        ai.Add("Maximum wait", "Strict live deadline in milliseconds; raw local text is pasted after a timeout.", LeftControl(_aiTimeoutMs), 62);
        ai.Add("API key", "Encrypted for your Windows account. Leave blank to keep an existing saved key.", keyRow, 68);
        AddPageItem(body, ai.Build());

        _runningAppCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _runningAppCombo.AccessibleName = "Running application for new profile";
        Theme.StyleCombo(_runningAppCombo);
        _runningAppCombo.DropDown += (_, _) => RefreshRunningApplications();
        var addAppProfile = new ActionButton("Add rule", ActionButtonStyle.Secondary) { Width = 92, BackColor = Theme.Surface };
        addAppProfile.Click += (_, _) => AddSelectedAppProfile();
        _appProfilesText = new TextBox
        {
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Code.exe => prompt, paste\nDiscord.exe => clean, inherit",
        };
        _appProfilesText.AccessibleName = "Application-aware writing and output profiles";
        var appProfilesFrame = new TextFieldFrame(_appProfilesText, 136, multiline: true) { Dock = DockStyle.Fill };
        var appProfiles = new SectionBuilder(
            "Application profiles",
            "Optional exact executable matches. Unmatched applications always inherit your global settings.");
        appProfiles.Add(
            "Running application",
            "Choose an open app to insert its exact executable name; no full path is saved.",
            HorizontalControl(_runningAppCombo, addAppProfile),
            64);
        appProfiles.Add(
            "Per-app behavior",
            "One per line: app.exe => writing, output. Writing: inherit/verbatim/clean/grammar/prompt. Output: inherit/paste/clipboard.",
            appProfilesFrame,
            156);
        AddPageItem(body, appProfiles.Build());
        RefreshRunningApplications();
    }

    private void RefreshRunningApplications()
    {
        if (_runningAppCombo is null)
            return;
        string selected = _runningAppCombo.SelectedItem?.ToString() ?? "";
        string[] executables = Process.GetProcesses()
            .Select(process =>
            {
                try
                {
                    return process.Id == Environment.ProcessId ? "" : process.ProcessName + ".exe";
                }
                catch
                {
                    return "";
                }
                finally
                {
                    process.Dispose();
                }
            })
            .Where(name => name.Length > 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _runningAppCombo.BeginUpdate();
        _runningAppCombo.Items.Clear();
        _runningAppCombo.Items.AddRange(executables);
        _runningAppCombo.EndUpdate();
        int index = Array.FindIndex(executables, name => name.Equals(selected, StringComparison.OrdinalIgnoreCase));
        _runningAppCombo.SelectedIndex = index >= 0 ? index : executables.Length > 0 ? 0 : -1;
    }

    private void AddSelectedAppProfile()
    {
        string executable = _runningAppCombo.SelectedItem?.ToString() ?? "";
        if (executable.Length == 0)
        {
            ShowFooter("No running application is available to add.", Theme.Warn);
            return;
        }
        try
        {
            IReadOnlyList<AppProfileEntry> current = AppProfileStore.Parse(_appProfilesText.Text);
            if (current.Any(profile => profile.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase)))
            {
                ShowFooter($"{executable} already has an application profile.", Theme.Warn);
                return;
            }
            string separator = _appProfilesText.TextLength == 0 || _appProfilesText.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                ? ""
                : Environment.NewLine;
            _appProfilesText.AppendText($"{separator}{executable} => inherit, inherit");
            _appProfilesText.Focus();
            _appProfilesText.SelectionStart = _appProfilesText.TextLength;
            ShowFooter($"Added {executable} · choose its writing and output modes", Theme.Accent);
        }
        catch (InvalidDataException ex)
        {
            _appProfilesText.Focus();
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void BuildHistoryPage()
    {
        var body = CreatePage(HistoryPage);
        _historyEnabled = new ToggleSwitch("Keep recent transcripts for recovery") { Dock = DockStyle.Left };
        _historyLimit = MakeNumber(5, 100, 5, 0, 148);
        _historyEnabled.AccessibleName = "Local transcript recovery";
        _historyLimit.AccessibleName = "Maximum retained transcripts";

        var privacy = new SectionBuilder("Local recovery", "Completed text is stored only in your Windows profile. Audio is never stored.");
        privacy.Add("Recovery history", "Disable this to stop saving new transcripts. Existing entries remain until cleared.", _historyEnabled, 64);
        privacy.Add("Retention", "Keep only the newest 5 to 100 transcripts.", LeftControl(_historyLimit), 62);
        AddPageItem(body, privacy.Build());

        _historyList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Control,
            ForeColor = Theme.Text,
            Font = Theme.Font(9f),
            AccessibleName = "Recent transcripts",
        };
        _historyList.SelectedIndexChanged += (_, _) => UpdateHistorySelection();
        _historyResultPreview = BuildHistoryPreview("Delivered transcript");
        _historyOriginalPreview = BuildHistoryPreview("Raw transcript before formatting and AI cleanup");
        _historyComparisonStatus = BuildInlineHint(Theme.Surface);
        _historyComparisonStatus.Text = "Select a transcript to compare both versions.";
        var comparison = BuildHistoryComparison();
        var copy = new ActionButton("Copy result", ActionButtonStyle.Primary) { BackColor = Theme.Surface };
        copy.Click += (_, _) => CopySelectedHistory(original: false);
        _historyCopyOriginalButton = new ActionButton("Copy original", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        _historyCopyOriginalButton.Click += (_, _) => CopySelectedHistory(original: true);
        var delete = new ActionButton("Delete", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        delete.Click += (_, _) => DeleteSelectedHistory();
        var learn = new ActionButton("Learn correction", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        learn.Click += (_, _) => LearnCorrectionFromHistory();
        var refresh = new ActionButton("Refresh", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        refresh.Click += (_, _) => LoadHistory();
        var clear = new ActionButton("Clear all", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        clear.Click += (_, _) => ClearHistory();
        _historyStatus = BuildInlineHint(Theme.Surface);
        _historyStatus.Text = "No transcripts saved yet.";

        var recovery = new SectionBuilder("Recent transcripts", "Newest first. Select an entry to inspect or copy it.");
        recovery.Add("Saved entries", "Timestamp and a short preview. Transcript text stays out of logs and diagnostics.", _historyList, 176);
        recovery.Add("Compare versions", "See the delivered result beside the untouched local transcript. Both remain private.", comparison, 220);
        recovery.Add("Actions", "Teach a correction once, copy either version, or manage the selected local recovery entry.", BuildHistoryActions(copy, _historyCopyOriginalButton, learn, delete, refresh, clear), 122);
        AddPageItem(body, recovery.Build());
    }

    private static TextBox BuildHistoryPreview(string accessibleName) => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Theme.Control,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = accessibleName,
    };

    private Control BuildHistoryComparison()
    {
        var comparison = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        comparison.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var resultLabel = Theme.Label("DELIVERED", Theme.TextSecondary, 8.1f, FontStyle.Bold, Theme.Surface);
        resultLabel.Dock = DockStyle.Fill;
        resultLabel.TextAlign = ContentAlignment.MiddleLeft;
        var originalLabel = Theme.Label("ORIGINAL", Theme.TextSecondary, 8.1f, FontStyle.Bold, Theme.Surface);
        originalLabel.Dock = DockStyle.Fill;
        originalLabel.TextAlign = ContentAlignment.MiddleLeft;
        comparison.Controls.Add(resultLabel, 0, 0);
        comparison.Controls.Add(originalLabel, 1, 0);

        _historyResultPreview.Margin = new Padding(0, 0, 6, 4);
        _historyOriginalPreview.Margin = new Padding(6, 0, 0, 4);
        comparison.Controls.Add(_historyResultPreview, 0, 1);
        comparison.Controls.Add(_historyOriginalPreview, 1, 1);
        _historyComparisonStatus.Dock = DockStyle.Fill;
        comparison.Controls.Add(_historyComparisonStatus, 0, 2);
        comparison.SetColumnSpan(_historyComparisonStatus, 2);
        return comparison;
    }

    private Control BuildHistoryActions(params ActionButton[] buttons)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel MakeRow(IEnumerable<ActionButton> rowButtons)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            foreach (ActionButton button in rowButtons)
            {
                button.Margin = new Padding(0, 0, 8, 4);
                row.Controls.Add(button);
            }
            return row;
        }

        panel.Controls.Add(MakeRow(buttons.Take(2)), 0, 0);
        panel.Controls.Add(MakeRow(buttons.Skip(2)), 0, 1);
        _historyStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_historyStatus, 0, 2);
        return panel;
    }

    private void BuildAdvancedPage()
    {
        var body = CreatePage(AdvancedPage);
        _themePicker = new ThemePicker { Dock = DockStyle.Fill };
        _themePicker.SelectValue(Theme.Current.Id);
        _themePicker.SelectedChanged += (_, _) => ApplySelectedTheme();
        _overlayStylePicker = new OverlayStylePicker { Dock = DockStyle.Fill };
        _overlayStylePicker.SelectedChanged += (_, _) =>
        {
            if (_loading)
                return;
            SavePreferences();
            OverlayStyleChanged?.Invoke(_overlayStylePicker.SelectedValue);
            ShowFooter($"{char.ToUpperInvariant(_overlayStylePicker.SelectedValue[0]) + _overlayStylePicker.SelectedValue[1..]} overlay applied", Theme.Ok);
        };
        var appearance = new SectionBuilder("Appearance", "A focused dark interface with instant local customization.");
        appearance.Add("Interface theme", "Graphite is the neutral default. Changes apply instantly and stay on this computer.", _themePicker, 96);
        appearance.Add("Recording overlay", "Choose a compact waveform, equalizer bars, or a minimal reactive microphone orb.", _overlayStylePicker, 104);
        AddPageItem(body, appearance.Build());

        var performance = new SectionBuilder("Recent performance", "Timing metadata from the local runtime log. No audio or transcript text is read.");
        _performanceLatest = BuildValueLabel();
        _performanceTypical = BuildValueLabel();
        _performanceMicrophone = BuildValueLabel();
        _performanceRecovery = BuildValueLabel();
        var refreshPerformance = new ActionButton("Refresh", ActionButtonStyle.Secondary) { Width = 88, BackColor = Theme.Surface };
        refreshPerformance.Click += (_, _) => RefreshPerformanceSnapshot();
        performance.Add("Latest result", "Language, confidence, and recognition time after key release.", HorizontalControl(_performanceLatest, refreshPerformance), 58);
        performance.Add("Typical latency", "Median and p95 recognition time across up to 50 recent recordings.", _performanceTypical, 58);
        performance.Add("Microphone response", "Median time from hotkey activation until audio capture is ready.", _performanceMicrophone, 58);
        performance.Add("Safety passes", "Bilingual retries, full-audio fallbacks, and median decoding speed.", _performanceRecovery, 58);
        AddPageItem(body, performance.Build());

        var diagnostics = new SectionBuilder("System diagnostics", "Read-only signals from the installed application and runtime.");
        _diagnosticDaemon = BuildValueLabel();
        _diagnosticRuntime = BuildValueLabel();
        _diagnosticConfig = BuildValueLabel();
        _diagnosticVersion = BuildValueLabel();
        diagnostics.Add("Daemon", "Current background speech service state.", _diagnosticDaemon, 58);
        diagnostics.Add("Runtime", "Local Python and faster-whisper integration.", _diagnosticRuntime, 58);
        diagnostics.Add("Configuration", "The active TOML settings file.", _diagnosticConfig, 58);
        diagnostics.Add("Application", "Installed VoicePrompt desktop build.", _diagnosticVersion, 58);
        AddPageItem(body, diagnostics.Build());

        var tools = new SectionBuilder("Tools", "Open support files or copy a compact report for troubleshooting.");
        var copy = new ActionButton("Copy diagnostics", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        copy.Click += (_, _) => CopyDiagnostics();
        var log = new ActionButton("Open daemon log", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        log.Click += (_, _) => OpenLog();
        var config = new ActionButton("Open config folder", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        config.Click += (_, _) => OpenConfigFolder();
        var release = new ActionButton("Latest release", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        release.Click += (_, _) => OpenExternal("https://github.com/seNkoKG/VoicePrompt/releases/latest");
        var privacy = new ActionButton("Privacy", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        privacy.Click += (_, _) => OpenInstalledDocument("PRIVACY.md");
        var terms = new ActionButton("Terms", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        terms.Click += (_, _) => OpenInstalledDocument("TERMS.md");
        var licenses = new ActionButton("Licenses", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        licenses.Click += (_, _) => OpenInstalledDocument("THIRD_PARTY_NOTICES.txt");
        _updateChannelChoice = new ChoiceStrip(new[] { "Stable", "Preview" }, new[] { "stable", "preview" }) { Dock = DockStyle.Fill };
        _updateChannelChoice.AccessibleName = "Application update channel";
        _updateChannelChoice.SelectedChanged += (_, _) =>
        {
            ResetUpdateCheck();
            if (!_loading)
                SavePreferences();
        };
        _updateStatus = BuildValueLabel();
        _updateButton = new ActionButton("Check now", ActionButtonStyle.Secondary) { Width = 150, BackColor = Theme.Surface };
        _updateButton.Click += async (_, _) =>
        {
            if (_availableUpdate?.Package is not null)
            {
                await InstallUpdateAsync(_availableUpdate);
                return;
            }
            if (!string.IsNullOrWhiteSpace(_updateReleaseUrl))
            {
                OpenExternal(_updateReleaseUrl);
                return;
            }
            await CheckForUpdatesAsync();
        };
        tools.Add("Support bundle", "No audio or transcript text is included in copied diagnostics.", HorizontalControl(copy, log), 62);
        tools.Add("Update channel", "Stable is recommended. Preview also checks explicit prereleases; updates are always installed manually.", _updateChannelChoice, 62);
        tools.Add("Application updates", "Checks GitHub only when clicked. Downloads are SHA-256 verified before the existing installer starts.", HorizontalControl(_updateStatus, _updateButton), 62);
        tools.Add("Files & updates", "Open the live config directory or the public download page.", HorizontalControl(config, release), 62);
        tools.Add("Legal & privacy", "Review the installed terms, privacy notice, and third-party licenses.", HorizontalControl(privacy, terms, licenses), 62);
        AddPageItem(body, tools.Build());
        ResetUpdateCheck();

        var portability = new SectionBuilder("Data portability", "Move your setup without exposing API keys or transcript history.");
        var exportBackup = new ActionButton("Export backup", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        exportBackup.Click += (_, _) => ExportAppBackup();
        var importBackup = new ActionButton("Import backup", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        importBackup.Click += (_, _) => ImportAppBackup();
        portability.Add("Settings & vocabulary", "Includes portable settings, corrections, and snippets. Review imports before Save & restart.", HorizontalControl(exportBackup, importBackup), 64);
        AddPageItem(body, portability.Build());

        var maintenance = new SectionBuilder("Maintenance", "Safe recovery actions. Nothing changes until you confirm or save.");
        var recommended = new ActionButton("Restore recommended setup", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        recommended.Click += (_, _) => RestoreRecommendedSetup();
        var restart = new ActionButton("Restart runtime", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        restart.Click += async (_, _) => await RestartDaemonAsync();
        maintenance.Add("Recognition defaults", "Resets language, audio detection, model, GPU, and precision; keeps your hotkey and vocabulary.", LeftControl(recommended), 64);
        maintenance.Add("Runtime recovery", "Restart the background service without changing settings.", LeftControl(restart), 62);
        AddPageItem(body, maintenance.Build());

        var paths = new SectionBuilder("Locations", "Exact paths used by this Windows account.");
        paths.Add("Configuration", "Live dictation settings.", ReadOnlyPath(_paths.ConfigPath), 62);
        paths.Add("Runtime log", "Recent startup, recording, and transcription events.", ReadOnlyPath(_paths.LogPath), 62);
        paths.Add("Runtime home", "Python environment and launcher.", ReadOnlyPath(_paths.Home), 62);
        AddPageItem(body, paths.Build());
    }

    private void ApplySelectedTheme()
    {
        if (_loading)
            return;

        ThemePalette previous = Theme.Use(_themePicker.SelectedValue);
        if (previous.Id == Theme.Current.Id)
            return;

        Theme.ApplyToTree(this, previous);
        NativeWindowStyle.Apply(this);
        SavePreferences();
        ShowFooter($"{Theme.Current.Name} theme applied", Theme.Ok);
    }

    private static void AddCardHeading(SurfacePanel card, string title, string subtitle)
    {
        var heading = Theme.Label(title, Theme.Text, 12f, FontStyle.Bold, Theme.Surface);
        heading.Location = new Point(26, 21);
        card.Controls.Add(heading);
        var description = Theme.Label(subtitle, Theme.TextSecondary, 8.7f, FontStyle.Regular, Theme.Surface);
        description.AutoSize = false;
        description.Bounds = new Rectangle(26, 49, 200, 20);
        card.SizeChanged += (_, _) => description.Width = Math.Max(80, card.ClientSize.Width - 52);
        card.Controls.Add(description);
    }

    private static Label BuildInlineHint(Color background)
    {
        return new Label
        {
            AutoSize = false,
            Height = 18,
            ForeColor = Theme.Muted,
            BackColor = background,
            Font = Theme.Font(8.25f),
            AutoEllipsis = true,
            UseMnemonic = false,
        };
    }

    private static Label BuildValueLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = "Checking…",
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Font = Theme.Font(9.25f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
    }

    private static Control StackControl(Control primary, Label hint, int primaryHeight)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        primary.Bounds = new Rectangle(0, 0, panel.Width, primaryHeight - 20);
        primary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        hint.Bounds = new Rectangle(2, primaryHeight - 16, panel.Width - 4, 18);
        hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(primary);
        panel.Controls.Add(hint);
        return panel;
    }

    private static Control HorizontalControl(params Control[] controls)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        foreach (Control control in controls)
            control.Dock = DockStyle.None;
        panel.Layout += (_, _) =>
        {
            int right = panel.ClientSize.Width;
            for (int i = controls.Length - 1; i >= 1; i--)
            {
                Control control = controls[i];
                int width = control.Width;
                right -= width;
                control.Bounds = new Rectangle(right, Math.Max(0, (panel.ClientSize.Height - control.Height) / 2), width, control.Height);
                right -= 8;
            }
            controls[0].Bounds = new Rectangle(0, Math.Max(0, (panel.ClientSize.Height - 40) / 2), Math.Max(60, right), 40);
        };
        foreach (Control control in controls)
            panel.Controls.Add(control);
        for (int i = 1; i < controls.Length; i++)
            controls[i].BringToFront();
        return panel;
    }

    private static Control LeftControl(Control control)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        panel.Layout += (_, _) =>
            control.Location = new Point(0, Math.Max(0, (panel.ClientSize.Height - control.Height) / 2));
        panel.Controls.Add(control);
        return panel;
    }

    private static NumericUpDown MakeNumber(decimal min, decimal max, decimal increment, int decimals, int width)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            DecimalPlaces = decimals,
            Width = width,
            Height = 34,
        };
        Theme.StyleNumeric(numeric);
        return numeric;
    }

    private static Control ReadOnlyPath(string path)
    {
        var text = new TextBox
        {
            Text = path,
            ReadOnly = true,
            TabStop = false,
        };
        return new TextFieldFrame(text) { Dock = DockStyle.Fill };
    }

    private void AttachChangeTracking()
    {
        _hotkeyRecorder.BindingChanged += (_, _) => MarkDirty();
        foreach (ChoiceStrip choice in new[]
        {
            _activationChoice,
            _outputChoice,
            _languageChoice,
            _sampleRateChoice,
            _aiModeChoice,
            _recognitionEngineChoice,
            _computeChoice,
            _processorChoice,
        })
            choice.SelectedChanged += (_, _) => MarkDirty();

        _autoStartToggle.CheckedChanged += (_, _) => MarkDirty();
        _voiceCommandsToggle.CheckedChanged += (_, _) => MarkDirty();
        _smartFormattingToggle.CheckedChanged += (_, _) => MarkDirty();
        _contextAwarenessToggle.CheckedChanged += (_, _) => MarkDirty();
        _bufferedTranscriptionToggle.CheckedChanged += (_, _) => MarkDirty();
        _historyEnabled.CheckedChanged += (_, _) => MarkDirty();
        foreach (TextBox text in new[]
        {
            _promptText,
            _correctionsText,
            _snippetsText,
            _aiEndpointText,
            _aiModelText,
            _aiKeyText,
            _recognitionServerUrl,
            _hotwordsText,
        })
            text.TextChanged += (_, _) => MarkDirty();
        _appProfilesText.TextChanged += (_, _) =>
        {
            MarkDirty();
            UpdateAiAvailability();
        };

        _recognitionModelCombo.TextChanged += (_, _) => MarkDirty();
        _additionalLanguageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _additionalLanguageCombo.SelectedItem is LanguageOption && _languageChoice.SelectedValue != "other")
                _languageChoice.SelectValue("other");
            MarkDirty();
            UpdateLanguageHint();
            UpdateOverview();
        };
        _additionalLanguageCombo.TextChanged += (_, _) =>
        {
            if (!_loading && ResolveAdditionalLanguage() != null && _languageChoice.SelectedValue != "other")
                _languageChoice.SelectValue("other");
            MarkDirty();
            UpdateLanguageHint();
            UpdateOverview();
        };
        _microphoneCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
                _microphoneCombo.Tag = (_microphoneCombo.SelectedItem as ComboItem)?.Value ?? "";
            MarkDirty();
        };
        foreach (NumericUpDown number in new[]
        {
            _threshold,
            _silenceMs,
            _minimumSpeechMs,
            _maximumSpeechSeconds,
            _temperature,
            _recognitionServerTimeout,
            _aiTimeoutMs,
            _historyLimit,
        })
            number.ValueChanged += (_, _) => MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading)
            return;
        SetDirty(true);
        UpdateOverview();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _saveButton.Enabled = dirty && !_busy;
        _discardButton.Enabled = dirty && !_busy;
        if (dirty)
            ShowFooter("Unsaved changes", Theme.Warn);
        else
            ShowFooter("All changes saved", Theme.Muted);
    }

    private void ShowFooter(string text, Color color)
    {
        _footerMessage.Text = text;
        _footerMessage.ForeColor = color;
    }

    private void ShowPage(string key, bool persist = true)
    {
        if (!_pages.ContainsKey(key))
            key = OverviewPage;

        _selectedPage = key;
        foreach ((string pageKey, Panel page) in _pages)
        {
            page.Visible = pageKey == key;
            if (page.Visible)
            {
                page.BringToFront();
                ResizePageBody(page, _pageBodies[pageKey]);
            }
        }
        foreach ((string pageKey, NavigationButton button) in _navigation)
            button.Selected = pageKey == key;

        _pageTitle.Text = _pageCopy[key].Title;
        _pageDescription.Text = _pageCopy[key].Description;
        if (key == HistoryPage)
            LoadHistory();
        if (key == AdvancedPage)
            RefreshPerformanceSnapshot();
        _inputLevelMeter.SetActive(key == AudioPage);
        if (persist)
            SavePreferences();
    }

    internal void ShowPageForDiagnostics(string key) => ShowPage(key, persist: false);

    private void UpdateActivationHint()
    {
        _activationHint.Text = _activationChoice.SelectedValue == "hold"
            ? "Recommended: release the shortcut to transcribe immediately."
            : "Press once to begin recording and again to finish.";
    }

    private void UpdateLanguageHint()
    {
        _languageHint.Text = _languageChoice.SelectedValue switch
        {
            "sl" => "Pins Slovenian for short Slovenian-only recordings.",
            "sl-slang" => "Automatic English detection with visible colloquial Slovenian hints.",
            "en" => "Pins English and skips language detection.",
            "other" when ResolveAdditionalLanguage() is { } option => $"Pins {option.Name}; recognition stays in {option.Name} and never translates it.",
            "other" => "Choose one supported language below before saving.",
            _ => "Recommended: detects English or Slovenian for every recording.",
        };
    }

    private void UpdateOutputHint()
    {
        _outputHint.Text = _outputChoice.SelectedValue == "clipboard"
            ? "Leaves the final transcript on the clipboard and sends no paste keystroke."
            : "Recommended: pastes once into the focused app, then restores your clipboard.";
    }

    private void UpdateLanguageControls()
    {
        UpdateLanguageHint();
    }

    private LanguageOption? ResolveAdditionalLanguage()
    {
        if (_additionalLanguageCombo.SelectedItem is LanguageOption selected)
            return selected;
        string value = _additionalLanguageCombo.Text.Trim();
        return LanguageCatalog.All.FirstOrDefault(option =>
            string.Equals(option.Code, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private string SelectedLanguageCode() => _languageChoice.SelectedValue == "other"
        ? ResolveAdditionalLanguage()?.Code ?? ""
        : _languageChoice.SelectedValue;

    private void ExportLanguageProfile()
    {
        if (_languageChoice.SelectedValue == "other" && ResolveAdditionalLanguage() is null)
        {
            ShowFooter("Choose the additional language before exporting its profile.", Theme.Warn);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            FileName = "VoicePrompt-language-profile.json",
            Filter = "VoicePrompt language profile (*.json)|*.json",
            OverwritePrompt = true,
            Title = "Export VoicePrompt language profile",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            LanguageProfileDocument profile = LanguageProfileStore.Create(
                SelectedLanguageCode(),
                _promptText.Text,
                _hotwordsText.Text,
                _correctionsText.Text);
            LanguageProfileStore.Save(dialog.FileName, profile);
            ShowFooter("Language profile exported · no secrets or device settings included", Theme.Ok);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not export profile · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void ImportLanguageProfile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "VoicePrompt language profile (*.json)|*.json",
            Multiselect = false,
            Title = "Import VoicePrompt language profile",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            ApplyLanguageProfile(LanguageProfileStore.Load(dialog.FileName));
            ShowFooter("Language profile loaded · review, then Save & restart", Theme.Accent);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not import profile · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void ApplyLanguageProfile(LanguageProfileDocument profile)
    {
        string? primary = LanguageCatalog.PrimaryModeFor(profile.Language);
        if (primary is not null)
        {
            _languageChoice.SelectValue(primary);
            _additionalLanguageCombo.SelectedIndex = -1;
            _additionalLanguageCombo.Text = "";
        }
        else
        {
            LanguageOption option = LanguageCatalog.Find(profile.Language)
                ?? throw new InvalidDataException("The profile language is not supported.");
            _languageChoice.SelectValue("other");
            _additionalLanguageCombo.SelectedItem = option;
        }
        _promptText.Text = profile.Prompt;
        _hotwordsText.Text = profile.Hotwords;
        _correctionsText.Text = profile.CorrectionsText;
        UpdateLanguageControls();
        UpdateOverview();
        MarkDirty();
    }

    private void ExportAppBackup()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            FileName = "VoicePrompt-settings-backup.json",
            Filter = "VoicePrompt settings backup (*.json)|*.json",
            OverwritePrompt = true,
            Title = "Export VoicePrompt settings and vocabulary",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            AppBackupStore.Save(dialog.FileName, BuildAppBackup());
            ShowFooter("Backup exported · no API key, transcript history, microphone, or machine paths included", Theme.Ok);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not export backup · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void ImportAppBackup()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "VoicePrompt settings backup (*.json)|*.json",
            Multiselect = false,
            Title = "Import VoicePrompt settings and vocabulary",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            ApplyAppBackup(AppBackupStore.Load(dialog.FileName));
            ShowFooter("Backup loaded · review each page, then Save & restart", Theme.Accent);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not import backup · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private VoicePromptBackupDocument BuildAppBackup()
    {
        LanguageProfileDocument profile = LanguageProfileStore.Create(
            SelectedLanguageCode(),
            _promptText.Text,
            _hotwordsText.Text,
            _correctionsText.Text);
        return new VoicePromptBackupDocument
        {
            Dictation = new BackupDictationSettings
            {
                Hotkey = _hotkeyRecorder.Binding,
                Activation = _activationChoice.SelectedValue,
                OutputMode = _outputChoice.SelectedValue,
                VoiceCommands = _voiceCommandsToggle.Checked,
                SmartFormatting = _smartFormattingToggle.Checked,
                ContextAwareness = _contextAwarenessToggle.Checked,
                Language = profile.Language,
                Prompt = profile.Prompt,
                Hotwords = profile.Hotwords,
            },
            Recognition = new BackupRecognitionSettings
            {
                EngineType = _recognitionEngineChoice.SelectedValue,
                ServerUrl = _recognitionServerUrl.Text,
                ServerTimeoutSeconds = (int)_recognitionServerTimeout.Value,
                Model = _recognitionModelCombo.Text,
                Processor = _processorChoice.SelectedValue,
                ComputeType = _computeChoice.SelectedValue,
                Temperature = (double)_temperature.Value,
                BufferedTranscription = _bufferedTranscriptionToggle.Checked,
            },
            Audio = new BackupAudioSettings
            {
                SampleRate = int.Parse(_sampleRateChoice.SelectedValue, System.Globalization.CultureInfo.InvariantCulture),
                Threshold = (double)_threshold.Value,
                SilenceMs = (int)_silenceMs.Value,
                MinimumSpeechMs = (int)_minimumSpeechMs.Value,
                MaximumSpeechSeconds = (double)_maximumSpeechSeconds.Value,
            },
            Writing = new BackupWritingSettings
            {
                Mode = _aiModeChoice.SelectedValue,
                Endpoint = _aiEndpointText.Text,
                Model = _aiModelText.Text,
                TimeoutMs = (int)_aiTimeoutMs.Value,
            },
            Recovery = new BackupRecoverySettings
            {
                Enabled = _historyEnabled.Checked,
                Limit = (int)_historyLimit.Value,
            },
            Corrections = profile.Corrections,
            Snippets = TextSnippetStore.Parse(_snippetsText.Text).ToList(),
            AppProfiles = AppProfileStore.Parse(_appProfilesText.Text).ToList(),
        };
    }

    private void ApplyAppBackup(VoicePromptBackupDocument backup)
    {
        _loading = true;
        try
        {
            _hotkeyRecorder.Binding = backup.Dictation.Hotkey;
            _activationChoice.SelectValue(backup.Dictation.Activation);
            _outputChoice.SelectValue(backup.Dictation.OutputMode);
            _voiceCommandsToggle.Checked = backup.Dictation.VoiceCommands;
            _smartFormattingToggle.Checked = backup.Dictation.SmartFormatting;
            _contextAwarenessToggle.Checked = backup.Dictation.ContextAwareness;
            ApplyLanguageProfile(new LanguageProfileDocument
            {
                Language = backup.Dictation.Language,
                Prompt = backup.Dictation.Prompt,
                Hotwords = backup.Dictation.Hotwords,
                Corrections = backup.Corrections,
            });
            _snippetsText.Text = TextSnippetStore.Format(backup.Snippets);
            _appProfilesText.Text = AppProfileStore.Format(backup.AppProfiles);

            _recognitionEngineChoice.SelectValue(backup.Recognition.EngineType);
            _recognitionServerUrl.Text = backup.Recognition.ServerUrl;
            _recognitionServerTimeout.Value = backup.Recognition.ServerTimeoutSeconds;
            _recognitionModelCombo.Text = backup.Recognition.Model;
            _processorChoice.SelectValue(backup.Recognition.Processor);
            _computeChoice.SelectValue(backup.Recognition.ComputeType);
            _temperature.Value = (decimal)backup.Recognition.Temperature;
            _bufferedTranscriptionToggle.Checked = backup.Recognition.BufferedTranscription;

            _sampleRateChoice.SelectValue(backup.Audio.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _threshold.Value = (decimal)backup.Audio.Threshold;
            _silenceMs.Value = backup.Audio.SilenceMs;
            _minimumSpeechMs.Value = backup.Audio.MinimumSpeechMs;
            _maximumSpeechSeconds.Value = (decimal)backup.Audio.MaximumSpeechSeconds;

            _aiModeChoice.SelectValue(backup.Writing.Mode);
            _aiEndpointText.Text = backup.Writing.Endpoint;
            _aiModelText.Text = backup.Writing.Model;
            _aiTimeoutMs.Value = backup.Writing.TimeoutMs;
            ResetAiKeyField();
            _historyEnabled.Checked = backup.Recovery.Enabled;
            _historyLimit.Value = backup.Recovery.Limit;
            UpdateActivationHint();
            UpdateOutputHint();
            UpdateLanguageControls();
            UpdateRecognitionEngineAvailability();
            UpdateAiAvailability();
            UpdateOverview();
        }
        finally
        {
            _loading = false;
        }
        MarkDirty();
    }

    private void UpdateOverview()
    {
        if (_hotkeySummary == null)
            return;

        _hotkeySummary.Text = string.IsNullOrWhiteSpace(_hotkeyRecorder.Binding)
            ? "Not configured"
            : _hotkeyRecorder.Binding.ToUpperInvariant() + (_activationChoice.SelectedValue == "hold" ? " · hold" : " · toggle");
        _languageSummary.Text = _languageChoice.SelectedValue switch
        {
            "sl" => "Slovenian only",
            "sl-slang" => "Auto + Slovenian slang",
            "en" => "English only",
            "other" when ResolveAdditionalLanguage() is { } option => option.Name + " only",
            "other" => "Choose a language",
            _ => "English + Slovenian Auto",
        };
        _microphoneSummary.Text = (_microphoneCombo.SelectedItem as ComboItem)?.Label ?? "System default";
        if (_recognitionEngineChoice.SelectedValue == "server")
        {
            _engineSummary.Text = Uri.TryCreate(_recognitionServerUrl.Text.Trim(), UriKind.Absolute, out Uri? serverUri)
                ? "Compatible server · " + serverUri.Authority
                : "Compatible server";
        }
        else
        {
            _engineSummary.Text = _processorChoice.SelectedValue == "cuda"
                ? "large-v3 · NVIDIA GPU"
                : (_recognitionModelCombo.Text.Contains("turbo", StringComparison.OrdinalIgnoreCase) ? "large-v3-turbo" : "large-v3") + " · " + _processorChoice.SelectedValue;
        }

        bool runtimeReady = _paths.Installed;
        string? hotkeyError = HotkeyBinding.Validate(_hotkeyRecorder.Binding);
        bool hotkeyReady = hotkeyError is null;
        bool audioReady = _microphoneCombo.Items.Count > 0;
        UpdateChecklist(_runtimeCheck, runtimeReady, runtimeReady ? "Installed and available" : "Installer repair required");
        UpdateChecklist(
            _hotkeyCheck,
            hotkeyReady,
            hotkeyReady ? _hotkeyRecorder.Binding.ToUpperInvariant() + " is configured" : hotkeyError!);
        UpdateChecklist(_audioCheck, audioReady, audioReady ? _microphoneSummary.Text : "No input devices found");
    }

    private static void UpdateChecklist(Label label, bool ready, string text)
    {
        label.Text = text;
        label.ForeColor = ready ? Theme.TextSecondary : Theme.Warn;
        Panel? dot = label.Parent?.Controls.OfType<Panel>()
            .Where(panel => Equals(panel.Tag, "check-dot"))
            .OrderBy(panel => Math.Abs(panel.Top - label.Top))
            .FirstOrDefault();
        if (dot != null)
            dot.BackColor = ready ? Theme.Ok : Theme.Warn;
    }

    private void LoadConfiguration()
    {
        _loading = true;
        string configuredHotkey = _config.GetString("hotkey", "binding") ?? "f1";
        if (HotkeyBinding.Validate(configuredHotkey) != null)
        {
            configuredHotkey = "f1";
            _config.Set("hotkey", "binding", configuredHotkey);
            _config.Save();
            _hotkeyWasMigrated = true;
        }
        _hotkeyRecorder.Binding = configuredHotkey;
        _activationChoice.SelectValue(_config.GetString("hotkey", "mode") ?? "hold");
        UpdateActivationHint();
        _outputChoice.SelectValue(string.Equals(
            _config.GetString("voiceprompt", "output_mode"),
            "clipboard",
            StringComparison.OrdinalIgnoreCase) ? "clipboard" : "paste");
        UpdateOutputHint();
        _voiceCommandsToggle.Checked = _config.GetBool("voiceprompt", "voice_commands") ?? false;
        _smartFormattingToggle.Checked = _config.GetBool("voiceprompt", "smart_formatting") ?? true;
        _contextAwarenessToggle.Checked = _config.GetBool("voiceprompt", "context_awareness") ?? true;

        string language = _config.GetString("server", "language") ?? "";
        string? primaryLanguage = LanguageCatalog.PrimaryModeFor(language);
        bool slang = primaryLanguage == "sl-slang";
        if (primaryLanguage != null)
        {
            _languageChoice.SelectValue(primaryLanguage);
            _additionalLanguageCombo.SelectedIndex = -1;
            _additionalLanguageCombo.Text = "";
        }
        else
        {
            _languageChoice.SelectValue("other");
            LanguageOption? option = LanguageCatalog.Find(language);
            _additionalLanguageCombo.SelectedItem = option;
            if (option == null)
                _additionalLanguageCombo.Text = language;
        }
        UpdateLanguageControls();
        _promptText.Text = slang
            ? _config.GetString("voiceprompt", "base_prompt") ?? _config.GetString("server", "prompt") ?? ""
            : _config.GetString("server", "prompt") ?? "";

        _recognitionEngineChoice.SelectValue(string.Equals(
            _config.GetString("engine", "type"),
            "server",
            StringComparison.OrdinalIgnoreCase) ? "server" : "local");
        _recognitionServerUrl.Text = _config.GetString("server", "url") ?? "http://localhost:8000";
        _recognitionServerTimeout.Value = Clamp(_config.GetInt("server", "timeout") ?? 60, _recognitionServerTimeout);
        _recognitionModelCombo.Text = _config.GetString("server", "model") ?? "Systran/faster-whisper-large-v3";
        _temperature.Value = Clamp((decimal)(_config.GetDouble("server", "temperature") ?? 0), _temperature);
        _hotwordsText.Text = slang
            ? _config.GetString("voiceprompt", "base_hotwords") ?? _config.GetString("server", "hotwords") ?? ""
            : _config.GetString("server", "hotwords") ?? "";
        _bufferedTranscriptionToggle.Checked = _config.GetBool("voiceprompt", "buffered_transcription") ?? true;

        _threshold.Value = Clamp((decimal)(_config.GetDouble("vad", "threshold") ?? 0.60), _threshold);
        _silenceMs.Value = Clamp(_config.GetInt("vad", "silence_ms") ?? 250, _silenceMs);
        _minimumSpeechMs.Value = Clamp(_config.GetInt("vad", "min_speech_ms") ?? 250, _minimumSpeechMs);
        _maximumSpeechSeconds.Value = Clamp((decimal)(_config.GetDouble("vad", "max_speech_s") ?? 180), _maximumSpeechSeconds);

        _microphoneCombo.Items.Clear();
        _microphoneCombo.Items.Add(new ComboItem("System default", ""));
        _microphoneCombo.SelectedIndex = 0;
        _microphoneCombo.Tag = _config.GetString("audio", "device") ?? "";
        _sampleRateChoice.SelectValue((_config.GetInt("audio", "sample_rate") ?? 16000).ToString());
        _computeChoice.SelectValue(_config.GetString("engine", "compute_type") ?? "float16");
        _processorChoice.SelectValue(_config.GetString("engine", "device") ?? "auto");
        _loading = false;
        UpdateRecognitionEngineAvailability();
    }

    private void LoadAiConfiguration()
    {
        _loading = true;
        _aiSettings = AiSettingsStore.Load(_paths.AiConfigPath);
        _aiModeChoice.SelectValue(_aiSettings.Mode);
        _aiEndpointText.Text = _aiSettings.Endpoint;
        _aiModelText.Text = _aiSettings.Model;
        _aiTimeoutMs.Value = Clamp(_aiSettings.TimeoutMs, _aiTimeoutMs);
        ResetAiKeyField();
        _loading = false;
        UpdateAiAvailability();
    }

    private void LoadLocalTextSettings()
    {
        _loading = true;
        HistorySettings settings = _historyStore.LoadSettings();
        _historyEnabled.Checked = settings.Enabled;
        _historyLimit.Value = Clamp(settings.Limit, _historyLimit);
        _correctionsText.Text = _dictionaryStore.LoadText();
        _snippetsText.Text = _snippetStore.LoadText();
        _appProfilesText.Text = _appProfileStore.LoadText();
        _loading = false;
        LoadHistory();
    }

    private void ResetAiKeyField()
    {
        _aiKeyText.Clear();
        _aiKeyText.PlaceholderText = string.IsNullOrEmpty(_aiSettings.ApiKeyProtected)
            ? "Optional for local providers"
            : "Saved securely · leave blank to keep";
        _aiKeyText.Modified = false;
    }

    private AiSettings BuildAiSettings()
    {
        string protectedKey = _aiSettings.ApiKeyProtected;
        if (_aiKeyText.Modified)
        {
            string entered = _aiKeyText.Text.Trim();
            protectedKey = entered.Length == 0 ? "" : AiSettingsStore.ProtectApiKey(entered);
        }

        return new AiSettings
        {
            Mode = _aiModeChoice.SelectedValue,
            Endpoint = _aiEndpointText.Text.Trim(),
            Model = _aiModelText.Text.Trim(),
            TimeoutMs = (int)_aiTimeoutMs.Value,
            ApiKeyProtected = protectedKey,
        };
    }

    private void UpdateAiAvailability()
    {
        if (_aiModeChoice == null)
            return;
        bool profileUsesAi = _appProfilesText is not null && AppProfileStore.UsesAi(_appProfilesText.Text);
        bool enabled = _aiModeChoice.SelectedValue != "off" || profileUsesAi;
        _aiEndpointText.Enabled = enabled;
        _aiModelText.Enabled = enabled;
        _aiTimeoutMs.Enabled = enabled;
        _aiKeyText.Enabled = enabled;
        _aiTestButton.Enabled = enabled && !_busy;
        _aiClearKeyButton.Enabled = enabled && !_busy &&
            (!string.IsNullOrEmpty(_aiSettings.ApiKeyProtected) || _aiKeyText.TextLength > 0);
        if (_aiModeChoice.SelectedValue == "off")
        {
            _aiResult.Text = profileUsesAi
                ? "Verbatim globally · matching application profiles use this provider."
                : "Verbatim adds zero delay and sends no transcript anywhere.";
            _aiResult.ForeColor = Theme.Muted;
            return;
        }
        _aiResult.Text = AiSettingsStore.PrivacyMessage(_aiEndpointText.Text);
        bool unsafeRemote = !RecognitionServer.IsLoopback(_aiEndpointText.Text) &&
            _aiEndpointText.Text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        _aiResult.ForeColor = unsafeRemote ? Theme.Warn : Theme.TextSecondary;
    }

    private void UpdateRecognitionEngineAvailability()
    {
        if (_recognitionEngineChoice == null)
            return;
        bool server = _recognitionEngineChoice.SelectedValue == "server";
        _recognitionServerUrl.Enabled = server;
        _recognitionServerTimeout.Enabled = server;
        _recognitionServerTestButton.Enabled = server && !_busy;
        _processorChoice.Enabled = !server;
        _computeChoice.Enabled = !server;
        _bufferedTranscriptionToggle.Enabled = !server;

        if (!server)
        {
            _recognitionServerResult.Text = "Recommended · audio stays on this PC and uses the selected local processor.";
            _recognitionServerResult.ForeColor = Theme.Muted;
            return;
        }

        _recognitionServerResult.Text = RecognitionServer.PrivacyMessage(_recognitionServerUrl.Text);
        bool unsafeRemote = !RecognitionServer.IsLoopback(_recognitionServerUrl.Text) &&
            _recognitionServerUrl.Text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        _recognitionServerResult.ForeColor = unsafeRemote ? Theme.Warn : Theme.TextSecondary;
    }

    private async Task TestRecognitionServerAsync()
    {
        if (_busy || _recognitionEngineChoice.SelectedValue != "server")
            return;
        _recognitionServerTestButton.Enabled = false;
        _recognitionServerResult.Text = "Checking server health…";
        _recognitionServerResult.ForeColor = Theme.TextSecondary;
        string testedUrl = _recognitionServerUrl.Text;
        RecognitionServerProbeResult result = await RecognitionServer.ProbeAsync(testedUrl);
        if (IsDisposed)
            return;
        if (_recognitionEngineChoice.SelectedValue != "server" ||
            !string.Equals(_recognitionServerUrl.Text, testedUrl, StringComparison.Ordinal))
        {
            UpdateRecognitionEngineAvailability();
            return;
        }
        _recognitionServerResult.Text = result.Message;
        _recognitionServerResult.ForeColor = result.Success ? Theme.Ok : Theme.Warn;
        _recognitionServerTestButton.Enabled = !_busy && _recognitionEngineChoice.SelectedValue == "server";
    }

    private async Task RefreshMicrophonesAsync()
    {
        if (_microphoneCombo == null)
            return;

        string saved = _microphoneCombo.Tag as string ?? (_microphoneCombo.SelectedItem as ComboItem)?.Value ?? "";
        ShowFooter("Refreshing microphones…", Theme.TextSecondary);
        DeviceScanResult scan = await Task.Run(_daemon.ListDevices);
        if (IsDisposed)
            return;

        _loading = true;
        var items = new List<ComboItem> { new("System default", "") };
        foreach (string raw in scan.Devices)
        {
            int separator = raw.IndexOf(':');
            string name = separator >= 0 ? raw[(separator + 1)..].Trim() : raw.Trim();
            if (name.Length > 0)
                items.Add(new ComboItem(raw, name));
        }

        _microphoneCombo.Items.Clear();
        foreach (ComboItem item in items)
            _microphoneCombo.Items.Add(item);
        int selected = items.FindIndex(item => saved.Length > 0 &&
            (item.Value.Equals(saved, StringComparison.OrdinalIgnoreCase) || item.Label.Contains(saved, StringComparison.OrdinalIgnoreCase)));
        _microphoneCombo.SelectedIndex = selected >= 0 ? selected : 0;
        _microphoneCombo.Tag = (_microphoneCombo.SelectedItem as ComboItem)?.Value ?? "";
        _loading = false;
        if (!_dirty)
        {
            if (scan.Error.Length > 0)
                ShowFooter("Could not scan microphones · " + ShortMessage(scan.Error), Theme.Warn);
            else
                ShowFooter(scan.Devices.Count == 0 ? "Using the Windows default microphone" : $"Found {scan.Devices.Count} microphone input(s)", Theme.Muted);
        }
        UpdateOverview();
    }

    private async Task TestAiAsync()
    {
        if (_busy)
            return;

        AiSettings settings;
        try
        {
            settings = BuildAiSettings();
        }
        catch (Exception ex)
        {
            SetAiResult("Could not protect the API key: " + ShortMessage(ex.Message), false);
            return;
        }

        string? validation = AiSettingsStore.Validate(settings);
        if (validation != null)
        {
            SetAiResult(validation, false);
            return;
        }
        if (settings.Mode == "off")
        {
            AppProfileEntry? profile;
            try
            {
                profile = AppProfileStore.Parse(_appProfilesText.Text)
                    .FirstOrDefault(value => value.WritingMode is "clean" or "grammar" or "prompt");
            }
            catch (InvalidDataException ex)
            {
                SetAiResult(ShortMessage(ex.Message), false);
                return;
            }
            if (profile is null)
            {
                SetAiResult("Choose Clean, Polish, Prompt, or an AI application profile before testing.", false);
                return;
            }
            settings = new AiSettings
            {
                Mode = profile.WritingMode,
                Endpoint = settings.Endpoint,
                Model = settings.Model,
                TimeoutMs = settings.TimeoutMs,
                ApiKeyProtected = settings.ApiKeyProtected,
            };
        }

        SetBusy(true);
        SetAiResult("Testing provider…", true);
        string temporary = _paths.AiConfigPath + ".test";
        try
        {
            AiSettingsStore.Save(temporary, settings);
            AiTestResult result = await Task.Run(() => _daemon.TestAi(temporary));
            SetAiResult(
                result.Ok
                    ? $"Provider ready in {result.LatencyMs} ms. Save changes to enable it."
                    : "Raw local text will be used · " + ShortMessage(result.Error),
                result.Ok);
        }
        catch (Exception ex)
        {
            SetAiResult("Test failed · " + ShortMessage(ex.Message), false);
        }
        finally
        {
            TryDelete(temporary);
            TryDelete(temporary + ".tmp");
            SetBusy(false);
        }
    }

    private void SetAiResult(string text, bool ok)
    {
        _aiResult.Text = text;
        _aiResult.ForeColor = ok ? Theme.Ok : Theme.Warn;
    }

    private async Task SaveAsync()
    {
        if (_busy || !_dirty)
            return;

        string hotkey = _hotkeyRecorder.Binding.Trim();
        string? hotkeyError = HotkeyBinding.Validate(hotkey);
        if (hotkeyError != null)
        {
            ShowPage(DictationPage);
            _hotkeyRecorder.Focus();
            ShowFooter(hotkeyError, Theme.Err);
            return;
        }

        string corrections = _correctionsText.Text;
        try
        {
            PersonalDictionaryStore.Parse(corrections);
        }
        catch (InvalidDataException ex)
        {
            ShowPage(DictationPage);
            _correctionsText.Focus();
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
            return;
        }

        string snippets = _snippetsText.Text;
        try
        {
            TextSnippetStore.Parse(snippets);
        }
        catch (InvalidDataException ex)
        {
            ShowPage(DictationPage);
            _snippetsText.Focus();
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
            return;
        }

        string appProfiles = _appProfilesText.Text;
        try
        {
            AppProfileStore.Parse(appProfiles);
        }
        catch (InvalidDataException ex)
        {
            ShowPage(IntelligencePage);
            _appProfilesText.Focus();
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
            return;
        }

        AiSettings aiSettings;
        try
        {
            aiSettings = BuildAiSettings();
        }
        catch (Exception ex)
        {
            ShowPage(IntelligencePage);
            ShowFooter("Could not protect the API key · " + ShortMessage(ex.Message), Theme.Err);
            return;
        }

        string? aiValidation = AiSettingsStore.Validate(aiSettings);
        if (aiValidation != null)
        {
            ShowPage(IntelligencePage);
            ShowFooter(aiValidation, Theme.Err);
            return;
        }

        string recognitionEngine = _recognitionEngineChoice.SelectedValue;
        string recognitionServerUrl = _recognitionServerUrl.Text.Trim();
        int recognitionServerTimeout = (int)_recognitionServerTimeout.Value;
        string? recognitionServerValidation = RecognitionServer.Validate(
            recognitionServerUrl,
            recognitionServerTimeout);
        if (recognitionServerValidation != null)
        {
            ShowPage(IntelligencePage);
            _recognitionServerUrl.Focus();
            ShowFooter(recognitionServerValidation, Theme.Err);
            return;
        }
        recognitionServerUrl = RecognitionServer.NormalizeUrl(recognitionServerUrl);

        string activation = _activationChoice.SelectedValue;
        string language = SelectedLanguageCode();
        if (_languageChoice.SelectedValue == "other" && !LanguageCatalog.IsSupported(language))
        {
            ShowPage(DictationPage);
            _additionalLanguageCombo.Focus();
            ShowFooter("Choose a supported additional language before saving.", Theme.Err);
            return;
        }
        bool slangProfile = language == "sl-slang";
        string basePrompt = _promptText.Text.Trim();
        string prompt = slangProfile ? SlovenianSlangProfile.ApplyPrompt(basePrompt) : basePrompt;
        string baseHotwords = _hotwordsText.Text.Trim();
        string hotwords = slangProfile ? SlovenianSlangProfile.ApplyHotwords(baseHotwords) : baseHotwords;
        string model = _recognitionModelCombo.Text.Trim();
        string microphone = (_microphoneCombo.SelectedItem as ComboItem)?.Value ?? "";
        int sampleRate = int.Parse(_sampleRateChoice.SelectedValue, System.Globalization.CultureInfo.InvariantCulture);
        double temperature = (double)_temperature.Value;
        double threshold = (double)_threshold.Value;
        int silenceMs = (int)_silenceMs.Value;
        int minimumSpeechMs = (int)_minimumSpeechMs.Value;
        double maximumSpeechSeconds = (double)_maximumSpeechSeconds.Value;
        string computeType = _computeChoice.SelectedValue;
        string processor = _processorChoice.SelectedValue;
        bool historyEnabled = _historyEnabled.Checked;
        int historyLimit = (int)_historyLimit.Value;
        bool bufferedTranscription = _bufferedTranscriptionToggle.Checked;
        string outputMode = _outputChoice.SelectedValue;
        bool voiceCommands = _voiceCommandsToggle.Checked;
        bool smartFormatting = _smartFormattingToggle.Checked;
        bool contextAwareness = _contextAwarenessToggle.Checked;

        SetBusy(true);
        ShowFooter("Saving settings and restarting the dictation runtime…", Theme.Accent);
        try
        {
            await Task.Run(() =>
            {
                _config.Set("hotkey", "binding", hotkey);
                _config.Set("hotkey", "mode", activation);
                _config.Set("server", "language", language);
                _config.Set("server", "url", recognitionServerUrl);
                _config.Set("server", "timeout", recognitionServerTimeout);
                _config.Set("server", "prompt", prompt);
                _config.Set("server", "model", model);
                _config.Set("server", "temperature", temperature);
                _config.Set("server", "hotwords", hotwords);
                _config.Set("voiceprompt", "slovenian_slang", slangProfile);
                _config.Set("voiceprompt", "base_prompt", basePrompt);
                _config.Set("voiceprompt", "base_hotwords", baseHotwords);
                _config.Set("voiceprompt", "buffered_transcription", bufferedTranscription);
                _config.Set("voiceprompt", "output_mode", outputMode);
                _config.Set("voiceprompt", "voice_commands", voiceCommands);
                _config.Set("voiceprompt", "smart_formatting", smartFormatting);
                _config.Set("voiceprompt", "context_awareness", contextAwareness);
                _config.Set("vad", "threshold", threshold);
                _config.Set("vad", "silence_ms", silenceMs);
                _config.Set("vad", "min_speech_ms", minimumSpeechMs);
                _config.Set("vad", "max_speech_s", maximumSpeechSeconds);
                _config.Set("audio", "device", microphone);
                _config.Set("audio", "sample_rate", sampleRate);
                _config.Set("engine", "type", recognitionEngine);
                _config.Set("engine", "compute_type", computeType);
                _config.Set("engine", "device", processor);
                _config.Save();
                AiSettingsStore.Save(_paths.AiConfigPath, aiSettings);
                _dictionaryStore.SaveText(corrections);
                _snippetStore.SaveText(snippets);
                _appProfileStore.SaveText(appProfiles);
                _historyStore.SaveSettings(historyEnabled, historyLimit);
                _daemon.Restart();
            });

            _aiSettings = aiSettings;
            _loading = true;
            ResetAiKeyField();
            _loading = false;
            string? startupWarning = ApplyAutoStart();
            SavePreferences();
            SetDirty(false);
            ShowFooter(
                startupWarning ?? "Saved · runtime restarted and ready",
                startupWarning == null ? Theme.Ok : Theme.Warn);
            DaemonRestarted?.Invoke();
            UpdateStatus(_daemon.Refresh(true));
        }
        catch (Exception ex)
        {
            ShowFooter("Save failed · " + ShortMessage(ex.Message), Theme.Err);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DiscardChanges()
    {
        if (_busy || !_dirty)
            return;
        LoadConfiguration();
        LoadLocalTextSettings();
        LoadAiConfiguration();
        LoadAutoStartState();
        SetDirty(false);
        UpdateOverview();
        ShowFooter("Unsaved changes discarded", Theme.Muted);
    }

    private async Task ToggleDaemonAsync()
    {
        await RunDaemonActionAsync(() =>
        {
            if (_daemon.Refresh(true).State == DaemonState.Running)
                _daemon.Stop();
            else
                _daemon.Start();
        }, "Runtime state updated");
    }

    private async Task RestartDaemonAsync()
    {
        await RunDaemonActionAsync(_daemon.Restart, "Runtime restarted and ready");
        DaemonRestarted?.Invoke();
    }

    private async Task RunDaemonActionAsync(Action action, string success)
    {
        if (_busy)
            return;
        SetBusy(true);
        ShowFooter("Working…", Theme.Accent);
        try
        {
            await Task.Run(action);
            UpdateStatus(_daemon.Refresh(true));
            ShowFooter(success, Theme.Ok);
        }
        catch (Exception ex)
        {
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _saveButton.Enabled = !busy && _dirty;
        _discardButton.Enabled = !busy && _dirty;
        _daemonToggleButton.Enabled = !busy;
        _daemonRestartButton.Enabled = !busy && _daemon.Info.State == DaemonState.Running;
        UpdateRecognitionEngineAvailability();
        UpdateAiAvailability();
    }

    public void UpdateStatus(DaemonInfo info)
    {
        Color color = info.State switch
        {
            DaemonState.Running => Theme.Ok,
            DaemonState.Stopped => Theme.Warn,
            _ => Theme.Muted,
        };
        string label = info.State switch
        {
            DaemonState.Running => "Ready",
            DaemonState.Stopped => "Stopped",
            _ => "Checking",
        };
        _headerStatus.SetStatus(label, color);
        _sidebarStatus.SetStatus(label, color);
        _overviewStatus.SetStatus(label, color);

        _sidebarStatusDetail.Text = info.State == DaemonState.Running
            ? $"{info.Hotkey?.ToUpperInvariant() ?? "Hotkey"} · {(info.Mode == "toggle" ? "toggle" : "hold to talk")}"
            : info.State == DaemonState.Stopped
                ? "Dictation is currently unavailable"
                : "Checking the local runtime";
        _overviewStatusText.Text = info.State == DaemonState.Running ? "Ready when you are" : "Runtime needs attention";
        _daemonToggleButton.Text = info.State == DaemonState.Running ? "Stop runtime" : "Start runtime";
        _daemonToggleButton.VisualStyle = info.State == DaemonState.Running ? ActionButtonStyle.Secondary : ActionButtonStyle.Primary;
        _daemonRestartButton.Enabled = info.State == DaemonState.Running && !_busy;

        _diagnosticDaemon.Text = info.State == DaemonState.Running
            ? $"Running · PID {info.Pid} · {info.Hotkey} ({info.Mode})"
            : info.State.ToString();
        _diagnosticRuntime.Text = _paths.Installed ? "Installed · local faster-whisper" : "Missing · run the installer";
        _diagnosticConfig.Text = File.Exists(_paths.ConfigPath) ? "Loaded · config.toml" : "Not found";
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion;
        _diagnosticVersion.Text = $"VoicePrompt {version} · Windows x64";
        UpdateOverview();
    }

    private void LoadHistory()
    {
        if (_historyList == null)
            return;
        string? selectedId = (_historyList.SelectedItem as HistoryListItem)?.Entry.Id;
        IReadOnlyList<TranscriptEntry> entries = _historyStore.Load();
        _historyList.BeginUpdate();
        _historyList.Items.Clear();
        foreach (TranscriptEntry entry in entries)
            _historyList.Items.Add(new HistoryListItem(entry));
        _historyList.EndUpdate();
        int selectedIndex = entries.ToList().FindIndex(entry => entry.Id == selectedId);
        if (_historyList.Items.Count > 0)
            _historyList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        else
            ClearHistorySelection();
        _historyStatus.Text = entries.Count == 0
            ? "No transcripts saved yet."
            : $"{entries.Count} local transcript{(entries.Count == 1 ? "" : "s")} available.";
        _historyStatus.ForeColor = entries.Count == 0 ? Theme.Muted : Theme.Ok;
    }

    private void UpdateHistorySelection()
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
        {
            ClearHistorySelection();
            return;
        }
        _historyResultPreview.Text = selected.Entry.Text;
        _historyOriginalPreview.Text = selected.Entry.SourceText;
        _historyCopyOriginalButton.Enabled = true;
        _historyComparisonStatus.Text = selected.Entry.WasRewritten
            ? "AI cleanup changed this transcript · both versions are recoverable"
            : "No AI changes recorded · both versions are identical";
        _historyComparisonStatus.ForeColor = selected.Entry.WasRewritten ? Theme.Ok : Theme.Muted;
    }

    private void ClearHistorySelection()
    {
        _historyResultPreview.Clear();
        _historyOriginalPreview.Clear();
        _historyCopyOriginalButton.Enabled = false;
        _historyComparisonStatus.Text = "Select a transcript to compare both versions.";
        _historyComparisonStatus.ForeColor = Theme.Muted;
    }

    private void CopySelectedHistory(bool original)
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
        {
            ShowFooter("Select a transcript first.", Theme.Warn);
            return;
        }
        try
        {
            Clipboard.SetDataObject(original ? selected.Entry.SourceText : selected.Entry.Text, true, 5, 40);
            ShowFooter(original ? "Original transcript copied" : "Delivered transcript copied", Theme.Ok);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not copy transcript · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void DeleteSelectedHistory()
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
            return;
        if (MessageBox.Show(this, "Delete the selected local transcript?", "Delete transcript", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _historyStore.Delete(selected.Entry.Id);
        LoadHistory();
        ShowFooter("Transcript deleted", Theme.Muted);
    }

    private void LearnCorrectionFromHistory()
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
        {
            ShowFooter("Select a transcript first.", Theme.Warn);
            return;
        }
        string source = _historyOriginalPreview.SelectedText.Trim();
        string result = _historyResultPreview.SelectedText.Trim();
        if (source.Length == 0)
            source = selected.Entry.SourceText.Trim();
        if (result.Length == 0)
            result = selected.Entry.Text.Trim();
        string heard = source.Length <= 120 ? source : "";
        string replacement = !source.Equals(result, StringComparison.Ordinal) && result.Length <= 120 ? result : "";
        using var dialog = new CorrectionLearningDialog(heard, replacement);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            _dictionaryStore.AddOrReplace(dialog.Heard, dialog.Replacement);
            _correctionsText.Text = _dictionaryStore.LoadText();
            ShowFooter("Correction learned · applies to the next dictation", Theme.Ok);
        }
        catch (InvalidDataException ex)
        {
            ShowFooter(ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void ClearHistory()
    {
        if (_historyList.Items.Count == 0)
            return;
        if (MessageBox.Show(this, "Delete every locally saved transcript? This cannot be undone.", "Clear recovery history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _historyStore.Clear();
        LoadHistory();
        ShowFooter("Recovery history cleared", Theme.Muted);
    }

    private void RestoreRecommendedSetup()
    {
        DialogResult confirm = MessageBox.Show(
            this,
            "Restore the tested recognition, audio, and performance defaults?\n\nYour hotkey, microphone, custom prompt, hotwords, and AI provider will not be changed.",
            "Restore recommended setup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
            return;

        _languageChoice.SelectValue("");
        _additionalLanguageCombo.SelectedIndex = -1;
        _additionalLanguageCombo.Text = "";
        _sampleRateChoice.SelectValue("16000");
        _threshold.Value = 0.60m;
        _silenceMs.Value = 250;
        _minimumSpeechMs.Value = 250;
        _maximumSpeechSeconds.Value = 180;
        _recognitionEngineChoice.SelectValue("local");
        _recognitionModelCombo.Text = "Systran/faster-whisper-large-v3";
        _processorChoice.SelectValue("cuda");
        _computeChoice.SelectValue("float16");
        _temperature.Value = 0;
        _bufferedTranscriptionToggle.Checked = true;
        _smartFormattingToggle.Checked = true;
        _contextAwarenessToggle.Checked = true;
        MarkDirty();
        ShowFooter("Recommended setup restored. Save to activate it.", Theme.Accent);
    }

    private void CopyDiagnostics()
    {
        try
        {
            DaemonInfo info = _daemon.Refresh(true);
            PerformanceSnapshot performance = PerformanceSnapshot.Read(_paths.LogPath);
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion;
            var report = new StringBuilder()
                .AppendLine($"VoicePrompt {version}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Runtime: {info.State} · PID {info.Pid}")
                .AppendLine($"Hotkey: {info.Hotkey} · Mode: {info.Mode}")
                .AppendLine($"Engine: {info.Engine}")
                .AppendLine($"Config exists: {File.Exists(_paths.ConfigPath)}")
                .AppendLine($"Runtime installed: {_paths.Installed}")
                .AppendLine($"Buffered long recordings: {_bufferedTranscriptionToggle.Checked}")
                .AppendLine($"Performance samples: {performance.Count}")
                .AppendLine(performance.Count > 0
                    ? $"Recognition median/p95: {performance.MedianTotalSeconds:0.000}s / {performance.P95TotalSeconds:0.000}s"
                    : "Recognition median/p95: unavailable")
                .AppendLine(performance.MedianMicrophoneMs is { } microphoneMs
                    ? $"Microphone-ready median: {microphoneMs:0}ms"
                    : "Microphone-ready median: unavailable")
                .AppendLine($"Bilingual retries: {performance.RetryCount}")
                .AppendLine($"Config: {_paths.ConfigPath}")
                .AppendLine($"Log: {_paths.LogPath}")
                .ToString();
            Clipboard.SetDataObject(report, true, 5, 40);
            ShowFooter("Diagnostics copied · no audio or transcript text included", Theme.Ok);
        }
        catch (Exception ex)
        {
            ShowFooter("Could not copy diagnostics · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    private void RefreshPerformanceSnapshot()
    {
        if (_performanceLatest == null)
            return;

        PerformanceSnapshot snapshot = PerformanceSnapshot.Read(_paths.LogPath);
        if (snapshot.Latest is not { } latest)
        {
            _performanceLatest.Text = "No completed recordings yet";
            _performanceTypical.Text = "Waiting for local timing data";
            _performanceMicrophone.Text = "Not measured yet";
            _performanceRecovery.Text = "No recovery passes measured";
            return;
        }

        string language = LanguageCatalog.Find(latest.Language)?.Name ?? latest.Language.ToUpperInvariant();
        _performanceLatest.Text = $"{language} · {latest.Confidence:P0} confidence · {latest.TotalSeconds:0.000} s";
        _performanceTypical.Text = $"{snapshot.MedianTotalSeconds:0.000} s median · {snapshot.P95TotalSeconds:0.000} s p95 · {snapshot.Count} samples";
        _performanceMicrophone.Text = snapshot.MedianMicrophoneMs is { } microphoneMs
            ? $"{microphoneMs:0} ms median"
            : "Not available in recent samples";
        string speed = snapshot.MedianRealtimeSpeed is { } realtimeSpeed ? $" · {realtimeSpeed:0.0}× real-time" : "";
        _performanceRecovery.Text = $"{snapshot.RetryCount} bilingual · {snapshot.FullFallbackCount} full-audio{speed}";
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_busy)
            return;
        _updateReleaseUrl = "";
        _availableUpdate = null;
        _updateButton.Enabled = false;
        _updateChannelChoice.Enabled = false;
        _updateButton.Text = "Checking…";
        UpdateChannel channel = SelectedUpdateChannel();
        _updateStatus.Text = channel == UpdateChannel.Preview
            ? "Checking stable and preview releases…"
            : "Checking the latest stable release…";
        try
        {
            string current = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
            UpdateResult result = await _updateChecker.CheckAsync(current, channel);
            if (result.State == UpdateState.Available && result.LatestVersion is not null)
            {
                _updateReleaseUrl = result.ReleaseUrl;
                _availableUpdate = result;
                _updateStatus.Text = $"Update available · {result.CurrentVersion.Display} → {result.LatestVersion.Display}";
                _updateStatus.ForeColor = Theme.Accent;
                _updateButton.Text = result.Package is null ? "Open release" : "Download & install";
                ShowFooter(
                    result.Package is null
                        ? $"VoicePrompt {result.LatestVersion.Display} is available; its installer is still publishing"
                        : $"VoicePrompt {result.LatestVersion.Display} is ready to install",
                    Theme.Accent);
            }
            else if (result.State == UpdateState.UpToDate)
            {
                _updateStatus.Text = $"Up to date · VoicePrompt {result.CurrentVersion.Display}";
                _updateStatus.ForeColor = Theme.Ok;
                _updateButton.Text = "Check again";
                ShowFooter("VoicePrompt is up to date", Theme.Ok);
            }
            else
            {
                _updateStatus.Text = result.Error;
                _updateStatus.ForeColor = Theme.Warn;
                _updateButton.Text = "Try again";
                ShowFooter(result.Error, Theme.Warn);
            }
        }
        finally
        {
            _updateButton.Enabled = true;
            _updateChannelChoice.Enabled = true;
        }
    }

    private async Task InstallUpdateAsync(UpdateResult update)
    {
        if (update.Package is null || update.LatestVersion is null)
            return;
        if (_busy)
            return;
        if (_dirty)
        {
            ShowFooter("Save or discard your changes before installing an update.", Theme.Warn);
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"Download and install VoicePrompt {update.LatestVersion.Display}?\n\n" +
            "The release will be verified before it runs. VoicePrompt will close, preserve your settings, install the update, and restart.",
            "Install VoicePrompt update",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);
        if (answer != DialogResult.OK)
            return;

        bool launched = false;
        SetBusy(true);
        _updateButton.Enabled = false;
        _updateChannelChoice.Enabled = false;
        _updateButton.Text = "Preparing…";
        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                _updateStatus.Text = value.Percentage is { } percentage
                    ? $"{value.Message} · {percentage}%"
                    : value.Message;
                _updateStatus.ForeColor = Theme.Accent;
            });
            StagedUpdate staged = await _updateInstaller.PrepareAsync(update.Package, progress);
            using Process installer = UpdateInstaller.Launch(staged);
            launched = true;
            _updateStatus.Text = $"Installing VoicePrompt {staged.Version.Display}…";
            _updateStatus.ForeColor = Theme.Ok;
            _updateButton.Text = "Restarting…";
            ShowFooter("Verified installer started · VoicePrompt will restart when the update is complete", Theme.Ok);
            UpdateInstallerLaunched?.Invoke();
        }
        catch (Exception ex)
        {
            _updateStatus.Text = "Update failed · " + ShortMessage(ex.Message);
            _updateStatus.ForeColor = Theme.Err;
            _updateButton.Text = "Try again";
            ShowFooter("Update failed · " + ShortMessage(ex.Message), Theme.Err);
        }
        finally
        {
            if (!launched && !IsDisposed)
            {
                SetBusy(false);
                _updateButton.Enabled = true;
                _updateChannelChoice.Enabled = true;
            }
        }
    }

    private UpdateChannel SelectedUpdateChannel() =>
        _updateChannelChoice.SelectedValue == "preview" ? UpdateChannel.Preview : UpdateChannel.Stable;

    private void ResetUpdateCheck()
    {
        _updateReleaseUrl = "";
        _availableUpdate = null;
        if (_updateButton is not null)
            _updateButton.Text = "Check now";
        if (_updateStatus is null || _updateChannelChoice is null)
            return;
        bool preview = SelectedUpdateChannel() == UpdateChannel.Preview;
        _updateStatus.Text = preview
            ? "Not checked · Preview includes prereleases"
            : "Not checked · Stable releases only";
        _updateStatus.ForeColor = Theme.Text;
    }

    private void OpenLog()
    {
        if (!File.Exists(_paths.LogPath))
        {
            ShowFooter("The daemon log does not exist yet.", Theme.Warn);
            return;
        }
        OpenExternal("notepad.exe", '"' + _paths.LogPath + '"');
    }

    private void OpenConfigFolder()
    {
        Directory.CreateDirectory(_paths.ConfigDir);
        OpenExternal("explorer.exe", '"' + _paths.ConfigDir + '"');
    }

    private void OpenInstalledDocument(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            ShowFooter("The installed document is missing · " + fileName, Theme.Warn);
            return;
        }
        OpenExternal("notepad.exe", '"' + path + '"');
    }

    private void OpenExternal(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                Arguments = arguments ?? "",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowFooter("Could not open the requested item · " + ShortMessage(ex.Message), Theme.Err);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            _ = SaveAsync();
            return true;
        }
        if (keyData == Keys.Escape && !_hotkeyRecorder.IsCapturing)
        {
            Hide();
            return true;
        }
        if ((keyData & Keys.Control) == Keys.Control)
        {
            string? page = (keyData & Keys.KeyCode) switch
            {
                Keys.D1 => OverviewPage,
                Keys.D2 => DictationPage,
                Keys.D3 => AudioPage,
                Keys.D4 => IntelligencePage,
                Keys.D5 => HistoryPage,
                Keys.D6 => AdvancedPage,
                _ => null,
            };
            if (page != null)
            {
                ShowPage(page);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SavePreferences();
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_inputLevelMeter is not null)
            _inputLevelMeter.SetActive(Visible && _selectedPage == AudioPage);
    }

    private string PreferencesPath => Path.Combine(_paths.AppDataDir, "prefs.json");

    private static string ReadThemePreference(string path)
    {
        try
        {
            string? json = ReadBoundedPreferences(path);
            if (json is null)
                return "graphite";
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("theme", out JsonElement theme)
                ? Theme.Find(theme.GetString()).Id
                : "graphite";
        }
        catch
        {
            return "graphite";
        }
    }

    private static string? ReadBoundedPreferences(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > PreferencesMaxBytes)
            return null;
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private void LoadPreferences()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            string? json = ReadBoundedPreferences(PreferencesPath);
            if (json is null)
                return;
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("bounds", out JsonElement bounds) &&
                bounds.TryGetProperty("x", out JsonElement x) &&
                bounds.TryGetProperty("y", out JsonElement y) &&
                bounds.TryGetProperty("w", out JsonElement width) &&
                bounds.TryGetProperty("h", out JsonElement height))
            {
                var rectangle = new Rectangle(x.GetInt32(), y.GetInt32(), width.GetInt32(), height.GetInt32());
                if (rectangle.Width >= MinimumSize.Width && rectangle.Height >= MinimumSize.Height &&
                    Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(rectangle)))
                    Bounds = rectangle;
            }
            if (root.TryGetProperty("page", out JsonElement page))
                _selectedPage = page.GetString() ?? OverviewPage;
            if (root.TryGetProperty("updateChannel", out JsonElement updateChannel))
                _updateChannelChoice.SelectValue(updateChannel.GetString() == "preview" ? "preview" : "stable");
            if (root.TryGetProperty("overlayStyle", out JsonElement overlayStyle))
                _overlayStylePicker.SelectValue(overlayStyle.GetString());
            _themePicker.SelectValue(Theme.Current.Id);
        }
        catch
        {
        }
    }

    private void SavePreferences()
    {
        string temporaryPath = PreferencesPath + ".tmp";
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            string json = JsonSerializer.Serialize(new
            {
                bounds = new { x = Bounds.X, y = Bounds.Y, w = Bounds.Width, h = Bounds.Height },
                page = _selectedPage,
                updateChannel = _updateChannelChoice.SelectedValue,
                theme = Theme.Current.Id,
                overlayStyle = _overlayStylePicker.SelectedValue,
            });
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, PreferencesPath, true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private string StartupShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "VoicePrompt.lnk");

    private string LegacyStartupShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Voice Typing (faster-whisper-dictation).lnk");

    private void LoadAutoStartState()
    {
        _loading = true;
        _autoStartToggle.Checked = File.Exists(StartupShortcut) || File.Exists(LegacyStartupShortcut);
        _loading = false;
    }

    private string? ApplyAutoStart()
    {
        try
        {
            if (_autoStartToggle.Checked)
            {
                if (!File.Exists(StartupShortcut))
                    CreateShortcut(StartupShortcut, Application.ExecutablePath, "--tray");
                if (File.Exists(LegacyStartupShortcut))
                    File.Delete(LegacyStartupShortcut);
            }
            else
            {
                if (File.Exists(StartupShortcut))
                    File.Delete(StartupShortcut);
                if (File.Exists(LegacyStartupShortcut))
                    File.Delete(LegacyStartupShortcut);
            }
            return null;
        }
        catch (Exception ex)
        {
            return "Settings saved, but startup could not be changed · " + ShortMessage(ex.Message);
        }
    }

    private static void CreateShortcut(string shortcutPath, string target, string arguments)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target)!;
        shortcut.Save();
    }

    private static decimal Clamp(decimal value, NumericUpDown numeric) =>
        Math.Clamp(value, numeric.Minimum, numeric.Maximum);

    private static string ShortMessage(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 120 ? oneLine : oneLine[..117] + "…";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record ComboItem(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record HistoryListItem(TranscriptEntry Entry)
    {
        public override string ToString()
        {
            string preview = Entry.Text.ReplaceLineEndings(" ").Trim();
            if (preview.Length > 62)
                preview = preview[..59] + "…";
            return $"{Entry.CreatedAt.ToLocalTime():g}  ·  {preview}";
        }
    }

    private sealed class SectionBuilder
    {
        private readonly SurfacePanel _card;
        private readonly TableLayoutPanel _table;
        private int _height = 86;

        public SectionBuilder(string title, string subtitle)
        {
            _card = new SurfacePanel { Height = 170 };
            var heading = Theme.Label(title, Theme.Text, 12f, FontStyle.Bold, Theme.Surface);
            heading.Location = new Point(26, 20);
            _card.Controls.Add(heading);
            var description = Theme.Label(subtitle, Theme.TextSecondary, 8.7f, FontStyle.Regular, Theme.Surface);
            description.AutoSize = false;
            description.Bounds = new Rectangle(26, 50, 200, 20);
            _card.Controls.Add(description);

            _table = new TableLayoutPanel
            {
                BackColor = Theme.Surface,
                ColumnCount = 2,
                RowCount = 0,
                Location = new Point(26, 84),
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            _card.Controls.Add(_table);
            _card.SizeChanged += (_, _) =>
            {
                description.Width = Math.Max(80, _card.ClientSize.Width - 52);
                _table.Width = Math.Max(300, _card.ClientSize.Width - 52);
            };
        }

        public void Add(string title, string description, Control control, int height)
        {
            int row = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var copy = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Margin = new Padding(0, 7, 20, 7),
            };
            var heading = Theme.Label(title, Theme.Text, 9.4f, FontStyle.Bold, Theme.Surface);
            heading.Location = new Point(0, 3);
            copy.Controls.Add(heading);
            var detail = new Label
            {
                Text = description,
                AutoSize = false,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                Font = Theme.Font(8.15f),
                UseMnemonic = false,
                AutoEllipsis = true,
                Bounds = new Rectangle(0, 25, 100, Math.Max(18, height - 32)),
            };
            copy.SizeChanged += (_, _) =>
            {
                detail.Width = Math.Max(20, copy.ClientSize.Width);
                detail.Height = Math.Max(16, copy.ClientSize.Height - detail.Top);
            };
            copy.Controls.Add(detail);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 8, 0, 8);
            _table.Controls.Add(copy, 0, row);
            _table.Controls.Add(control, 1, row);
            _height += height;
        }

        public SurfacePanel Build()
        {
            _table.Height = _height - 86;
            _card.Height = _height + 20;
            return _card;
        }
    }
}
