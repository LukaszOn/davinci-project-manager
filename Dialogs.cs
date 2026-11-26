using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProjectLibraryDesk;

public sealed record MemberInput(string Login, string Name, string Role, string? Password, bool Grant);

public sealed class MemberDialog : Window
{
    public MemberDialog(string action, Member? person, string library)
    {
        Title = action switch { "create" => "Dodaj użytkownika", "edit" => "Edytuj użytkownika", _ => "Zmień hasło" };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Width = 465;
        var content = new StackPanel { Margin = new Thickness(22) };
        Background = Brushes.White;
        Content = content;

        var login = new TextBox { Text = person?.Login ?? "", MaxLength = 31 };
        var display = new TextBox { Text = person?.Name ?? "", MaxLength = 80 };
        var role = new ComboBox { ItemsSource = new[] { "Współpracownik", "Administrator panelu" }, SelectedIndex = person?.Role == "administrator" ? 1 : 0 };
        if (action == "password") content.Children.Add(new TextBlock { Text = person!.Login, FontSize = 17, FontWeight = FontWeight.SemiBold });
        else
        {
            AddField(content, "Nazwa osoby", display);
            AddField(content, "Login w Resolve", login);
            AddField(content, "Rola w module kont", role);
        }

        var password = new TextBox { MaxLength = 128, PasswordChar = '●' };
        var confirmation = new TextBox { MaxLength = 128, PasswordChar = '●' };
        if (action != "edit")
        {
            AddField(content, "Hasło użytkownika", password);
            AddField(content, "Powtórz hasło", confirmation);
        }
        var grant = new CheckBox { Content = $"Nadaj dostęp do {library}", IsChecked = true, Margin = new Thickness(0, 2, 0, 8) };
        if (action == "create") content.Children.Add(grant);

        var feedback = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(feedback);
        AddButtons(this, content, "Zapisz", () =>
        {
            string? secret = password.Text;
            if (action != "edit" && (secret is null || secret.Length < 8 || secret != confirmation.Text))
            {
                feedback.Text = "Wpisz dwa razy to samo hasło. Użyj co najmniej 8 znaków.";
                return;
            }
            string username = login.Text?.Trim().ToLowerInvariant() ?? "";
            string label = display.Text?.Trim() ?? "";
            if (username.Length == 0 || label.Length == 0)
            {
                feedback.Text = "Podaj nazwę osoby i jej login.";
                return;
            }
            var result = new MemberInput(username, label, role.SelectedIndex == 1 ? "administrator" : "collaborator",
                action == "edit" ? null : secret, grant.IsChecked is true);
            password.Text = null;
            confirmation.Text = null;
            Close(result);
        });
        Opened += (_, _) => { if (action == "password") password.Focus(); else display.Focus(); };
    }

    internal static void AddField(Panel panel, string caption, Control control)
    {
        var label = new TextBlock { Text = caption, Margin = new Thickness(0, 5, 0, 3) };
        Avalonia.Automation.AutomationProperties.SetName(control, caption);
        control.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(label);
        panel.Children.Add(control);
    }

    internal static void AddButtons(Window dialog, Panel panel, string caption, Action accept)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 13, 0, 0) };
        var cancel = new Button { Content = "Anuluj" };
        var confirm = new Button { Content = caption };
        confirm.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) => accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
    }
}

internal static class TextDialog
{
    public static Task<string?> PromptAsync(Window owner, string title, string caption, string defaultValue)
    {
        var window = new Window
        {
            Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Width = 438, SizeToContent = SizeToContent.Height, CanResize = false,
            Background = Brushes.White,
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var field = new TextBox { Text = defaultValue, MaxLength = 63 };
        MemberDialog.AddField(panel, caption, field);
        MemberDialog.AddButtons(window, panel, "Dalej", () => window.Close(field.Text));
        window.Content = panel;
        window.Opened += (_, _) => { field.Focus(); field.SelectAll(); };
        return window.ShowDialog<string?>(owner);
    }

    public static Task<bool> ConfirmAsync(Window owner, string message)
    {
        var dialog = new Window
        {
            Title = owner.Title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Width = 472, SizeToContent = SizeToContent.Height, CanResize = false,
            Background = Brushes.White,
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        MemberDialog.AddButtons(dialog, panel, "Potwierdź", () => dialog.Close(true));
        dialog.Content = panel;
        return dialog.ShowDialog<bool>(owner);
    }
}

internal static class SetupDialog
{
    public static Task<bool> ShowAsync(Window owner, string library, string server, string rules)
    {
        var window = new Window
        {
            Title = "Przygotuj obsługę kont", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 695, MinWidth = 650, SizeToContent = SizeToContent.Height, ShowInTaskbar = false,
            Background = Brushes.White,
        };
        var content = new StackPanel { Margin = new Thickness(21) };
        content.Children.Add(new TextBlock { Text = $"{library} na {server}", FontSize = 20, FontWeight = FontWeight.SemiBold });
        AddText("Administrator serwera wykonuje ten krok przed rejestracją biblioteki. Reguły w pg_hba.conf ograniczają logowanie kont z panelu do bibliotek, które im przydzielisz.");
        AddText("Dopisz te dwie reguły na serwerze przed regułami odrzucenia grupy resolve_panel_members i regułami ogólnymi.");
        AddRules(rules);
        AddText("Dodaj poniższą blokadę raz dla całej instancji, pod regułami bibliotek i przed regułami ogólnymi.");
        AddRules("local all +resolve_panel_members reject\r\nhost all +resolve_panel_members all reject");
        AddText("Zapisz i przeładuj konfigurację PostgreSQL. Następnie kliknij Zainstaluj moduł. Instalacja rejestruje bibliotekę i tworzy funkcje do nadawania dostępu.");
        MemberDialog.AddButtons(window, content, "Zainstaluj moduł", () => window.Close(true));
        window.Content = content;
        return window.ShowDialog<bool>(owner);

        void AddText(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) });
        void AddRules(string text) => content.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("monospace") });
    }
}
