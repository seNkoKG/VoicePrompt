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

    private readonly DaemonManager _daemon;
    private readonly ConfigManager _config;
    private readonly AppPaths _paths;
    private readonly TranscriptHistoryStore _historyStore;
    private readonly PersonalDictionaryStore _dictionaryStore;
    private readonly Dictionary<string, Panel> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlowLayoutPanel> _pageBodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavigationButton> _navigation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Title, string Description)> _pageCopy = new(StringComparer.Ordinal)
    {
        [OverviewPage] = ("Overview", "Everything you need to confirm VoicePrompt is ready."),
        [DictationPage] = ("Dictation", "Choose how recording starts and how your speech is interpreted."),
        [AudioPage] = ("Audio", "Select the microphone and tune speech detection without guesswork."),
        [IntelligencePage] = ("Intelligence", "Control recognition performance and optional AI text cleanup."),
        [HistoryPage] = ("Recovery", "Recover, copy, or remove recent transcripts stored only on this computer."),
        [AdvancedPage] = ("Advanced", "Diagnostics, application paths, maintenance, and recovery tools."),
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
    private ToggleSwitch _autoStartToggle = null!;
    private ChoiceStrip _languageChoice = null!;
    private ComboBox _additionalLanguageCombo = null!;
    private Label _languageHint = null!;
    private TextBox _promptText = null!;
    private TextBox _correctionsText = null!;

    private ComboBox _microphoneCombo = null!;
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
    private ActionButton _aiClearKeyButton = null!;
    private ActionButton _aiTestButton = null!;
    private Label _aiResult = null!;
    private AiSettings _aiSettings = new();

    private ToggleSwitch _historyEnabled = null!;
    private NumericUpDown _historyLimit = null!;
    private ListBox _historyList = null!;
    private TextBox _historyPreview = null!;
    private Label _historyStatus = null!;

    private ComboBox _recognitionModelCombo = null!;
    private ChoiceStrip _computeChoice = null!;
    private ChoiceStrip _processorChoice = null!;
    private NumericUpDown _temperature = null!;
    private TextBox _hotwordsText = null!;

    private Label _diagnosticDaemon = null!;
    private Label _diagnosticRuntime = null!;
    private Label _diagnosticConfig = null!;
    private Label _diagnosticVersion = null!;
    private Label _performanceLatest = null!;
    private Label _performanceTypical = null!;
    private Label _performanceMicrophone = null!;
    private Label _performanceRecovery = null!;

    private bool _loading = true;
    private bool _dirty;
    private bool _busy;
    private string _selectedPage = OverviewPage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string SelectedPage => _selectedPage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool HasUnsavedChanges => _dirty;

    public event Action? DaemonRestarted;

    public MainForm(DaemonManager daemon, AppPaths paths)
    {
        _daemon = daemon;
        _paths = paths;
        _config = new ConfigManager(paths.ConfigPath);
        _historyStore = new TranscriptHistoryStore(paths.HistoryPath, paths.HistorySettingsPath);
        _dictionaryStore = new PersonalDictionaryStore(paths.CorrectionsPath);

        Text = "VoicePrompt Settings";
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.Font();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);
        Size = new Size(1080, 780);
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
            Width = 232,
            BackColor = Theme.Sidebar,
            Padding = new Padding(14, 16, 14, 16),
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, sidebar.ClientSize.Width - 1, 0, sidebar.ClientSize.Width - 1, sidebar.ClientSize.Height);
        };

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = Theme.Sidebar,
        };
        var logo = new PictureBox
        {
            Bounds = new Rectangle(6, 7, 38, 38),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Sidebar,
        };
        try
        {
            logo.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap();
        }
        catch
        {
        }
        brand.Controls.Add(logo);

        var title = Theme.Label("VoicePrompt", Theme.Text, 12.25f, FontStyle.Bold, Theme.Sidebar);
        title.Location = new Point(54, 7);
        brand.Controls.Add(title);
        var caption = Theme.Label("Local voice to text", Theme.Muted, 8.4f, FontStyle.Regular, Theme.Sidebar);
        caption.Location = new Point(54, 33);
        brand.Controls.Add(caption);
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 294,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Sidebar,
            Padding = new Padding(0, 2, 0, 0),
        };
        AddNavigation(nav, OverviewPage, "Overview", NavigationGlyph.Overview);
        AddNavigation(nav, DictationPage, "Dictation", NavigationGlyph.Dictation);
        AddNavigation(nav, AudioPage, "Audio", NavigationGlyph.Audio);
        AddNavigation(nav, IntelligencePage, "Intelligence", NavigationGlyph.Intelligence);
        AddNavigation(nav, HistoryPage, "Recovery", NavigationGlyph.History);
        AddNavigation(nav, AdvancedPage, "Advanced", NavigationGlyph.Advanced);
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(brand);

        var sidebarBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 162,
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
            Bounds = new Rectangle(6, 88, 198, 38),
            BackColor = Theme.Sidebar,
        };
        hideButton.Click += (_, _) => Hide();
        sidebarBottom.Controls.Add(hideButton);

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion;
        var versionLabel = Theme.Label($"Version {version}", Theme.Muted, 8f, FontStyle.Regular, Theme.Sidebar);
        versionLabel.Location = new Point(6, 138);
        sidebarBottom.Controls.Add(versionLabel);
        sidebar.Controls.Add(sidebarBottom);
        return sidebar;
    }

    private void AddNavigation(FlowLayoutPanel parent, string key, string text, NavigationGlyph glyph)
    {
        var button = new NavigationButton(key, text, glyph)
        {
            Width = 204,
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
            Height = 104,
            BackColor = Theme.Canvas,
            Padding = new Padding(32, 22, 32, 12),
        };
        _pageTitle = Theme.Label("Overview", Theme.Text, 18.5f, FontStyle.Bold, Theme.Canvas);
        _pageTitle.Location = new Point(32, 20);
        header.Controls.Add(_pageTitle);
        _pageDescription = Theme.Label("", Theme.TextSecondary, 9.25f, FontStyle.Regular, Theme.Canvas);
        _pageDescription.Location = new Point(33, 56);
        header.Controls.Add(_pageDescription);
        _headerStatus = new StatusPill { BackColor = Theme.Canvas, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Controls.Add(_headerStatus);
        header.SizeChanged += (_, _) => _headerStatus.Location = new Point(header.ClientSize.Width - _headerStatus.Width - 32, 29);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            BackColor = Theme.Sidebar,
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _footerMessage = Theme.Label("All changes saved", Theme.Muted, 8.8f, FontStyle.Regular, Theme.Sidebar);
        _footerMessage.AutoSize = false;
        _footerMessage.Bounds = new Rectangle(32, 26, 430, 24);
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
            _saveButton.Location = new Point(footer.ClientSize.Width - _saveButton.Width - 32, 17);
            _discardButton.Location = new Point(_saveButton.Left - _discardButton.Width - 10, 17);
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
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(32, 8, 32, 40),
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
        int availableWidth = Math.Max(610, page.ClientSize.Width - (page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        body.Width = availableWidth;
        int cardWidth = Math.Max(550, availableWidth - body.Padding.Horizontal);
        foreach (Control child in body.Controls)
            child.Width = cardWidth;
    }

    private static void AddPageItem(FlowLayoutPanel body, Control control)
    {
        control.Margin = new Padding(0, 0, 0, 14);
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
        _autoStartToggle = new ToggleSwitch("Launch VoicePrompt when I sign in") { Dock = DockStyle.Left };

        var shortcut = new SectionBuilder("Shortcut & behavior", "A global key works from browsers, editors, chat apps, and games.");
        shortcut.Add("Global hotkey", "Click the field, press a key or combination, then press Enter.", _hotkeyRecorder, 76);
        shortcut.Add("Activation", "Hold mode is fastest and avoids accidental long recordings.", StackControl(_activationChoice, _activationHint, 64), 82);
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
        AddPageItem(body, language.Build());
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
        var hotwordsFrame = new TextFieldFrame(_hotwordsText) { Dock = DockStyle.Fill };

        var engine = new SectionBuilder("Recognition engine", "Local Whisper settings balance accuracy, latency, and memory usage.");
        engine.Add("Model", "large-v3 gives the best Slovenian accuracy; Turbo trades some accuracy for speed.", _recognitionModelCombo, 64);
        engine.Add("Processor", "Use NVIDIA GPU for the fastest local transcription.", _processorChoice, 64);
        engine.Add("Precision", "FP16 is recommended on modern NVIDIA GPUs; INT8 is useful on CPU.", _computeChoice, 64);
        engine.Add("Temperature", "0 uses deterministic decoding with automatic fallback only when quality checks fail.", LeftControl(_temperature), 62);
        engine.Add("Hotwords", "Extra words to boost. Built-in English/Slovenian vocabulary is added automatically in Auto.", hotwordsFrame, 64);
        AddPageItem(body, engine.Build());

        _aiModeChoice = new ChoiceStrip(new[] { "Off", "Grammar", "Prompt" }, new[] { "off", "grammar", "prompt" }) { Dock = DockStyle.Fill };
        _aiModeChoice.AccessibleName = "AI cleanup mode";
        _aiModeChoice.SelectedChanged += (_, _) => UpdateAiAvailability();
        _aiEndpointText = new TextBox { PlaceholderText = "http://127.0.0.1:11434/v1/chat/completions" };
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
        _aiResult.Text = "Off adds zero delay and sends no transcript anywhere.";

        var ai = new SectionBuilder("Optional AI cleanup", "Disabled by default. When enabled, only completed text is sent to the configured provider.");
        ai.Add("Cleanup mode", "Grammar is conservative; Prompt restructures rough speech without translating it.", StackControl(_aiModeChoice, _aiResult, 64), 84);
        ai.Add("Endpoint", "Any OpenAI-compatible chat completions endpoint, local or cloud.", new TextFieldFrame(_aiEndpointText) { Dock = DockStyle.Fill }, 64);
        ai.Add("Model", "The model name expected by your provider.", new TextFieldFrame(_aiModelText) { Dock = DockStyle.Fill }, 64);
        ai.Add("Maximum wait", "Strict live deadline in milliseconds; raw local text is pasted after a timeout.", LeftControl(_aiTimeoutMs), 62);
        ai.Add("API key", "Encrypted for your Windows account. Leave blank to keep an existing saved key.", keyRow, 68);
        AddPageItem(body, ai.Build());
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
        _historyPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Control,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Selected transcript text",
        };
        var copy = new ActionButton("Copy transcript", ActionButtonStyle.Primary) { BackColor = Theme.Surface };
        copy.Click += (_, _) => CopySelectedHistory();
        var delete = new ActionButton("Delete", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        delete.Click += (_, _) => DeleteSelectedHistory();
        var refresh = new ActionButton("Refresh", ActionButtonStyle.Secondary) { BackColor = Theme.Surface };
        refresh.Click += (_, _) => LoadHistory();
        var clear = new ActionButton("Clear all", ActionButtonStyle.Quiet) { BackColor = Theme.Surface };
        clear.Click += (_, _) => ClearHistory();
        _historyStatus = BuildInlineHint(Theme.Surface);
        _historyStatus.Text = "No transcripts saved yet.";

        var recovery = new SectionBuilder("Recent transcripts", "Newest first. Select an entry to inspect or copy it.");
        recovery.Add("Saved entries", "Timestamp and a short preview. Transcript text stays out of logs and diagnostics.", _historyList, 176);
        recovery.Add("Selected text", "If AI cleanup changed the text, recovery keeps the pasted result.", _historyPreview, 148);
        recovery.Add("Actions", "Copy returns the exact saved text to your clipboard.", StackControl(HorizontalControl(copy, delete, refresh, clear), _historyStatus, 58), 80);
        AddPageItem(body, recovery.Build());
    }

    private void BuildAdvancedPage()
    {
        var body = CreatePage(AdvancedPage);
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
        performance.Add("Safety recovery", "How often the bilingual fallback was needed, plus median decoding speed.", _performanceRecovery, 58);
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
        tools.Add("Support bundle", "No audio or transcript text is included in copied diagnostics.", HorizontalControl(copy, log), 62);
        tools.Add("Files & updates", "Open the live config directory or the public download page.", HorizontalControl(config, release), 62);
        AddPageItem(body, tools.Build());

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

    private static void AddCardHeading(SurfacePanel card, string title, string subtitle)
    {
        var heading = Theme.Label(title, Theme.Text, 11.5f, FontStyle.Bold, Theme.Surface);
        heading.Location = new Point(24, 20);
        card.Controls.Add(heading);
        var description = Theme.Label(subtitle, Theme.Muted, 8.7f, FontStyle.Regular, Theme.Surface);
        description.AutoSize = false;
        description.Bounds = new Rectangle(24, 47, 200, 20);
        card.SizeChanged += (_, _) => description.Width = Math.Max(80, card.ClientSize.Width - 48);
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
            _languageChoice,
            _sampleRateChoice,
            _aiModeChoice,
            _computeChoice,
            _processorChoice,
        })
            choice.SelectedChanged += (_, _) => MarkDirty();

        _autoStartToggle.CheckedChanged += (_, _) => MarkDirty();
        _historyEnabled.CheckedChanged += (_, _) => MarkDirty();
        foreach (TextBox text in new[]
        {
            _promptText,
            _correctionsText,
            _aiEndpointText,
            _aiModelText,
            _aiKeyText,
            _hotwordsText,
        })
            text.TextChanged += (_, _) => MarkDirty();

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
        _engineSummary.Text = _processorChoice.SelectedValue == "cuda"
            ? "large-v3 · NVIDIA GPU"
            : (_recognitionModelCombo.Text.Contains("turbo", StringComparison.OrdinalIgnoreCase) ? "large-v3-turbo" : "large-v3") + " · " + _processorChoice.SelectedValue;

        bool runtimeReady = _paths.Installed;
        bool hotkeyReady = !string.IsNullOrWhiteSpace(_hotkeyRecorder.Binding);
        bool audioReady = _microphoneCombo.Items.Count > 0;
        UpdateChecklist(_runtimeCheck, runtimeReady, runtimeReady ? "Installed and available" : "Installer repair required");
        UpdateChecklist(_hotkeyCheck, hotkeyReady, hotkeyReady ? _hotkeyRecorder.Binding.ToUpperInvariant() + " is configured" : "Choose a global shortcut");
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
        _hotkeyRecorder.Binding = _config.GetString("hotkey", "binding") ?? "f1";
        _activationChoice.SelectValue(_config.GetString("hotkey", "mode") ?? "hold");
        UpdateActivationHint();

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

        _recognitionModelCombo.Text = _config.GetString("server", "model") ?? "Systran/faster-whisper-large-v3";
        _temperature.Value = Clamp((decimal)(_config.GetDouble("server", "temperature") ?? 0), _temperature);
        _hotwordsText.Text = slang
            ? _config.GetString("voiceprompt", "base_hotwords") ?? _config.GetString("server", "hotwords") ?? ""
            : _config.GetString("server", "hotwords") ?? "";

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
        bool enabled = _aiModeChoice.SelectedValue != "off";
        _aiEndpointText.Enabled = enabled;
        _aiModelText.Enabled = enabled;
        _aiTimeoutMs.Enabled = enabled;
        _aiKeyText.Enabled = enabled;
        _aiTestButton.Enabled = enabled && !_busy;
        _aiClearKeyButton.Enabled = enabled && !_busy &&
            (!string.IsNullOrEmpty(_aiSettings.ApiKeyProtected) || _aiKeyText.TextLength > 0);
        if (!enabled)
        {
            _aiResult.Text = "Off adds zero delay and sends no transcript anywhere.";
            _aiResult.ForeColor = Theme.Muted;
        }
    }

    private async Task RefreshMicrophonesAsync()
    {
        if (_microphoneCombo == null)
            return;

        string saved = _microphoneCombo.Tag as string ?? (_microphoneCombo.SelectedItem as ComboItem)?.Value ?? "";
        ShowFooter("Refreshing microphones…", Theme.TextSecondary);
        IReadOnlyList<string> devices = await Task.Run(_daemon.ListDevices);
        if (IsDisposed)
            return;

        _loading = true;
        var items = new List<ComboItem> { new("System default", "") };
        foreach (string raw in devices)
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
            ShowFooter(devices.Count == 0 ? "Using the Windows default microphone" : $"Found {devices.Count} microphone input(s)", Theme.Muted);
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
            SetAiResult("Choose Grammar or Prompt before testing.", false);
            return;
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
        if (hotkey.Length == 0)
        {
            ShowPage(DictationPage);
            _hotkeyRecorder.Focus();
            ShowFooter("Choose a global hotkey before saving.", Theme.Err);
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

        SetBusy(true);
        ShowFooter("Saving settings and restarting the local runtime…", Theme.Accent);
        try
        {
            await Task.Run(() =>
            {
                _config.Set("hotkey", "binding", hotkey);
                _config.Set("hotkey", "mode", activation);
                _config.Set("server", "language", language);
                _config.Set("server", "prompt", prompt);
                _config.Set("server", "model", model);
                _config.Set("server", "temperature", temperature);
                _config.Set("server", "hotwords", hotwords);
                _config.Set("voiceprompt", "slovenian_slang", slangProfile);
                _config.Set("voiceprompt", "base_prompt", basePrompt);
                _config.Set("voiceprompt", "base_hotwords", baseHotwords);
                _config.Set("vad", "threshold", threshold);
                _config.Set("vad", "silence_ms", silenceMs);
                _config.Set("vad", "min_speech_ms", minimumSpeechMs);
                _config.Set("vad", "max_speech_s", maximumSpeechSeconds);
                _config.Set("audio", "device", microphone);
                _config.Set("audio", "sample_rate", sampleRate);
                _config.Set("engine", "compute_type", computeType);
                _config.Set("engine", "device", processor);
                _config.Save();
                AiSettingsStore.Save(_paths.AiConfigPath, aiSettings);
                _dictionaryStore.SaveText(corrections);
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
        _diagnosticConfig.Text = File.Exists(_paths.ConfigPath) ? "Loaded · " + _paths.ConfigPath : "Not found";
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
            _historyPreview.Clear();
        _historyStatus.Text = entries.Count == 0
            ? "No transcripts saved yet."
            : $"{entries.Count} local transcript{(entries.Count == 1 ? "" : "s")} available.";
        _historyStatus.ForeColor = entries.Count == 0 ? Theme.Muted : Theme.Ok;
    }

    private void UpdateHistorySelection()
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
        {
            _historyPreview.Clear();
            return;
        }
        _historyPreview.Text = string.IsNullOrWhiteSpace(selected.Entry.OriginalText)
            ? selected.Entry.Text
            : selected.Entry.Text + Environment.NewLine + Environment.NewLine + "Original transcript:" + Environment.NewLine + selected.Entry.OriginalText;
    }

    private void CopySelectedHistory()
    {
        if (_historyList.SelectedItem is not HistoryListItem selected)
        {
            ShowFooter("Select a transcript first.", Theme.Warn);
            return;
        }
        try
        {
            Clipboard.SetText(selected.Entry.Text);
            ShowFooter("Transcript copied to clipboard", Theme.Ok);
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
        _recognitionModelCombo.Text = "Systran/faster-whisper-large-v3";
        _processorChoice.SelectValue("cuda");
        _computeChoice.SelectValue("float16");
        _temperature.Value = 0;
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
            Clipboard.SetText(report);
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
        _performanceRecovery.Text = $"{snapshot.RetryCount} of {snapshot.Count} recordings ({(double)snapshot.RetryCount / snapshot.Count:P0}){speed}";
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

    private string PreferencesPath => Path.Combine(_paths.AppDataDir, "prefs.json");

    private void LoadPreferences()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            if (!File.Exists(PreferencesPath))
                return;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PreferencesPath));
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
        }
        catch
        {
        }
    }

    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            string json = JsonSerializer.Serialize(new
            {
                bounds = new { x = Bounds.X, y = Bounds.Y, w = Bounds.Width, h = Bounds.Height },
                page = _selectedPage,
            });
            File.WriteAllText(PreferencesPath, json);
        }
        catch
        {
        }
    }

    private string StartupShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Voice Typing (faster-whisper-dictation).lnk");

    private void LoadAutoStartState()
    {
        _loading = true;
        _autoStartToggle.Checked = File.Exists(StartupShortcut);
        _loading = false;
    }

    private string? ApplyAutoStart()
    {
        try
        {
            if (_autoStartToggle.Checked && !File.Exists(StartupShortcut))
                CreateShortcut(StartupShortcut, Application.ExecutablePath, "--tray");
            else if (!_autoStartToggle.Checked && File.Exists(StartupShortcut))
                File.Delete(StartupShortcut);
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
        private int _height = 78;

        public SectionBuilder(string title, string subtitle)
        {
            _card = new SurfacePanel { Height = 160 };
            var heading = Theme.Label(title, Theme.Text, 11.5f, FontStyle.Bold, Theme.Surface);
            heading.Location = new Point(24, 18);
            _card.Controls.Add(heading);
            var description = Theme.Label(subtitle, Theme.Muted, 8.65f, FontStyle.Regular, Theme.Surface);
            description.AutoSize = false;
            description.Bounds = new Rectangle(24, 45, 200, 20);
            _card.Controls.Add(description);

            _table = new TableLayoutPanel
            {
                BackColor = Theme.Surface,
                ColumnCount = 2,
                RowCount = 0,
                Location = new Point(24, 76),
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            _card.Controls.Add(_table);
            _card.SizeChanged += (_, _) =>
            {
                description.Width = Math.Max(80, _card.ClientSize.Width - 48);
                _table.Width = Math.Max(300, _card.ClientSize.Width - 48);
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
            _table.Height = _height - 78;
            _card.Height = _height + 18;
            return _card;
        }
    }
}
