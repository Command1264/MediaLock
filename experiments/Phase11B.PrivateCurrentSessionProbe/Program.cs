using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NPSMLib;
using Windows.Media;
using Windows.Media.Control;

namespace MediaLock.PrivateCurrentSessionProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ProbeForm());
    }
}

internal sealed class ProbeForm : Form
{
    private readonly TextBox output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9),
    };
    private readonly Button inspectButton = new()
    {
        Text = "Inspect current Sessions",
        AutoSize = true,
    };
    private readonly Button setCurrentButton = new()
    {
        Text = "Set this probe Current once (private)",
        AutoSize = true,
        Enabled = false,
    };
    private readonly Button copyButton = new()
    {
        Text = "Copy evidence",
        AutoSize = true,
    };
    private readonly Button exitButton = new()
    {
        Text = "Disable and exit",
        AutoSize = true,
    };

    private SystemMediaTransportControls? controls;
    private bool privateSetterAttempted;

    public ProbeForm()
    {
        Text = "Media Lock Phase 11B — PRIVATE API SANDBOX PROBE";
        Width = 940;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;

        var warning = new Label
        {
            AutoSize = true,
            ForeColor = Color.DarkRed,
            Font = new Font(Font, FontStyle.Bold),
            Text = "UNSUPPORTED PRIVATE API — WINDOWS SANDBOX ONLY — ONE SETTER CALL",
            Padding = new Padding(8),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange([inspectButton, setCurrentButton, copyButton, exitButton]);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(warning, 0, 0);
        layout.Controls.Add(output, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        Shown += OnShown;
        FormClosing += OnFormClosing;
        inspectButton.Click += async (_, _) => await InspectAsync("manual inspection");
        setCurrentButton.Click += async (_, _) => await SetCurrentOnceAsync();
        copyButton.Click += (_, _) => Clipboard.SetText(output.Text);
        exitButton.Click += (_, _) => Close();
    }

    private async void OnShown(object? sender, EventArgs args)
    {
        try
        {
            Log($"Timestamp: {DateTimeOffset.Now:O}");
            Log($"Windows: {DescribeWindows()}");
            Log($"Architecture: {RuntimeInformation.OSArchitecture}");
            Log($"Process: {Environment.ProcessId}; HWND: 0x{Handle.ToInt64():X}");
            Log("Publishing documented desktop SMTC Session...");

            controls = SystemMediaTransportControlsInterop.GetForWindow(Handle);
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsNextEnabled = true;
            controls.IsPreviousEnabled = true;
            controls.IsStopEnabled = true;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            controls.DisplayUpdater.MusicProperties.Title = "Media Lock private Current Session probe";
            controls.DisplayUpdater.MusicProperties.Artist = "Phase 11B — Windows Sandbox only";
            controls.DisplayUpdater.Update();
            controls.PlaybackStatus = MediaPlaybackStatus.Playing;
            controls.IsEnabled = true;

            var timeline = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = TimeSpan.FromSeconds(30),
                MaxSeekTime = TimeSpan.FromMinutes(3),
                EndTime = TimeSpan.FromMinutes(3),
            };
            controls.UpdateTimelineProperties(timeline);

            await Task.Delay(750);
            await InspectAsync("after documented SMTC publication");
            setCurrentButton.Enabled = true;
        }
        catch (Exception exception)
        {
            LogException("SMTC publication failed", exception);
        }
    }

    private static string DescribeWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        var productName = key?.GetValue("ProductName") as string;
        var displayVersion = key?.GetValue("DisplayVersion") as string;
        var currentBuild = key?.GetValue("CurrentBuild") as string;
        var updateBuildRevision = key?.GetValue("UBR")?.ToString();
        var fullBuild = string.IsNullOrWhiteSpace(updateBuildRevision)
            ? currentBuild
            : $"{currentBuild}.{updateBuildRevision}";

        return $"{productName ?? RuntimeInformation.OSDescription} " +
               $"{displayVersion ?? "unknown"} (build {fullBuild ?? "unknown"})";
    }

    private async Task InspectAsync(string reason)
    {
        Log(string.Empty);
        Log($"=== {reason} ===");
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var current = manager.GetCurrentSession();
            Log($"Public GSMTC Current: {current?.SourceAppUserModelId ?? "<none>"}");
            var sessions = manager.GetSessions();
            for (var index = 0; index < sessions.Count; index++)
            {
                var marker = ReferenceEquals(sessions[index], current) ? " CURRENT" : string.Empty;
                Log($"Public GSMTC [{index}]: {sessions[index].SourceAppUserModelId}{marker}");
            }
        }
        catch (Exception exception)
        {
            LogException("Public GSMTC inspection failed", exception);
        }

        InspectPrivateSessions();
    }

    private void InspectPrivateSessions()
    {
        try
        {
            var manager = new NowPlayingSessionManager();
            var current = manager.CurrentSession;
            Log($"Private NPSM Current PID: {current?.PID.ToString() ?? "<none>"}; AppId: {current?.SourceAppId ?? "<none>"}");
            foreach (var session in manager.GetSessions())
            {
                var marker = session.PID == Environment.ProcessId ? " THIS-PROBE" : string.Empty;
                Log($"Private NPSM Session: PID={session.PID}; HWND=0x{session.Hwnd.ToInt64():X}; AppId={session.SourceAppId}{marker}");
            }
        }
        catch (Exception exception)
        {
            LogException("Private NPSM inspection failed", exception);
        }
    }

    private async Task SetCurrentOnceAsync()
    {
        if (privateSetterAttempted)
        {
            Log("Private setter was already attempted; a second call is blocked.");
            return;
        }

        privateSetterAttempted = true;
        setCurrentButton.Enabled = false;
        Log(string.Empty);
        Log("=== ONE PRIVATE SetCurrentSession ATTEMPT ===");

        try
        {
            var manager = new NowPlayingSessionManager();
            var ownSession = manager.GetSessions().SingleOrDefault(
                session => session.PID == Environment.ProcessId);
            if (ownSession is null)
            {
                Log("RESULT: private NPSM did not expose a Session for this probe PID; setter was not called.");
                return;
            }

            var succeeded = manager.SetCurrentSession(ownSession.GetSessionInfo());
            Log($"Private SetCurrentSession returned: {succeeded}");
            await Task.Delay(750);
            await InspectAsync("after one private setter call");
        }
        catch (Exception exception)
        {
            LogException("Private SetCurrentSession failed", exception);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (controls is null)
        {
            return;
        }

        try
        {
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            controls.DisplayUpdater.ClearAll();
            controls.DisplayUpdater.Update();
            controls.IsEnabled = false;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void Log(string message) => output.AppendText(message + Environment.NewLine);

    private void LogException(string message, Exception exception) =>
        Log($"{message}: {exception.GetType().FullName}: {exception.Message} (0x{exception.HResult:X8})");
}
