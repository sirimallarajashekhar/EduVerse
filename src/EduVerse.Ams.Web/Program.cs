var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/ams/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
// Public URLs keep the /ams prefix when forwarded by the Portal's YARP routes.
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/ams" });
app.UseRouting();
app.MapHealthChecks("/ams/health/live");
app.MapControllers();
app.Run();

public partial class Program { }
