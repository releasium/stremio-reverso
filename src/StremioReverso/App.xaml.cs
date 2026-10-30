using System.Windows;
using Forms = System.Windows.Forms;

namespace StremioReverso;

public partial class App : Application
{
    Forms.NotifyIcon? _tray;
    Mutex? _single;
    UI.SignInWindow? _signIn;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, "StremioReverso.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { Log.Error("unhandled", args.Exception); args.Handled = true; };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Sign in to Reverso…", null, (_, _) => ShowSignIn());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Stremio Reverso",
            Visible = true,
            ContextMenuStrip = menu,
        };

        var watcher = new HoverWatcher();
        watcher.StatusChanged += s => _tray.Text = s.Length > 63 ? s[..63] : s;
        watcher.Start();
    }

    void ShowSignIn()
    {
        if (_signIn is { IsLoaded: true }) { _signIn.Activate(); return; }
        _signIn = new UI.SignInWindow();
        _signIn.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _single?.Dispose();
        base.OnExit(e);
    }
}
