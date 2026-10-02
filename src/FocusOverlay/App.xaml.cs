using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FocusOverlay.Services;

namespace FocusOverlay;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private OverlayController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, "FocusOverlay.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Focus Overlay уже запущен — ищите иконку в трее.", "Focus Overlay");
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _controller = new OverlayController();
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

        MessageBox.Show(e.Exception.Message, "Focus Overlay — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
