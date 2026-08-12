using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Text;
using VoicePromptTray;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var paths = AppPaths.Default;
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

string overlayPath = Path.Combine(Path.GetTempPath(), "voiceprompt_overlay.png");
long overlayActivationMs;
using (var overlay = new RecordingOverlay
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
    Opacity = 0.96,
})
{
    using (var map = MemoryMappedFile.CreateOrOpen("VoicePrompt.AudioMeter.v2", 64))
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
    if (!overlay.Visible || overlay.Opacity < 0.90 || overlayActivationMs > 100)
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

form.Close();
return layoutFailures + behaviorFailures;
