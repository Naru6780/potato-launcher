using System.Drawing;
using System.Windows.Forms;

namespace PotatoLauncher;

internal static class AppNotification
{
    private static readonly object Sync = new();
    private static readonly List<NotifyIcon> ActiveNotifications = [];

    public static void Show(IWin32Window? owner, string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            var notification = new NotifyIcon
            {
                Icon = ResolveIcon(owner),
                Text = Shorten(title, 63),
                Visible = true,
                BalloonTipIcon = ToolTipIcon.Info,
                BalloonTipTitle = title,
                BalloonTipText = message
            };

            lock (Sync)
            {
                ActiveNotifications.Add(notification);
            }

            notification.ShowBalloonTip(2800);
            var cleanupTimer = new System.Windows.Forms.Timer { Interval = 4500 };
            cleanupTimer.Tick += (_, _) =>
            {
                cleanupTimer.Stop();
                cleanupTimer.Dispose();
                lock (Sync)
                {
                    ActiveNotifications.Remove(notification);
                }

                notification.Visible = false;
                notification.Dispose();
            };
            cleanupTimer.Start();
        }
        catch
        {
            // Notifications are feedback only; saving should never fail because Windows blocked a balloon tip.
        }
    }

    private static Icon ResolveIcon(IWin32Window? owner)
    {
        return owner is Form form && form.Icon is not null ? form.Icon : SystemIcons.Application;
    }

    private static string Shorten(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Potato Launcher";
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
