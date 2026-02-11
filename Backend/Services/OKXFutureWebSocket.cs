using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace CryptoFutureMonitor.Services;

public class OKXFutureWebSocket
{
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly string _symbol;
    private readonly Action<string, decimal, decimal, DateTime> _onDataReceived;
    private static readonly HttpClient _httpClient = new HttpClient();
    private decimal _lastPrice = 0;
    private decimal _lastFunding = 0;
    private DateTime _lastNextTime = DateTime.MinValue;

    public OKXFutureWebSocket(string symbol, Action<string, decimal, decimal, DateTime> onDataReceived)
    {
        _symbol = symbol;
        _onDataReceived = onDataReceived;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var initialData = await GetFundingAndPriceAsync();
            if (!initialData.success)
                return false;

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            string wsUrl = "wss://ws.okx.com:8443/ws/v5/public";

            Console.WriteLine($"[OKX] Connecting to: {wsUrl}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

            Console.WriteLine($"[OKX] Connected for {_symbol}");

            await SubscribeToTrades();
            _ = Task.Run(() => ReceiveDataAsync());

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OKX] Error connecting for {_symbol}: {ex.Message}");
            return false;
        }
    }

    private async Task SubscribeToTrades()
    {
        try
        {
            var subscribeMessage = new
            {
                op = "subscribe",
                args = new[]
                {
                    new
                    {
                        channel = "trades",
                        instId = _symbol
                    }
                }
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

            Console.WriteLine($"[OKX] Subscribed to trades for {_symbol}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OKX] Error subscribing: {ex.Message}");
        }
    }

    private async Task ReceiveDataAsync()
    {
        var buffer = new byte[4096];

        while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[OKX] WebSocket closed for {_symbol}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OKX] Error receiving data for {_symbol}: {ex.Message}");
                break;
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var json = JObject.Parse(message);

            if (json["event"] != null)
            {
                string eventType = json["event"].ToString();
                if (eventType == "subscribe")
                {
                    Console.WriteLine($"[OKX] Subscribe confirmed for {_symbol}");
                }
                return;
            }

            var data = json["data"] as JArray;
            if (data != null && data.Count > 0)
            {
                var trade = data[0];
                string px = trade["px"]?.ToString();

                if (!string.IsNullOrEmpty(px))
                {
                    decimal price = decimal.Parse(px);
                    if (price > 0)
                    {
                        _lastPrice = price;
                        Console.WriteLine($"[OKX] {_symbol} price update: {price}");
                        _onDataReceived?.Invoke(_symbol, price, _lastFunding, _lastNextTime);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OKX] Error processing message for {_symbol}: {ex.Message}");
        }
    }

    public async Task<(bool success, decimal price, decimal fundingRate, DateTime nextFundingTime)> GetFundingAndPriceAsync()
    {
        try
        {
            string fundingUrl = $"https://www.okx.com/api/v5/public/funding-rate?instId={_symbol}";

            var fundingResponse = await _httpClient.GetStringAsync(fundingUrl);
            var fundingJson = JObject.Parse(fundingResponse);

            var dataArray = fundingJson["data"] as JArray;
            if (dataArray == null || dataArray.Count == 0)
            {
                Console.WriteLine($"[OKX] No funding data for {_symbol}");
                return (false, 0, 0, DateTime.MinValue);
            }

            var data = dataArray[0];
            decimal fundingRate = decimal.Parse(data["fundingRate"]?.ToString() ?? "0");

            string nextFundingTimeStr = data["nextFundingTime"]?.ToString() ?? "0";
            Console.WriteLine($"[OKX] Raw nextFundingTime value: {nextFundingTimeStr}");

            long nextFundingTimeMs = long.Parse(nextFundingTimeStr);

            DateTimeOffset dto = DateTimeOffset.FromUnixTimeMilliseconds(nextFundingTimeMs);
            DateTime nextFundingTime = dto.LocalDateTime;

            TimeSpan timeUntilFunding = nextFundingTime - DateTime.Now;

            if (timeUntilFunding.TotalHours > 8)
            {
                nextFundingTime = nextFundingTime.AddHours(-8);
                timeUntilFunding = nextFundingTime - DateTime.Now;
                Console.WriteLine($"[OKX] Corrected timestamp by -8 hours. New time: {nextFundingTime:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine($"[OKX] ===== FUNDING TIME ANALYSIS =====");
            Console.WriteLine($"[OKX] Raw timestamp: {nextFundingTimeMs}");
            Console.WriteLine($"[OKX] UTC time: {dto.UtcDateTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"[OKX] Local time: {nextFundingTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"[OKX] Current Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"[OKX] Time diff: {timeUntilFunding.TotalHours:F2} hours ({timeUntilFunding:hh\\:mm\\:ss})");
            Console.WriteLine($"[OKX] ===============================");

            string tickerUrl = $"https://www.okx.com/api/v5/market/ticker?instId={_symbol}";
            var tickerResponse = await _httpClient.GetStringAsync(tickerUrl);
            var tickerJson = JObject.Parse(tickerResponse);

            var tickerArray = tickerJson["data"] as JArray;
            decimal lastPrice = 0;
            if (tickerArray != null && tickerArray.Count > 0)
            {
                lastPrice = decimal.Parse(tickerArray[0]["last"]?.ToString() ?? "0");
            }

            // Store funding data
            _lastFunding = fundingRate;
            _lastNextTime = nextFundingTime;
            if (lastPrice > 0) _lastPrice = lastPrice;

            Console.WriteLine($"[OKX] {_symbol} - Price: {lastPrice}, Funding: {fundingRate}, Next: {nextFundingTime}");

            _onDataReceived?.Invoke(_symbol, lastPrice, fundingRate, nextFundingTime);

            return (true, lastPrice, fundingRate, nextFundingTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OKX] Error getting data for {_symbol}: {ex.Message}");
            return (false, 0, 0, DateTime.MinValue);
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
            Console.WriteLine($"[OKX] Error disconnecting for {_symbol}: {ex.Message}");
        }
    }

    public async Task StartFundingRateUpdateAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(30000, _cancellationTokenSource.Token);
                await GetFundingAndPriceAsync();
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public decimal GetLastPrice()
    {
        return _lastPrice;
    }
}