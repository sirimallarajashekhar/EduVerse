using Microsoft.Maui.Controls;

namespace EduVerse.Mobile;

/// <summary>
/// Thin admin screens. All records and authorization decisions come from existing EduVerse APIs.
/// </summary>
public sealed partial class App
{
    private Button AdminStudentManagementAction(Guid campusId)
    {
        var button = Action("Guardians and enrollments");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var students = await _api.GetRegisteredStudentsAsync(campusId);
                var layout = new VerticalStackLayout { Spacing = 12 };
                layout.Children.Add(Heading("Manage students"));
                if (students.Count == 0) layout.Children.Add(Note("No students registered for this campus."));
                foreach (var student in students)
                {
                    var selected = student;
                    var open = Action($"{selected.GivenName} {selected.FamilyName} ({selected.AdmissionNumber})");
                    open.Clicked += async (_, _) =>
                        await _root.Navigation.PushAsync(AdminStudentDetailsPage(campusId, selected));
                    layout.Children.Add(open);
                }
                await _root.Navigation.PushAsync(Page("Manage students", layout));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load students", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private ContentPage AdminStudentDetailsPage(Guid campusId, MobileStudentRecord student)
    {
        var layout = new VerticalStackLayout { Spacing = 12 };
        layout.Children.Add(Heading($"{student.GivenName} {student.FamilyName}"));
        layout.Children.Add(Note($"Admission: {student.AdmissionNumber} • {student.Status}"));
        layout.Children.Add(GuardianAction(campusId, student.Id));
        layout.Children.Add(EnrollmentAction(campusId, student.Id));
        return Page("Student administration", layout);
    }

    private Button GuardianAction(Guid campusId, Guid studentId)
    {
        var button = Action("Guardians");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var layout = new VerticalStackLayout { Spacing = 12 };
                layout.Children.Add(Heading("Guardians"));
                var guardians = await _api.GetGuardiansAsync(campusId, studentId);
                if (guardians.Count == 0) layout.Children.Add(Note("No guardians on record."));
                foreach (var guardian in guardians)
                    layout.Children.Add(Note($"{guardian.FullName} • {guardian.Relationship}" +
                        (guardian.IsPrimary ? " • Primary" : "")));
                layout.Children.Add(AddGuardianAction(campusId, studentId));
                await _root.Navigation.PushAsync(Page("Guardians", layout));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load guardians", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button AddGuardianAction(Guid campusId, Guid studentId)
    {
        var button = Action("Add guardian");
        button.Clicked += async (_, _) =>
        {
            var layout = new VerticalStackLayout { Spacing = 12 };
            layout.Children.Add(Heading("Add guardian"));
            var name = new Entry { Placeholder = "Full name", AutomationId = "GuardianFullName" };
            var relationship = new Picker { Title = "Relationship", AutomationId = "GuardianRelationship" };
            foreach (var value in new[] { "MOTHER", "FATHER", "GUARDIAN", "OTHER" })
                relationship.Items.Add(value);
            var phone = new Entry { Placeholder = "Phone (optional)", Keyboard = Keyboard.Telephone };
            var email = new Entry { Placeholder = "Email (optional)", Keyboard = Keyboard.Email };
            var primary = new CheckBox { AutomationId = "GuardianPrimary" };
            var status = Note("");
            var submit = Action("Save guardian");
            submit.Clicked += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text) || relationship.SelectedIndex < 0)
                {
                    status.Text = "Enter a name and select a relationship.";
                    return;
                }
                submit.IsEnabled = false;
                status.Text = "Saving…";
                try
                {
                    var saved = await _api.AddGuardianAsync(campusId, studentId,
                        new MobileGuardianCreate(name.Text.Trim(),
                            relationship.Items[relationship.SelectedIndex],
                            string.IsNullOrWhiteSpace(phone.Text) ? null : phone.Text.Trim(),
                            string.IsNullOrWhiteSpace(email.Text) ? null : email.Text.Trim(),
                            primary.IsChecked));
                    status.Text = $"Guardian saved: {saved.FullName}";
                    await ShellAlertAsync("Guardian saved", "Reopen the guardians list to see the new record.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    status.Text = "Guardian was not saved.";
                    await HandleTeacherAccessErrorAsync(ex);
                }
                catch (Exception ex)
                {
                    status.Text = "Unable to confirm save. Check the records before retrying.";
                    await ShellAlertAsync("Guardian not saved", ex.Message);
                }
                finally { submit.IsEnabled = true; }
            };
            layout.Children.Add(name);
            layout.Children.Add(relationship);
            layout.Children.Add(phone);
            layout.Children.Add(email);
            layout.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8, Children = { primary, Note("Primary guardian") }
            });
            layout.Children.Add(submit);
            layout.Children.Add(status);
            await _root.Navigation.PushAsync(Page("Add guardian", layout));
        };
        return button;
    }

    private Button EnrollmentAction(Guid campusId, Guid studentId)
    {
        var button = Action("Academic enrollments");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var enrollments = await _api.GetEnrollmentsAsync(campusId, studentId);
                var layout = new VerticalStackLayout { Spacing = 12 };
                layout.Children.Add(Heading("Enrollments"));
                if (enrollments.Count == 0) layout.Children.Add(Note("No academic enrollments on record."));
                foreach (var enrollment in enrollments)
                    layout.Children.Add(Note($"Academic year: {enrollment.AcademicYearId:D}" +
                        $" • Class/item: {enrollment.AcademicItemId:D}" +
                        $" • Status: {enrollment.Status}"));
                layout.Children.Add(AddEnrollmentAction(campusId, studentId));
                await _root.Navigation.PushAsync(Page("Enrollments", layout));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load enrollments", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button AddEnrollmentAction(Guid campusId, Guid studentId)
    {
        var button = Action("Enroll in academic year");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var years = (await _api.GetAcademicYearsAsync(campusId))
                    .Where(year => year.IsActive).ToList();
                var items = (await _api.GetAcademicItemsAsync(campusId))
                    .Where(item => item.IsActive && item.Kind is "CLASS" or "GRADE" or "SECTION"
                        or "PROGRAM" or "COURSE" or "BATCH").ToList();
                var layout = new VerticalStackLayout { Spacing = 12 };
                layout.Children.Add(Heading("Academic placement"));
                if (years.Count == 0 || items.Count == 0)
                    layout.Children.Add(Note("The campus needs an active academic year and academic catalog entry before enrollment."));
                var yearPicker = new Picker { Title = "Academic year", AutomationId = "EnrollmentYear" };
                foreach (var year in years) yearPicker.Items.Add($"{year.Name} ({year.Code})");
                var itemPicker = new Picker { Title = "Class / academic item", AutomationId = "EnrollmentItem" };
                var eligible = new List<MobileAcademicItem>();
                void RefreshItems()
                {
                    eligible = yearPicker.SelectedIndex < 0 ? new List<MobileAcademicItem>()
                        : items.Where(item => item.AcademicYearId is null ||
                            item.AcademicYearId == years[yearPicker.SelectedIndex].Id).ToList();
                    itemPicker.Items.Clear();
                    foreach (var item in eligible)
                        itemPicker.Items.Add($"{item.Name} ({item.Kind} • {item.Code})");
                    itemPicker.SelectedIndex = eligible.Count == 0 ? -1 : 0;
                }
                yearPicker.SelectedIndexChanged += (_, _) => RefreshItems();
                if (years.Count > 0) yearPicker.SelectedIndex = 0;
                layout.Children.Add(yearPicker);
                layout.Children.Add(itemPicker);
                var status = Note("");
                var submit = Action("Confirm enrollment");
                submit.IsEnabled = years.Count > 0 && items.Count > 0;
                submit.Clicked += async (_, _) =>
                {
                    if (yearPicker.SelectedIndex < 0 ||
                        itemPicker.SelectedIndex < 0 || itemPicker.SelectedIndex >= eligible.Count)
                    {
                        status.Text = "Select an academic year and class/item.";
                        return;
                    }
                    var year = years[yearPicker.SelectedIndex];
                    var item = eligible[itemPicker.SelectedIndex];
                    var currentPage = _root.Navigation.NavigationStack.LastOrDefault();
                    if (currentPage is null || !await currentPage.DisplayAlertAsync(
                        "Confirm enrollment", $"Enroll in {item.Name} for {year.Name}?",
                        "Enroll", "Cancel")) return;
                    submit.IsEnabled = false;
                    status.Text = "Submitting enrollment…";
                    try
                    {
                        var result = await _api.EnrollStudentAsync(campusId, studentId,
                            new MobileEnrollmentCreate(year.Id, item.Id));
                        status.Text = $"Enrollment saved: {result.Status}";
                        await ShellAlertAsync("Enrollment saved", "Reopen the enrollment list to see the record.");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        status.Text = "Enrollment was not saved.";
                        await HandleTeacherAccessErrorAsync(ex);
                    }
                    catch (Exception ex)
                    {
                        status.Text = "Unable to confirm enrollment. Check records before retrying.";
                        await ShellAlertAsync("Enrollment not saved", ex.Message);
                    }
                    finally { submit.IsEnabled = true; }
                };
                layout.Children.Add(submit);
                layout.Children.Add(status);
                await _root.Navigation.PushAsync(Page("Enroll student", layout));
            }
            catch (UnauthorizedAccessException ex) { await HandleTeacherAccessErrorAsync(ex); }
            catch (Exception ex) { await ShellAlertAsync("Unable to load academic catalog", ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
}
