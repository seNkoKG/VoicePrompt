using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Text;
using VoicePromptTray;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

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
    using var bitmap = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
    bitmap.Save(path);
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
using (var historyComparisonBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(historyComparisonBitmap, new Rectangle(Point.Empty, form.Size));
    historyComparisonBitmap.Save(historyComparisonPath);
}

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
pageMap["advanced"].AutoScrollPosition = new Point(0, 460);
Application.DoEvents();
string advancedToolsPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_advanced_tools.png");
using (var advancedToolsBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(advancedToolsBitmap, new Rectangle(Point.Empty, form.Size));
    advancedToolsBitmap.Save(advancedToolsPath);
}
pageMap["advanced"].AutoScrollPosition = new Point(0, 720);
Application.DoEvents();
string dataPortabilityPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_data_portability.png");
using (var dataPortabilityBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(dataPortabilityBitmap, new Rectangle(Point.Empty, form.Size));
    dataPortabilityBitmap.Save(dataPortabilityPath);
}

form.ShowPageForDiagnostics("intelligence");
pageMap["intelligence"].AutoScrollPosition = new Point(0, 430);
Application.DoEvents();
string writingModesPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_writing_modes.png");
using (var writingModesBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(writingModesBitmap, new Rectangle(Point.Empty, form.Size));
    writingModesBitmap.Save(writingModesPath);
}
pageMap["intelligence"].AutoScrollPosition = new Point(0, 800);
Application.DoEvents();
string applicationProfilesPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_application_profiles.png");
using (var applicationProfilesBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(applicationProfilesBitmap, new Rectangle(Point.Empty, form.Size));
    applicationProfilesBitmap.Save(applicationProfilesPath);
}

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
        Recognition = new BackupRecognitionSettings(),
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
    !appProfilesText.Text.Contains("Code.exe => prompt, inherit"))
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
using (var languageProfileBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(languageProfileBitmap, new Rectangle(Point.Empty, form.Size));
    languageProfileBitmap.Save(languageProfilePath);
}
pageMap["dictation"].AutoScrollPosition = new Point(0, 980);
Application.DoEvents();
string snippetsPath = Path.Combine(Path.GetTempPath(), "voiceprompt_ui_snippets.png");
using (var snippetsBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(snippetsBitmap, new Rectangle(Point.Empty, form.Size));
    snippetsBitmap.Save(snippetsPath);
}
typeof(MainForm).GetMethod("DiscardChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(form, Array.Empty<object>());
Application.DoEvents();

string overlayPath = Path.Combine(Path.GetTempPath(), "voiceprompt_overlay.png");
long overlayActivationMs;
using (var overlay = new RecordingOverlay
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
    Opacity = 0.96,
})
{
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
    if (overlay.Width > 190)
    {
        layoutFailures++;
        failures.AppendLine($"LAYOUT overlay is too wide at {overlay.Width}px");
    }

    var waveform = (float[])typeof(RecordingOverlay)
        .GetField("_waveform", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(overlay)!;
    for (int i = 0; i < waveform.Length; i++)
    {
        float envelope = MathF.Sin(MathF.PI * i / (waveform.Length - 1));
        waveform[i] = MathF.Sin(i * 1.18f) * (0.18f + 0.68f * envelope);
    }

    overlay.Show();
    Application.DoEvents();
    using var bitmap = new Bitmap(overlay.Width, overlay.Height);
    overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, overlay.Size));
    bitmap.Save(overlayPath);
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
using (var inputTestBitmap = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(inputTestBitmap, new Rectangle(Point.Empty, form.Size));
    inputTestBitmap.Save(inputTestPath);
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
}

Console.Write(failures.ToString());
Console.WriteLine($"RESULT layout={layoutFailures} behavior={behaviorFailures}");
Console.WriteLine($"OVERLAY_ACTIVATION_MS={overlayActivationMs}");
foreach (string page in pages)
    Console.WriteLine($"SCREENSHOT {page}={Path.Combine(Path.GetTempPath(), screenshotNames[page])}");
Console.WriteLine($"SCREENSHOT overlay={overlayPath}");
Console.WriteLine($"SCREENSHOT advanced-tools={advancedToolsPath}");
Console.WriteLine($"SCREENSHOT data-portability={dataPortabilityPath}");
Console.WriteLine($"SCREENSHOT writing-modes={writingModesPath}");
Console.WriteLine($"SCREENSHOT application-profiles={applicationProfilesPath}");
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
