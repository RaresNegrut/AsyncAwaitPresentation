// Slide 6: SynchronizationContext in WPF — Demo
// After await, the continuation is posted back to the UI thread
// via DispatcherSynchronizationContext. That's why updating UI controls works.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Slide06;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        var syncCtx = SynchronizationContext.Current?.GetType().Name ?? "null";
        var threadBefore = Environment.CurrentManagedThreadId;

        StatusLabel.Text = $"Loading... (Thread: {threadBefore}, SyncCtx: {syncCtx})";

        // Simulate async I/O — continuation will be posted back to UI thread
        await Task.Delay(1500);

        var threadAfter = Environment.CurrentManagedThreadId;
        syncCtx = SynchronizationContext.Current?.GetType().Name ?? "null";

        // ✅ Safe — we're back on the UI thread thanks to DispatcherSynchronizationContext
        StatusLabel.Text = $"Done! (Thread: {threadAfter}, SyncCtx: {syncCtx})\n"
            + $"Before: Thread {threadBefore} → After: Thread {threadAfter}\n"
            + "Continuation was posted back to the UI thread. ✅";
    }
}
