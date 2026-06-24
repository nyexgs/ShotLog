using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Timer = System.Windows.Forms.Timer;

namespace ShotLog;

internal static class Program
{
    internal static readonly Version AppVersion = GetAppVersion();

    private static Version GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        info = info?.Split('+')[0].Trim().TrimStart('v', 'V');

        if (!string.IsNullOrWhiteSpace(info) && Version.TryParse(info, out var version))
        {
            return version;
        }

        return assembly.GetName().Version ?? new Version(0, 12, 0);
    }

    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatalException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleFatalException(ex);
            }
        };

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            HandleFatalException(ex);
        }
    }

    private static void HandleFatalException(Exception ex)
    {
        var logPath = Path.Combine(AppPaths.AppDataFolder, "error.log");

        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n", Encoding.UTF8);
        }
        catch
        {
        }

        try
        {
            MessageBox.Show(
                "ShotLog에서 오류가 발생했습니다.\n\n" + ex.Message + "\n\n오류 로그 위치:\n" + logPath,
                "ShotLog 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        catch
        {
        }
    }
}

internal static class AppPaths
{
    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShotLog");
    public static string LocalAppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShotLog");
    public static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ShotLog");
    public static string SettingsPath => Path.Combine(AppDataFolder, "settings.json");
    public static string BufferFolder => Path.Combine(LocalAppDataFolder, "buffer");
    public static string BinFolder => Path.Combine(LocalAppDataFolder, "bin");
    public static string UpdatesFolder => Path.Combine(LocalAppDataFolder, "updates");
}

internal sealed class MainForm : Form
{
    private const int HotKeyId = 9001;
    private const int WmHotKey = 0x0312;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly AppSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Icon _formIcon;
    private readonly Icon _trayIconImage;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _openMenuItem = new("ShotLog 열기");
    private readonly ToolStripMenuItem _startupMenuItem = new("시작 프로그램에 추가");
    private readonly ToolStripMenuItem _exitMenuItem = new("프로그램 종료하기");

    private readonly PictureBox _logoBox = new();
    private readonly Label _statusLabel = new();
    private readonly Label _hotkeyInfoLabel = new();
    private readonly Label _trayInfoLabel = new();
    private readonly Label _availableLabel = new();
    private readonly Label _folderLabel = new();
    private readonly Label _engineLabel = new();
    private readonly TextBox _logBox = new();
    private readonly NumericUpDown _secondsInput = new();
    private readonly NumericUpDown _fpsInput = new();
    private readonly NumericUpDown _bitrateInput = new();
    private readonly ComboBox _engineCombo = new();
    private readonly ComboBox _hotkeyCombo = new();
    private readonly CheckBox _ctrlCheck = new();
    private readonly CheckBox _altCheck = new();
    private readonly CheckBox _shiftCheck = new();
    private readonly CheckBox _audioCheck = new();
    private readonly CheckedListBox _outputDeviceList = new();
    private readonly CheckedListBox _micDeviceList = new();
    private readonly NumericUpDown _audioDelayInput = new();
    private readonly Button _refreshAudioDevicesButton = new();
    private readonly CheckBox _mouseCheck = new();
    private readonly Button _toggleRecordButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _setFolderButton = new();
    private readonly Button _applyHotkeyButton = new();
    private readonly TextBox _githubOwnerBox = new();
    private readonly TextBox _githubRepoBox = new();
    private readonly CheckBox _checkUpdatesOnStartCheck = new();
    private readonly Button _checkUpdateButton = new();

    private readonly Timer _bufferTimer = new() { Interval = 1000 };
    private Process? _ffmpegProcess;
    private bool _recording;
    private bool _saving;
    private bool _exitRequested;
    private bool _hotkeyRegistered;
    private bool _manualStopping;
    private DateTime _recordingStartedAt;
    private string _activeEngineName = "대기 중";
    private readonly StringBuilder _ffmpegLog = new();
    private bool _forceGdiUntilManualStart;

    private readonly List<AudioCaptureSession> _audioSessions = new();
    private readonly List<AudioDeviceItem> _outputDevices = new();
    private readonly List<AudioDeviceItem> _micDevices = new();
    private bool _audioRunning;

    private int BufferSeconds => (int)_secondsInput.Value;
    private int Fps => (int)_fpsInput.Value;
    private int BitrateMbps => (int)_bitrateInput.Value;

    public MainForm()
    {
        _settings = LoadSettings();
        _settings.Normalize();
        SaveSettings();

        _formIcon = LoadIconResource();
        _trayIconImage = LoadIconResource();

        Text = "ShotLog";
        Width = 820;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 900);
        BackColor = Color.FromArgb(18, 19, 25);
        ForeColor = Color.White;
        Icon = _formIcon;

        Directory.CreateDirectory(AppPaths.AppDataFolder);
        Directory.CreateDirectory(AppPaths.LocalAppDataFolder);
        Directory.CreateDirectory(AppPaths.BufferFolder);
        Directory.CreateDirectory(_settings.OutputFolder);

        BuildUi();
        ApplySettingsToUi();
        BuildTrayMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "ShotLog",
            Icon = _trayIconImage,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        _bufferTimer.Tick += (_, _) => UpdateBufferState();
        _bufferTimer.Start();

        _ = StartRecordingAsync();

        if (_settings.CheckUpdatesOnStart)
        {
            Shown += async (_, _) => await CheckForUpdatesAsync(false);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyHotkey(false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            WindowState = FormWindowState.Minimized;
            _notifyIcon.Visible = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _bufferTimer.Stop();
        StopRecording(false);
        UnregisterCurrentHotkey();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _logoBox.Image?.Dispose();
        _formIcon.Dispose();
        _trayIconImage.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotKeyId)
        {
            _ = SaveClipAsync("hotkey");
            return;
        }

        base.WndProc(ref m);
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "ShotLog",
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            Left = 104,
            Top = 24,
            Width = 260,
            Height = 42
        };

        var subtitle = new Label
        {
            Text = "발로란트 클립 저장 도구",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(190, 190, 200),
            Left = 107,
            Top = 66,
            Width = 420,
            Height = 24
        };

        _logoBox.Left = 26;
        _logoBox.Top = 25;
        _logoBox.Width = 64;
        _logoBox.Height = 64;
        _logoBox.SizeMode = PictureBoxSizeMode.Zoom;
        _logoBox.Image = LoadPngResource();

        _statusLabel.Text = "상시녹화 준비 중";
        _statusLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _statusLabel.Left = 530;
        _statusLabel.Top = 34;
        _statusLabel.Width = 250;
        _statusLabel.Height = 24;
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;

        _engineLabel.Text = "캡처 엔진 : 대기 중";
        _engineLabel.Font = new Font("Segoe UI", 9);
        _engineLabel.ForeColor = Color.FromArgb(190, 190, 200);
        _engineLabel.Left = 430;
        _engineLabel.Top = 62;
        _engineLabel.Width = 350;
        _engineLabel.Height = 24;
        _engineLabel.TextAlign = ContentAlignment.MiddleRight;

        _hotkeyInfoLabel.Text = "클립 저장 단축키 : F8";
        _hotkeyInfoLabel.Left = 26;
        _hotkeyInfoLabel.Top = 108;
        _hotkeyInfoLabel.Width = 330;
        _hotkeyInfoLabel.Height = 24;
        _hotkeyInfoLabel.ForeColor = Color.FromArgb(220, 220, 230);

        _trayInfoLabel.Text = "프로그램 종료 시 시스템 트레이로 최소화됩니다.";
        _trayInfoLabel.Left = 390;
        _trayInfoLabel.Top = 108;
        _trayInfoLabel.Width = 390;
        _trayInfoLabel.Height = 24;
        _trayInfoLabel.ForeColor = Color.FromArgb(190, 190, 200);
        _trayInfoLabel.TextAlign = ContentAlignment.MiddleRight;

        var captureBox = CreateGroupBox("상시녹화 설정", 22, 142, 758, 160);
        AddLabel(captureBox, "저장할 이전 초", 18, 32, 120);
        _secondsInput.Left = 138;
        _secondsInput.Top = 28;
        _secondsInput.Width = 80;
        _secondsInput.Minimum = 5;
        _secondsInput.Maximum = 120;

        AddLabel(captureBox, "FPS", 240, 32, 50);
        _fpsInput.Left = 290;
        _fpsInput.Top = 28;
        _fpsInput.Width = 80;
        _fpsInput.Minimum = 30;
        _fpsInput.Maximum = 144;

        AddLabel(captureBox, "비트레이트 Mbps", 392, 32, 115);
        _bitrateInput.Left = 508;
        _bitrateInput.Top = 28;
        _bitrateInput.Width = 80;
        _bitrateInput.Minimum = 5;
        _bitrateInput.Maximum = 80;

        AddLabel(captureBox, "캡처 방식", 18, 76, 80);
        _engineCombo.Left = 100;
        _engineCombo.Top = 72;
        _engineCombo.Width = 180;
        _engineCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _engineCombo.Items.AddRange(new object[] { "자동", "고성능(DDA)", "호환(GDI)" });

        _audioCheck.Text = "오디오 녹음 사용";
        _audioCheck.Left = 310;
        _audioCheck.Top = 74;
        _audioCheck.Width = 135;
        _audioCheck.ForeColor = Color.White;

        _mouseCheck.Text = "마우스 커서 포함";
        _mouseCheck.Left = 470;
        _mouseCheck.Top = 74;
        _mouseCheck.Width = 145;
        _mouseCheck.ForeColor = Color.White;

        _toggleRecordButton.Text = "상시녹화 시작";
        _toggleRecordButton.Left = 18;
        _toggleRecordButton.Top = 112;
        _toggleRecordButton.Width = 140;
        _toggleRecordButton.Height = 34;
        _toggleRecordButton.Click += async (_, _) =>
        {
            if (_recording)
            {
                StopRecording(true);
            }
            else
            {
                _forceGdiUntilManualStart = false;
                await StartRecordingAsync();
            }
        };

        _saveButton.Text = "지금 클립 저장";
        _saveButton.Left = 170;
        _saveButton.Top = 112;
        _saveButton.Width = 140;
        _saveButton.Height = 34;
        _saveButton.Click += (_, _) => _ = SaveClipAsync("button");

        _availableLabel.Text = "저장 가능 영상 : 0초 / 30초";
        _availableLabel.Left = 330;
        _availableLabel.Top = 117;
        _availableLabel.Width = 390;
        _availableLabel.Height = 24;
        _availableLabel.ForeColor = Color.FromArgb(210, 210, 220);
        _availableLabel.TextAlign = ContentAlignment.MiddleRight;

        captureBox.Controls.AddRange(new Control[]
        {
            _secondsInput, _fpsInput, _bitrateInput, _engineCombo, _audioCheck, _mouseCheck,
            _toggleRecordButton, _saveButton, _availableLabel
        });

        var audioBox = CreateGroupBox("오디오 설정", 22, 316, 758, 170);
        AddLabel(audioBox, "출력 장치", 18, 30, 80);
        _outputDeviceList.Left = 18;
        _outputDeviceList.Top = 54;
        _outputDeviceList.Width = 340;
        _outputDeviceList.Height = 76;
        _outputDeviceList.CheckOnClick = true;
        _outputDeviceList.BackColor = Color.FromArgb(10, 11, 15);
        _outputDeviceList.ForeColor = Color.FromArgb(225, 225, 230);

        AddLabel(audioBox, "마이크", 382, 30, 80);
        _micDeviceList.Left = 382;
        _micDeviceList.Top = 54;
        _micDeviceList.Width = 340;
        _micDeviceList.Height = 76;
        _micDeviceList.CheckOnClick = true;
        _micDeviceList.BackColor = Color.FromArgb(10, 11, 15);
        _micDeviceList.ForeColor = Color.FromArgb(225, 225, 230);

        AddLabel(audioBox, "소리 동기 보정(ms)", 18, 138, 130);
        _audioDelayInput.Left = 154;
        _audioDelayInput.Top = 134;
        _audioDelayInput.Width = 90;
        _audioDelayInput.Minimum = 0;
        _audioDelayInput.Maximum = 5000;
        _audioDelayInput.Increment = 100;

        _refreshAudioDevicesButton.Text = "오디오 장치 새로고침";
        _refreshAudioDevicesButton.Left = 382;
        _refreshAudioDevicesButton.Top = 132;
        _refreshAudioDevicesButton.Width = 340;
        _refreshAudioDevicesButton.Height = 30;
        _refreshAudioDevicesButton.Click += (_, _) => LoadAudioDevicesToUi(true);

        audioBox.Controls.AddRange(new Control[]
        {
            _outputDeviceList, _micDeviceList, _audioDelayInput, _refreshAudioDevicesButton
        });

        var hotkeyBox = CreateGroupBox("단축키 설정", 22, 502, 370, 112);
        _ctrlCheck.Text = "Ctrl";
        _ctrlCheck.Left = 18;
        _ctrlCheck.Top = 32;
        _ctrlCheck.Width = 60;
        _ctrlCheck.ForeColor = Color.White;

        _altCheck.Text = "Alt";
        _altCheck.Left = 82;
        _altCheck.Top = 32;
        _altCheck.Width = 55;
        _altCheck.ForeColor = Color.White;

        _shiftCheck.Text = "Shift";
        _shiftCheck.Left = 140;
        _shiftCheck.Top = 32;
        _shiftCheck.Width = 70;
        _shiftCheck.ForeColor = Color.White;

        _hotkeyCombo.Left = 220;
        _hotkeyCombo.Top = 29;
        _hotkeyCombo.Width = 120;
        _hotkeyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _hotkeyCombo.Items.AddRange(new object[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Insert", "Home", "End", "PageUp", "PageDown" });

        _applyHotkeyButton.Text = "단축키 적용";
        _applyHotkeyButton.Left = 18;
        _applyHotkeyButton.Top = 68;
        _applyHotkeyButton.Width = 322;
        _applyHotkeyButton.Height = 30;
        _applyHotkeyButton.Click += (_, _) => ApplyHotkey(true);

        hotkeyBox.Controls.AddRange(new Control[] { _ctrlCheck, _altCheck, _shiftCheck, _hotkeyCombo, _applyHotkeyButton });

        var folderBox = CreateGroupBox("저장 폴더", 410, 502, 370, 112);
        _folderLabel.Text = "";
        _folderLabel.Left = 18;
        _folderLabel.Top = 28;
        _folderLabel.Width = 322;
        _folderLabel.Height = 32;
        _folderLabel.ForeColor = Color.FromArgb(200, 200, 210);

        _openFolderButton.Text = "저장 폴더 열기";
        _openFolderButton.Left = 18;
        _openFolderButton.Top = 68;
        _openFolderButton.Width = 150;
        _openFolderButton.Height = 30;
        _openFolderButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_settings.OutputFolder) { UseShellExecute = true });

        _setFolderButton.Text = "저장 폴더 설정";
        _setFolderButton.Left = 188;
        _setFolderButton.Top = 68;
        _setFolderButton.Width = 150;
        _setFolderButton.Height = 30;
        _setFolderButton.Click += (_, _) => SetOutputFolder();

        folderBox.Controls.AddRange(new Control[] { _folderLabel, _openFolderButton, _setFolderButton });

        var updateBox = CreateGroupBox("자동 업데이트", 22, 628, 758, 96);
        AddLabel(updateBox, "GitHub 소유자", 18, 32, 95);
        _githubOwnerBox.Left = 112;
        _githubOwnerBox.Top = 28;
        _githubOwnerBox.Width = 160;

        AddLabel(updateBox, "저장소", 292, 32, 55);
        _githubRepoBox.Left = 348;
        _githubRepoBox.Top = 28;
        _githubRepoBox.Width = 160;

        _checkUpdatesOnStartCheck.Text = "시작 시 업데이트 확인";
        _checkUpdatesOnStartCheck.Left = 18;
        _checkUpdatesOnStartCheck.Top = 64;
        _checkUpdatesOnStartCheck.Width = 170;
        _checkUpdatesOnStartCheck.ForeColor = Color.White;
        _checkUpdatesOnStartCheck.CheckedChanged += (_, _) => SaveUiSettings();

        _checkUpdateButton.Text = "업데이트 확인";
        _checkUpdateButton.Left = 570;
        _checkUpdateButton.Top = 28;
        _checkUpdateButton.Width = 150;
        _checkUpdateButton.Height = 34;
        _checkUpdateButton.Click += async (_, _) =>
        {
            SaveUiSettings();
            await CheckForUpdatesAsync(true);
        };

        updateBox.Controls.AddRange(new Control[] { _githubOwnerBox, _githubRepoBox, _checkUpdatesOnStartCheck, _checkUpdateButton });

        var logBox = CreateGroupBox("로그", 22, 738, 758, 106);
        _logBox.Left = 18;
        _logBox.Top = 26;
        _logBox.Width = 722;
        _logBox.Height = 64;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(10, 11, 15);
        _logBox.ForeColor = Color.FromArgb(225, 225, 230);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        logBox.Controls.Add(_logBox);

        Controls.AddRange(new Control[]
        {
            _logoBox, title, subtitle, _statusLabel, _engineLabel, _hotkeyInfoLabel, _trayInfoLabel,
            captureBox, audioBox, hotkeyBox, folderBox, updateBox, logBox
        });

        foreach (var button in GetAllControls(this).OfType<Button>())
        {
            button.BackColor = Color.FromArgb(255, 70, 85);
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
        }
    }

    private static GroupBox CreateGroupBox(string text, int left, int top, int width, int height)
    {
        return new GroupBox
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ForeColor = Color.FromArgb(225, 225, 230),
            BackColor = Color.FromArgb(18, 19, 25)
        };
    }

    private static void AddLabel(Control parent, string text, int left, int top, int width)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = 24,
            ForeColor = Color.FromArgb(210, 210, 220)
        });
    }

    private static IEnumerable<Control> GetAllControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in GetAllControls(control))
            {
                yield return child;
            }
        }
    }

    private void BuildTrayMenu()
    {
        _openMenuItem.Click += (_, _) => ShowWindow();
        _startupMenuItem.Click += (_, _) => ToggleStartup();
        _exitMenuItem.Click += (_, _) => ExitApp();
        _trayMenu.Items.Add(_openMenuItem);
        _trayMenu.Items.Add(_startupMenuItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_exitMenuItem);
        RefreshStartupMenu();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    private void ApplySettingsToUi()
    {
        _secondsInput.Value = Clamp(_settings.BufferSeconds, 5, 120);
        _fpsInput.Value = Clamp(_settings.Fps, 30, 144);
        _bitrateInput.Value = Clamp(_settings.BitrateMbps, 5, 80);
        _audioCheck.Checked = _settings.IncludeAudio;
        _mouseCheck.Checked = _settings.DrawMouse;
        _ctrlCheck.Checked = _settings.HotkeyCtrl;
        _altCheck.Checked = _settings.HotkeyAlt;
        _shiftCheck.Checked = _settings.HotkeyShift;
        _hotkeyCombo.SelectedItem = _settings.HotkeyKey;
        if (_hotkeyCombo.SelectedIndex < 0)
        {
            _hotkeyCombo.SelectedItem = "F8";
        }

        _engineCombo.SelectedIndex = _settings.CaptureMode switch
        {
            "dda" => 1,
            "gdi" => 2,
            _ => 0
        };

        _audioDelayInput.Value = Clamp(_settings.AudioDelayMs, 0, 5000);
        _githubOwnerBox.Text = _settings.GitHubOwner;
        _githubRepoBox.Text = _settings.GitHubRepo;
        _checkUpdatesOnStartCheck.Checked = _settings.CheckUpdatesOnStart;
        _folderLabel.Text = ShortenPath(_settings.OutputFolder, 46);
        LoadAudioDevicesToUi(false);
        UpdateHotkeyLabel();
    }

    private void SaveUiSettings()
    {
        _settings.BufferSeconds = BufferSeconds;
        _settings.Fps = Fps;
        _settings.BitrateMbps = BitrateMbps;
        _settings.IncludeAudio = _audioCheck.Checked;
        _settings.OutputDeviceIds = _outputDeviceList.CheckedItems.OfType<AudioDeviceItem>().Select(item => item.Id).ToList();
        _settings.MicDeviceIds = _micDeviceList.CheckedItems.OfType<AudioDeviceItem>().Select(item => item.Id).ToList();
        _settings.AudioDelayMs = (int)_audioDelayInput.Value;
        _settings.DrawMouse = _mouseCheck.Checked;
        _settings.HotkeyCtrl = _ctrlCheck.Checked;
        _settings.HotkeyAlt = _altCheck.Checked;
        _settings.HotkeyShift = _shiftCheck.Checked;
        _settings.HotkeyKey = Convert.ToString(_hotkeyCombo.SelectedItem) ?? "F8";
        _settings.CaptureMode = _engineCombo.SelectedIndex switch
        {
            1 => "dda",
            2 => "gdi",
            _ => "auto"
        };
        _settings.GitHubOwner = _githubOwnerBox.Text.Trim();
        _settings.GitHubRepo = _githubRepoBox.Text.Trim();
        _settings.CheckUpdatesOnStart = _checkUpdatesOnStartCheck.Checked;
        _settings.Normalize();
        SaveSettings();
    }

    private async Task StartRecordingAsync()
    {
        if (_recording)
        {
            Log("상시녹화가 이미 실행 중입니다.");
            return;
        }

        SaveUiSettings();
        Directory.CreateDirectory(AppPaths.BufferFolder);
        CleanBufferFolder(true);
        _ffmpegLog.Clear();
        _manualStopping = false;
        _statusLabel.Text = "상시녹화 시작 중";
        _engineLabel.Text = "캡처 엔진 : 준비 중";
        _toggleRecordButton.Enabled = false;

        var attempts = BuildCaptureAttempts();

        foreach (var attempt in attempts)
        {
            try
            {
                Log($"캡처 엔진 시작 시도: {attempt.Name}");
                var process = StartFfmpegProcess(attempt.Arguments);
                await Task.Delay(2800);

                if (!process.HasExited)
                {
                    _ffmpegProcess = process;
                    _recording = true;
                    _recordingStartedAt = DateTime.Now;
                    _activeEngineName = attempt.Name;
                    _statusLabel.Text = "상시녹화 중";
                    _engineLabel.Text = "캡처 엔진 : " + attempt.Name;
                    _toggleRecordButton.Text = "상시녹화 중지";
                    _toggleRecordButton.Enabled = true;
                    StartAudioCaptureIfNeeded();
                    Log("상시녹화가 시작되었습니다.");
                    return;
                }

                Log($"캡처 엔진 실패: {attempt.Name}");
                Log(GetLastFfmpegLine());
            }
            catch (Exception ex)
            {
                Log($"캡처 엔진 시작 실패: {attempt.Name} / {ex.Message}");
            }
        }

        _recording = false;
        _activeEngineName = "실패";
        _statusLabel.Text = "상시녹화 실패";
        _engineLabel.Text = "캡처 엔진 : 실패";
        _toggleRecordButton.Text = "상시녹화 시작";
        _toggleRecordButton.Enabled = true;
        Log("모든 캡처 엔진 시작에 실패했습니다. 창 전체화면 또는 관리자 권한 실행으로 다시 확인해 주세요.");
    }

    private void StopRecording(bool log)
    {
        _manualStopping = true;
        var process = _ffmpegProcess;
        _ffmpegProcess = null;

        if (process != null && !process.HasExited)
        {
            try
            {
                process.StandardInput.WriteLine("q");
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                }
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }
            }
        }

        StopAudioCapture();
        process?.Dispose();
        _recording = false;
        _activeEngineName = "대기 중";
        _statusLabel.Text = "상시녹화 중지됨";
        _engineLabel.Text = "캡처 엔진 : 대기 중";
        _toggleRecordButton.Text = "상시녹화 시작";
        _toggleRecordButton.Enabled = true;

        if (log)
        {
            Log("상시녹화를 중지했습니다.");
        }
    }

    private Process StartFfmpegProcess(string arguments)
    {
        var ffmpegPath = EnsureFfmpeg();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = AppPaths.BufferFolder
            },
            EnableRaisingEvents = true
        };

        process.ErrorDataReceived += (_, e) => AppendFfmpegLog(e.Data);
        process.OutputDataReceived += (_, e) => AppendFfmpegLog(e.Data);
        process.Exited += (_, _) =>
        {
            if (!_manualStopping && _recording)
            {
                var previousEngine = _activeEngineName;

                SafeUi(() =>
                {
                    _recording = false;
                    _ffmpegProcess = null;
                    StopAudioCapture();
                    _statusLabel.Text = "상시녹화 중단됨";
                    _engineLabel.Text = "캡처 엔진 : 중단됨";
                    _toggleRecordButton.Text = "상시녹화 시작";
                    Log("FFmpeg 캡처 프로세스가 종료되었습니다. " + GetLastFfmpegLine());

                    if (_settings.CaptureMode == "auto" && previousEngine.Contains("DDA", StringComparison.OrdinalIgnoreCase))
                    {
                        _forceGdiUntilManualStart = true;
                        Log("DDA 캡처가 중단되어 호환 GDI로 자동 재시도합니다.");
                        _ = Task.Delay(800).ContinueWith(_ => SafeUi(() => _ = StartRecordingAsync()));
                    }
                });
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }

    private List<CaptureAttempt> BuildCaptureAttempts()
    {
        var attempts = new List<CaptureAttempt>();
        var mode = _settings.CaptureMode;

        if (mode is "auto" or "dda")
        {
            if (!_forceGdiUntilManualStart || mode == "dda")
            {
                attempts.Add(BuildDdaAttempt());
            }
        }

        if (mode is "auto" or "gdi")
        {
            attempts.Add(BuildGdiAttempt());
        }

        return attempts;
    }

    private CaptureAttempt BuildDdaAttempt()
    {
        var segmentPath = Path.Combine(AppPaths.BufferFolder, "seg_%Y%m%d_%H%M%S.ts");
        var mouse = _settings.DrawMouse ? "1" : "0";
        var videoInput = $"-f lavfi -i \"ddagrab=framerate={Fps}:output_idx=0:draw_mouse={mouse}\"";

        var args = $"-hide_banner -loglevel warning {videoInput} -map 0:v:0 -an " +
                   $"-c:v h264_nvenc -preset p5 -rc vbr -cq 23 -b:v {BitrateMbps}M -maxrate {BitrateMbps * 2}M -bufsize {BitrateMbps * 4}M " +
                   $"-g {Fps} -force_key_frames \"expr:gte(t,n_forced*1)\" " +
                   $"-f segment -segment_time 1 -segment_format mpegts -reset_timestamps 1 -strftime 1 \"{segmentPath}\"";

        return new CaptureAttempt("고성능 DDA", args);
    }

    private CaptureAttempt BuildGdiAttempt()
    {
        var segmentPath = Path.Combine(AppPaths.BufferFolder, "seg_%Y%m%d_%H%M%S.ts");
        var mouse = _settings.DrawMouse ? "1" : "0";
        var videoInput = $"-f gdigrab -framerate {Fps} -draw_mouse {mouse} -i desktop";

        var args = $"-hide_banner -loglevel warning {videoInput} -map 0:v:0 -an " +
                   $"-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p -b:v {BitrateMbps}M " +
                   $"-g {Fps} -force_key_frames \"expr:gte(t,n_forced*1)\" " +
                   $"-f segment -segment_time 1 -segment_format mpegts -reset_timestamps 1 -strftime 1 \"{segmentPath}\"";

        return new CaptureAttempt("호환 GDI", args);
    }

    private void LoadAudioDevicesToUi(bool log)
    {
        var previousOutputIds = _outputDeviceList.CheckedItems.OfType<AudioDeviceItem>().Select(item => item.Id).ToHashSet();
        var previousMicIds = _micDeviceList.CheckedItems.OfType<AudioDeviceItem>().Select(item => item.Id).ToHashSet();

        if (previousOutputIds.Count == 0)
        {
            previousOutputIds = _settings.OutputDeviceIds.ToHashSet();
        }

        if (previousMicIds.Count == 0)
        {
            previousMicIds = _settings.MicDeviceIds.ToHashSet();
        }

        _outputDevices.Clear();
        _micDevices.Clear();
        _outputDeviceList.Items.Clear();
        _micDeviceList.Items.Clear();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultOutputId = string.Empty;

            try
            {
                defaultOutputId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch
            {
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var item = new AudioDeviceItem(device.ID, device.FriendlyName, false);
                _outputDevices.Add(item);
                var index = _outputDeviceList.Items.Add(item);
                var shouldCheck = previousOutputIds.Count > 0 ? previousOutputIds.Contains(item.Id) : item.Id == defaultOutputId;
                _outputDeviceList.SetItemChecked(index, shouldCheck);
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                var item = new AudioDeviceItem(device.ID, device.FriendlyName, true);
                _micDevices.Add(item);
                var index = _micDeviceList.Items.Add(item);
                _micDeviceList.SetItemChecked(index, previousMicIds.Contains(item.Id));
            }

            if (log)
            {
                Log($"오디오 장치 목록을 새로고침했습니다. 출력 {_outputDevices.Count}개 / 마이크 {_micDevices.Count}개");
            }
        }
        catch (Exception ex)
        {
            if (log)
            {
                Log("오디오 장치 목록 불러오기 실패: " + ex.Message);
            }
        }
    }

    private void StartAudioCaptureIfNeeded()
    {
        StopAudioCapture();

        if (!_settings.IncludeAudio)
        {
            return;
        }

        SaveUiSettings();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var started = 0;

            var outputIds = _settings.OutputDeviceIds.ToList();
            if (outputIds.Count == 0)
            {
                try
                {
                    outputIds.Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID);
                }
                catch
                {
                }
            }

            foreach (var id in outputIds.Distinct())
            {
                try
                {
                    var device = enumerator.GetDevice(id);
                    var capture = new WasapiLoopbackCapture(device);
                    var session = new AudioCaptureSession(device.FriendlyName, capture, capture.WaveFormat);
                    capture.DataAvailable += (_, e) => OnAudioDataAvailable(session, e);
                    capture.RecordingStopped += (_, e) =>
                    {
                        session.IsRunning = false;
                        if (e.Exception != null)
                        {
                            SafeUi(() => Log($"출력 장치 녹음 중단: {session.Name} / {e.Exception.Message}"));
                        }
                    };
                    _audioSessions.Add(session);
                    capture.StartRecording();
                    session.IsRunning = true;
                    started++;
                    Log("출력 장치 녹음 시작: " + device.FriendlyName);
                }
                catch (Exception ex)
                {
                    Log("출력 장치 녹음 시작 실패: " + ex.Message);
                }
            }

            foreach (var id in _settings.MicDeviceIds.Distinct())
            {
                try
                {
                    var device = enumerator.GetDevice(id);
                    var capture = new WasapiCapture(device);
                    var session = new AudioCaptureSession(device.FriendlyName, capture, capture.WaveFormat);
                    capture.DataAvailable += (_, e) => OnAudioDataAvailable(session, e);
                    capture.RecordingStopped += (_, e) =>
                    {
                        session.IsRunning = false;
                        if (e.Exception != null)
                        {
                            SafeUi(() => Log($"마이크 녹음 중단: {session.Name} / {e.Exception.Message}"));
                        }
                    };
                    _audioSessions.Add(session);
                    capture.StartRecording();
                    session.IsRunning = true;
                    started++;
                    Log("마이크 녹음 시작: " + device.FriendlyName);
                }
                catch (Exception ex)
                {
                    Log("마이크 녹음 시작 실패: " + ex.Message);
                }
            }

            _audioRunning = started > 0;

            if (!_audioRunning)
            {
                Log("선택된 오디오 장치로 녹음을 시작하지 못했습니다.");
            }
        }
        catch (Exception ex)
        {
            _audioRunning = false;
            Log("오디오 녹음 시작 실패: " + ex.Message);
        }
    }

    private void StopAudioCapture()
    {
        foreach (var session in _audioSessions.ToList())
        {
            try
            {
                session.Capture.StopRecording();
            }
            catch
            {
            }

            try
            {
                if (session.Capture is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
            }
        }

        _audioSessions.Clear();
        _audioRunning = false;
    }

    private void OnAudioDataAvailable(AudioCaptureSession session, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        lock (session.Lock)
        {
            for (var i = 0; i < e.BytesRecorded; i++)
            {
                session.Ring.Add(e.Buffer[i]);
            }

            var averageBytesPerSecond = Math.Max(1, session.Format.AverageBytesPerSecond);
            var maxBytes = averageBytesPerSecond * (BufferSeconds + 12);
            if (session.Ring.Count > maxBytes)
            {
                session.Ring.RemoveRange(0, session.Ring.Count - maxBytes);
            }
        }
    }

    private async Task<List<string>> WriteRecentAudioFilesAsync(string folder, int seconds)
    {
        var files = new List<string>();
        var delayMs = Clamp(_settings.AudioDelayMs, 0, 5000);
        var index = 0;

        foreach (var session in _audioSessions.ToList())
        {
            byte[] audioBytes;
            var format = session.Format;

            lock (session.Lock)
            {
                if (session.Ring.Count == 0)
                {
                    continue;
                }

                var blockAlign = Math.Max(1, format.BlockAlign);
                var delayBytes = AlignToBlock((int)(format.AverageBytesPerSecond * (delayMs / 1000.0)), blockAlign);
                var availableBytes = Math.Max(0, session.Ring.Count - delayBytes);
                var requestedBytes = AlignToBlock(format.AverageBytesPerSecond * Math.Max(1, seconds), blockAlign);
                var bytesToTake = Math.Min(availableBytes, requestedBytes);
                bytesToTake = AlignToBlock(bytesToTake, blockAlign);

                if (bytesToTake < blockAlign * 100)
                {
                    continue;
                }

                var start = Math.Max(0, session.Ring.Count - delayBytes - bytesToTake);
                audioBytes = session.Ring.Skip(start).Take(bytesToTake).ToArray();
            }

            if (audioBytes.Length < 1024)
            {
                continue;
            }

            var path = Path.Combine(folder, $"audio_{index++:00}.wav");
            await Task.Run(() =>
            {
                using var writer = new WaveFileWriter(path, format);
                writer.Write(audioBytes, 0, audioBytes.Length);
            });

            if (File.Exists(path) && new FileInfo(path).Length > 1024)
            {
                files.Add(path);
            }
        }

        return files;
    }

    private static int AlignToBlock(int value, int blockAlign)
    {
        if (blockAlign <= 1)
        {
            return value;
        }

        return value - value % blockAlign;
    }

    private async Task SaveClipAsync(string reason)
    {
        if (_saving)
        {
            Log("이미 클립을 저장 중입니다.");
            return;
        }

        var segments = GetCompletedSegments().TakeLast(BufferSeconds).ToList();
        if (segments.Count == 0)
        {
            Log("저장할 영상이 아직 없습니다. 상시녹화를 잠시 유지한 뒤 다시 저장해 주세요.");
            return;
        }

        _saving = true;
        _saveButton.Enabled = false;
        _statusLabel.Text = "클립 저장 중";

        var seconds = Math.Min(BufferSeconds, segments.Count);
        var workFolder = Path.Combine(AppPaths.LocalAppDataFolder, "clipwork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workFolder);

        var joinedTsPath = Path.Combine(workFolder, "joined.ts");
        var tempVideoPath = Path.Combine(workFolder, "video.mp4");
        var outputPath = Path.Combine(_settings.OutputFolder, $"ShotLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{reason}.mp4");

        try
        {
            await JoinTransportStreamFilesAsync(segments, joinedTsPath);

            if (!File.Exists(joinedTsPath) || new FileInfo(joinedTsPath).Length < 1024 * 100)
            {
                Log("클립 저장 실패: 임시 영상 조각이 너무 작거나 비어 있습니다.");
                return;
            }

            var ffmpegPath = EnsureFfmpeg();
            Log($"클립 생성 중: {segments.Count}개 조각 / 약 {seconds}초");

            var copyArgs = $"-hide_banner -loglevel warning -y -fflags +genpts -i \"{joinedTsPath}\" -map 0:v:0 -c:v copy -an -movflags +faststart \"{tempVideoPath}\"";
            var copyResult = await RunProcessAsync(ffmpegPath, copyArgs);

            if (copyResult.ExitCode != 0 || !File.Exists(tempVideoPath) || new FileInfo(tempVideoPath).Length < 1024)
            {
                Log("빠른 저장 실패, 재인코딩으로 다시 저장합니다: " + LastLine(copyResult.ErrorText));
                var reencodeArgs = $"-hide_banner -loglevel warning -y -fflags +genpts -i \"{joinedTsPath}\" -map 0:v:0 -c:v h264_nvenc -preset p5 -cq 23 -b:v {BitrateMbps}M -an -movflags +faststart \"{tempVideoPath}\"";
                var reencodeResult = await RunProcessAsync(ffmpegPath, reencodeArgs);

                if (reencodeResult.ExitCode != 0 || !File.Exists(tempVideoPath) || new FileInfo(tempVideoPath).Length < 1024)
                {
                    Log("NVENC 재인코딩 실패, CPU 인코딩으로 다시 저장합니다: " + LastLine(reencodeResult.ErrorText));
                    var x264Args = $"-hide_banner -loglevel warning -y -fflags +genpts -i \"{joinedTsPath}\" -map 0:v:0 -c:v libx264 -preset veryfast -crf 23 -an -movflags +faststart \"{tempVideoPath}\"";
                    var x264Result = await RunProcessAsync(ffmpegPath, x264Args);

                    if (x264Result.ExitCode != 0 || !File.Exists(tempVideoPath) || new FileInfo(tempVideoPath).Length < 1024)
                    {
                        Log("클립 저장 실패: " + LastLine(x264Result.ErrorText));
                        return;
                    }
                }
            }

            var audioAdded = false;
            var audioFiles = _settings.IncludeAudio ? await WriteRecentAudioFilesAsync(workFolder, seconds) : new List<string>();

            if (audioFiles.Count > 0)
            {
                var inputArgs = new StringBuilder();
                inputArgs.Append($"-hide_banner -loglevel warning -y -i \"{tempVideoPath}\" ");

                foreach (var audioFile in audioFiles)
                {
                    inputArgs.Append($"-i \"{audioFile}\" ");
                }

                string muxArgs;

                if (audioFiles.Count == 1)
                {
                    muxArgs = inputArgs + $"-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart \"{outputPath}\"";
                }
                else
                {
                    var filterInputs = string.Concat(Enumerable.Range(1, audioFiles.Count).Select(i => $"[{i}:a]"));
                    muxArgs = inputArgs + $"-filter_complex \"{filterInputs}amix=inputs={audioFiles.Count}:duration=longest:dropout_transition=0[aout]\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart \"{outputPath}\"";
                }

                var muxResult = await RunProcessAsync(ffmpegPath, muxArgs);
                audioAdded = muxResult.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024;

                if (!audioAdded)
                {
                    Log("소리 병합 실패, 영상만 저장합니다: " + LastLine(muxResult.ErrorText));
                }
            }

            if (!audioAdded)
            {
                File.Copy(tempVideoPath, outputPath, true);
            }

            Log($"클립 저장 완료: 약 {seconds}초 / {(audioAdded ? $"소리 포함({audioFiles.Count}개)" : "영상만")} / {Path.GetFileName(outputPath)}");
        }
        catch (Exception ex)
        {
            Log("클립 저장 실패: " + ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(workFolder, true);
            }
            catch
            {
            }

            _saving = false;
            _saveButton.Enabled = true;
            _statusLabel.Text = _recording ? "상시녹화 중" : "상시녹화 중지됨";
        }
    }


    private static async Task JoinTransportStreamFilesAsync(List<string> segments, string outputPath)
    {
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        foreach (var segment in segments)
        {
            try
            {
                await using var input = new FileStream(segment, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await input.CopyToAsync(output);
            }
            catch
            {
            }
        }
    }

    private void UpdateBufferState()
    {
        CleanBufferFolder(false);
        var count = GetCompletedSegments().Count;
        var available = Math.Min(BufferSeconds, count);
        var audioStatus = _settings.IncludeAudio ? (_audioRunning ? $" / 오디오 {_audioSessions.Count}개 녹음 중" : " / 오디오 대기") : "";
        _availableLabel.Text = $"저장 가능 영상 : {available}초 / {BufferSeconds}초 ({count}개 조각){audioStatus}";
        _toggleRecordButton.Text = _recording ? "상시녹화 중지" : "상시녹화 시작";
        _engineLabel.Text = "캡처 엔진 : " + _activeEngineName;
    }

    private List<string> GetCompletedSegments()
    {
        if (!Directory.Exists(AppPaths.BufferFolder))
        {
            return new List<string>();
        }

        var now = DateTime.Now;
        return Directory.GetFiles(AppPaths.BufferFolder, "seg_*.ts")
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.Length > 1024 * 100 && (now - file.LastWriteTime).TotalMilliseconds > 2200)
            .OrderBy(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToList();
    }

    private void CleanBufferFolder(bool all)
    {
        if (!Directory.Exists(AppPaths.BufferFolder))
        {
            return;
        }

        var cutoff = DateTime.Now.AddSeconds(-(BufferSeconds + 12));
        foreach (var file in Directory.GetFiles(AppPaths.BufferFolder, "seg_*.ts"))
        {
            try
            {
                var info = new FileInfo(file);
                if (all || info.LastWriteTime < cutoff)
                {
                    info.Delete();
                }
            }
            catch
            {
            }
        }
    }

    private async Task CheckForUpdatesAsync(bool showNoUpdate)
    {
        SaveUiSettings();

        if (string.IsNullOrWhiteSpace(_settings.GitHubOwner) || string.IsNullOrWhiteSpace(_settings.GitHubRepo))
        {
            if (showNoUpdate)
            {
                MessageBox.Show("GitHub 소유자와 저장소 이름을 입력해 주세요.", "ShotLog 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            _checkUpdateButton.Enabled = false;
            Log("업데이트 확인 중...");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShotLog", Program.AppVersion.ToString()));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var url = $"https://api.github.com/repos/{_settings.GitHubOwner}/{_settings.GitHubRepo}/releases/latest";
            var json = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = ParseVersion(tag);

            if (latestVersion == null || latestVersion.CompareTo(Program.AppVersion) <= 0)
            {
                if (showNoUpdate)
                {
                    MessageBox.Show("현재 최신 버전을 사용 중입니다.", "ShotLog 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                Log("최신 버전입니다.");
                return;
            }

            var asset = FindUpdateAsset(root.GetProperty("assets"));
            if (asset == null)
            {
                MessageBox.Show("최신 릴리즈에 ShotLog.exe 또는 ShotLog.zip 파일이 없습니다.", "ShotLog 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"새 버전이 있습니다.\n\n현재 버전: {Program.AppVersion}\n최신 버전: {latestVersion}\n\n지금 업데이트할까요?",
                "ShotLog 업데이트",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            await DownloadAndApplyUpdateAsync(client, asset.Value.DownloadUrl, asset.Value.Name);
        }
        catch (Exception ex)
        {
            Log("업데이트 확인 실패: " + ex.Message);
            if (showNoUpdate)
            {
                MessageBox.Show("업데이트 확인에 실패했습니다.\n\n" + ex.Message, "ShotLog 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkUpdateButton.Enabled = true;
        }
    }

    private async Task DownloadAndApplyUpdateAsync(HttpClient client, string downloadUrl, string assetName)
    {
        Directory.CreateDirectory(AppPaths.UpdatesFolder);
        foreach (var file in Directory.GetFiles(AppPaths.UpdatesFolder))
        {
            try { File.Delete(file); } catch { }
        }

        var downloadPath = Path.Combine(AppPaths.UpdatesFolder, assetName);
        await using (var stream = await client.GetStreamAsync(downloadUrl))
        await using (var file = File.Create(downloadPath))
        {
            await stream.CopyToAsync(file);
        }

        var newExe = downloadPath;
        if (Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractFolder = Path.Combine(AppPaths.UpdatesFolder, "extracted");
            if (Directory.Exists(extractFolder))
            {
                Directory.Delete(extractFolder, true);
            }
            ZipFile.ExtractToDirectory(downloadPath, extractFolder);
            newExe = Directory.GetFiles(extractFolder, "ShotLog.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new FileNotFoundException("압축 파일 안에서 ShotLog.exe를 찾지 못했습니다.");
        }

        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("게시된 ShotLog.exe로 실행 중일 때만 자동 업데이트를 적용할 수 있습니다.");
        }

        var updaterPath = Path.Combine(AppPaths.UpdatesFolder, "update.cmd");
        var script = $$"""
@echo off
chcp 65001 > nul
timeout /t 2 /nobreak > nul
taskkill /PID {{Environment.ProcessId}} /F > nul 2>nul
copy /Y "{{newExe}}" "{{currentExe}}" > nul
start "" "{{currentExe}}"
del "{{newExe}}" > nul 2>nul
del "%~f0" > nul 2>nul
""";
        await File.WriteAllTextAsync(updaterPath, script, Encoding.UTF8);

        Log("업데이트 파일 다운로드 완료. ShotLog를 재시작합니다.");
        Process.Start(new ProcessStartInfo(updaterPath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        _exitRequested = true;
        Close();
    }

    private static UpdateAsset? FindUpdateAsset(JsonElement assets)
    {
        UpdateAsset? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (name.Equals("ShotLog.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateAsset(name, url);
            }

            if (name.Equals("ShotLog.zip", StringComparison.OrdinalIgnoreCase))
            {
                fallback = new UpdateAsset(name, url);
            }
            else if (fallback == null && name.Contains("ShotLog", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                fallback = new UpdateAsset(name, url);
            }
        }

        return fallback;
    }

    private static Version? ParseVersion(string tag)
    {
        tag = tag.Trim().TrimStart('v', 'V');
        var match = Regex.Match(tag, @"\d+(\.\d+){1,3}");
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var version) ? version : null;
    }

    private void SetOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "클립 저장 폴더를 선택하세요.",
            SelectedPath = _settings.OutputFolder,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.OutputFolder = dialog.SelectedPath;
        Directory.CreateDirectory(_settings.OutputFolder);
        SaveSettings();
        _folderLabel.Text = ShortenPath(_settings.OutputFolder, 46);
        Log("저장 폴더를 변경했습니다.");
    }

    private void ApplyHotkey(bool showMessage)
    {
        SaveUiSettings();
        UnregisterCurrentHotkey();

        var vk = HotkeyToVirtualKey(_settings.HotkeyKey);
        var modifiers = ModNoRepeat;
        if (_settings.HotkeyCtrl) modifiers |= ModControl;
        if (_settings.HotkeyAlt) modifiers |= ModAlt;
        if (_settings.HotkeyShift) modifiers |= ModShift;

        _hotkeyRegistered = RegisterHotKey(Handle, HotKeyId, modifiers, vk);
        UpdateHotkeyLabel();

        if (!_hotkeyRegistered)
        {
            Log("단축키 등록에 실패했습니다. 다른 프로그램이 같은 단축키를 사용 중일 수 있습니다.");
        }
        else if (showMessage)
        {
            Log("단축키를 적용했습니다: " + GetHotkeyDisplay());
        }
    }

    private void UnregisterCurrentHotkey()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotKeyId);
            _hotkeyRegistered = false;
        }
    }

    private void UpdateHotkeyLabel()
    {
        _hotkeyInfoLabel.Text = "클립 저장 단축키 : " + GetHotkeyDisplay();
    }

    private string GetHotkeyDisplay()
    {
        var parts = new List<string>();
        if (_settings.HotkeyCtrl) parts.Add("Ctrl");
        if (_settings.HotkeyAlt) parts.Add("Alt");
        if (_settings.HotkeyShift) parts.Add("Shift");
        parts.Add(_settings.HotkeyKey);
        return string.Join(" + ", parts);
    }

    private static int HotkeyToVirtualKey(string key)
    {
        return key switch
        {
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            _ when Regex.IsMatch(key, @"^F\d{1,2}$") => 0x70 + int.Parse(key[1..]) - 1,
            _ => 0x77
        };
    }

    private void ToggleStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null)
            {
                return;
            }

            if (IsStartupEnabled())
            {
                key.DeleteValue("ShotLog", false);
                Log("시작 프로그램에서 제거했습니다.");
            }
            else
            {
                var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue("ShotLog", $"\"{exePath}\"");
                Log("시작 프로그램에 추가했습니다.");
            }

            RefreshStartupMenu();
        }
        catch (Exception ex)
        {
            Log("시작 프로그램 설정 실패: " + ex.Message);
        }
    }

    private bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("ShotLog") != null;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshStartupMenu()
    {
        _startupMenuItem.Checked = IsStartupEnabled();
        _startupMenuItem.Text = IsStartupEnabled() ? "시작 프로그램에 추가됨" : "시작 프로그램에 추가";
    }

    private void AppendFfmpegLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_ffmpegLog)
        {
            _ffmpegLog.AppendLine(line);
            if (_ffmpegLog.Length > 12000)
            {
                _ffmpegLog.Remove(0, _ffmpegLog.Length - 8000);
            }
        }
    }

    private string GetLastFfmpegLine()
    {
        lock (_ffmpegLog)
        {
            var lines = _ffmpegLog.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.LastOrDefault() ?? "FFmpeg 로그 없음";
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private string EnsureFfmpeg()
    {
        Directory.CreateDirectory(AppPaths.BinFolder);
        var target = Path.Combine(AppPaths.BinFolder, "ffmpeg.exe");

        if (File.Exists(target) && new FileInfo(target).Length > 1024 * 1024)
        {
            return target;
        }

        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local))
        {
            File.Copy(local, target, true);
            return target;
        }

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ffmpeg.exe");
        if (resource == null)
        {
            throw new FileNotFoundException("ffmpeg.exe를 찾지 못했습니다.");
        }

        using var file = File.Create(target);
        resource.CopyTo(file);
        return target;
    }

    private static string EscapeConcatPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "'\\''");
    }

    private static string LastLine(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? text;
    }

    private void Log(string message)
    {
        SafeUi(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            _logBox.AppendText(line);
        });
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
        }
        else
        {
            action();
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(_settings, _jsonOptions), Encoding.UTF8);
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath, Encoding.UTF8)) ?? new AppSettings();
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    private Icon LoadIconResource()
    {
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShotLog.ico");
            if (resource != null)
            {
                return new Icon(resource);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    private Image? LoadPngResource()
    {
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShotLog.png");
            if (resource != null)
            {
                using var ms = new MemoryStream();
                resource.CopyTo(ms);
                return Image.FromStream(new MemoryStream(ms.ToArray()));
            }
        }
        catch
        {
        }

        return null;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string ShortenPath(string path, int max)
    {
        if (path.Length <= max)
        {
            return path;
        }

        return path[..18] + "..." + path[^Math.Min(24, path.Length)..];
    }

    private sealed class AudioDeviceItem
    {
        public AudioDeviceItem(string id, string name, bool isInput)
        {
            Id = id;
            Name = name;
            IsInput = isInput;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsInput { get; }

        public override string ToString() => Name;
    }

    private sealed class AudioCaptureSession
    {
        public AudioCaptureSession(string name, IWaveIn capture, WaveFormat format)
        {
            Name = name;
            Capture = capture;
            Format = format;
        }

        public string Name { get; }
        public IWaveIn Capture { get; }
        public WaveFormat Format { get; }
        public object Lock { get; } = new();
        public List<byte> Ring { get; } = new();
        public bool IsRunning { get; set; }
    }

    private readonly record struct CaptureAttempt(string Name, string Arguments);
    private readonly record struct ProcessResult(int ExitCode, string OutputText, string ErrorText);
    private readonly record struct UpdateAsset(string Name, string DownloadUrl);
}

internal sealed class AppSettings
{
    public int BufferSeconds { get; set; } = 30;
    public int Fps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 20;
    public bool IncludeAudio { get; set; } = true;
    public List<string> OutputDeviceIds { get; set; } = new();
    public List<string> MicDeviceIds { get; set; } = new();
    public int AudioDelayMs { get; set; } = 1500;
    public bool DrawMouse { get; set; } = false;
    public string CaptureMode { get; set; } = "auto";
    public string OutputFolder { get; set; } = AppPaths.DefaultOutputFolder;
    public string HotkeyKey { get; set; } = "F8";
    public bool HotkeyCtrl { get; set; } = false;
    public bool HotkeyAlt { get; set; } = false;
    public bool HotkeyShift { get; set; } = false;
    public bool CheckUpdatesOnStart { get; set; } = true;
    public string GitHubOwner { get; set; } = "nyexgs";
    public string GitHubRepo { get; set; } = "ShotLog";

    public void Normalize()
    {
        BufferSeconds = Math.Max(5, Math.Min(120, BufferSeconds));
        Fps = Fps < 30 ? 60 : Math.Max(30, Math.Min(144, Fps));
        BitrateMbps = Math.Max(5, Math.Min(80, BitrateMbps));
        OutputDeviceIds ??= new List<string>();
        MicDeviceIds ??= new List<string>();
        AudioDelayMs = Math.Max(0, Math.Min(5000, AudioDelayMs));

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            OutputFolder = AppPaths.DefaultOutputFolder;
        }

        if (string.IsNullOrWhiteSpace(HotkeyKey))
        {
            HotkeyKey = "F8";
        }

        if (CaptureMode is not ("auto" or "dda" or "gdi"))
        {
            CaptureMode = "auto";
        }
    }
}
