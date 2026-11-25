using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Установщик SuperD3Hud.
//
// Внутри программы лежит один архив со всеми файлами лаунчера: сам
// SuperD3Hud.exe, библиотеки плагинов TurboHUD, компилятор Roslyn (он нужен,
// чтобы плагины .cs собирались прямо на компьютере пользователя), примеры
// плагинов и настройки по умолчанию.
//
// Установщик распаковывает архив в ту папку, где лежит сам, и запускает
// лаунчер. Никаких путей вне этой папки программа не использует, поэтому
// папку можно скопировать на любой компьютер и запустить установщик оттуда.
static class SetupInfo
{
    public const string PayloadResource = "SuperD3Hud.Setup.payload.zip";
    public const string LauncherName = "SuperD3Hud.exe";
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool launch = true;
        string target = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--no-launch") launch = false;
            else if (args[i] == "--target" && i + 1 < args.Length) target = args[++i];
        }

        string own = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(own)) own = Environment.CurrentDirectory;
        if (string.IsNullOrEmpty(target)) target = own;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SetupWindow window = new SetupWindow(target, launch);
        Application.Run(window);
        return window.ExitCode;
    }
}

sealed class SetupWindow : Form
{
    private readonly string _target;
    private readonly bool _launch;
    private readonly Label _pathLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;
    private readonly Button _startButton;
    private readonly CheckBox _launchCheck;
    private int _exitCode;

    public int ExitCode { get { return _exitCode; } }

    public SetupWindow(string target, bool launch)
    {
        _target = target;
        _launch = launch;

        Text = "Установка SuperD3Hud";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 210);
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(28, 30, 34);
        ForeColor = Color.WhiteSmoke;

        Label title = new Label();
        title.Text = "SuperD3Hud — установка";
        title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        title.AutoSize = true;
        title.Location = new Point(18, 16);
        title.ForeColor = Color.White;
        Controls.Add(title);

        _pathLabel = new Label();
        _pathLabel.Text = "Папка: " + target;
        _pathLabel.AutoSize = false;
        _pathLabel.Location = new Point(18, 52);
        _pathLabel.Size = new Size(484, 20);
        _pathLabel.ForeColor = Color.FromArgb(185, 190, 200);
        Controls.Add(_pathLabel);

        _progress = new ProgressBar();
        _progress.Location = new Point(18, 84);
        _progress.Size = new Size(484, 18);
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        Controls.Add(_progress);

        _statusLabel = new Label();
        _statusLabel.Text = "Подготовка...";
        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(18, 110);
        _statusLabel.Size = new Size(484, 40);
        _statusLabel.ForeColor = Color.FromArgb(210, 215, 225);
        Controls.Add(_statusLabel);

        _launchCheck = new CheckBox();
        _launchCheck.Text = "Запустить лаунчер после установки";
        _launchCheck.Checked = launch;
        _launchCheck.Location = new Point(18, 155);
        _launchCheck.AutoSize = true;
        _launchCheck.ForeColor = Color.FromArgb(210, 215, 225);
        Controls.Add(_launchCheck);

        _startButton = new Button();
        _startButton.Text = "Готово";
        _startButton.Location = new Point(406, 152);
        _startButton.Size = new Size(96, 30);
        _startButton.Enabled = false;
        _startButton.Click += delegate { Close(); };
        Controls.Add(_startButton);

        Shown += delegate { StartWork(); };
    }

    private void StartWork()
    {
        Thread worker = new Thread(new ThreadStart(Extract));
        worker.IsBackground = true;
        worker.Start();
    }

    private void Extract()
    {
        try
        {
            using (Stream payload = OpenPayload())
            {
                if (payload == null)
                {
                    SetStatus("Не найден архив с файлами. Установщик повреждён.", true);
                    return;
                }

                using (ZipArchive archive = new ZipArchive(payload, ZipArchiveMode.Read, true))
                {
                    List<ZipArchiveEntry> entries = new List<ZipArchiveEntry>(archive.Entries);
                    int done = 0;
                    foreach (ZipArchiveEntry entry in entries)
                    {
                        // The archive may use either separator, and a name must
                        // never point outside the folder of the installation.
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.StartsWith("/")) name = name.Substring(1);
                        if (name.Length == 0) continue;

                        bool isDirectory = name.EndsWith("/");
                        if (isDirectory) name = name.Substring(0, name.Length - 1);
                        if (name.Length == 0) continue;

                        bool safe = true;
                        foreach (string part in name.Split('/'))
                        {
                            if (part == "..") safe = false;
                        }
                        if (!safe) continue;

                        string relative = name.Replace('/', Path.DirectorySeparatorChar);
                        string path = Path.Combine(_target, relative);

                        if (isDirectory)
                        {
                            Directory.CreateDirectory(path);
                            continue;
                        }

                        string dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                        using (Stream source = entry.Open())
                        using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                            source.CopyTo(file);

                        done++;
                        int percent = entries.Count == 0 ? 100 : (int)(done * 100L / entries.Count);
                        SetProgress(percent, "Распаковка: " + Path.GetFileName(relative));
                    }
                }
            }

            SetProgress(100, "Готово. Файлы распакованы в папку установки.");
            Finish(true);
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось распаковать файлы: " + ex.Message, true);
            Finish(false);
        }
    }

    private static Stream OpenPayload()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        Stream stream = assembly.GetManifestResourceStream(SetupInfo.PayloadResource);
        if (stream != null) return stream;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                return assembly.GetManifestResourceStream(name);
        }
        return null;
    }

    private void SetProgress(int percent, string text)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetProgress(percent, text); })); return; }
            _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, percent));
            _statusLabel.Text = text;
        }
        catch { }
    }

    private void SetStatus(string text, bool error)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetStatus(text, error); })); return; }
            _statusLabel.Text = text;
            _statusLabel.ForeColor = error ? Color.FromArgb(255, 140, 120) : Color.FromArgb(210, 215, 225);
        }
        catch { }
    }

    private void Finish(bool ok)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { Finish(ok); }));
                return;
            }

            _startButton.Enabled = true;
            _startButton.Text = "Закрыть";
            _startButton.BringToFront();

            if (!ok)
            {
                _exitCode = 1;
                return;
            }

            _exitCode = 0;
            if (_launch && _launchCheck.Checked) StartLauncher();
            else _statusLabel.Text = "Готово. Лаунчер можно запустить: " + SetupInfo.LauncherName;
        }
        catch { }
    }

    private void StartLauncher()
    {
        try
        {
            string exe = Path.Combine(_target, SetupInfo.LauncherName);
            if (!File.Exists(exe))
            {
                _statusLabel.Text = "Лаунчер не найден: " + exe;
                return;
            }
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe);
            psi.WorkingDirectory = _target;
            psi.UseShellExecute = true;
            System.Diagnostics.Process.Start(psi);
            _statusLabel.Text = "Готово. Лаунчер запущен.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Готово, но запустить лаунчер не вышло: " + ex.Message;
        }
    }
}
