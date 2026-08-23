using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;
using ShapePath = System.Windows.Shapes.Path;

namespace MediaLock.App.Tests;

public sealed class FormControlStyleContractTests
{
    [Fact]
    public void CheckBoxUsesFixedRoundedIndicatorWithoutChangingSizeWhenChecked()
    {
        WpfTestHost.Run(() =>
        {
            var checkBox = new CheckBox
            {
                Content = "Option",
                IsChecked = false,
            };
            var window = ShowControl(checkBox);

            try
            {
                var indicator = Assert.Single(
                    WpfTestHost.FindVisualChildren<Border>(checkBox),
                    candidate => candidate.Name == "Indicator");
                var checkMark = Assert.Single(
                    WpfTestHost.FindVisualChildren<ShapePath>(checkBox),
                    candidate => candidate.Name == "CheckMark");
                var originalSize = new Size(indicator.ActualWidth, indicator.ActualHeight);

                Assert.Equal(new Size(18, 18), originalSize);
                Assert.Equal(new CornerRadius(5), indicator.CornerRadius);
                Assert.Equal(Visibility.Collapsed, checkMark.Visibility);

                checkBox.IsChecked = true;
                checkBox.UpdateLayout();

                Assert.Equal(originalSize, new Size(indicator.ActualWidth, indicator.ActualHeight));
                Assert.Equal(Visibility.Visible, checkMark.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TextBoxUsesRoundedChromeWithoutChangingItsBorderThicknessOnFocus()
    {
        WpfTestHost.Run(() =>
        {
            var textBox = new TextBox { Text = "15" };
            var window = ShowControl(textBox);

            try
            {
                var chrome = Assert.Single(
                    WpfTestHost.FindVisualChildren<Border>(textBox),
                    candidate => candidate.Name == "Chrome");
                var originalThickness = chrome.BorderThickness;

                Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
                Assert.Equal(new Thickness(1), originalThickness);
                Assert.Equal(new Thickness(4, 2, 4, 2), chrome.Padding);

                textBox.Focus();
                textBox.UpdateLayout();

                Assert.Equal(originalThickness, chrome.BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window ShowControl(Control control)
    {
        var window = new Window
        {
            Width = 320,
            Height = 160,
            Content = control,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };
        window.Show();
        control.ApplyTemplate();
        window.UpdateLayout();
        return window;
    }
}
