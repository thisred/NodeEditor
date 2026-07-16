using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NodeEditor.Wpf;

/// <summary>
/// 简单的暗色主题输入对话框 — 用于输入文件名等
/// </summary>
public static class SimpleInputDialog
{
    /// <summary>显示对话框，返回用户输入的文本（null = 取消）</summary>
    public static string? Show(string title, string prompt, string defaultValue = "", Window? owner = null)
    {
        var window = new Window
        {
            Title = title,
            Width = 380,
            Height = 180,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Owner = owner
        };

        var textBox = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(16, 0, 16, 12),
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 13,
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
        };

        textBox.SelectAll();
        textBox.Focus();

        string? result = null;

        var okBtn = new Button
        {
            Content = "确定",
            Width = 70,
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6B, 0x3A)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70,
            Padding = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0)
        };

        okBtn.Click += (_, _) =>
        {
            result = textBox.Text;
            window.Close();
        };

        cancelBtn.Click += (_, _) => window.Close();

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16)
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);

        var promptText = new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            FontSize = 12,
            Margin = new Thickness(16, 16, 16, 8)
        };

        var panel = new StackPanel();
        panel.Children.Add(promptText);
        panel.Children.Add(textBox);
        panel.Children.Add(btnPanel);

        window.Content = panel;

        // Enter = OK, Esc = Cancel
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = textBox.Text;
                window.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                window.Close();
                e.Handled = true;
            }
        };

        window.ShowDialog();
        return string.IsNullOrEmpty(result) ? null : result;
    }
}
