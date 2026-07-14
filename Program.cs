var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.AddHttpClient<HospitalChatModel>();
builder.Services.AddHttpClient<MessengerService>();
builder.Services.AddScoped<OpenAiService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddSingleton<MessengerWebhookVerifier>();
builder.Services.AddSingleton<AppDatabase>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminFrontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("App:CorsOrigins")
            .Get<string[]>() ?? [];

        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});
builder.Services.AddControllers();

var app = builder.Build();

await app.Services.GetRequiredService<AppDatabase>().InitializeAsync(CancellationToken.None);

app.UseCors("AdminFrontend");
app.MapControllers();
app.Run();
