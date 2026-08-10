using System.Text;
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

Console.WriteLine(sb.ToString());
Console.WriteLine($"RESULT overlaps={overlaps} overflow={overflows} png={png}");
form.Close();
return overlaps + overflows;
