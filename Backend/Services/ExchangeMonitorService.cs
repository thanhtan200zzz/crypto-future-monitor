using Microsoft.AspNetCore.SignalR;
using CryptoFutureMonitor.Models;
using CryptoFutureMonitor.Hubs;
using System.Collections.Concurrent;

namespace CryptoFutureMonitor.Services;

public class ExchangeMonitorService : BackgroundService
{
    private readonly IHubContext<PriceHub> _hubContext;
    private readonly ConcurrentDictionary<string, BinanceFutureWebSocket> _binanceConnections = new();
    private readonly ConcurrentDictionary<string, GateIOFutureWebSocket> _gateConnections = new();
    private readonly ConcurrentDictionary<string, OKXFutureWebSocket> _okxConnections = new();
    private readonly ConcurrentDictionary<string, BybitFutureWebSocket> _bybitConnections = new();
    private readonly ConcurrentDictionary<string, HTXFutureWebSocket> _htxConnections = new();
    private readonly ConcurrentDictionary<string, MEXCFutureWebSocket> _mexcConnections = new();
    private readonly ConcurrentDictionary<string, CombinedExchangeData> _symbolData = new();
    private readonly ILogger<ExchangeMonitorService> _logger;

    public ExchangeMonitorService(IHubContext<PriceHub> hubContext, ILogger<ExchangeMonitorService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<bool> AddSymbol(string symbol)
    {
        symbol = symbol.ToUpper();

        if (_binanceConnections.ContainsKey(symbol))
        {
            _logger.LogWarning($"Symbol {symbol} already exists");
            return false;
        }

        try
        {
            _symbolData[symbol] = new CombinedExchangeData(symbol);

            // BINANCE - callback: Action<FutureSymbolData>
            var binanceWs = new BinanceFutureWebSocket(symbol, data => OnBinanceDataReceived(symbol, data));
            _binanceConnections[symbol] = binanceWs;
            await binanceWs.ConnectAsync();
            _ = Task.Run(() => binanceWs.StartFundingRateUpdateAsync());

            // GATE.IO - symbol format: BTC_USDT, callback: Action<string, decimal, decimal, DateTime>
            var gateSymbol = symbol.Replace("USDT", "_USDT");
            var gateWs = new GateIOFutureWebSocket(gateSymbol, (sym, price, funding, nextTime) => OnGateDataReceived(symbol, price, funding, nextTime));
            _gateConnections[symbol] = gateWs;
            _ = Task.Run(async () =>
            {
                if (await gateWs.ConnectAsync())
                {
                    await gateWs.StartFundingRateUpdateAsync();
                }
            });

            // OKX - symbol format: BTC-USDT-SWAP, callback: Action<string, decimal, decimal, DateTime>
            var okxSymbol = symbol.Replace("USDT", "") + "-USDT-SWAP";
            var okxWs = new OKXFutureWebSocket(okxSymbol, (sym, price, funding, nextTime) => OnOKXDataReceived(symbol, price, funding, nextTime));
            _okxConnections[symbol] = okxWs;
            _ = Task.Run(async () =>
            {
                if (await okxWs.ConnectAsync())
                {
                    await okxWs.StartFundingRateUpdateAsync();
                }
            });

            // BYBIT - symbol format: BTCUSDT (same as input), callback: Action<string, decimal, decimal, DateTime>
            var bybitWs = new BybitFutureWebSocket(symbol, (sym, price, funding, nextTime) => OnBybitDataReceived(symbol, price, funding, nextTime));
            _bybitConnections[symbol] = bybitWs;
            _ = Task.Run(async () =>
            {
                if (await bybitWs.ConnectAsync())
                {
                    await bybitWs.StartFundingRateUpdateAsync();
                }
            });

            // HTX - symbol format: BTC-USDT, callback: Action<string, decimal, decimal, DateTime>
            var htxSymbol = symbol.Replace("USDT", "-USDT");
            var htxWs = new HTXFutureWebSocket(htxSymbol, (sym, price, funding, nextTime) => OnHTXDataReceived(symbol, price, funding, nextTime));
            _htxConnections[symbol] = htxWs;
            _ = Task.Run(async () =>
            {
                if (await htxWs.ConnectAsync())
                {
                    await htxWs.StartFundingRateUpdateAsync();
                }
            });

            // MEXC - symbol format: BTC_USDT, callback: Action<string, decimal, decimal, DateTime>
            var mexcSymbol = symbol.Replace("USDT", "_USDT");
            var mexcWs = new MEXCFutureWebSocket(mexcSymbol, (sym, price, funding, nextTime) => OnMEXCDataReceived(symbol, price, funding, nextTime));
            _mexcConnections[symbol] = mexcWs;
            _ = Task.Run(async () =>
            {
                if (await mexcWs.ConnectAsync())
                {
                    await mexcWs.StartFundingRateUpdateAsync();
                }
            });

            _logger.LogInformation($"Successfully added symbol: {symbol}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error adding symbol {symbol}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveSymbol(string symbol)
    {
        symbol = symbol.ToUpper();

        if (_binanceConnections.TryRemove(symbol, out var binanceWs))
            await binanceWs.DisconnectAsync();

        if (_gateConnections.TryRemove(symbol, out var gateWs))
            await gateWs.DisconnectAsync();

        if (_okxConnections.TryRemove(symbol, out var okxWs))
            await okxWs.DisconnectAsync();

        if (_bybitConnections.TryRemove(symbol, out var bybitWs))
            await bybitWs.DisconnectAsync();

        if (_htxConnections.TryRemove(symbol, out var htxWs))
            await htxWs.DisconnectAsync();

        if (_mexcConnections.TryRemove(symbol, out var mexcWs))
            await mexcWs.DisconnectAsync();

        _symbolData.TryRemove(symbol, out _);

        _logger.LogInformation($"Removed symbol: {symbol}");
        return true;
    }

    public async Task RemoveAllSymbols()
    {
        foreach (var symbol in _binanceConnections.Keys.ToList())
        {
            await RemoveSymbol(symbol);
        }
    }

    public List<string> GetActiveSymbols()
    {
        return _symbolData.Keys.ToList();
    }

    public CombinedExchangeData? GetSymbolData(string symbol)
    {
        symbol = symbol.ToUpper();
        _symbolData.TryGetValue(symbol, out var data);
        return data;
    }

    // Binance callback: Action<FutureSymbolData>
    private async void OnBinanceDataReceived(string symbol, FutureSymbolData data)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.BinancePrice = data.MarkPrice;
            combinedData.BinanceFundingRate = data.FundingRate;
            combinedData.BinanceNextFunding = data.NextFundingTime;
            combinedData.HasBinance = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    // Gate.io callback: Action<string, decimal, decimal, DateTime>
    private async void OnGateDataReceived(string symbol, decimal price, decimal funding, DateTime nextTime)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.GatePrice = price;
            combinedData.GateFundingRate = funding;
            combinedData.GateNextFunding = nextTime;
            combinedData.HasGate = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    // OKX callback
    private async void OnOKXDataReceived(string symbol, decimal price, decimal funding, DateTime nextTime)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.OKXPrice = price;
            combinedData.OKXFundingRate = funding;
            combinedData.OKXNextFunding = nextTime;
            combinedData.HasOKX = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    // Bybit callback
    private async void OnBybitDataReceived(string symbol, decimal price, decimal funding, DateTime nextTime)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.BybitPrice = price;
            combinedData.BybitFundingRate = funding;
            combinedData.BybitNextFunding = nextTime;
            combinedData.HasBybit = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    // HTX callback
    private async void OnHTXDataReceived(string symbol, decimal price, decimal funding, DateTime nextTime)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.HTXPrice = price;
            combinedData.HTXFundingRate = funding;
            combinedData.HTXNextFunding = nextTime;
            combinedData.HasHTX = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    // MEXC callback
    private async void OnMEXCDataReceived(string symbol, decimal price, decimal funding, DateTime nextTime)
    {
        if (_symbolData.TryGetValue(symbol, out var combinedData))
        {
            combinedData.MEXCPrice = price;
            combinedData.MEXCFundingRate = funding;
            combinedData.MEXCNextFunding = nextTime;
            combinedData.HasMEXC = true;
            combinedData.LastUpdate = DateTime.Now;
            await _hubContext.Clients.Group(symbol).SendAsync("ReceivePriceUpdate", combinedData);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExchangeMonitorService started with 6 exchanges");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(10000, stoppingToken);
        }

        await RemoveAllSymbols();
        _logger.LogInformation("ExchangeMonitorService stopped");
    }
}