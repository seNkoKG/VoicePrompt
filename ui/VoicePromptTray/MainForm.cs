using System.ComponentModel;
using System.Text.Json;

namespace VoicePromptTray;

internal sealed class MainForm : Form
{
    private const int LabelX = 20;
    private const int ControlX = 200;
    private const int SidePad = 20;
    private const int RowAdvance = 46;
    private const int RowAdvanceHint = 65;
    private const int ControlHeight = 32;
    private const int RowsStartY = 54;

    private readonly DaemonManager _daemon;
    private readonly ConfigManager _cfg;
    private readonly AppPaths _paths;

    private Panel _content = null!;
    private HotkeyRecorder _recorder = null!;
    private SegmentedControl _modeSeg = null!;
    private Label _modeHint = null!;
    private CheckBox _autoStart = null!;
    private SegmentedControl _langSeg = null!;
    private Label _languageHint = null!;
    private TextBox _promptBox = null!;
    private SegmentedControl _aiModeSeg = null!;
    private TextBox _aiEndpointBox = null!;
    private TextBox _aiModelBox = null!;
    private NumericUpDown _aiTimeoutNum = null!;
    private TextBox _aiKeyBox = null!;
    private FlatButton _clearAiKeyBtn = null!;
    private FlatButton _testAiBtn = null!;
    private Label _aiStatus = null!;
    private AiSettings _aiSettings = new();
    private NumericUpDown _thresholdNum = null!;
    private NumericUpDown _silenceNum = null!;
    private NumericUpDown _minSpeechNum = null!;
    private NumericUpDown _maxSpeechNum = null!;
    private ComboBox _deviceCombo = null!;
    private SegmentedControl _rateSeg = null!;
    private ComboBox _modelCombo = null!;
    private SegmentedControl _computeSeg = null!;
    private SegmentedControl _gpuSeg = null!;
    private NumericUpDown _tempNum = null!;
    private TextBox _hotwordsBox = null!;
    private FlatButton _startStopBtn = null!;
    private FlatButton _restartBtn = null!;
    private StatusDot _statusDot = null!;
    private Label _statusText = null!;
    private bool _busy;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public event Action? DaemonRestarted;

    public MainForm(DaemonManager daemon, AppPaths paths)
    {
        _daemon = daemon;
        _paths = paths;
        _cfg = new ConfigManager(paths.ConfigPath);

        Text = "Voice Typing — Settings";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 620);
        Size = new Size(780, 840);
        DoubleBuffered = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        BuildContent();
        BuildHeader();
        BuildStatusBar();

        LoadConfig();
        LoadAiSettings();
        LoadPrefs();
        LoadAutoStartState();
        _ = RefreshDevicesAsync();

        UpdateStatus(_daemon.Refresh(true));
    }

    private void BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Theme.Bar,
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var icon = new PictureBox
        {
            Size = new Size(30, 30),
            Location = new Point(18, 17),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        try
        {
            icon.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap();
        }
        catch
        {
        }
        header.Controls.Add(icon);

        var title = Theme.Label("Voice Typing", Theme.Text, 12.5f, true);
        title.BackColor = Theme.Bar;
        title.Location = new Point(58, 13);
        header.Controls.Add(title);

        var subtitle = Theme.Label("faster-whisper-dictation · local GPU", Theme.Muted, 8.25f);
        subtitle.BackColor = Theme.Bar;
        subtitle.Location = new Point(58, 36);
        header.Controls.Add(subtitle);

        var save = new FlatButton("Save & Restart", FlatButton.ButtonStyle.Accent);
        var hide = new FlatButton("Hide to tray", FlatButton.ButtonStyle.Subtle);
        save.Click += async (_, _) => await SaveAsync();
        hide.Click += (_, _) => Hide();
        header.Controls.Add(save);
        header.Controls.Add(hide);

        void PositionButtons()
        {
            save.Location = new Point(header.Width - save.Width - 18, 15);
            hide.Location = new Point(save.Left - hide.Width - 8, 15);
        }
        header.SizeChanged += (_, _) => PositionButtons();
        PositionButtons();

        Controls.Add(header);
    }

    private void BuildStatusBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Theme.Bar,
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };

        _statusDot = new StatusDot { Location = new Point(20, 23) };
        bar.Controls.Add(_statusDot);

        _statusText = Theme.Label("…", Theme.Muted);
        _statusText.BackColor = Theme.Bar;
        _statusText.Location = new Point(40, 20);
        bar.Controls.Add(_statusText);

        _startStopBtn = new FlatButton("Start daemon", FlatButton.ButtonStyle.Subtle);
        _restartBtn = new FlatButton("Restart", FlatButton.ButtonStyle.Subtle);
        _startStopBtn.Click += async (_, _) =>
        {
            await RunBusyAsync(() =>
            {
                if (_daemon.Refresh(true).State == DaemonState.Running)
                    _daemon.Stop();
                else
                    _daemon.Start();
            });
        };
        _restartBtn.Click += async (_, _) =>
        {
            await RunBusyAsync(() =>
            {
                _daemon.Restart();
                DaemonRestarted?.Invoke();
            });
        };
        bar.Controls.Add(_startStopBtn);
        bar.Controls.Add(_restartBtn);

        void PositionButtons()
        {
            _startStopBtn.Location = new Point(bar.Width - _startStopBtn.Width - _restartBtn.Width - 26, 11);
            _restartBtn.Location = new Point(bar.Width - _restartBtn.Width - 18, 11);
        }
        bar.SizeChanged += (_, _) => PositionButtons();
        PositionButtons();

        Controls.Add(bar);
    }

    private sealed class CardBuilder
    {
        private readonly CardPanel _card;
        private int _y = RowsStartY;

        public CardBuilder(string title, int width)
        {
            _card = new CardPanel(title) { Width = width };
        }

        public CardPanel Card => _card;

        private int ControlWidth => _card.Width - ControlX - SidePad;

        public Label Row(string label, Control ctrl, string? hint = null, int ctrlHeight = ControlHeight)
        {
            var lbl = Theme.Label(label);
            int labelY = ctrlHeight > 48 ? _y + 8 : _y + Math.Max(0, (ctrlHeight - lbl.Height) / 2);
            lbl.Location = new Point(LabelX, labelY);
            _card.Controls.Add(lbl);

            ctrl.Bounds = new Rectangle(ControlX, _y, ControlWidth, ctrlHeight);
            ctrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _card.Controls.Add(ctrl);

            var hintLabel = new Label();
            if (hint != null)
            {
                hintLabel = Theme.Label(hint, Theme.Muted, 8.25f);
                hintLabel.AutoSize = false;
                hintLabel.Bounds = new Rectangle(ControlX, _y + ctrlHeight + 3, ControlWidth, 15);
                hintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                hintLabel.AutoEllipsis = true;
                _card.Controls.Add(hintLabel);
                _y += ctrlHeight + 3 + 15 + 14;
            }
            else
            {
                _y += ctrlHeight + 14;
            }
            return hintLabel;
        }

        public void RowPair(string label1, Control ctrl1, string label2, Control ctrl2, int ctrlWidth1 = 88, int ctrlWidth2 = 0)
        {
            int half = (_card.Width - 2 * SidePad - 16) / 2;
            Place(SidePad, label1, ctrl1, half, ctrlWidth1);
            Place(SidePad + half + 16, label2, ctrl2, half, ctrlWidth2);
            _y += ControlHeight + 14;

            void Place(int x, string label, Control ctrl, int halfWidth, int ctrlWidth)
            {
                var lbl = Theme.Label(label);
                lbl.Location = new Point(x, _y + Math.Max(0, (ControlHeight - lbl.Height) / 2));
                _card.Controls.Add(lbl);
                int w = ctrlWidth > 0 ? ctrlWidth : halfWidth - 146;
                ctrl.Bounds = new Rectangle(x + 140, _y, w, ControlHeight);
                _card.Controls.Add(ctrl);
            }
        }

        public CardPanel Done()
        {
            _card.Height = _y + 4;
            return _card;
        }
    }

    private void BuildContent()
    {
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            AutoScroll = true,
        };
        Controls.Add(_content);

        int cardW = Math.Max(600, ClientSize.Width - 40);
        int x = 20;
        int y = 14;
        var cards = new List<CardPanel>();

        CardBuilder NewCard(string title)
        {
            var b = new CardBuilder(title, cardW);
            return b;
        }

        void FinishCard(CardBuilder b)
        {
            var card = b.Done();
            card.Location = new Point(x, y);
            y += card.Height + 12;
            cards.Add(card);
            _content.Controls.Add(card);
        }

        _recorder = new HotkeyRecorder();
        _modeSeg = new SegmentedControl(new[] { "Hold to talk", "Toggle" }, new[] { "hold", "toggle" });
        _modeSeg.SelectedChanged += (_, _) => UpdateModeHint();
        _autoStart = new CheckBox
        {
            Text = "Launch Voice Typing on login",
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            Font = Theme.Font(),
        };

        var hotkey = NewCard("Hotkey");
        hotkey.Row("Global hotkey", _recorder, hint: "Single key (F1, Space, 7) or a combo (Ctrl+Shift+F1, Alt+Space)");
        _modeHint = hotkey.Row("Activation", _modeSeg, hint: "");
        hotkey.Row("Windows startup", _autoStart);
        FinishCard(hotkey);

        _langSeg = new SegmentedControl(
            new[] { "Auto", "Slovenian", "Slovenian slang", "English" },
            new[] { "", "sl", "sl-slang", "en" });
        _langSeg.SelectedChanged += (_, _) => UpdateLanguageHint();
        _promptBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        var promptFrame = new InputFrame(_promptBox, 88, multiline: true);

        var dictation = NewCard("Dictation");
        _languageHint = dictation.Row("Language", _langSeg, hint: "");
        dictation.Row("Prompt", promptFrame, ctrlHeight: 88);
        FinishCard(dictation);

        _aiModeSeg = new SegmentedControl(
            new[] { "Off", "Grammar", "Prompt" },
            new[] { "off", "grammar", "prompt" });
        _aiModeSeg.SelectedChanged += (_, _) => UpdateAiControls();
        _aiEndpointBox = new TextBox();
        var aiEndpointFrame = new InputFrame(_aiEndpointBox);
        _aiModelBox = new TextBox();
        var aiModelFrame = new InputFrame(_aiModelBox);
        _aiTimeoutNum = MakeNum(400, 3000, 100, 0);
        _aiTimeoutNum.Increment = 100;
        _aiKeyBox = new TextBox { UseSystemPasswordChar = true };
        var aiKeyFrame = new InputFrame(_aiKeyBox);
        _clearAiKeyBtn = new FlatButton("Clear", FlatButton.ButtonStyle.Subtle, surface: Theme.Card);
        _testAiBtn = new FlatButton("Test", FlatButton.ButtonStyle.Subtle, surface: Theme.Card);
        _testAiBtn.Click += async (_, _) => await TestAiAsync();
        _clearAiKeyBtn.Click += (_, _) =>
        {
            _aiSettings.ApiKeyProtected = "";
            _aiKeyBox.Clear();
            _aiKeyBox.Modified = true;
            _aiStatus.Text = "API key cleared. Save & Restart to apply.";
            UpdateAiControls();
        };
        _aiKeyBox.TextChanged += (_, _) => UpdateAiControls();
        var aiKeyRow = new Panel { BackColor = Theme.Card, Height = 34 };
        aiKeyRow.Controls.Add(aiKeyFrame);
        aiKeyRow.Controls.Add(_clearAiKeyBtn);
        aiKeyRow.Controls.Add(_testAiBtn);
        aiKeyRow.Layout += (_, _) =>
        {
            _testAiBtn.Bounds = new Rectangle(aiKeyRow.Width - _testAiBtn.Width, 0, _testAiBtn.Width, 34);
            _clearAiKeyBtn.Bounds = new Rectangle(_testAiBtn.Left - _clearAiKeyBtn.Width - 8, 0, _clearAiKeyBtn.Width, 34);
            aiKeyFrame.Bounds = new Rectangle(0, 1, Math.Max(40, _clearAiKeyBtn.Left - 10), 32);
        };

        var ai = NewCard("AI text cleanup");
        ai.Row("Mode", _aiModeSeg, hint: "Off adds no delay. Grammar cleans speech; Prompt structures AI requests.");
        ai.Row("Endpoint", aiEndpointFrame, hint: "OpenAI-compatible chat completions URL, local or cloud");
        ai.RowPair("Model", aiModelFrame, "Max wait (ms)", _aiTimeoutNum, ctrlWidth1: 0, ctrlWidth2: 88);
        _aiStatus = ai.Row("API key", aiKeyRow, hint: "Optional for local providers. Stored with Windows account encryption.", ctrlHeight: 34);
        FinishCard(ai);

        _thresholdNum = MakeNum(0.0m, 1.0m, 0.05m, 2);
        _silenceNum = MakeNum(50, 5000, 50, 0);
        _minSpeechNum = MakeNum(50, 5000, 50, 0);
        _maxSpeechNum = MakeNum(1, 600, 5, 1);

        var vad = NewCard("Voice activity detection");
        vad.RowPair("Threshold", _thresholdNum, "Silence (ms)", _silenceNum);
        vad.RowPair("Min speech (ms)", _minSpeechNum, "Max utterance (s)", _maxSpeechNum);
        FinishCard(vad);

        _deviceCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Theme.StyleCombo(_deviceCombo);
        var deviceRefresh = new FlatButton("⟳", FlatButton.ButtonStyle.Subtle, surface: Theme.Card) { Width = 34 };
        deviceRefresh.Click += async (_, _) => await RefreshDevicesAsync();
        var deviceRow = new Panel { BackColor = Theme.Card, Height = ControlHeight };
        deviceRow.Controls.Add(_deviceCombo);
        deviceRow.Controls.Add(deviceRefresh);
        deviceRow.Layout += (_, _) =>
        {
            deviceRefresh.Bounds = new Rectangle(deviceRow.Width - 34, 0, 34, ControlHeight);
            _deviceCombo.Bounds = new Rectangle(0, 1, deviceRow.Width - 44, ControlHeight - 2);
        };

        _rateSeg = new SegmentedControl(
            new[] { "8000", "16000", "22050", "44100", "48000" },
            new[] { "8000", "16000", "22050", "44100", "48000" });

        var audio = NewCard("Audio input");
        audio.Row("Microphone", deviceRow, hint: "System default uses the Windows default input device");
        audio.Row("Sample rate (Hz)", _rateSeg);
        FinishCard(audio);

        _modelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        Theme.StyleCombo(_modelCombo);
        _modelCombo.Items.AddRange(new object[]
        {
            "Systran/faster-whisper-large-v3",
            "Systran/faster-whisper-large-v3-turbo",
        });
        _computeSeg = new SegmentedControl(new[] { "auto", "float16", "int8" }, new[] { "auto", "float16", "int8" });
        _gpuSeg = new SegmentedControl(new[] { "auto", "cuda", "cpu" }, new[] { "auto", "cuda", "cpu" });
        _tempNum = MakeNum(0.0m, 1.0m, 0.1m, 1);
        _hotwordsBox = new TextBox();
        var hotwordsFrame = new InputFrame(_hotwordsBox);

        var engine = NewCard("Engine & recognition");
        engine.Row("Model", _modelCombo, hint: "large-v3 = best accuracy · large-v3-turbo = faster");
        engine.RowPair("Compute", _computeSeg, "Device", _gpuSeg, ctrlWidth1: 0, ctrlWidth2: 0);
        engine.RowPair("Temperature", _tempNum, "Hotwords", hotwordsFrame, ctrlWidth1: 88, ctrlWidth2: 0);
        FinishCard(engine);

        var note = Theme.Label($"Config: {_paths.ConfigPath}  ·  changes apply on Save & Restart", Theme.Muted, 8.25f);
        note.BackColor = Theme.Bg;
        note.Location = new Point(x, y + 2);
        _content.Controls.Add(note);

        Resize += (_, _) =>
        {
            int w = Math.Max(600, ClientSize.Width - 40);
            foreach (var card in cards)
                card.Width = w;
        };
    }

    private static NumericUpDown MakeNum(decimal min, decimal max, decimal inc, int decimals)
    {
        var n = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = inc,
            DecimalPlaces = decimals,
        };
        Theme.StyleNumeric(n);
        return n;
    }

    private void UpdateModeHint()
    {
        if (_modeHint == null)
            return;
        _modeHint.Text = _modeSeg.SelectedValue == "hold"
            ? "Hold the hotkey while speaking — release to transcribe"
            : "Press once to start recording, press again to stop";
    }

    private void UpdateLanguageHint()
    {
        if (_languageHint == null)
            return;
        _languageHint.Text = _langSeg.SelectedValue switch
        {
            "sl-slang" => "Keeps English automatic; retries other-language mistakes as colloquial Slovenian",
            "sl" => "Pins standard Slovenian so short phrases are not mistaken for another language",
            "en" => "Pins English for consistent English-only dictation",
            _ => "Detects the language per utterance; best when switching between Slovenian and English",
        };
    }

    private void LoadAiSettings()
    {
        _aiSettings = AiSettingsStore.Load(_paths.AiConfigPath);
        _aiModeSeg.SelectValue(_aiSettings.Mode);
        _aiEndpointBox.Text = _aiSettings.Endpoint;
        _aiModelBox.Text = _aiSettings.Model;
        _aiTimeoutNum.Value = Clamp(_aiSettings.TimeoutMs, _aiTimeoutNum);
        ResetAiKeyBox();
        UpdateAiControls();
    }

    private void ResetAiKeyBox()
    {
        _aiKeyBox.Clear();
        _aiKeyBox.PlaceholderText = string.IsNullOrEmpty(_aiSettings.ApiKeyProtected)
            ? "Optional"
            : "Saved securely (leave blank to keep)";
        _aiKeyBox.Modified = false;
    }

    private void UpdateAiControls()
    {
        if (_aiModeSeg == null)
            return;
        bool enabled = _aiModeSeg.SelectedValue != "off";
        _aiEndpointBox.Enabled = enabled;
        _aiModelBox.Enabled = enabled;
        _aiTimeoutNum.Enabled = enabled;
        _aiKeyBox.Enabled = enabled;
        _clearAiKeyBtn.Enabled = enabled && !_busy &&
            (!string.IsNullOrEmpty(_aiSettings.ApiKeyProtected) || _aiKeyBox.TextLength > 0);
        _testAiBtn.Enabled = enabled && !_busy;
    }

    private AiSettings BuildAiSettings()
    {
        string protectedKey = _aiSettings.ApiKeyProtected;
        if (_aiKeyBox.Modified)
        {
            string apiKey = _aiKeyBox.Text.Trim();
            protectedKey = apiKey == "" ? "" : AiSettingsStore.ProtectApiKey(apiKey);
        }

        return new AiSettings
        {
            Mode = _aiModeSeg.SelectedValue,
            Endpoint = _aiEndpointBox.Text.Trim(),
            Model = _aiModelBox.Text.Trim(),
            TimeoutMs = (int)_aiTimeoutNum.Value,
            ApiKeyProtected = protectedKey,
        };
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
            _aiStatus.Text = "Could not protect API key: " + ex.Message;
            return;
        }
        string? validation = AiSettingsStore.Validate(settings);
        if (validation != null)
        {
            _aiStatus.Text = validation;
            return;
        }
        if (settings.Mode == "off")
        {
            _aiStatus.Text = "Choose Grammar or Prompt before testing.";
            return;
        }

        _busy = true;
        SetBusy(true);
        _aiStatus.Text = "Testing provider...";
        string testConfigPath = _paths.AiConfigPath + ".test";
        try
        {
            AiSettingsStore.Save(testConfigPath, settings);
            AiTestResult result = await Task.Run(() => _daemon.TestAi(testConfigPath));
            _aiStatus.Text = result.Ok
                ? $"Ready in {result.LatencyMs} ms · Save & Restart to enable"
                : "Original text fallback · " + ShortMessage(result.Error);
        }
        catch (Exception ex)
        {
            _aiStatus.Text = "Test failed · " + ShortMessage(ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(testConfigPath);
                File.Delete(testConfigPath + ".tmp");
            }
            catch
            {
            }
            _busy = false;
            SetBusy(false);
        }
    }

    private static string ShortMessage(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 100 ? oneLine : oneLine[..97] + "...";
    }

    private void LoadConfig()
    {
        _recorder.Binding = _cfg.GetString("hotkey", "binding") ?? "f1";
        _modeSeg.SelectValue(_cfg.GetString("hotkey", "mode") ?? "hold");
        UpdateModeHint();

        string savedLanguage = _cfg.GetString("server", "language") ?? "";
        bool slangProfile = savedLanguage == "sl-slang";
        _langSeg.SelectValue(slangProfile ? "sl-slang" : savedLanguage);
        UpdateLanguageHint();
        _promptBox.Text = slangProfile
            ? _cfg.GetString("voiceprompt", "base_prompt") ?? _cfg.GetString("server", "prompt") ?? ""
            : _cfg.GetString("server", "prompt") ?? "";
        _modelCombo.Text = _cfg.GetString("server", "model") ?? "";
        _tempNum.Value = Clamp((decimal)(_cfg.GetDouble("server", "temperature") ?? 0.0), _tempNum);
        _hotwordsBox.Text = slangProfile
            ? _cfg.GetString("voiceprompt", "base_hotwords") ?? _cfg.GetString("server", "hotwords") ?? ""
            : _cfg.GetString("server", "hotwords") ?? "";

        _thresholdNum.Value = Clamp((decimal)(_cfg.GetDouble("vad", "threshold") ?? 0.6), _thresholdNum);
        _silenceNum.Value = Clamp((decimal)(_cfg.GetInt("vad", "silence_ms") ?? 250), _silenceNum);
        _minSpeechNum.Value = Clamp((decimal)(_cfg.GetInt("vad", "min_speech_ms") ?? 250), _minSpeechNum);
        _maxSpeechNum.Value = Clamp((decimal)(_cfg.GetDouble("vad", "max_speech_s") ?? 90.0), _maxSpeechNum);

        string device = _cfg.GetString("audio", "device") ?? "";
        _deviceCombo.Items.Clear();
        _deviceCombo.Items.Add(new ComboItem("System default", ""));
        _deviceCombo.SelectedIndex = 0;
        _deviceCombo.Tag = device;

        _rateSeg.SelectValue((_cfg.GetInt("audio", "sample_rate") ?? 16000).ToString());
        _computeSeg.SelectValue(_cfg.GetString("engine", "compute_type") ?? "float16");
        _gpuSeg.SelectValue(_cfg.GetString("engine", "device") ?? "auto");
    }

    private static decimal Clamp(decimal v, NumericUpDown n) =>
        Math.Clamp(v, n.Minimum, n.Maximum);

    private async Task RefreshDevicesAsync()
    {
        var devices = await Task.Run(_daemon.ListDevices);
        if (IsDisposed)
            return;

        string saved = _deviceCombo.Tag as string ?? "";
        var items = new List<ComboItem> { new("System default", "") };
        foreach (var d in devices)
        {
            int idx = d.IndexOf(':');
            string name = d[(idx + 1)..].Trim();
            items.Add(new ComboItem(d, name));
        }

        int select = 0;
        for (int i = 1; i < items.Count; i++)
        {
            if (saved != "" && (items[i].Value == saved || items[i].Label.Contains(saved, StringComparison.OrdinalIgnoreCase)))
            {
                select = i;
                break;
            }
        }

        _deviceCombo.Items.Clear();
        foreach (var it in items)
            _deviceCombo.Items.Add(it);
        _deviceCombo.SelectedIndex = select;
    }

    private async Task SaveAsync()
    {
        if (_busy)
            return;

        string binding = _recorder.Binding.Trim();
        if (binding == "")
        {
            MessageBox.Show(this, "Record a hotkey first — click the hotkey box and press a key or combination.", "Voice Typing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AiSettings aiSettings;
        try
        {
            aiSettings = BuildAiSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not protect the AI API key:\n" + ex.Message, "Voice Typing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string? aiValidation = AiSettingsStore.Validate(aiSettings);
        if (aiValidation != null)
        {
            MessageBox.Show(this, aiValidation, "Voice Typing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string hotkeyMode = _modeSeg.SelectedValue;
        string languageSelection = _langSeg.SelectedValue;
        bool slovenianSlang = languageSelection == "sl-slang";
        string language = languageSelection;
        string basePrompt = _promptBox.Text.Trim();
        string prompt = slovenianSlang ? SlovenianSlangProfile.ApplyPrompt(basePrompt) : basePrompt;
        string model = _modelCombo.Text.Trim();
        double temperature = (double)_tempNum.Value;
        string baseHotwords = _hotwordsBox.Text.Trim();
        string hotwords = slovenianSlang ? SlovenianSlangProfile.ApplyHotwords(baseHotwords) : baseHotwords;
        double threshold = (double)_thresholdNum.Value;
        int silenceMs = (int)_silenceNum.Value;
        int minSpeechMs = (int)_minSpeechNum.Value;
        double maxSpeechSeconds = (double)_maxSpeechNum.Value;
        string audioDevice = (_deviceCombo.SelectedItem as ComboItem)?.Value ?? "";
        int sampleRate = int.Parse(_rateSeg.SelectedValue, System.Globalization.CultureInfo.InvariantCulture);
        string computeType = _computeSeg.SelectedValue;
        string engineDevice = _gpuSeg.SelectedValue;

        _busy = true;
        SetBusy(true);
        try
        {
            await Task.Run(() =>
            {
                _cfg.Set("hotkey", "binding", binding);
                _cfg.Set("hotkey", "mode", hotkeyMode);

                _cfg.Set("server", "language", language);
                _cfg.Set("server", "prompt", prompt);
                _cfg.Set("server", "model", model);
                _cfg.Set("server", "temperature", temperature);
                _cfg.Set("server", "hotwords", hotwords);
                _cfg.Set("voiceprompt", "slovenian_slang", slovenianSlang);
                _cfg.Set("voiceprompt", "base_prompt", basePrompt);
                _cfg.Set("voiceprompt", "base_hotwords", baseHotwords);

                _cfg.Set("vad", "threshold", threshold);
                _cfg.Set("vad", "silence_ms", silenceMs);
                _cfg.Set("vad", "min_speech_ms", minSpeechMs);
                _cfg.Set("vad", "max_speech_s", maxSpeechSeconds);

                _cfg.Set("audio", "device", audioDevice);
                _cfg.Set("audio", "sample_rate", sampleRate);

                _cfg.Set("engine", "compute_type", computeType);
                _cfg.Set("engine", "device", engineDevice);

                _cfg.Save();
                AiSettingsStore.Save(_paths.AiConfigPath, aiSettings);
                _daemon.Restart();
            });

            _aiSettings = aiSettings;
            ResetAiKeyBox();
            ApplyAutoStart();
            SavePrefs();
            DaemonRestarted?.Invoke();
            UpdateStatus(_daemon.Refresh(true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to apply settings:\n" + ex.Message, "Voice Typing", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private async Task RunBusyAsync(Action action)
    {
        if (_busy)
            return;
        _busy = true;
        SetBusy(true);
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Voice Typing", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
            UpdateStatus(_daemon.Refresh(true));
        }
    }

    private void SetBusy(bool busy)
    {
        _startStopBtn.Enabled = !busy;
        _restartBtn.Enabled = !busy && _daemon.Info.State == DaemonState.Running;
        UpdateAiControls();
        UseWaitCursor = busy;
    }

    public void UpdateStatus(DaemonInfo info)
    {
        _statusDot.DotColor = info.State switch
        {
            DaemonState.Running => Theme.Ok,
            DaemonState.Stopped => Theme.Warn,
            _ => Theme.Muted,
        };

        string mode = info.Mode ?? "hold";
        string modeLabel = mode == "hold" ? "hold to talk" : "toggle";
        _statusText.Text = info.State switch
        {
            DaemonState.Running => $"Daemon running · PID {info.Pid} · hotkey {info.Hotkey} ({modeLabel})",
            DaemonState.Stopped => "Daemon stopped — press Start daemon or Save & Restart",
            _ => "Daemon status unknown",
        };

        _startStopBtn.Text = info.State == DaemonState.Running ? "Stop daemon" : "Start daemon";
        _restartBtn.Enabled = info.State == DaemonState.Running && !_busy;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SavePrefs();
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private string PrefsPath => Path.Combine(_paths.AppDataDir, "prefs.json");

    private void LoadPrefs()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            if (!File.Exists(PrefsPath))
                return;
            using var doc = JsonDocument.Parse(File.ReadAllText(PrefsPath));
            if (doc.RootElement.TryGetProperty("bounds", out var b) &&
                b.TryGetProperty("x", out var bx) &&
                b.TryGetProperty("y", out var by) &&
                b.TryGetProperty("w", out var bw) &&
                b.TryGetProperty("h", out var bh))
            {
                var rect = new Rectangle(bx.GetInt32(), by.GetInt32(), bw.GetInt32(), bh.GetInt32());
                if (rect.Width >= MinimumSize.Width && rect.Height >= MinimumSize.Height &&
                    Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
                    Bounds = rect;
            }
        }
        catch
        {
        }
    }

    private void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            var payload = JsonSerializer.Serialize(new
            {
                bounds = new
                {
                    x = Bounds.X,
                    y = Bounds.Y,
                    w = Bounds.Width,
                    h = Bounds.Height,
                },
            });
            File.WriteAllText(PrefsPath, payload);
        }
        catch
        {
        }
    }

    private string StartupLnk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Voice Typing (faster-whisper-dictation).lnk");

    private void LoadAutoStartState() => _autoStart.Checked = File.Exists(StartupLnk);

    private void ApplyAutoStart()
    {
        try
        {
            if (_autoStart.Checked && !File.Exists(StartupLnk))
            {
                CreateShortcut(StartupLnk, Application.ExecutablePath, "--tray");
            }
            else if (!_autoStart.Checked && File.Exists(StartupLnk))
            {
                File.Delete(StartupLnk);
            }
        }
        catch
        {
        }
    }

    private static void CreateShortcut(string lnkPath, string target, string args)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = target;
        lnk.Arguments = args;
        lnk.WorkingDirectory = Path.GetDirectoryName(target)!;
        lnk.Save();
    }

    private sealed record ComboItem(string Label, string Value)
    {
        public override string ToString() => Label;
    }
}
