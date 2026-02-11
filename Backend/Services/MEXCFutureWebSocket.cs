using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace CryptoFutureMonitor.Services;

public class MEXCFutureWebSocket
{
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly string _symbol;
    private readonly Action<string, decimal, decimal, DateTime> _onDataReceived;
    private static readonly HttpClient _httpClient = new HttpClient();
    private decimal _lastPrice = 0;
    private decimal _lastFunding = 0;
    private DateTime _lastNextTime = DateTime.MinValue;

    public MEXCFutureWebSocket(string symbol, Action<string, decimal, decimal, DateTime> onDataReceived)
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

            string wsUrl = "wss://contract.mexc.com/edge";

            Console.WriteLine($"[MEXC] Connecting to: {wsUrl}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

            Console.WriteLine($"[MEXC] Connected for {_symbol}");

            await SubscribeToTicker();
            _ = Task.Run(() => ReceiveDataAsync());

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEXC] Error connecting for {_symbol}: {ex.Message}");
            return false;
        }
    }

    private async Task SubscribeToTicker()
    {
        try
        {
            var subscribeMessage = new
            {
                method = "sub.ticker",
                param = new
                {
                    symbol = _symbol
                }
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage);
            Console.WriteLine($"[MEXC] Sending subscription: {json}");

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

            Console.WriteLine($"[MEXC] Subscribed to ticker for {_symbol}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEXC] Error subscribing: {ex.Message}");
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
                    Console.WriteLine($"[MEXC] WebSocket closed for {_symbol}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MEXC] Error receiving data for {_symbol}: {ex.Message}");
                break;
            }
        }
    }

    private async void ProcessMessage(string message)
    {
        try
        {
            Console.WriteLine($"[MEXC] Raw message: {message}");

            var json = JObject.Parse(message);

            if (json["channel"]?.ToString() == "pong")
            {
                return;
            }

            if (json["channel"]?.ToString() == "rs.error")
            {
                Console.WriteLine($"[MEXC] Subscribe error for {_symbol}: {message}");
                return;
            }

            if (json["channel"]?.ToString() == "rs.sub.ticker")
            {
                Console.WriteLine($"[MEXC] Subscribe confirmed for {_symbol}");
                return;
            }

            string channel = json["channel"]?.ToString();
            if (channel == "push.ticker")
            {
                var data = json["data"];
                string symbol = json["symbol"]?.ToString();

                if (symbol == _symbol && data != null)
                {
                    decimal price = data["lastPrice"]?.Value<decimal>() ?? 0;

                    if (price > 0)
                    {
                        _lastPrice = price;
                        Console.WriteLine($"[MEXC] {_symbol} price update: {price}");
                        _onDataReceived?.Invoke(_symbol, price, _lastFunding, _lastNextTime);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEXC] Error processing message for {_symbol}: {ex.Message}");
            Console.WriteLine($"[MEXC] Message was: {message}");
        }
    }

    public async Task<(bool success, decimal price, decimal fundingRate, DateTime nextFundingTime)> GetFundingAndPriceAsync()
    {
        try
        {
            string tickerUrl = $"https://contract.mexc.com/api/v1/contract/ticker?symbol={_symbol}";

            var tickerResponse = await _httpClient.GetStringAsync(tickerUrl);
            Console.WriteLine($"[MEXC] Ticker API response: {tickerResponse}");

            var tickerJson = JObject.Parse(tickerResponse);

            if (tickerJson["success"]?.Value<bool>() != true)
            {
                Console.WriteLine($"[MEXC] API error for {_symbol}, code: {tickerJson["code"]}");
                return (false, 0, 0, DateTime.MinValue);
            }

            var data = tickerJson["data"];
            if (data == null)
            {
                Console.WriteLine($"[MEXC] No data in response for {_symbol}");
                return (false, 0, 0, DateTime.MinValue);
            }

            decimal lastPrice = data["lastPrice"]?.Value<decimal>() ?? 0;
            decimal fundingRate = data["fundingRate"]?.Value<decimal>() ?? 0;

            DateTime nextFundingTime = CalculateNextFundingTime();

            // Store funding data
            _lastFunding = fundingRate;
            _lastNextTime = nextFundingTime;
            if (lastPrice > 0) _lastPrice = lastPrice;

            Console.WriteLine($"[MEXC] {_symbol} - Price: {lastPrice}, Funding: {fundingRate}, Next: {nextFundingTime}");

            _onDataReceived?.Invoke(_symbol, lastPrice, fundingRate, nextFundingTime);

            return (true, lastPrice, fundingRate, nextFundingTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEXC] Error getting data for {_symbol}: {ex.Message}");
            return (false, 0, 0, DateTime.MinValue);
        }
    }

    private DateTime CalculateNextFundingTime()
    {
        var now = DateTime.UtcNow;
        var fundingHours = new[] { 0, 8, 16 };

        foreach (var hour in fundingHours)
        {
            var fundingTime = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Utc);
            if (fundingTime > now)
            {
                return fundingTime.ToLocalTime();
            }
        }

        var nextDay = now.Date.AddDays(1);
        return new DateTime(nextDay.Year, nextDay.Month, nextDay.Day, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();
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
            Console.WriteLine($"[MEXC] Error disconnecting for {_symbol}: {ex.Message}");
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