using EduVerse.AuthWeb;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddEduVerseCookieAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient("EduVerseApi", client =>
{
    var url = builder.Configuration["Api:BaseUrl"]
        ?? throw new InvalidOperationException("Api:BaseUrl must be configured.");
    client.BaseAddress = new Uri(url, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapReverseProxy().RequireAuthorization();
app.Run();

public partial class Program { }
