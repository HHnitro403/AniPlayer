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
        HomeButton.Classes.Set("NavActive", page == "Home");
        LibraryButton.Classes.Set("NavActive", page == "Library");
        PlayerButton.Classes.Set("NavActive", page == "Player");
        SettingsButton.Classes.Set("NavActive", page == "Settings");
    }

    private void HomeButton_Click(object? sender, RoutedEventArgs e) => HomeClicked?.Invoke();
    private void LibraryButton_Click(object? sender, RoutedEventArgs e) => LibraryClicked?.Invoke();
    private void PlayerButton_Click(object? sender, RoutedEventArgs e) => PlayerClicked?.Invoke();
    private void SettingsButton_Click(object? sender, RoutedEventArgs e) => SettingsClicked?.Invoke();
}
