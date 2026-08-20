using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Text;
using VoicePromptTray;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
ApplicationConfiguration.Initialize();
Application.SetColorMode(SystemColorMode.Dark);

var paths = AppPaths.Default;
string preferencesPath = Path.Combine(paths.AppDataDir, "prefs.json");
byte[]? preferencesBackup = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
using var form = new MainForm(new DaemonManager(paths), paths)
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
};

form.AllowClose = true;
form.Show();
Application.DoEvents();
Thread.Sleep(350);
Application.DoEvents();

var failures = new StringBuilder();
int layoutFailures = 0;
int behaviorFailures = 0;
var pages = new[] { "overview", "dictation", "audio", "intelligence", "history", "advanced" };
var screenshotNames = new Dictionary<string, string>
{
    ["overview"] = "voiceprompt_ui.png",
    ["dictation"] = "voiceprompt_ui_dictation.png",
    ["audio"] = "voiceprompt_ui_audio.png",
    ["intelligence"] = "voiceprompt_ai_settings.png",
    ["history"] = "voiceprompt_ui_history.png",
    ["advanced"] = "voiceprompt_ui_advanced.png",
};

static string Describe(Control control)
{
    string text = (control.Text ?? "").ReplaceLineEndings(" ").Trim();
    if (text.Length > 32)
        text = text[..32] + "...";
    return $"{control.GetType().Name} \"{text}\" {control.Bounds}";
}

void CheckTree(Control parent, string context)
{
    foreach (Control child in parent.Controls)
    {
        if (!child.Visible)
            continue;

        if (child.Width <= 0 || child.Height <= 0)
        {
            layoutFailures++;
            failures.AppendLine($"LAYOUT {context}: zero-sized {Describe(child)}");
        }

        bool namedInteractive = child is ActionButton or NavigationButton or ChoiceStrip or ToggleSwitch or
            HotkeyRecorder or ComboBox or TextBox or NumericUpDown;
        if (parent is NumericUpDown)
            namedInteractive = false;
        if (namedInteractive && child.TabStop && string.IsNullOrWhiteSpace(child.AccessibleName))
        {
            behaviorFailures++;
            failures.AppendLine($"ACCESSIBILITY {context}: unnamed {Describe(child)}");
        }

        bool parentScrolls = parent is ScrollableControl scrollable && scrollable.AutoScroll;
        bool positionedByLayout = parent is TableLayoutPanel or FlowLayoutPanel;
        if (!parentScrolls && !positionedByLayout && child.Dock == DockStyle.None && parent is not Form)
        {
            var tolerance = new Rectangle(-2, -2, parent.ClientSize.Width + 4, parent.ClientSize.Height + 4);
            if (!tolerance.Contains(child.Bounds))
            {
                layoutFailures++;
                failures.AppendLine($"LAYOUT {context}: outside {parent.GetType().Name} {parent.ClientSize}: {Describe(child)}");
            }
        }

        CheckTree(child, context);
    }
}

void SaveClientScreenshot(string path)
{
    using var window = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(window, new Rectangle(Point.Empty, form.Size));
    Rectangle clientOnScreen = form.RectangleToScreen(form.ClientRectangle);
    int left = Math.Clamp(clientOnScreen.Left - form.Left, 0, Math.Max(0, window.Width - 1));
    int top = Math.Clamp(clientOnScreen.Top - form.Top, 0, Math.Max(0, window.Height - 1));
    var clientInWindow = new Rectangle(
        left,
        top,
        Math.Max(1, Math.Min(form.ClientSize.Width, window.Width - left)),
        Math.Max(1, Math.Min(form.ClientSize.Height, window.Height - top)));
    using var client = new Bitmap(clientInWindow.Width, clientInWindow.Height);
    using (Graphics graphics = Graphics.FromImage(client))
    {
        graphics.DrawImage(
            window,
            new Rectangle(Point.Empty, client.Size),
            clientInWindow,
            GraphicsUnit.Pixel);
    }
    client.Save(path);
}

void RenderPage(string page, Size size, bool saveScreenshot)
{
    form.ClientSize = size;
    form.ShowPageForDiagnostics(page);
    Application.DoEvents();
    Thread.Sleep(80);
    Application.DoEvents();

    if (form.SelectedPage != page)
    {
        behaviorFailures++;
        failures.AppendLine($"BEHAVIOR requested page '{page}', selected '{form.SelectedPage}'");
    }

    CheckTree(form, $"{page}@{size.Width}x{size.Height}");

    if (!saveScreenshot)
        return;

    string path = Path.Combine(Path.GetTempPath(), screenshotNames[page]);
    SaveClientScreenshot(path);
}

foreach (string page in pages)
    RenderPage(page, new Size(1080, 780), saveScreenshot: true);

form.ShowPageForDiagnostics("history");
Application.DoEvents();
var historyResultPreview = (TextBox)typeof(MainForm)
    .GetField("_historyResultPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var historyOriginalPreview = (TextBox)typeof(MainForm)
    .GetField("_historyOriginalPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var historyCopyOriginal = (ActionButton)typeof(MainForm)
    .GetField("_historyCopyOriginalButton", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
if (historyResultPreview.Bounds == historyOriginalPreview.Bounds ||
    historyResultPreview.Width < 100 || historyOriginalPreview.Width < 100 ||
    string.IsNullOrWhiteSpace(historyResultPreview.AccessibleName) ||
    string.IsNullOrWhiteSpace(historyOriginalPreview.AccessibleName) ||
    historyCopyOriginal.Parent is null)
{
    layoutFailures++;
    failures.AppendLine("LAYOUT Recovery comparison is missing, overlapping, or inaccessible");
}
var pageMap = (Dictionary<string, Panel>)typeof(MainForm)
    .GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
pageMap["history"].AutoScrollPosition = new Point(0, 300);
Application.DoEvents();
string historyComparisonPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_history_comparison.png");
SaveClientScreenshot(historyComparisonPath);

form.ClientSize = new Size(1080, 780);
form.ShowPageForDiagnostics("advanced");
Application.DoEvents();
var updateButton = (ActionButton)typeof(MainForm)
    .GetField("_updateButton", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
if (!updateButton.Visible || updateButton.Parent is null ||
    !updateButton.Parent.ClientRectangle.Contains(updateButton.Bounds) ||
    updateButton.Parent.Controls.GetChildIndex(updateButton) != 0)
{
    layoutFailures++;
    failures.AppendLine("LAYOUT update action is hidden or covered in its shared row");
}
string originalUpdateButtonText = updateButton.Text;
updateButton.Text = "Download & install";
Size updateTextSize = TextRenderer.MeasureText(updateButton.Text, updateButton.Font);
if (updateTextSize.Width + 24 > updateButton.ClientSize.Width)
{
    layoutFailures++;
    failures.AppendLine("LAYOUT verified update action text is clipped");
}
updateButton.Text = originalUpdateButtonText;
var updateChannel = (ChoiceStrip)typeof(MainForm)
    .GetField("_updateChannelChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var updateStatus = (Label)typeof(MainForm)
    .GetField("_updateStatus", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalUpdateChannel = updateChannel.SelectedValue;
updateChannel.SelectValue(originalUpdateChannel == "stable" ? "preview" : "stable");
Application.DoEvents();
if (form.HasUnsavedChanges || !updateStatus.Text.Contains(
        updateChannel.SelectedValue == "preview" ? "Preview" : "Stable",
        StringComparison.Ordinal))
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR update channel did not change independently of runtime settings");
}
updateChannel.SelectValue(originalUpdateChannel);
Application.DoEvents();
var themePicker = (ThemePicker)typeof(MainForm)
    .GetField("_themePicker", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalTheme = themePicker.SelectedValue;
string alternateTheme = originalTheme == "evergreen" ? "ember" : "evergreen";
themePicker.SelectValue(alternateTheme);
Application.DoEvents();
if (Theme.Current.Id != alternateTheme || form.BackColor != Theme.Canvas || form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR interface theme did not apply instantly and independently");
}
themePicker.SelectValue(originalTheme);
Application.DoEvents();
pageMap["advanced"].AutoScrollPosition = new Point(0, 460);
Application.DoEvents();
string advancedToolsPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_advanced_tools.png");
SaveClientScreenshot(advancedToolsPath);
pageMap["advanced"].AutoScrollPosition = new Point(0, 600);
Application.DoEvents();
string applicationUpdatesPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_application_updates.png");
SaveClientScreenshot(applicationUpdatesPath);
pageMap["advanced"].AutoScrollPosition = new Point(0, 720);
Application.DoEvents();
string dataPortabilityPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_data_portability.png");
SaveClientScreenshot(dataPortabilityPath);

form.ShowPageForDiagnostics("intelligence");
pageMap["intelligence"].AutoScrollPosition = new Point(0, 430);
Application.DoEvents();
string writingModesPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_writing_modes.png");
SaveClientScreenshot(writingModesPath);
pageMap["intelligence"].AutoScrollPosition = new Point(0, 800);
Application.DoEvents();
string applicationProfilesPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_application_profiles.png");
SaveClientScreenshot(applicationProfilesPath);

foreach (string page in pages)
    RenderPage(page, new Size(900, 650), saveScreenshot: false);

form.UpdateStatus(new DaemonInfo
{
    State = DaemonState.Running,
    Pid = 4242,
    Hotkey = "f1",
    Mode = "hold",
    Engine = "faster-whisper",
});
form.ShowPageForDiagnostics("overview");
Application.DoEvents();
CheckTree(form, "overview-running");

if (form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR opening and navigating settings marked the form as modified");
}

var overlayStylePicker = (OverlayStylePicker)typeof(MainForm)
    .GetField("_overlayStylePicker", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalOverlayStyle = overlayStylePicker.SelectedValue;
overlayStylePicker.SelectValue(originalOverlayStyle == RecordingOverlay.BarsStyle
    ? RecordingOverlay.WaveStyle
    : RecordingOverlay.BarsStyle);
Application.DoEvents();
if (form.HasUnsavedChanges || form.OverlayStyle == originalOverlayStyle)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR overlay style did not apply instantly as a local preference");
}
overlayStylePicker.SelectValue(originalOverlayStyle);
Application.DoEvents();

var thresholdField = typeof(MainForm).GetField("_threshold", BindingFlags.Instance | BindingFlags.NonPublic)!;
var threshold = (NumericUpDown)thresholdField.GetValue(form)!;
decimal originalThreshold = threshold.Value;
threshold.Value = originalThreshold == threshold.Maximum
    ? originalThreshold - threshold.Increment
    : originalThreshold + threshold.Increment;
Application.DoEvents();
if (!form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR changing a setting did not enable the unsaved state");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();

if (form.HasUnsavedChanges || threshold.Value != originalThreshold)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore the saved settings state");
}

var outputChoice = (ChoiceStrip)typeof(MainForm)
    .GetField("_outputChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var outputHint = (Label)typeof(MainForm)
    .GetField("_outputHint", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalOutput = outputChoice.SelectedValue;
outputChoice.SelectValue(originalOutput == "clipboard" ? "paste" : "clipboard");
Application.DoEvents();
if (!form.HasUnsavedChanges ||
    (outputChoice.SelectedValue == "clipboard" && !outputHint.Text.Contains("no paste keystroke", StringComparison.Ordinal)))
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR output mode did not update its unsaved state and delivery guidance");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || outputChoice.SelectedValue != originalOutput)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore the transcript output mode");
}

var voiceCommands = (ToggleSwitch)typeof(MainForm)
    .GetField("_voiceCommandsToggle", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
bool originalVoiceCommands = voiceCommands.Checked;
voiceCommands.Checked = !originalVoiceCommands;
Application.DoEvents();
if (!form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR voice commands did not update the unsaved state");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || voiceCommands.Checked != originalVoiceCommands)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore the voice-command setting");
}

var snippetsText = (TextBox)typeof(MainForm)
    .GetField("_snippetsText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalSnippets = snippetsText.Text;
snippetsText.Text = originalSnippets + (originalSnippets.Length == 0 ? "" : Environment.NewLine) + "test => Verified";
Application.DoEvents();
if (!form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR editing snippets did not update the unsaved state");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || snippetsText.Text != originalSnippets)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore text snippets");
}

var aiModeChoice = (ChoiceStrip)typeof(MainForm)
    .GetField("_aiModeChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var aiEndpoint = (TextBox)typeof(MainForm)
    .GetField("_aiEndpointText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalAiMode = aiModeChoice.SelectedValue;
aiModeChoice.SelectValue("clean");
Application.DoEvents();
if (!form.HasUnsavedChanges || !aiEndpoint.Enabled)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Clean writing mode did not enable provider settings and unsaved state");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || aiModeChoice.SelectedValue != originalAiMode)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore the writing mode");
}

var appProfilesText = (TextBox)typeof(MainForm)
    .GetField("_appProfilesText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var runningAppCombo = (ComboBox)typeof(MainForm)
    .GetField("_runningAppCombo", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
if (runningAppCombo.Items.Count == 0 || string.IsNullOrWhiteSpace(runningAppCombo.AccessibleName))
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR running-application profile picker is empty or inaccessible");
}
string originalAppProfiles = appProfilesText.Text;
aiModeChoice.SelectValue("off");
appProfilesText.Text = "Code.exe => prompt, inherit";
Application.DoEvents();
if (!form.HasUnsavedChanges || !aiEndpoint.Enabled)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR AI application profile did not enable provider settings and unsaved state");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || appProfilesText.Text != originalAppProfiles || aiModeChoice.SelectedValue != originalAiMode)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore application profiles");
}

var recognitionEngine = (ChoiceStrip)typeof(MainForm)
    .GetField("_recognitionEngineChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var recognitionServerUrl = (TextBox)typeof(MainForm)
    .GetField("_recognitionServerUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var recognitionServerTimeout = (NumericUpDown)typeof(MainForm)
    .GetField("_recognitionServerTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var processorChoice = (ChoiceStrip)typeof(MainForm)
    .GetField("_processorChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var bufferedTranscription = (ToggleSwitch)typeof(MainForm)
    .GetField("_bufferedTranscriptionToggle", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var recognitionServerResult = (Label)typeof(MainForm)
    .GetField("_recognitionServerResult", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
string originalRecognitionEngine = recognitionEngine.SelectedValue;
recognitionEngine.SelectValue("server");
recognitionServerUrl.Text = "http://speech.example.test";
recognitionServerTimeout.Value = 90;
Application.DoEvents();
if (!form.HasUnsavedChanges || !recognitionServerUrl.Enabled || !recognitionServerTimeout.Enabled ||
    processorChoice.Enabled || bufferedTranscription.Enabled ||
    !recognitionServerResult.Text.StartsWith("Warning ·", StringComparison.Ordinal))
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR recognition server mode did not expose safe remote controls and privacy guidance");
}
form.ShowPageForDiagnostics("intelligence");
pageMap["intelligence"].AutoScrollPosition = Point.Empty;
Application.DoEvents();
string recognitionServerPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_recognition_server.png");
SaveClientScreenshot(recognitionServerPath);
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();
if (form.HasUnsavedChanges || recognitionEngine.SelectedValue != originalRecognitionEngine)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR Discard did not restore the recognition engine");
}

var importedBackup = VoicePromptTray.AppBackupStore.Deserialize(
    VoicePromptTray.AppBackupStore.Serialize(new VoicePromptBackupDocument
    {
        Dictation = new BackupDictationSettings
        {
            Hotkey = "ctrl+shift+f2",
            Activation = "toggle",
            OutputMode = "clipboard",
            VoiceCommands = true,
            Language = "en",
            Prompt = "Product: VoicePrompt",
            Hotwords = "Codex",
        },
        Recognition = new BackupRecognitionSettings
        {
            EngineType = "server",
            ServerUrl = "https://speech.example.test",
            ServerTimeoutSeconds = 75,
        },
        Audio = new BackupAudioSettings(),
        Writing = new BackupWritingSettings { Mode = "clean" },
        Recovery = new BackupRecoverySettings { Limit = 30 },
        Corrections = [new CorrectionEntry("codecs", "Codex")],
        Snippets = [new TextSnippetEntry("reply", "Thank you")],
        AppProfiles = [new AppProfileEntry("Code.exe", "prompt", "inherit")],
    }));
typeof(MainForm).GetMethod("ApplyAppBackup", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, new object[] { importedBackup });
Application.DoEvents();
string backupLanguage = (string)typeof(MainForm)
    .GetMethod("SelectedLanguageCode", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>())!;
if (!form.HasUnsavedChanges || backupLanguage != "en" || outputChoice.SelectedValue != "clipboard" ||
    !voiceCommands.Checked || aiModeChoice.SelectedValue != "clean" || !snippetsText.Text.Contains("reply => Thank you") ||
    !appProfilesText.Text.Contains("Code.exe => prompt, inherit") || recognitionEngine.SelectedValue != "server" ||
    recognitionServerUrl.Text != "https://speech.example.test" || recognitionServerTimeout.Value != 75)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR imported settings backup did not populate reviewable portable fields");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();

var languageChoice = (ChoiceStrip)typeof(MainForm)
    .GetField("_languageChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var additionalLanguage = (ComboBox)typeof(MainForm)
    .GetField("_additionalLanguageCombo", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
languageChoice.SelectValue("other");
additionalLanguage.SelectedItem = additionalLanguage.Items.Cast<object>()
    .Single(item => item.ToString() == "German (de)");
string selectedLanguage = (string)typeof(MainForm)
    .GetMethod("SelectedLanguageCode", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>())!;
if (!additionalLanguage.Enabled || selectedLanguage != "de")
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR additional language profile did not resolve to pinned German");
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();

var importedProfile = LanguageProfileStore.Create(
    "fr",
    "Noms propres: Élodie",
    "Codex, VoicePrompt",
    "codecs => Codex");
typeof(MainForm).GetMethod("ApplyLanguageProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, new object[] { importedProfile });
Application.DoEvents();
string importedLanguage = (string)typeof(MainForm)
    .GetMethod("SelectedLanguageCode", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>())!;
var promptText = (TextBox)typeof(MainForm)
    .GetField("_promptText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var hotwordsText = (TextBox)typeof(MainForm)
    .GetField("_hotwordsText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var correctionsText = (TextBox)typeof(MainForm)
    .GetField("_correctionsText", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
if (importedLanguage != "fr" || !promptText.Text.Contains("Élodie") ||
    !hotwordsText.Text.Contains("VoicePrompt") || !correctionsText.Text.Contains("codecs => Codex") ||
    !form.HasUnsavedChanges)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR imported language profile did not populate reviewable unsaved fields");
}
form.ShowPageForDiagnostics("dictation");
pageMap["dictation"].AutoScrollPosition = new Point(0, 560);
Application.DoEvents();
string languageProfilePath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_language_profile.png");
SaveClientScreenshot(languageProfilePath);
pageMap["dictation"].AutoScrollPosition = new Point(0, 980);
Application.DoEvents();
string snippetsPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_snippets.png");
SaveClientScreenshot(snippetsPath);
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();

string overlayPath = Path.Combine(Path.GetTempPath(), "voiceprompt_overlay.png");
var overlayPaths = new Dictionary<string, string>();
long overlayActivationMs;
using (var overlay = new RecordingOverlay
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
    Opacity = 0.96,
})
{
    var overlayTimer = (System.Windows.Forms.Timer)typeof(RecordingOverlay)
        .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(overlay)!;
    if (overlayTimer.Interval != 50)
    {
        behaviorFailures++;
        failures.AppendLine($"PERFORMANCE hidden overlay polls every {overlayTimer.Interval}ms instead of 50ms");
    }
    using (var map = MemoryMappedFile.CreateOrOpen(AudioMeterReader.MapName, AudioMeterReader.MapSize))
    using (var view = map.CreateViewAccessor())
    {
        int sequence = (view.ReadInt32(0) & 0x7FFFFFFE) + 2;
        view.Write(0, sequence | 1);
        view.Write(4, 1);
        view.Write(8, 0.5f);
        view.Write(12, Environment.ProcessId);
        view.Write(0, sequence);
    }
    var activationTimer = System.Diagnostics.Stopwatch.StartNew();
    var updateMeter = typeof(RecordingOverlay).GetMethod("UpdateMeter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    updateMeter.Invoke(overlay, Array.Empty<object>());
    Application.DoEvents();
    overlayActivationMs = activationTimer.ElapsedMilliseconds;
    // Hosted Windows runners can add substantial compositor scheduling jitter.
    // Keep this strict enough to catch the old multi-second cold-start regression.
    if (!overlay.Visible || overlay.Opacity < 0.90 || overlayActivationMs > 250)
    {
        behaviorFailures++;
        failures.AppendLine($"BEHAVIOR overlay cold activation took {overlayActivationMs}ms at opacity {overlay.Opacity:0.00}");
    }
    if (overlayTimer.Interval != 25)
    {
        behaviorFailures++;
        failures.AppendLine($"PERFORMANCE active overlay animates every {overlayTimer.Interval}ms instead of 25ms");
    }
    if (overlay.Width > 190)
    {
        layoutFailures++;
        failures.AppendLine($"LAYOUT overlay is too wide at {overlay.Width}px");
    }

    using (var map = MemoryMappedFile.CreateOrOpen(AudioMeterReader.MapName, AudioMeterReader.MapSize))
    using (var view = map.CreateViewAccessor())
    {
        int sequence = (view.ReadInt32(0) & 0x7FFFFFFE) + 2;
        view.Write(0, sequence | 1);
    }
    updateMeter.Invoke(overlay, Array.Empty<object>());
    Application.DoEvents();
    if (!overlay.Visible)
    {
        behaviorFailures++;
        failures.AppendLine("BEHAVIOR transient meter write hid the active recording overlay");
    }

    var waveform = (float[])typeof(RecordingOverlay)
        .GetField("_waveform", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(overlay)!;
    for (int i = 0; i < waveform.Length; i++)
    {
        float envelope = MathF.Sin(MathF.PI * i / (waveform.Length - 1));
        waveform[i] = MathF.Sin(i * 1.18f) * (0.18f + 0.68f * envelope);
    }

    foreach (string style in RecordingOverlay.SupportedStyles)
    {
        overlay.SelectStyle(style);
        overlay.Location = new Point(-30000, -30000);
        overlay.Show();
        Application.DoEvents();
        if (overlay.Width > 190 || overlay.Width < 40 || overlay.Height is < 40 or > 56)
        {
            layoutFailures++;
            failures.AppendLine($"LAYOUT {style} overlay has invalid compact size {overlay.Width}x{overlay.Height}");
        }
        string stylePath = Path.Combine(Path.GetTempPath(), $"voiceprompt_overlay_{style}.png");
        using var bitmap = new Bitmap(overlay.Width, overlay.Height);
        overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, overlay.Size));
        bitmap.Save(stylePath);
        overlayPaths[style] = stylePath;
        if (style == RecordingOverlay.WaveStyle)
            bitmap.Save(overlayPath);
    }
}

form.ShowPageForDiagnostics("audio");
Application.DoEvents();
var inputMeter = (InputLevelMeter)typeof(MainForm)
    .GetField("_inputLevelMeter", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
var updateInputLevel = typeof(InputLevelMeter)
    .GetMethod("UpdateLevel", BindingFlags.Instance | BindingFlags.NonPublic)!;
if (!inputMeter.Active)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR navigating to Audio did not start the input test reader");
}
using (var map = MemoryMappedFile.CreateOrOpen(AudioMeterReader.MapName, AudioMeterReader.MapSize))
using (var view = map.CreateViewAccessor())
{
    int sequence = (view.ReadInt32(0) & 0x7FFFFFFE) + 2;
    view.Write(0, sequence | 1);
    view.Write(4, 1);
    view.Write(8, 0.62f);
    view.Write(12, Environment.ProcessId);
    view.Write(0, sequence);
}
updateInputLevel.Invoke(inputMeter, Array.Empty<object>());
Application.DoEvents();
if (!inputMeter.Listening || inputMeter.DisplayLevel < 0.20f)
{
    behaviorFailures++;
    failures.AppendLine($"BEHAVIOR input test did not read the shared microphone level: {inputMeter.DisplayLevel:0.00}");
}
string inputTestPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_input_test.png");
SaveClientScreenshot(inputTestPath);
form.Hide();
Application.DoEvents();
if (inputMeter.Active)
{
    behaviorFailures++;
    failures.AppendLine("PERFORMANCE hidden settings window kept the input-level reader active");
}
form.ShowPageForDiagnostics("audio");
Application.DoEvents();
if (inputMeter.Active)
{
    behaviorFailures++;
    failures.AppendLine("PERFORMANCE selecting Audio while hidden started the input-level reader");
}
form.Show();
Application.DoEvents();
if (!inputMeter.Active)
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR showing the Audio page did not resume the input-level reader");
}

var sampleRateChoice = (ChoiceStrip)typeof(MainForm)
    .GetField("_sampleRateChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
if (sampleRateChoice.SelectedValue != "16000")
{
    behaviorFailures++;
    failures.AppendLine($"BEHAVIOR local audio rate is not pinned to 16 kHz: {sampleRateChoice.SelectedValue}");
}

var setAiResult = typeof(MainForm).GetMethod("SetAiResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
var setBusy = typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
var aiResult = (Label)typeof(MainForm)
    .GetField("_aiResult", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(form)!;
setAiResult.Invoke(form, new object[] { "Provider result remains visible", true });
setBusy.Invoke(form, new object[] { false });
if (aiResult.Text != "Provider result remains visible")
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR AI provider result was overwritten when busy state cleared");
}

var runDaemonAction = typeof(MainForm).GetMethod(
    "RunDaemonActionAsync",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var failedDaemonTask = (Task<bool>)runDaemonAction.Invoke(
    form,
    new object[] { (Action)(() => throw new InvalidOperationException("expected diagnostic failure")), "unexpected success" })!;
while (!failedDaemonTask.IsCompleted)
{
    Application.DoEvents();
    Thread.Sleep(10);
}
if (failedDaemonTask.GetAwaiter().GetResult())
{
    behaviorFailures++;
    failures.AppendLine("BEHAVIOR failed runtime action reported success");
}

using (var recorder = new HotkeyRecorder { Binding = "f1" })
{
    var beginCapture = typeof(HotkeyRecorder).GetMethod("BeginCapture", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var keyDown = typeof(HotkeyRecorder).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;

    beginCapture.Invoke(recorder, Array.Empty<object>());
    keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.F2) });
    keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.Escape) });
    if (recorder.Binding != "f1")
    {
        behaviorFailures++;
        failures.AppendLine("BEHAVIOR hotkey Escape did not restore the committed binding");
    }

    beginCapture.Invoke(recorder, Array.Empty<object>());
    keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.F2) });
    keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.Enter) });
    if (recorder.Binding != "f2")
    {
        behaviorFailures++;
        failures.AppendLine($"BEHAVIOR hotkey Enter did not commit F2: {recorder.Binding}");
    }

    beginCapture.Invoke(recorder, Array.Empty<object>());
    keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.F12) });
    if (recorder.Binding != "f2")
    {
        behaviorFailures++;
        failures.AppendLine("BEHAVIOR hotkey recorder accepted Windows-reserved F12");
    }
}

Console.Write(failures.ToString());
Console.WriteLine($"RESULT layout={layoutFailures} behavior={behaviorFailures}");
Console.WriteLine($"OVERLAY_ACTIVATION_MS={overlayActivationMs}");
foreach (string page in pages)
    Console.WriteLine($"SCREENSHOT {page}={Path.Combine(Path.GetTempPath(), screenshotNames[page])}");
Console.WriteLine($"SCREENSHOT overlay={overlayPath}");
foreach ((string style, string path) in overlayPaths)
    Console.WriteLine($"SCREENSHOT overlay-{style}={path}");
Console.WriteLine($"SCREENSHOT advanced-tools={advancedToolsPath}");
Console.WriteLine($"SCREENSHOT application-updates={applicationUpdatesPath}");
Console.WriteLine($"SCREENSHOT data-portability={dataPortabilityPath}");
Console.WriteLine($"SCREENSHOT writing-modes={writingModesPath}");
Console.WriteLine($"SCREENSHOT application-profiles={applicationProfilesPath}");
Console.WriteLine($"SCREENSHOT recognition-server={recognitionServerPath}");
Console.WriteLine($"SCREENSHOT input-test={inputTestPath}");
Console.WriteLine($"SCREENSHOT history-comparison={historyComparisonPath}");
Console.WriteLine($"SCREENSHOT language-profile={languageProfilePath}");
Console.WriteLine($"SCREENSHOT snippets={snippetsPath}");

form.Close();
if (preferencesBackup is null)
    File.Delete(preferencesPath);
else
    File.WriteAllBytes(preferencesPath, preferencesBackup);
return layoutFailures + behaviorFailures;
    }
}
