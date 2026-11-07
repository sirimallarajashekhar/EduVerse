using EduVerse.AuthWeb;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();
builder.Services.AddEduVerseCookieAuthentication(builder.Configuration, builder.Environment);
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/hrms/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/hrms" });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/hrms/health/live");
app.MapControllers();
app.Run();

public partial class Program { }
