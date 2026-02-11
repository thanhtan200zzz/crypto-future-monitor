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
