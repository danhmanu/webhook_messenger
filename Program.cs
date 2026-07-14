var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.AddHttpClient<OpenAiService>();
builder.Services.AddHttpClient<MessengerService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddSingleton<MessengerWebhookVerifier>();
builder.Services.AddSingleton<AppDatabase>();
builder.Services.AddControllers();

var app = builder.Build();

await app.Services.GetRequiredService<AppDatabase>().InitializeAsync(CancellationToken.None);

app.UseStaticFiles();
app.MapControllers();
app.Run();
