using System.Windows;

namespace CDir.Dialogs;

public partial class InputDialog : Window
{
    private InputDialog(string title, string label, string value)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = value;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string? Value { get; private set; }

    public static string? Show(Window? owner, string title, string label, string value)
    {
        var dialog = new InputDialog(title, label, value);
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = ValueBox.Text.Trim();
        if (text.Length == 0) return;

        Value = text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnValueKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OnOk(sender, e);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
