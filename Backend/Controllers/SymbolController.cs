using Microsoft.AspNetCore.Mvc;
using CryptoFutureMonitor.Services;
using CryptoFutureMonitor.Models;

namespace CryptoFutureMonitor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SymbolController : ControllerBase
{
    private readonly ExchangeMonitorService _monitorService;

    public SymbolController(ExchangeMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    [HttpGet]
    public ActionResult<List<string>> GetSymbols()
    {
        return Ok(_monitorService.GetActiveSymbols());
    }

    [HttpGet("{symbol}")]
    public ActionResult<CombinedExchangeData> GetSymbol(string symbol)
    {
        var data = _monitorService.GetSymbolData(symbol);
        if (data == null)
            return NotFound();
        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult> AddSymbol([FromBody] AddSymbolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest("Symbol is required");

        var success = await _monitorService.AddSymbol(request.Symbol);
        if (!success)
            return Conflict("Symbol already exists or failed to add");

        return Ok(new { message = $"Symbol {request.Symbol} added successfully" });
    }

    [HttpDelete("{symbol}")]
    public async Task<ActionResult> RemoveSymbol(string symbol)
    {
        await _monitorService.RemoveSymbol(symbol);
        return Ok(new { message = $"Symbol {symbol} removed" });
    }

    [HttpDelete]
    public async Task<ActionResult> RemoveAllSymbols()
    {
        await _monitorService.RemoveAllSymbols();
        return Ok(new { message = "All symbols removed" });
    }
}

public class AddSymbolRequest
{
    public string Symbol { get; set; } = string.Empty;
}
