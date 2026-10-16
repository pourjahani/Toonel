using System.Windows;
using System.Windows.Media.Animation;

namespace Toonel.App;

/// <summary>
/// Small themed notification that fades in near the taskbar, holds, then fades out.
/// Fire-and-forget: creates its own window, never steals focus.
/// </summary>
public partial class ToastWindow : Window
{
    public static void Show(string message, int holdMs = 2600)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null) return;
        app.Dispatcher.Invoke(() =>
        {
            try
            {
                var toast = new ToastWindow(message, holdMs);
                toast.Show();
            }
            catch { /* toast is best-effort */ }
        });
    }

    private ToastWindow(string message, int holdMs)
    {
        InitializeComponent();
        Message.Text = message;

        // Bottom-right of the primary screen work area, above the taskbar.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - Height - 24;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = TimeSpan.FromMilliseconds(180 + holdMs),
        };
        fadeOut.Completed += (_, _) => Close();
        this.BeginAnimation(OpacityProperty, fadeIn);
        this.BeginAnimation(OpacityProperty, fadeOut);
    }
}
