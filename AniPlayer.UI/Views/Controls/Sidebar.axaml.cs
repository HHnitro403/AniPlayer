using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;

namespace AniPlayer.UI;

public partial class Sidebar : UserControl
{
    public event Action? HomeClicked;
    public event Action? LibraryClicked;
    public event Action? PlayerClicked;
    public event Action? SettingsClicked;

    public Sidebar()
    {
        InitializeComponent();
    }

    public void SetActive(string page)
    {
        var activeBrush = new SolidColorBrush(Color.Parse("#E0E0FF"));
        var inactiveBrush = new SolidColorBrush(Color.Parse("#AAAACC"));
        var activeWeight = FontWeight.SemiBold;
        var normalWeight = FontWeight.Normal;

        HomeButton.Foreground = page == "Home" ? activeBrush : inactiveBrush;
        HomeButton.FontWeight = page == "Home" ? activeWeight : normalWeight;

        LibraryButton.Foreground = page == "Library" ? activeBrush : inactiveBrush;
        LibraryButton.FontWeight = page == "Library" ? activeWeight : normalWeight;

        PlayerButton.Foreground = page == "Player" ? activeBrush : inactiveBrush;
        PlayerButton.FontWeight = page == "Player" ? activeWeight : normalWeight;

        SettingsButton.Foreground = page == "Settings" ? activeBrush : inactiveBrush;
        SettingsButton.FontWeight = page == "Settings" ? activeWeight : normalWeight;
    }

    private void HomeButton_Click(object? sender, RoutedEventArgs e) => HomeClicked?.Invoke();
    private void LibraryButton_Click(object? sender, RoutedEventArgs e) => LibraryClicked?.Invoke();
    private void PlayerButton_Click(object? sender, RoutedEventArgs e) => PlayerClicked?.Invoke();
    private void SettingsButton_Click(object? sender, RoutedEventArgs e) => SettingsClicked?.Invoke();
}
