using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

public class ExternalTargetForm : Form
{
    private readonly string _appName;
    private readonly string _statusPath;
    private readonly TextBox _editorBox;
    private readonly Button _actionBtn;
    private readonly Button _menuBtn;
    private int _buttonClicks;
    private int _menuClicks;
    private int _focusCount;
    private bool _isActive;

    public ExternalTargetForm(string appName, string statusPath, int left, int top, int width, int height)
    {
        _appName = appName;
        _statusPath = statusPath;

        Text = appName;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(left, top);
        Size = new Size(width, height);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        _actionBtn = new Button();
        _actionBtn.Text = "Run Action";
        _actionBtn.Location = new Point(15, 15);
        _actionBtn.Size = new Size(120, 36);
        _actionBtn.BackColor = Color.FromArgb(14, 116, 144);
        _actionBtn.ForeColor = Color.White;
        _actionBtn.Click += delegate {
            _buttonClicks++;
            WriteStatus();
        };
        Controls.Add(_actionBtn);

        _menuBtn = new Button();
        _menuBtn.Text = "File Menu";
        _menuBtn.Location = new Point(150, 15);
        _menuBtn.Size = new Size(120, 36);
        _menuBtn.BackColor = Color.FromArgb(67, 56, 202);
        _menuBtn.ForeColor = Color.White;
        _menuBtn.Click += delegate {
            _menuClicks++;
            WriteStatus();
        };
        Controls.Add(_menuBtn);

        _editorBox = new TextBox();
        _editorBox.Multiline = true;
        _editorBox.Location = new Point(15, 65);
        _editorBox.Size = new Size(width - 45, height - 115);
        _editorBox.BackColor = Color.FromArgb(17, 24, 39);
        _editorBox.ForeColor = Color.FromArgb(167, 243, 208);
        _editorBox.Font = new Font("Consolas", 11f);
        _editorBox.TextChanged += delegate {
            WriteStatus();
        };
        Controls.Add(_editorBox);

        Activated += delegate {
            _isActive = true;
            _focusCount++;
            WriteStatus();
        };
        Deactivate += delegate {
            _isActive = false;
            WriteStatus();
        };
        Shown += delegate {
            WriteStatus();
        };
    }

    private void WriteStatus()
    {
        try
        {
            Point editorScreenPt = _editorBox.PointToScreen(new Point(_editorBox.Width / 2, 30));
            Point actionBtnScreenPt = _actionBtn.PointToScreen(new Point(_actionBtn.Width / 2, _actionBtn.Height / 2));
            Point menuBtnScreenPt = _menuBtn.PointToScreen(new Point(_menuBtn.Width / 2, _menuBtn.Height / 2));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"appName\": \"{0}\",\n", _appName);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"processId\": {0},\n", System.Diagnostics.Process.GetCurrentProcess().Id);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hwnd\": {0},\n", Handle.ToInt64());
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isActive\": {0},\n", _isActive ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"focusCount\": {0},\n", _focusCount);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"buttonClicks\": {0},\n", _buttonClicks);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"menuClicks\": {0},\n", _menuClicks);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"editorText\": \"{0}\",\n", (_editorBox.Text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"editorScreenX\": {0},\n", editorScreenPt.X);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"editorScreenY\": {0},\n", editorScreenPt.Y);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"actionBtnScreenX\": {0},\n", actionBtnScreenPt.X);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"actionBtnScreenY\": {0},\n", actionBtnScreenPt.Y);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"menuBtnScreenX\": {0},\n", menuBtnScreenPt.X);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"menuBtnScreenY\": {0}\n", menuBtnScreenPt.Y);
            sb.AppendLine("}");

            string tmp = _statusPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            File.Copy(tmp, _statusPath, true);
        }
        catch { }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string name = args.Length > 0 ? args[0] : "ExternalApp";
        string path = args.Length > 1 ? args[1] : "ext-status.json";
        int l = args.Length > 2 ? int.Parse(args[2]) : 760;
        int t = args.Length > 3 ? int.Parse(args[3]) : 60;
        int w = args.Length > 4 ? int.Parse(args[4]) : 440;
        int h = args.Length > 5 ? int.Parse(args[5]) : 360;
        Application.Run(new ExternalTargetForm(name, path, l, t, w, h));
        return 0;
    }
}
