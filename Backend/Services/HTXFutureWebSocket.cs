using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;

namespace CryptoFutureMonitor.Services;

public class HTXFutureWebSocket
{
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly string _symbol;
    private readonly Action<string, decimal, decimal, DateTime> _onDataReceived;
    private static readonly HttpClient _httpClient = new HttpClient();
    private decimal _lastPrice = 0;
    private decimal _lastFunding = 0;
    private DateTime _lastNextTime = DateTime.MinValue;

    public HTXFutureWebSocket(string symbol, Action<string, decimal, decimal, DateTime> onDataReceived)
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

            string wsUrl = "wss://api.hbdm.com/linear-swap-ws";

            Console.WriteLine($"[HTX] Connecting to: {wsUrl}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

            Console.WriteLine($"[HTX] Connected for {_symbol}");

            await SubscribeToTrades();
            _ = Task.Run(() => ReceiveDataAsync());

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTX] Error connecting for {_symbol}: {ex.Message}");
            return false;
        }
    }

    private async Task SubscribeToTrades()
    {
        try
        {
            var subscribeMessage = new
            {
                sub = $"market.{_symbol}.trade.detail",
                id = "trade_" + DateTime.Now.Ticks
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

            Console.WriteLine($"[HTX] Subscribed to trades for {_symbol}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTX] Error subscribing: {ex.Message}");
        }
    }

    private async Task ReceiveDataAsync()
    {
        var buffer = new byte[8192];

        while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[HTX] WebSocket closed for {_symbol}");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                string message = DecompressMessage(buffer, result.Count);
                ProcessMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTX] Error receiving data for {_symbol}: {ex.Message}");
                break;
            }
        }
    }

    private string DecompressMessage(byte[] data, int count)
    {
        try
        {
            using (var compressedStream = new MemoryStream(data, 0, count))
            using (var decompressor = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                decompressor.CopyTo(resultStream);
                return Encoding.UTF8.GetString(resultStream.ToArray());
            }
        }
        catch
        {
            return Encoding.UTF8.GetString(data, 0, count);
        }
    }

    private async void ProcessMessage(string message)
    {
        try
        {
            var json = JObject.Parse(message);

            if (json["ping"] != null)
            {
                long ts = json["ping"].Value<long>();
                var pongMessage = new { pong = ts };
                string pongJson = Newtonsoft.Json.JsonConvert.SerializeObject(pongMessage);
                byte[] pongBytes = Encoding.UTF8.GetBytes(pongJson);
                await _webSocket.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                return;
            }

            if (json["subbed"] != null)
            {
                Console.WriteLine($"[HTX] Subscribe confirmed for {_symbol}");
                return;
            }

            string ch = json["ch"]?.ToString();
            if (ch != null && ch.Contains("trade.detail"))
            {
                var tick = json["tick"];
                var data = tick?["data"] as JArray;

                if (data != null && data.Count > 0)
                {
                    var trade = data[0];
                    decimal price = trade["price"]?.Value<decimal>() ?? 0;

                    if (price > 0)
                    {
                        _lastPrice = price;
                        Console.WriteLine($"[HTX] {_symbol} price update: {price}");
                        _onDataReceived?.Invoke(_symbol, price, _lastFunding, _lastNextTime);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTX] Error processing message for {_symbol}: {ex.Message}");
        }
    }

    public async Task<(bool success, decimal price, decimal fundingRate, DateTime nextFundingTime)> GetFundingAndPriceAsync()
    {
        try
        {
            string fundingUrl = $"https://api.hbdm.com/linear-swap-api/v1/swap_funding_rate?contract_code={_symbol}";

            var fundingResponse = await _httpClient.GetStringAsync(fundingUrl);
            Console.WriteLine($"[HTX] Funding API response: {fundingResponse}");

            var fundingJson = JObject.Parse(fundingResponse);

            if (fundingJson["status"]?.ToString() != "ok")
            {
                Console.WriteLine($"[HTX] API error for {_symbol}, status: {fundingJson["status"]}");
                return (false, 0, 0, DateTime.MinValue);
            }

            var data = fundingJson["data"];

            string fundingRateStr = data["funding_rate"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(fundingRateStr))
            {
                Console.WriteLine($"[HTX] Empty funding_rate for {_symbol}, response: {fundingResponse}");
                return (false, 0, 0, DateTime.MinValue);
            }

            if (!decimal.TryParse(fundingRateStr, out decimal fundingRate))
            {
                Console.WriteLine($"[HTX] Cannot parse funding_rate: '{fundingRateStr}'");
                return (false, 0, 0, DateTime.MinValue);
            }

            string nextTimeStr = data["next_funding_time"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(nextTimeStr))
            {
                nextTimeStr = data["funding_time"]?.ToString() ?? "0";
                Console.WriteLine($"[HTX] next_funding_time is null, using funding_time: {nextTimeStr}");
            }

            if (!long.TryParse(nextTimeStr, out long nextFundingTimeMs))
            {
                Console.WriteLine($"[HTX] Cannot parse next_funding_time: '{nextTimeStr}'");
                nextFundingTimeMs = 0;
            }

            DateTime nextFundingTime = DateTimeOffset.FromUnixTimeMilliseconds(nextFundingTimeMs).LocalDateTime;

            string tickerUrl = $"https://api.hbdm.com/linear-swap-ex/market/detail/merged?contract_code={_symbol}";
            var tickerResponse = await _httpClient.GetStringAsync(tickerUrl);
            var tickerJson = JObject.Parse(tickerResponse);

            decimal lastPrice = 0;
            if (tickerJson["status"]?.ToString() == "ok")
            {
                var tick = tickerJson["tick"];
                lastPrice = tick?["close"]?.Value<decimal>() ?? 0;
            }

            // Store funding data
            _lastFunding = fundingRate;
            _lastNextTime = nextFundingTime;
            if (lastPrice > 0) _lastPrice = lastPrice;

            Console.WriteLine($"[HTX] {_symbol} - Price: {lastPrice}, Funding: {fundingRate}, Next: {nextFundingTime}");

            _onDataReceived?.Invoke(_symbol, lastPrice, fundingRate, nextFundingTime);

            return (true, lastPrice, fundingRate, nextFundingTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTX] Error getting data for {_symbol}: {ex.Message}");
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
            Console.WriteLine($"[HTX] Error disconnecting for {_symbol}: {ex.Message}");
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