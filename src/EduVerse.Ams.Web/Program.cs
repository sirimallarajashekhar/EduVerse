using EduVerse.AuthWeb;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();
builder.Services.AddEduVerseCookieAuthentication(builder.Configuration, builder.Environment);
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/ams/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/ams" });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/ams/health/live");
app.MapControllers();
app.Run();

public partial class Program { }
