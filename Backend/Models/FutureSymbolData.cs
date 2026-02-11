namespace CryptoFutureMonitor.Models;

public class FutureSymbolData
{
    public string Symbol { get; set; } = string.Empty;
    public decimal MarkPrice { get; set; }
    public decimal PriceChange24h { get; set; }
    public decimal FundingRate { get; set; }
    public DateTime NextFundingTime { get; set; }
    public DateTime LastUpdate { get; set; }

    public FutureSymbolData()
    {
        LastUpdate = DateTime.Now;
        NextFundingTime = DateTime.MinValue;
    }

    public FutureSymbolData(string symbol) : this()
    {
        Symbol = symbol;
    }
}
