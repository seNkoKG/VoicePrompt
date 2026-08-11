using System.Text;
using System.Reflection;
using VoicePromptTray;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var paths = AppPaths.Default;
var form = new MainForm(new DaemonManager(paths), paths)
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
};
form.Show();
form.AllowClose = true;
Application.DoEvents();
Thread.Sleep(700);
Application.DoEvents();

int overlaps = 0;
int overflows = 0;
int behaviorFailures = 0;
var sb = new StringBuilder();

string Describe(Control c)
{
    string t = c.Text ?? "";
    if (t.Length > 28)
        t = t[..28] + "…";
    return $"{c.GetType().Name} \"{t}\" {c.Bounds}";
}

void Walk(Control parent)
{
    var kids = parent.Controls.Cast<Control>().Where(c => c.Visible).ToList();
    for (int i = 0; i < kids.Count; i++)
    {
        for (int j = i + 1; j < kids.Count; j++)
        {
            if (kids[i].Bounds.IntersectsWith(kids[j].Bounds))
            {
                overlaps++;
                sb.AppendLine($"OVERLAP in {parent.GetType().Name}: {Describe(kids[i])}  vs  {Describe(kids[j])}");
            }
        }
    }

    bool scrollable = parent is Panel p && p.AutoScroll;
    foreach (var k in kids)
    {
        if (!scrollable && parent is not Form &&
            (k.Left < 0 || k.Top < 0 || k.Right > parent.ClientSize.Width || k.Bottom > parent.ClientSize.Height))
        {
            overflows++;
            sb.AppendLine($"OVERFLOW in {parent.GetType().Name} size={parent.ClientSize}: {Describe(k)}");
        }
        Walk(k);
    }
}

Walk(form);

string png = Path.Combine(Path.GetTempPath(), "voiceprompt_ui.png");
using (var bmp = new Bitmap(form.Width, form.Height))
{
    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
    bmp.Save(png);
}

string aiPng = Path.Combine(Path.GetTempPath(), "voiceprompt_ai_settings.png");
var content = form.Controls.OfType<Panel>().First(p => p.AutoScroll);
var cards = content.Controls.OfType<CardPanel>().OrderBy(c => c.Top).ToList();
if (cards.Count >= 3)
{
    content.AutoScrollPosition = new Point(0, cards[2].Top - 12);
    Application.DoEvents();
    using var bmp = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
    bmp.Save(aiPng);
}

string overlayPng = Path.Combine(Path.GetTempPath(), "voiceprompt_overlay.png");
using (var overlay = new RecordingOverlay
{
    StartPosition = FormStartPosition.Manual,
    Location = new Point(-30000, -30000),
    Opacity = 0.96,
})
{
    var waveform = (float[])typeof(RecordingOverlay)
        .GetField("_waveform", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(overlay)!;
    for (int i = 0; i < waveform.Length; i++)
    {
        float edge = MathF.Sin(MathF.PI * i / (waveform.Length - 1));
        waveform[i] = MathF.Sin(i * 1.18f) * (0.18f + 0.68f * edge);
    }
    overlay.Show();
    Application.DoEvents();
    using var bmp = new Bitmap(overlay.Width, overlay.Height);
    overlay.DrawToBitmap(bmp, new Rectangle(0, 0, overlay.Width, overlay.Height));
    bmp.Save(overlayPng);
}

var recorder = new HotkeyRecorder { Binding = "f1" };
var gotFocus = typeof(HotkeyRecorder).GetMethod("OnGotFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
var keyDown = typeof(HotkeyRecorder).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
gotFocus.Invoke(recorder, new object[] { EventArgs.Empty });
keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.F2) });
keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.Escape) });
if (recorder.Binding != "f1")
{
    behaviorFailures++;
    sb.AppendLine("BEHAVIOR hotkey Escape did not restore committed binding");
}
gotFocus.Invoke(recorder, new object[] { EventArgs.Empty });
keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.F2) });
keyDown.Invoke(recorder, new object[] { new KeyEventArgs(Keys.Enter) });
if (recorder.Binding != "f2")
{
    behaviorFailures++;
    sb.AppendLine($"BEHAVIOR hotkey Enter did not commit pending binding: {recorder.Binding}");
}
recorder.Dispose();

Console.WriteLine(sb.ToString());
Console.WriteLine($"RESULT overlaps={overlaps} overflow={overflows} behavior={behaviorFailures} png={png} ai={aiPng} overlay={overlayPng}");
form.Close();
return overlaps + overflows + behaviorFailures;
