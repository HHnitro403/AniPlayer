using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AniPlayer.UI;

public partial class Toast : UserControl
{
    private const int ToastDurationMs = 3000;
    public event Action<Toast>? Dismissed;

    public Toast()
    {
        InitializeComponent();
    }

    public void Show(string message, bool isError)
    {
        MessageText.Text = message;
        
        if (isError)
        {
            StatusStrip.Background = this.FindResource("DangerRed") as IBrush ?? Brushes.Red;
        }
        else
        {
            StatusStrip.Background = this.FindResource("AccentPrimary") as IBrush ?? Brushes.Blue;
        }

        // Animate in
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50); // Allow layout
            ToastBorder.Classes.Add("Visible");
            
            // Auto dismiss
            await Task.Delay(ToastDurationMs);
            Close();
        });
    }

    public void Close()
    {
        ToastBorder.Classes.Remove("Visible");
        
        // Wait for animation then fire dismissed
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(300); // Match transition duration
            Dismissed?.Invoke(this);
        });
    }
}
