using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using FocusOverlay.Services;

namespace FocusOverlay;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private OverlayController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        LanguageManager.Apply(AppLanguage.System);
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, "FocusOverlay.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(Strings.AppAlreadyRunning, "Focus Overlay");
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        var state = StateStore.Load();
        LanguageManager.Apply(state.Settings.Language);
        _controller = new OverlayController(state);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        if (_controller?.RestartRequested == true && Environment.ProcessPath != null)
        {
            Process.Start(Environment.ProcessPath);
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(StateStore.DataDirectory);
            File.AppendAllText(
                Path.Combine(StateStore.DataDirectory, "error.log"),
                $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        MessageBox.Show(e.Exception.Message, Strings.AppErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
