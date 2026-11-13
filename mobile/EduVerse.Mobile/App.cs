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
        var checkConnection = Action("Test API connection");
        checkConnection.Clicked += async (_, _) =>
        {
            checkConnection.IsEnabled = false;
            status.Text = "Checking API connection…";
            try
            {
                _api.ApiUrl = server.Text ?? "";
                status.Text = await _api.CheckApiConnectionAsync();
            }
            catch (HttpRequestException ex)
            {
                status.Text = $"API is unreachable or returned an error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                status.Text = "Connection timed out. Check the server address and network.";
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
            finally { checkConnection.IsEnabled = true; }
        };
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
                server, checkConnection, institution, email, password, login, status
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
            layout.Children.Add(StudentDashboardAction());
        }
        else if (role is "ADMIN" or "INSTITUTION_ADMIN")
        {
            layout.Children.Add(AdminDashboardAction());
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

    private Button AdminDashboardAction()
    {
        var button = Action("Open Admin Dashboard");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                // Admin authorization and metrics are evaluated by the existing API and MySQL services.
                var dashboard = await _api.GetInstitutionDashboardAsync();
                var content = new VerticalStackLayout { Spacing = 14 };
                content.Children.Add(Heading("Institution Administration"));
                content.Children.Add(Note("Live institution dashboard"));
                content.Children.Add(new Border
                {
                    Stroke = Color.FromArgb("#E0E6ED"), StrokeThickness = 1,
                    Padding = new Thickness(12),
                    Content = Note(DisplayRecord(dashboard))
                });
                content.Children.Add(ActionFor("Institution profile",
                    "api/v1/institution-admin/profile"));
                content.Children.Add(ActionFor("Subscription",
                    "api/v1/institution-admin/subscription"));
                content.Children.Add(ActionFor("Institution settings",
                    "api/v1/institution-admin/settings"));
                content.Children.Add(AdminCampusAction());
                await _root.Navigation.PushAsync(Page("Admin Dashboard", content));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Dashboard unavailable", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button AdminCampusAction()
    {
        var button = Action("Campuses and students");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var campuses = (await _api.GetCampusesAsync())
                    .Where(campus => campus.IsActive).ToList();
                var content = new VerticalStackLayout { Spacing = 12 };
                content.Children.Add(Heading("Campuses"));
                if (campuses.Count == 0) content.Children.Add(Note("No active campuses available."));
                foreach (var campus in campuses)
                {
                    var current = campus;
                    var open = Action($"{current.DisplayName} ({current.Code})");
                    open.Clicked += async (_, _) =>
                    {
                        var details = new VerticalStackLayout { Spacing = 12 };
                        details.Children.Add(Heading(current.DisplayName));
                        details.Children.Add(ActionFor("Students",
                            $"api/v1/students?campusId={current.Id:D}"));
                        details.Children.Add(AdminAdmissionsAction(current.Id));
                        details.Children.Add(AdminRegisterStudentAction(current.Id));
                        details.Children.Add(ActionFor("Academic overview",
                            $"api/v1/academic/overview?campusId={current.Id:D}"));
                        details.Children.Add(ActionFor("Attendance dashboard",
                            $"api/v1/attendance/dashboard?campusId={current.Id:D}"));
                        await _root.Navigation.PushAsync(Page(current.DisplayName, details));
                    };
                    content.Children.Add(open);
                }
                await _root.Navigation.PushAsync(Page("Campuses", content));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load campuses", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button AdminAdmissionsAction(Guid campusId)
    {
        var button = Action("Review admissions");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var applications = await _api.GetAdmissionsAsync(campusId);
                var content = new VerticalStackLayout { Spacing = 12 };
                content.Children.Add(Heading("Admission applications"));
                if (applications.Count == 0) content.Children.Add(Note("No applications found."));
                foreach (var application in applications)
                {
                    var current = application;
                    var item = new VerticalStackLayout { Spacing = 8 };
                    item.Children.Add(Note($"{current.GivenName} {current.FamilyName}"));
                    item.Children.Add(Note($"Application #{current.ApplicationNumber} • {current.Status}"));
                    item.Children.Add(Note($"DOB: {current.DateOfBirth:dd MMM yyyy}"));
                    if (string.Equals(current.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                    {
                        var decisionButtons = new List<Button>();
                        foreach (var decision in new[] { "APPROVED", "REJECTED" })
                        {
                            var chosen = decision;
                            var decide = Action(chosen == "APPROVED" ? "Approve" : "Reject");
                            decide.Clicked += async (_, _) =>
                            {
                                var page = _root.Navigation.NavigationStack.LastOrDefault();
                                if (page is null || !await page.DisplayAlertAsync(
                                    "Confirm admission decision",
                                    $"{chosen} application #{current.ApplicationNumber} for {current.GivenName} {current.FamilyName}?",
                                    "Confirm", "Cancel")) return;
                                decide.IsEnabled = false;
                                var saved = false;
                                try
                                {
                                    await _api.DecideAdmissionAsync(campusId, current.Id, chosen);
                                    await ShellAlertAsync("Decision saved", "The API accepted the admission decision. Reopen this page to refresh.");
                                    saved = true;
                                    foreach (var action in decisionButtons) action.IsEnabled = false;
                                }
                                catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
                                catch (Exception ex) { await ShellAlertAsync("Decision not saved", ex.Message); }
                                finally { if (!saved) decide.IsEnabled = true; }
                            };
                            decisionButtons.Add(decide);
                            item.Children.Add(decide);
                        }
                    }
                    content.Children.Add(new Border
                    {
                        Stroke = Color.FromArgb("#E0E6ED"),
                        Padding = new Thickness(12),
                        Content = item
                    });
                }
                await _root.Navigation.PushAsync(Page("Admissions", content));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Admissions unavailable", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button AdminRegisterStudentAction(Guid campusId)
    {
        var button = Action("Register student");
        button.Clicked += async (_, _) =>
            await _root.Navigation.PushAsync(AdminStudentRegistrationPage(campusId));
        return button;
    }

    private ContentPage AdminStudentRegistrationPage(Guid campusId)
    {
        var layout = new VerticalStackLayout { Spacing = 14 };
        layout.Children.Add(Heading("Register student"));
        layout.Children.Add(Note("Registration is saved to the existing EduVerse API and MySQL database."));
        var givenName = new Entry { Placeholder = "Given name", AutomationId = "StudentGivenName" };
        var familyName = new Entry { Placeholder = "Family name", AutomationId = "StudentFamilyName" };
        var birthDate = new DatePicker
        {
            Date = DateTime.Today.AddYears(-10),
            MaximumDate = DateTime.Today,
            AutomationId = "StudentDateOfBirth"
        };
        var applicationId = new Entry
        {
            Placeholder = "Approved admission application ID (optional)",
            AutomationId = "StudentApplicationId"
        };
        var status = Note("");
        var submit = Action("Create student");
        submit.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(givenName.Text) || string.IsNullOrWhiteSpace(familyName.Text))
            {
                status.Text = "Enter the student's given and family name.";
                return;
            }
            if (birthDate.Date is not DateTime selectedDate)
            {
                status.Text = "Select a date of birth.";
                return;
            }
            Guid? approvedApplication = null;
            if (!string.IsNullOrWhiteSpace(applicationId.Text))
            {
                if (!Guid.TryParse(applicationId.Text.Trim(), out var parsed) || parsed == Guid.Empty)
                {
                    status.Text = "Enter a valid application ID or leave the field empty.";
                    return;
                }
                approvedApplication = parsed;
            }
            var page = _root.Navigation.NavigationStack.LastOrDefault();
            if (page is null || !await page.DisplayAlertAsync(
                "Confirm student registration",
                $"Create a student record for {givenName.Text.Trim()} {familyName.Text.Trim()}?",
                "Create", "Cancel")) return;
            submit.IsEnabled = false;
            status.Text = "Saving student…";
            try
            {
                var created = await _api.RegisterStudentAsync(new MobileStudentCreate(
                    campusId, approvedApplication, givenName.Text.Trim(), familyName.Text.Trim(),
                    DateOnly.FromDateTime(selectedDate)));
                status.Text = $"Student registered: {created.AdmissionNumber}";
                await ShellAlertAsync("Student registered",
                    $"Admission number: {created.AdmissionNumber}");
            }
            catch (UnauthorizedAccessException ex)
            {
                status.Text = "Registration was not completed.";
                await HandleTeacherAccessErrorAsync(ex);
            }
            catch (Exception ex)
            {
                status.Text = "Registration was not completed. Check the API response before retrying.";
                await ShellAlertAsync("Registration failed", ex.Message);
            }
            finally { submit.IsEnabled = true; }
        };
        layout.Children.Add(givenName);
        layout.Children.Add(familyName);
        layout.Children.Add(Note("Date of birth"));
        layout.Children.Add(birthDate);
        layout.Children.Add(applicationId);
        layout.Children.Add(submit);
        layout.Children.Add(status);
        return Page("Register student", layout);
    }

    private Button StudentDashboardAction()
    {
        var button = Action("Open Student Dashboard");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var students = await _api.GetLinkedStudentsAsync(false);
                var content = new VerticalStackLayout { Spacing = 14 };
                content.Children.Add(Heading("My Student Dashboard"));
                if (students.Count == 0)
                    content.Children.Add(Note("No linked student profile. Ask your institution administrator to link your account."));
                foreach (var linked in students)
                {
                    var current = linked;
                    var profile = await _api.GetMyStudentProfileAsync(current.CampusId, current.StudentId);
                    if (profile is null)
                    {
                        content.Children.Add(Note("Your student profile is currently unavailable."));
                        continue;
                    }
                    var details = new VerticalStackLayout { Spacing = 8 };
                    details.Children.Add(Heading($"{profile.GivenName} {profile.FamilyName}"));
                    details.Children.Add(Note($"Admission: {profile.AdmissionNumber}"));
                    details.Children.Add(Note($"Status: {profile.Status}"));
                    var open = Action("View academics");
                    open.Clicked += async (_, _) =>
                        await _root.Navigation.PushAsync(LinkedStudentPage(current, false));
                    details.Children.Add(open);
                    content.Children.Add(new Border
                    {
                        Stroke = Color.FromArgb("#E0E6ED"), StrokeThickness = 1,
                        Padding = new Thickness(14), Content = details
                    });
                }
                await _root.Navigation.PushAsync(Page("Student Dashboard", content));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Student dashboard unavailable", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
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
                            {
                                var enrolled = student;
                                var attendanceButton = Action($"Attendance: {enrolled.GivenName} {enrolled.FamilyName} ({enrolled.AdmissionNumber})");
                                attendanceButton.Clicked += async (_, _) =>
                                    await _root.Navigation.PushAsync(TeacherAttendancePage(current.CampusId, enrolled));
                                list.Children.Add(attendanceButton);
                            }
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

    private ContentPage TeacherAttendancePage(Guid campusId, MobileTeacherStudent student)
    {
        var layout = new VerticalStackLayout { Spacing = 14 };
        layout.Children.Add(Heading($"Attendance — {student.GivenName} {student.FamilyName}"));
        layout.Children.Add(Note($"Admission: {student.AdmissionNumber}"));
        var date = new DatePicker { Date = DateTime.Today, AutomationId = "AttendanceDate" };
        var status = new Picker { Title = "Attendance status", AutomationId = "AttendanceStatus" };
        foreach (var option in new[] { "PRESENT", "ABSENT", "LATE", "EXCUSED" })
            status.Items.Add(option);
        var periods = new List<MobileAttendancePeriod>();
        var periodPicker = new Picker { Title = "Attendance period", AutomationId = "AttendancePeriod" };
        periodPicker.Items.Add("Daily (no period)");
        periodPicker.SelectedIndex = 0;
        var loadPeriods = Action("Load attendance periods");
        loadPeriods.Clicked += async (_, _) =>
        {
            loadPeriods.IsEnabled = false;
            try
            {
                periods = (await _api.GetAttendancePeriodsAsync(campusId))
                    .Where(x => x.IsActive && x.CampusId == campusId).ToList();
                periodPicker.Items.Clear();
                periodPicker.Items.Add("Daily (no period)");
                foreach (var period in periods)
                    periodPicker.Items.Add($"{period.Name} ({period.Code}) {period.StartsAt:HH:mm}–{period.EndsAt:HH:mm}");
                periodPicker.SelectedIndex = 0;
                if (periods.Count == 0)
                    await ShellAlertAsync("Attendance periods", "No active periods found. Daily attendance is still available.");
            }
            catch (UnauthorizedAccessException ex)
            {
                await HandleTeacherAccessErrorAsync(ex);
            }
            catch (Exception ex) { await ShellAlertAsync("Unable to load periods", ex.Message); }
            finally { loadPeriods.IsEnabled = true; }
        };
        var remarks = new Entry { Placeholder = "Remarks (optional, max 250 characters)", MaxLength = 250 };
        var result = Note("");
        var save = Action("Save attendance");
        save.Clicked += async (_, _) =>
        {
            if (status.SelectedIndex < 0)
            {
                result.Text = "Select an attendance status.";
                return;
            }
            if (date.Date is not DateTime selectedDate)
            {
                result.Text = "Select an attendance date.";
                return;
            }
            if (periodPicker.SelectedIndex < 0 || periodPicker.SelectedIndex > periods.Count)
            {
                result.Text = "Select a valid attendance period.";
                return;
            }
            var selectedPeriodId = periodPicker.SelectedIndex == 0
                ? (Guid?)null : periods[periodPicker.SelectedIndex - 1].Id;
            save.IsEnabled = false;
            result.Text = "Saving attendance…";
            try
            {
                await _api.MarkTeacherAttendanceAsync(new MobileAttendanceMark(
                    campusId, student.EnrollmentId, DateOnly.FromDateTime(selectedDate),
                    selectedPeriodId, status.Items[status.SelectedIndex], remarks.Text?.Trim()));
                result.Text = "Attendance saved.";
                await ShellAlertAsync("Attendance saved", "The server accepted the attendance entry.");
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Text = "Attendance was not saved.";
                await HandleTeacherAccessErrorAsync(ex);
            }
            catch (HttpRequestException ex)
            {
                result.Text = ex.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? "Attendance cannot be recorded for this date (for example, an academic holiday)."
                    : "The server rejected the attendance entry. No success has been recorded.";
                await ShellAlertAsync("Attendance not saved", result.Text);
            }
            catch (Exception ex)
            {
                result.Text = "Attendance could not be saved.";
                await ShellAlertAsync("Unable to save", ex.Message);
            }
            finally { save.IsEnabled = true; }
        };
        layout.Children.Add(Note("Select the date and status. The backend validates permissions and attendance rules."));
        layout.Children.Add(date);
        layout.Children.Add(status);
        layout.Children.Add(Note("Use daily attendance or load an authorized class period. Periods may require additional read access."));
        layout.Children.Add(loadPeriods);
        layout.Children.Add(periodPicker);
        layout.Children.Add(remarks);
        layout.Children.Add(save);
        layout.Children.Add(result);
        return Page("Mark attendance", layout);
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
