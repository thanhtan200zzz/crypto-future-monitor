#!/bin/bash
cd /home/claude/CryptoFutureMonitor/Backend

# Create PriceHub
cat > Hubs/PriceHub.cs << 'EOF'
using Microsoft.AspNetCore.SignalR;

namespace CryptoFutureMonitor.Hubs;

public class PriceHub : Hub
{
    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol);
        Console.WriteLine($"Client {Context.ConnectionId} subscribed to {symbol}");
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol);
    }
}
EOF

# Create SymbolController
cat > Controllers/SymbolController.cs << 'EOF'
using Microsoft.AspNetCore.Mvc;
using CryptoFutureMonitor.Services;
using CryptoFutureMonitor.Models;

namespace CryptoFutureMonitor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SymbolController : ControllerBase
{
    private readonly ExchangeMonitorService _monitorService;

    public SymbolController(ExchangeMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    [HttpGet]
    public ActionResult<List<string>> GetSymbols()
    {
        return Ok(_monitorService.GetActiveSymbols());
    }

    [HttpGet("{symbol}")]
    public ActionResult<CombinedExchangeData> GetSymbol(string symbol)
    {
        var data = _monitorService.GetSymbolData(symbol);
        if (data == null)
            return NotFound();
        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult> AddSymbol([FromBody] AddSymbolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest("Symbol is required");

        var success = await _monitorService.AddSymbol(request.Symbol);
        if (!success)
            return Conflict("Symbol already exists or failed to add");

        return Ok(new { message = $"Symbol {request.Symbol} added successfully" });
    }

    [HttpDelete("{symbol}")]
    public async Task<ActionResult> RemoveSymbol(string symbol)
    {
        await _monitorService.RemoveSymbol(symbol);
        return Ok(new { message = $"Symbol {symbol} removed" });
    }

    [HttpDelete]
    public async Task<ActionResult> RemoveAllSymbols()
    {
        await _monitorService.RemoveAllSymbols();
        return Ok(new { message = "All symbols removed" });
    }
}

public class AddSymbolRequest
{
    public string Symbol { get; set; } = string.Empty;
}
EOF

# Create Program.cs with CORS FIXED
cat > Program.cs << 'EOF'
using CryptoFutureMonitor.Hubs;
using CryptoFutureMonitor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ExchangeMonitorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ExchangeMonitorService>());

// CORS - FIXED for SignalR
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:8080", 
                "http://127.0.0.1:8080",
                "http://localhost:5500"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PriceHub>("/priceHub");

Console.WriteLine("===========================================");
Console.WriteLine("🚀 Crypto Future Monitor - 6 Exchanges");
Console.WriteLine("===========================================");
Console.WriteLine("Binance | Gate.io | OKX | Bybit | HTX | MEXC");
Console.WriteLine($"API: http://localhost:5000/api/symbol");
Console.WriteLine($"Hub: http://localhost:5000/priceHub");
Console.WriteLine("===========================================");

app.Run();
EOF

# Create .csproj with all packages
cat > CryptoFutureMonitor.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
  </ItemGroup>
</Project>
EOF

# Create appsettings.json
cat > appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:5000"
}
EOF

echo "Backend files created!"
