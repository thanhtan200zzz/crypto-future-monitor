namespace CryptoFutureMonitor.Models;

public class CombinedExchangeData
{
    public string Symbol { get; set; } = string.Empty;

    // Binance
    public decimal BinancePrice { get; set; }
    public decimal BinanceFundingRate { get; set; }
    public DateTime BinanceNextFunding { get; set; }
    public bool HasBinance { get; set; }

    // Gate.io
    public decimal GatePrice { get; set; }
    public decimal GateFundingRate { get; set; }
    public DateTime GateNextFunding { get; set; }
    public bool HasGate { get; set; }

    // OKX
    public decimal OKXPrice { get; set; }
    public decimal OKXFundingRate { get; set; }
    public DateTime OKXNextFunding { get; set; }
    public bool HasOKX { get; set; }

    // Bybit
    public decimal BybitPrice { get; set; }
    public decimal BybitFundingRate { get; set; }
    public DateTime BybitNextFunding { get; set; }
    public bool HasBybit { get; set; }

    // HTX
    public decimal HTXPrice { get; set; }
    public decimal HTXFundingRate { get; set; }
    public DateTime HTXNextFunding { get; set; }
    public bool HasHTX { get; set; }

    // MEXC
    public decimal MEXCPrice { get; set; }
    public decimal MEXCFundingRate { get; set; }
    public DateTime MEXCNextFunding { get; set; }
    public bool HasMEXC { get; set; }

    public DateTime LastUpdate { get; set; }

    public CombinedExchangeData(string symbol)
    {
        Symbol = symbol;
        LastUpdate = DateTime.Now;
        
        BinanceNextFunding = DateTime.MinValue;
        GateNextFunding = DateTime.MinValue;
        OKXNextFunding = DateTime.MinValue;
        BybitNextFunding = DateTime.MinValue;
        HTXNextFunding = DateTime.MinValue;
        MEXCNextFunding = DateTime.MinValue;
    }
}
