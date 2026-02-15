#nullable enable
// Slide 10: ConfigureAwait — The Classic Deadlock
// Blocking on async code (.Result) with a SynchronizationContext causes deadlock.
// ConfigureAwait(false) in the library method prevents it.

using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Slide10;

public class MainForm : Form
{
    private readonly Button _btnDeadlock;
    private readonly Button _btnFixed;
    private readonly TextBox _output;

    public MainForm()
    {
        Text = "Slide 10: Classic Deadlock Demo";
        Width = 500;
        Height = 300;

        _btnDeadlock = new Button
        {
            Text = "💀 Deadlock (.Result)",
            Left = 20, Top = 20, Width = 200, Height = 35
        };
        _btnDeadlock.Click += BtnDeadlock_Click;

        _btnFixed = new Button
        {
            Text = "✅ Fixed (async all the way)",
            Left = 240, Top = 20, Width = 200, Height = 35
        };
        _btnFixed.Click += BtnFixed_Click;

        _output = new TextBox
        {
            Multiline = true, ReadOnly = true,
            Left = 20, Top = 70, Width = 440, Height = 170,
            ScrollBars = ScrollBars.Vertical
        };

        Controls.AddRange(new Control[] { _btnDeadlock, _btnFixed, _output });
    }

    private void BtnDeadlock_Click(object? sender, EventArgs e)
    {
        _output.Text = "Calling GetDataAsync().Result — this will deadlock!\r\n"
            + "The UI is frozen. The task's continuation needs the UI thread,\r\n"
            + "but .Result is blocking it. Close with Task Manager if needed.";
        _output.Refresh();

        // 💀 DEADLOCK: .Result blocks the UI thread.
        // GetDataAsync's continuation needs the UI thread to resume.
        var result = GetDataAsync().Result;
        _output.Text = result; // Never reached
    }

    private async void BtnFixed_Click(object? sender, EventArgs e)
    {
        _output.Text = "Calling await GetDataAsync() — no deadlock...\r\n";
        _output.Refresh();

        // ✅ async all the way — no blocking
        var result = await GetDataAsync();
        _output.Text += $"Result: {result}\r\n✅ No deadlock!";
    }

    // Library method — captures SynchronizationContext by default
    private static async Task<string> GetDataAsync()
    {
        await Task.Delay(1000); // continuation posted to UI SyncContext
        return "Data loaded successfully!";
    }
}
