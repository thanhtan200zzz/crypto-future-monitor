using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace CryptoFutureMonitor.Services;

public class BybitFutureWebSocket
{
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly string _symbol;
    private readonly Action<string, decimal, decimal, DateTime> _onDataReceived;
    private static readonly HttpClient _httpClient = new HttpClient();
    private decimal _lastPrice = 0;
    private decimal _lastFunding = 0;
    private DateTime _lastNextTime = DateTime.MinValue;

    public BybitFutureWebSocket(string symbol, Action<string, decimal, decimal, DateTime> onDataReceived)
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

            string wsUrl = "wss://stream.bybit.com/v5/public/linear";

            Console.WriteLine($"[Bybit] Connecting to: {wsUrl}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

            Console.WriteLine($"[Bybit] Connected for {_symbol}");

            await SubscribeToTrades();
            _ = Task.Run(() => ReceiveDataAsync());

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bybit] Error connecting for {_symbol}: {ex.Message}");
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
                args = new[] { $"publicTrade.{_symbol}" }
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

            Console.WriteLine($"[Bybit] Subscribed to publicTrade for {_symbol}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bybit] Error subscribing: {ex.Message}");
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
                    Console.WriteLine($"[Bybit] WebSocket closed for {_symbol}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bybit] Error receiving data for {_symbol}: {ex.Message}");
                break;
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var json = JObject.Parse(message);

            if (json["op"] != null && json["op"].ToString() == "ping")
            {
                _ = Task.Run(async () =>
                {
                    var pongMessage = new { op = "pong" };
                    string pongJson = Newtonsoft.Json.JsonConvert.SerializeObject(pongMessage);
                    byte[] pongBytes = Encoding.UTF8.GetBytes(pongJson);
                    await _webSocket.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                });
                return;
            }

            if (json["success"] != null && json["success"].ToString() == "True")
            {
                Console.WriteLine($"[Bybit] Subscribe confirmed for {_symbol}");
                return;
            }

            string topic = json["topic"]?.ToString();
            if (topic != null && topic.StartsWith("publicTrade."))
            {
                var data = json["data"] as JArray;
                if (data != null && data.Count > 0)
                {
                    var trade = data[0];
                    string priceStr = trade["p"]?.ToString();

                    if (!string.IsNullOrEmpty(priceStr))
                    {
                        decimal price = decimal.Parse(priceStr);
                        if (price > 0)
                        {
                            _lastPrice = price;
                            Console.WriteLine($"[Bybit] {_symbol} price update: {price}");
                            
                            // Invoke callback with latest price and preserved funding data
                            _onDataReceived?.Invoke(_symbol, price, _lastFunding, _lastNextTime);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bybit] Error processing message for {_symbol}: {ex.Message}");
        }
    }

    public async Task<(bool success, decimal price, decimal fundingRate, DateTime nextFundingTime)> GetFundingAndPriceAsync()
    {
        try
        {
            string tickerUrl = $"https://api.bybit.com/v5/market/tickers?category=linear&symbol={_symbol}";

            var response = await _httpClient.GetStringAsync(tickerUrl);
            var json = JObject.Parse(response);

            var list = json["result"]?["list"] as JArray;
            if (list == null || list.Count == 0)
            {
                Console.WriteLine($"[Bybit] No data for {_symbol}");
                return (false, 0, 0, DateTime.MinValue);
            }

            var data = list[0];
            decimal lastPrice = decimal.Parse(data["lastPrice"]?.ToString() ?? "0");
            decimal fundingRate = decimal.Parse(data["fundingRate"]?.ToString() ?? "0");
            long nextFundingTimeMs = long.Parse(data["nextFundingTime"]?.ToString() ?? "0");
            DateTime nextFundingTime = DateTimeOffset.FromUnixTimeMilliseconds(nextFundingTimeMs).LocalDateTime;

            // Store funding data to preserve between price updates
            _lastFunding = fundingRate;
            _lastNextTime = nextFundingTime;
            if (lastPrice > 0) _lastPrice = lastPrice;

            Console.WriteLine($"[Bybit] {_symbol} - Price: {lastPrice}, Funding: {fundingRate}, Next: {nextFundingTime}");

            _onDataReceived?.Invoke(_symbol, lastPrice, fundingRate, nextFundingTime);

            return (true, lastPrice, fundingRate, nextFundingTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bybit] Error getting data for {_symbol}: {ex.Message}");
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
            Console.WriteLine($"[Bybit] Error disconnecting for {_symbol}: {ex.Message}");
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