using CryptoFutureMonitor.Services;
using CryptoFutureMonitor.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ExchangeMonitorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ExchangeMonitorService>());

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Print startup banner
Console.WriteLine("===========================================");
Console.WriteLine("🚀 Crypto Future Monitor - 6 Exchanges");
Console.WriteLine("===========================================");
Console.WriteLine("Binance | Gate.io | OKX | Bybit | HTX | MEXC");
Console.WriteLine($"API: http://localhost:5000/api/symbol");
Console.WriteLine($"Hub: http://localhost:5000/priceHub");
Console.WriteLine("===========================================");

app.UseCors();

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.MapControllers();
app.MapHub<PriceHub>("/priceHub");

// Fallback to index.html for SPA
app.MapFallbackToFile("index.html");

app.Run();
