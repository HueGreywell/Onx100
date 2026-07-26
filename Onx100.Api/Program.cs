using dotenv.net;
using Onx100Api;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DeviceManager>();
builder.Services.AddHostedService<DeviceEventBroadcaster>();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy =
        System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var apiUrl = Environment.GetEnvironmentVariable("API_URL");
if (apiUrl == null)
    throw new InvalidOperationException("API_URL environment variable is not set.");

builder.WebHost.UseUrls(apiUrl);

var app = builder.Build();

app.UseCors();
app.UseDeviceExceptionHandler();
app.MapHub<DeviceHub>("/hub");
app.MapDeviceEndpoints();

app.Run();
