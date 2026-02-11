using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using CryptoFutureMonitor.Models;

namespace CryptoFutureMonitor.Services;

public class BinanceFutureWebSocket
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly string _symbol;
    private readonly Action<FutureSymbolData> _onDataReceived;
    private static readonly HttpClient _httpClient = new HttpClient();

    public BinanceFutureWebSocket(string symbol, Action<FutureSymbolData> onDataReceived)
    {
        _symbol = symbol.ToLower();
        _onDataReceived = onDataReceived;
    }

    public async Task ConnectAsync()
    {
        try
        {
            await UpdateFundingRateAsync();

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            string wsUrl = $"wss://fstream.binance.com/ws/{_symbol}@aggTrade";
            Console.WriteLine($"[Binance] Connecting to: {wsUrl}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);
            Console.WriteLine($"[Binance] Connected successfully for {_symbol}");

            _ = Task.Run(() => ReceiveDataAsync());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance] Error connecting WebSocket for {_symbol}: {ex.Message}");
        }
    }

    private async Task ReceiveDataAsync()
    {
        var buffer = new byte[4096];

        while (_webSocket?.State == WebSocketState.Open && _cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[Binance] WebSocket closed for {_symbol}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Binance] Error receiving data for {_symbol}: {ex.Message}");
                break;
            }
        }
    }

    private FutureSymbolData _lastData = null;

    private void ProcessMessage(string message)
    {
        try
        {
            var json = JObject.Parse(message);
            var lastPrice = decimal.Parse(json["p"]?.ToString() ?? "0");

            if (lastPrice > 0)
            {
                // Create new data object, preserve funding info if exists
                var data = new FutureSymbolData(_symbol.ToUpper())
                {
                    MarkPrice = lastPrice,
                    LastUpdate = DateTime.Now
                };

                // Preserve funding data from last update
                if (_lastData != null)
                {
                    data.FundingRate = _lastData.FundingRate;
                    data.NextFundingTime = _lastData.NextFundingTime;
                    data.PriceChange24h = _lastData.PriceChange24h;
                }

                _lastData = data;
                _onDataReceived?.Invoke(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance] Error processing message for {_symbol}: {ex.Message}");
        }
    }

    private async Task UpdateFundingRateAsync()
    {
        try
        {
            string premiumUrl = $"https://fapi.binance.com/fapi/v1/premiumIndex?symbol={_symbol.ToUpper()}";
            string tickerUrl = $"https://fapi.binance.com/fapi/v1/ticker/24hr?symbol={_symbol.ToUpper()}";

            var premiumResponse = await _httpClient.GetStringAsync(premiumUrl);
            var premiumJson = JObject.Parse(premiumResponse);

            var tickerResponse = await _httpClient.GetStringAsync(tickerUrl);
            var tickerJson = JObject.Parse(tickerResponse);

            var data = new FutureSymbolData(_symbol.ToUpper())
            {
                MarkPrice = decimal.Parse(tickerJson["lastPrice"]?.ToString() ?? "0"),
                PriceChange24h = decimal.Parse(tickerJson["priceChangePercent"]?.ToString() ?? "0"),
                FundingRate = decimal.Parse(premiumJson["lastFundingRate"]?.ToString() ?? "0"),
                NextFundingTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(premiumJson["nextFundingTime"]?.ToString() ?? "0")).LocalDateTime
            };

            _lastData = data;
            _onDataReceived?.Invoke(data);
            
            Console.WriteLine($"[Binance] {_symbol} - Funding: {data.FundingRate:P4}, Next: {data.NextFundingTime:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance] Error getting funding rate for {_symbol}: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance] Error disconnecting WebSocket for {_symbol}: {ex.Message}");
        }
    }

    public async Task StartFundingRateUpdateAsync()
    {
        while (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(30000, _cancellationTokenSource.Token);
                await UpdateFundingRateAsync();
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}