using static ProjectLibraryDesk.UiText;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProjectLibraryDesk;

public sealed record MemberInput(string Login, string Name, string Role, string? Password, bool Grant);

internal sealed record GroupInput(MemberGroup? Group, string? Label, string[] Logins, string[] Libraries);

internal sealed class GroupsDialog : Window
{
    public GroupsDialog(GroupDirectory directory, IReadOnlyList<Member> members)
    {
        Classes.Add("manager-dialog");
        Title = T("UserGroups");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = 670;
        Height = 565;
        MinWidth = 530;
        MinHeight = 430;
        Background = Brushes.White;

        var groupPicker = new ComboBox
        {
            ItemsSource = new[] { T("NewGroup") }.Concat(directory.Groups.Select(group => group.Name)).ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var name = new TextBox { MaxLength = 80 };
        var fields = new StackPanel();
        MemberDialog.AddField(fields, T("Group"), groupPicker);
        MemberDialog.AddField(fields, T("GroupName"), name);
        var memberChecks = members.ToDictionary(member => member.Login, member =>
            CreateCheck($"{member.Name} ({member.Login})"), StringComparer.Ordinal);
        var libraryChecks = directory.Libraries.ToDictionary(library => library, CreateCheck, StringComparer.Ordinal);
        var lists = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        lists.Children.Add(CheckList(T("Users"), memberChecks.Values, T("AddUsersToChooseGroupMembers")));
        Control libraries = CheckList(T("Libraries"), libraryChecks.Values, T("RegisterALibraryToAssignToAGroup"));
        Grid.SetColumn(libraries, 1);
        lists.Children.Add(libraries);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var delete = new Button { Content = T("DeleteGroup"), VerticalAlignment = VerticalAlignment.Bottom };
        delete.Classes.Add("danger");
        delete.Classes.Add("form-action");
        delete.Click += (_, _) =>
        {
            if (SelectedGroup() is { } group) Close(new GroupInput(group, null, [], []));
        };
        footer.Children.Add(delete);
        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        var feedback = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        MemberDialog.AddButtons(this, buttons, T("SaveGroup"), () =>
        {
            string label = name.Text?.Trim() ?? "";
            if (label.Length == 0)
            {
                feedback.Text = T("EnterAGroupName");
                name.Focus();
                return;
            }
            Close(new GroupInput(SelectedGroup(), label,
                memberChecks.Where(pair => pair.Value.IsChecked is true).Select(pair => pair.Key).ToArray(),
                libraryChecks.Where(pair => pair.Value.IsChecked is true).Select(pair => pair.Key).ToArray()));
        });

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 9, Margin = new Thickness(22) };
        layout.Children.Add(fields);
        Grid.SetRow(lists, 1);
        layout.Children.Add(lists);
        Grid.SetRow(feedback, 2);
        layout.Children.Add(feedback);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        Content = layout;

        groupPicker.SelectionChanged += (_, _) =>
        {
            MemberGroup? group = SelectedGroup();
            name.Text = group?.Name;
            delete.IsVisible = group is not null;
            feedback.Text = null;
            foreach (var (login, check) in memberChecks)
                check.IsChecked = group?.Logins.Contains(login, StringComparer.Ordinal) is true;
            foreach (var (library, check) in libraryChecks)
                check.IsChecked = group?.Libraries.Contains(library, StringComparer.Ordinal) is true;
        };
        groupPicker.SelectedIndex = 0;
        Opened += (_, _) => name.Focus();

        MemberGroup? SelectedGroup() => groupPicker.SelectedIndex > 0 ? directory.Groups[groupPicker.SelectedIndex - 1] : null;
    }

    private static CheckBox CreateCheck(string label)
    {
        var checkbox = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } };
        Avalonia.Automation.AutomationProperties.SetName(checkbox, label);
        return checkbox;
    }

    private static Control CheckList(string title, ICollection<CheckBox> checks, string emptyText)
    {
        var items = new StackPanel { Spacing = 3, Margin = new Thickness(8, 4) };
        foreach (CheckBox checkbox in checks)
            items.Children.Add(checkbox);
        if (checks.Count == 0)
            items.Children.Add(new TextBlock { Text = emptyText, TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = items };
        var border = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#D8DFED")), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true, Child = scroll };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 5 };
        grid.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        Grid.SetRow(border, 1);
        grid.Children.Add(border);
        return grid;
    }
}

public sealed class MemberDialog : Window
{
    public MemberDialog(string action, Member? person, string library)
    {
        Classes.Add("manager-dialog");
        Title = action switch { "create" => T("AddUser"), "edit" => T("EditUser"), _ => T("ChangePassword") };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Width = 465;
        var content = new StackPanel { Margin = new Thickness(22) };
        Background = Brushes.White;
        Content = new ScrollViewer { Content = content };

        var login = new TextBox { Text = person?.Login ?? "", MaxLength = 31 };
        var display = new TextBox { Text = person?.Name ?? "", MaxLength = 80 };
        var role = new ComboBox { ItemsSource = new[] { T("Collaborator"), T("PanelAdministrator") }, SelectedIndex = person?.Role == "administrator" ? 1 : 0 };
        if (action == "password")
        {
            content.Children.Add(new TextBlock { Text = person!.Name, TextWrapping = TextWrapping.Wrap, FontSize = 17, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = person.Login, Margin = new Thickness(0, 0, 0, 8) });
        }
        else
        {
            AddField(content, T("DisplayName"), display);
            AddField(content, T("ResolveUsername"), login);
            AddField(content, T("AccountRole"), role);
        }

        var password = new TextBox { MaxLength = 128, PasswordChar = '●' };
        var confirmation = new TextBox { MaxLength = 128, PasswordChar = '●' };
        if (action != "edit")
        {
            AddField(content, T("UserPassword"), password);
            AddField(content, T("RepeatPassword"), confirmation);
        }
        var grant = new CheckBox { Content = new TextBlock { Text = T("GrantAccessTo", library), TextWrapping = TextWrapping.Wrap }, IsChecked = true, Margin = new Thickness(0, 2, 0, 8) };
        if (action == "create") content.Children.Add(grant);

        var feedback = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        content.Children.Add(feedback);
        AddButtons(this, content, T("Save"), () =>
        {
            string username = login.Text?.Trim().ToLowerInvariant() ?? "";
            string label = display.Text?.Trim() ?? "";
            if (action != "password" && (label.Length == 0 || username.Length == 0))
            {
                feedback.Text = T("EnterADisplayNameAndUsername");
                feedback.IsVisible = true;
                (label.Length == 0 ? display : login).Focus();
                return;
            }
            string? secret = password.Text;
            if (action != "edit" && (secret is null || secret.Length < 8 || secret != confirmation.Text))
            {
                feedback.Text = T("EnterTheSamePasswordTwiceUse");
                feedback.IsVisible = true;
                (secret?.Length >= 8 ? confirmation : password).Focus();
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
        label.Classes.Add("dialog-label");
        Avalonia.Automation.AutomationProperties.SetName(control, caption);
        control.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(label);
        panel.Children.Add(control);
    }

    internal static Button AddButtons(Window dialog, Panel panel, string caption, Action accept)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = T("Cancel") };
        var confirm = new Button { Content = caption };
        cancel.Classes.Add("form-action");
        confirm.Classes.Add("form-action");
        confirm.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) => accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        return confirm;
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
        window.Classes.Add("manager-dialog");
        var panel = new StackPanel { Margin = new Thickness(22) };
        var field = new TextBox { Text = defaultValue, MaxLength = 63 };
        MemberDialog.AddField(panel, caption, field);
        var feedback = new TextBlock { Text = T("LibraryNameFormat"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(feedback);
        var next = MemberDialog.AddButtons(window, panel, T("Continue"), () => window.Close(field.Text));
        void Validate()
        {
            next.IsEnabled = PgServer.ValidLibraryName(field.Text ?? "");
            feedback.IsVisible = !next.IsEnabled;
        }
        field.TextChanged += (_, _) => Validate();
        Validate();
        window.Content = panel;
        window.Opened += (_, _) => { field.Focus(); field.SelectAll(); };
        return window.ShowDialog<string?>(owner);
    }

    public static Task<bool> ConfirmAsync(Window owner, string message, string caption, bool destructive = false)
    {
        var dialog = new Window
        {
            Title = owner.Title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Width = 472, SizeToContent = SizeToContent.Height, CanResize = false,
            Background = Brushes.White,
        };
        dialog.Classes.Add("manager-dialog");
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var action = MemberDialog.AddButtons(dialog, panel, caption, () => dialog.Close(true));
        if (destructive)
        {
            action.Classes.Remove("primary");
            action.Classes.Add("danger");
        }
        dialog.Content = panel;
        return dialog.ShowDialog<bool>(owner);
    }
}

internal static class SetupDialog
{
    public static Task<bool> ShowAsync(Window owner, string library, string server, string rules, string memberRole)
    {
        var window = new Window
        {
            Title = T("SetUpAccountManagement"), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 695, MinWidth = 650, SizeToContent = SizeToContent.Height, ShowInTaskbar = false,
            Background = Brushes.White,
        };
        window.Classes.Add("manager-dialog");
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = T("LibraryOnServer", library, server), TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeight.SemiBold });
        AddText(T("AServerAdministratorMustCompleteThis"));
        AddText(T("AddTheseTwoRulesOnThe"));
        AddRules(rules);
        AddText(T("AddTheFollowingRejectionRuleOnce"));
        AddRules($"local all +{memberRole} reject\r\nhost all +{memberRole} all reject");
        AddText(T("SaveAndReloadThePostgreSQLConfiguration"));
        MemberDialog.AddButtons(window, content, T("InstallModule"), () => window.Close(true));
        window.Content = new ScrollViewer { Content = content };
        return window.ShowDialog<bool>(owner);

        void AddText(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) });
        void AddRules(string text) => content.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("monospace") });
    }
}
