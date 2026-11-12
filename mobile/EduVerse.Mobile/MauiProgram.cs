using Microsoft.Extensions.Logging;

namespace EduVerse.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(25) });
        builder.Services.AddSingleton<EduVerseApi>();
        builder.Services.AddSingleton<App>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
