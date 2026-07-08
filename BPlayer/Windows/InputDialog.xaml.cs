using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BPlayer.Windows;

public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string title, string defaultText = "")
    {
        InitializeComponent();
        DialogTitle.Text = title;
        InputBox.Text = defaultText;
        InputBox.SelectAll();

        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ConfirmBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (e.Key == Key.Escape) DialogResult = false;
        };

        Loaded += (_, _) => InputBox.Focus();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(InputBox.Text))
            DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
