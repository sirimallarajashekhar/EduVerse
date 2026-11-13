using System.Text.Json;
using Microsoft.Maui.Controls;

namespace EduVerse.Mobile;

public sealed class App : Application
{
    private readonly EduVerseApi _api;
    private readonly NavigationPage _root;

    public App(EduVerseApi api)
    {
        _api = api;
        _root = new NavigationPage(new ContentPage { Content = new ActivityIndicator { IsRunning = true, VerticalOptions = LayoutOptions.Center } });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_root);
        _ = OpenInitialAsync();
        return window;
    }

    private async Task OpenInitialAsync()
    {
        var session = await _api.GetSessionAsync();
        await MainThread.InvokeOnMainThreadAsync(async () =>
            await _root.Navigation.PushAsync(session is null ? LoginPage() : HomePage(session)));
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_root.Navigation.NavigationStack.Count > 1)
                _root.Navigation.RemovePage(_root.Navigation.NavigationStack[0]);
            await Task.CompletedTask;
        });
    }

    private static Label Heading(string text) => new() { Text = text, FontSize = 27, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173D7A") };
    private static Label Note(string text) => new() { Text = text, FontSize = 14, TextColor = Colors.DimGray };
    private static Button Action(string text) => new() { Text = text, BackgroundColor = Color.FromArgb("#173D7A"), TextColor = Colors.White, CornerRadius = 12 };
    private static ContentPage Page(string title, View content) => new()
    {
        Title = title,
        BackgroundColor = Colors.White,
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = new Thickness(22, 28), Spacing = 15, Children = { content } } }
    };

    private ContentPage LoginPage()
    {
        var server = new Entry { Placeholder = "https://api.your-school.com/", Keyboard = Keyboard.Url, Text = _api.ApiUrl, AutomationId = "ApiUrl" };
        var institution = new Entry { Placeholder = "Institution slug", AutomationId = "TenantSlug" };
        var email = new Entry { Placeholder = "Email", Keyboard = Keyboard.Email, AutomationId = "Email" };
        var password = new Entry { Placeholder = "Password", IsPassword = true, AutomationId = "Password" };
        var status = Note("");
        var login = Action("Sign in");
        login.Clicked += async (_, _) =>
        {
            login.IsEnabled = false;
            status.Text = "Signing in…";
            try
            {
                _api.ApiUrl = server.Text ?? "";
                var result = await _api.LoginAsync(new MobileLogin(
                    institution.Text?.Trim() ?? "", email.Text?.Trim() ?? "", password.Text ?? ""));
                await _root.Navigation.PushAsync(HomePage(result));
                _root.Navigation.RemovePage(_root.Navigation.NavigationStack[0]);
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { login.IsEnabled = true; }
        };
        var layout = new VerticalStackLayout
        {
            Spacing = 17,
            Children = {
                Heading("EduVerse"),
                Note("Your school and college, in your pocket."),
                Note("Connect to your institution's secure EduVerse API."),
                server, institution, email, password, login, status
            }
        };
        return Page("Sign in", layout);
    }

    private ContentPage HomePage(MobileSession session)
    {
        var layout = new VerticalStackLayout { Spacing = 14 };
        layout.Children.Add(Heading($"Welcome, {session.DisplayName}"));
        layout.Children.Add(Note($"Role: {session.RoleCode} • Institution: {session.TenantId}"));
        var role = session.RoleCode.ToUpperInvariant();
        if (role.Contains("PARENT"))
        {
            layout.Children.Add(LinkedStudentsAction("My children", true));
        }
        else if (role.Contains("STUDENT"))
        {
            layout.Children.Add(LinkedStudentsAction("My student profile", false));
        }
        else if (role.Contains("TEACHER"))
        {
            layout.Children.Add(Note("Select a campus available to your institution account."));
            var campuses = new List<MobileCampus>();
            var picker = new Picker { Title = "Select campus", AutomationId = "CampusPicker" };
            var load = Action("Load campuses");
            load.Clicked += async (_, _) =>
            {
                load.IsEnabled = false;
                try
                {
                    campuses = (await _api.GetCampusesAsync()).Where(x => x.IsActive).ToList();
                    picker.Items.Clear();
                    foreach (var item in campuses)
                        picker.Items.Add($"{item.DisplayName} ({item.Code})");
                    if (campuses.Count > 0) picker.SelectedIndex = 0;
                    else await ShellAlertAsync("No campuses", "No active campuses are available for this account.");
                }
                catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
                catch (Exception ex) { await ShellAlertAsync("Unable to load", ex.Message); }
                finally { load.IsEnabled = true; }
            };
            layout.Children.Add(load);
            layout.Children.Add(picker);
            Guid SelectedCampus()
            {
                if (picker.SelectedIndex < 0 || picker.SelectedIndex >= campuses.Count)
                    throw new ArgumentException("Load and select a campus first.");
                return campuses[picker.SelectedIndex].Id;
            }
            layout.Children.Add(TeacherClassesAction(SelectedCampus));
            layout.Children.Add(ActionFor("My lessons",
                () => $"api/v1/portals/teacher/lessons?campusId={SelectedCampus():D}"));
        }
        else
        {
            layout.Children.Add(Note("Administrative features are available through your institution's web portal. Mobile administration will be added module by module."));
        }
        var signOut = Action("Sign out");
        signOut.Clicked += async (_, _) =>
        {
            signOut.IsEnabled = false;
            await _api.LogoutAsync();
            await _root.Navigation.PushAsync(LoginPage());
            _root.Navigation.RemovePage(_root.Navigation.NavigationStack[0]);
        };
        layout.Children.Add(signOut);
        return Page("Dashboard", layout);
    }

    private Button TeacherClassesAction(Func<Guid> selectedCampus)
    {
        var button = Action("My classes");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var campusId = selectedCampus();
                var classes = await _api.GetTeacherClassesAsync(campusId);
                var layout = new VerticalStackLayout { Spacing = 14 };
                layout.Children.Add(Heading("My classes"));
                if (classes.Count == 0)
                    layout.Children.Add(Note("No assigned classes found for this campus."));
                foreach (var assigned in classes)
                {
                    var current = assigned;
                    var rosterButton = Action($"Class {current.ClassId:D} — students");
                    rosterButton.Clicked += async (_, _) =>
                    {
                        rosterButton.IsEnabled = false;
                        try
                        {
                            var students = await _api.GetTeacherRosterAsync(current.CampusId, current.ClassId);
                            var list = new VerticalStackLayout { Spacing = 12 };
                            list.Children.Add(Heading("Class roster"));
                            if (students.Count == 0) list.Children.Add(Note("No enrolled students found."));
                            foreach (var student in students)
                                list.Children.Add(Note($"{student.GivenName} {student.FamilyName} — {student.AdmissionNumber}"));
                            await _root.Navigation.PushAsync(Page("Class roster", list));
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            await HandleTeacherAccessErrorAsync(ex);
                        }
                        catch (Exception ex) { await ShellAlertAsync("Unable to load", ex.Message); }
                        finally { rosterButton.IsEnabled = true; }
                    };
                    layout.Children.Add(rosterButton);
                }
                await _root.Navigation.PushAsync(Page("My classes", layout));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task HandleTeacherAccessErrorAsync(UnauthorizedAccessException ex)
    {
        if (await _api.GetSessionAsync() is not null)
        {
            await ShellAlertAsync("Access denied", ex.Message);
            return;
        }
        await ShellAlertAsync("Session expired", ex.Message);
        await _root.Navigation.PushAsync(LoginPage());
        foreach (var oldPage in _root.Navigation.NavigationStack.ToArray().SkipLast(1))
            _root.Navigation.RemovePage(oldPage);
    }

    private Button LinkedStudentsAction(string title, bool parent)
    {
        var button = Action(title);
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var students = await _api.GetLinkedStudentsAsync(parent);
                var layout = new VerticalStackLayout { Spacing = 14 };
                layout.Children.Add(Heading(title));
                if (students.Count == 0)
                    layout.Children.Add(Note("No linked student records are available for this account."));
                foreach (var link in students)
                {
                    var student = link;
                    var open = Action($"Student {student.StudentId:D}");
                    open.Clicked += async (_, _) =>
                        await _root.Navigation.PushAsync(LinkedStudentPage(student, parent));
                    layout.Children.Add(open);
                }
                await _root.Navigation.PushAsync(Page(title, layout));
            }
            catch (UnauthorizedAccessException ex)
            {
                if (await _api.GetSessionAsync() is null)
                {
                    await ShellAlertAsync("Session expired", ex.Message);
                    await _root.Navigation.PushAsync(LoginPage());
                    foreach (var oldPage in _root.Navigation.NavigationStack.ToArray().SkipLast(1))
                        _root.Navigation.RemovePage(oldPage);
                }
                else await ShellAlertAsync("Access denied", ex.Message);
            }
            catch (Exception ex) { await ShellAlertAsync("Unable to load", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private ContentPage LinkedStudentPage(MobileLinkedStudent link, bool parent)
    {
        var layout = new VerticalStackLayout { Spacing = 14 };
        layout.Children.Add(Heading(parent ? "Child details" : "My academic details"));
        layout.Children.Add(Note($"Student ID: {link.StudentId:D}"));
        layout.Children.Add(Note($"Campus ID: {link.CampusId:D}"));

        var path = parent
            ? $"api/v1/portals/parent/children/{link.StudentId:D}/"
            : $"api/v1/portals/student/{link.StudentId:D}/";
        var campus = $"?campusId={link.CampusId:D}";
        layout.Children.Add(ActionFor("Attendance", path + "attendance" + campus));
        layout.Children.Add(ActionFor("Results", path + "results" + campus));
        if (!parent)
        {
            layout.Children.Add(ActionFor("Timetable", path + "timetable" + campus));
            layout.Children.Add(ActionFor("Assignments", path + "assignments" + campus));
        }
        layout.Children.Add(ActionFor("Notices", path + "notices" + campus));
        return Page(parent ? "Child details" : "My academic details", layout);
    }

    private Button ActionFor(string title, string path) => ActionFor(title, () => path);

    private Button ActionFor(string title, Func<string> path)
    {
        var button = Action(title);
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var result = await _api.GetAsync(path());
                await _root.Navigation.PushAsync(ResultPage(title, result));
            }
            catch (UnauthorizedAccessException ex)
            {
                if (await _api.GetSessionAsync() is null)
                {
                    await ShellAlertAsync("Session expired", ex.Message);
                    await _root.Navigation.PushAsync(LoginPage());
                    foreach (var oldPage in _root.Navigation.NavigationStack.ToArray().SkipLast(1))
                        _root.Navigation.RemovePage(oldPage);
                }
                else
                {
                    await ShellAlertAsync("Access denied", ex.Message);
                }
            }
            catch (Exception ex)
            {
                await ShellAlertAsync("Unable to load", ex.Message);
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task ShellAlertAsync(string title, string message)
    {
        var current = _root.Navigation.NavigationStack.LastOrDefault();
        if (current is not null) await current.DisplayAlertAsync(title, message, "OK");
    }

    private ContentPage ResultPage(string title, JsonElement result)
    {
        var list = new VerticalStackLayout { Spacing = 12 };
        list.Children.Add(Heading(title));
        if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 0)
            list.Children.Add(Note("No records found."));
        else if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray())
                list.Children.Add(new Border
                {
                    Stroke = Color.FromArgb("#E0E6ED"),
                    StrokeThickness = 1,
                    Padding = new Thickness(12),
                    Content = new Label { Text = DisplayRecord(item), LineBreakMode = LineBreakMode.WordWrap }
                });
        }
        else list.Children.Add(Note(DisplayRecord(result)));
        return Page(title, list);
    }

    private static string DisplayRecord(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return item.ToString();
        return string.Join(Environment.NewLine, item.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .Select(p => $"{p.Name}: {p.Value}"));
    }
}
