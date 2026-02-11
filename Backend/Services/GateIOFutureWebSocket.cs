using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace CryptoFutureMonitor.Services;

    public class GateIOFutureWebSocket
{
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly string _symbol;
        private readonly Action<string, decimal, decimal, DateTime> _onDataReceived;
        private static readonly HttpClient _httpClient = new HttpClient();
        private decimal _lastPrice = 0;
        private decimal _lastFunding = 0;
        private DateTime _lastNextTime = DateTime.MinValue;

        public GateIOFutureWebSocket(string symbol, Action<string, decimal, decimal, DateTime> onDataReceived)
        {
            _symbol = symbol;
            _onDataReceived = onDataReceived;
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                // Lấy funding rate và giá ban đầu qua API
                var initialData = await GetFundingAndPriceAsync();
                if (!initialData.success)
                    return false;

                _webSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                // WebSocket URL cho Gate.io futures
                string wsUrl = "wss://fx-ws.gateio.ws/v4/ws/usdt";
                
                Console.WriteLine($"[Gate.io] Connecting to: {wsUrl}");
                
                await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

                Console.WriteLine($"[Gate.io] Connected for {_symbol}");

                // Subscribe to trades channel
                await SubscribeToTrades();

                // Bắt đầu nhận dữ liệu
                _ = Task.Run(() => ReceiveDataAsync());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gate.io] Error connecting for {_symbol}: {ex.Message}");
                return false;
            }
        }

        private async Task SubscribeToTrades()
        {
            try
            {
                // Gate.io subscribe message format
                var subscribeMessage = new
                {
                    time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    channel = "futures.trades",
                    @event = "subscribe",
                    payload = new[] { _symbol }
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
                
                Console.WriteLine($"[Gate.io] Subscribed to trades for {_symbol}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gate.io] Error subscribing: {ex.Message}");
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
                        Console.WriteLine($"[Gate.io] WebSocket closed for {_symbol}");
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gate.io] Error receiving data for {_symbol}: {ex.Message}");
                    break;
                }
            }
        }

        private void ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                
                string channel = json["channel"]?.ToString();
                string eventType = json["event"]?.ToString();

                if (channel == "futures.trades" && eventType == "update")
                {
                    var result = json["result"] as JArray;
                    if (result != null && result.Count > 0)
                    {
                        var trade = result[0];
                        decimal price = decimal.Parse(trade["price"]?.ToString() ?? "0");
                        
                        if (price > 0)
                        {
                            _lastPrice = price;
                            Console.WriteLine($"[Gate.io] {_symbol} price update: {price}");
                            
                            // Invoke callback with latest price and preserved funding data
                            _onDataReceived?.Invoke(_symbol, price, _lastFunding, _lastNextTime);
                        }
                    }
                }
                else if (eventType == "subscribe")
                {
                    Console.WriteLine($"[Gate.io] Subscribe confirmed for {_symbol}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gate.io] Error processing message for {_symbol}: {ex.Message}");
            }
        }

        public async Task<(bool success, decimal price, decimal fundingRate, DateTime nextFundingTime)> GetFundingAndPriceAsync()
        {
            try
            {
                // API để lấy contract info (funding rate)
                string contractUrl = $"https://api.gateio.ws/api/v4/futures/usdt/contracts/{_symbol}";
                
                var contractResponse = await _httpClient.GetStringAsync(contractUrl);
                var contractJson = JObject.Parse(contractResponse);

                decimal fundingRate = decimal.Parse(contractJson["funding_rate"]?.ToString() ?? "0");
                long nextFundingTimeUnix = long.Parse(contractJson["funding_next_apply"]?.ToString() ?? "0");
                DateTime nextFundingTime = DateTimeOffset.FromUnixTimeSeconds(nextFundingTimeUnix).LocalDateTime;

                // API để lấy last price
                string tickerUrl = $"https://api.gateio.ws/api/v4/futures/usdt/tickers?contract={_symbol}";
                var tickerResponse = await _httpClient.GetStringAsync(tickerUrl);
                var tickerArray = JArray.Parse(tickerResponse);
                
                decimal lastPrice = 0;
                if (tickerArray.Count > 0)
                {
                    lastPrice = decimal.Parse(tickerArray[0]["last"]?.ToString() ?? "0");
                }

                // Store funding data to preserve between price updates
                _lastFunding = fundingRate;
                _lastNextTime = nextFundingTime;
                if (lastPrice > 0) _lastPrice = lastPrice;

                Console.WriteLine($"[Gate.io] {_symbol} - Price: {lastPrice}, Funding: {fundingRate}, Next: {nextFundingTime}");

                _onDataReceived?.Invoke(_symbol, lastPrice, fundingRate, nextFundingTime);

                return (true, lastPrice, fundingRate, nextFundingTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gate.io] Error getting data for {_symbol}: {ex.Message}");
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
                Console.WriteLine($"[Gate.io] Error disconnecting for {_symbol}: {ex.Message}");
            }
        }

        public async Task StartFundingRateUpdateAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, _cancellationTokenSource.Token); // 30 seconds
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