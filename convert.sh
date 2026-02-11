#!/bin/bash
# Convert all C# WebSocket files from uploaded to new project

cd /home/claude/CryptoFutureMonitor/Backend/Services

# Gate.io
sed 's/namespace BinanceFutureTracker/namespace CryptoFutureMonitor.Services;/g' /mnt/user-data/uploads/GateIOFutureWebSocket.cs | \
sed 's/System.Diagnostics.Debug.WriteLine/Console.WriteLine/g' > GateIOFutureWebSocket.cs

# OKX  
sed 's/namespace BinanceFutureTracker/namespace CryptoFutureMonitor.Services;/g' /mnt/user-data/uploads/OKXFutureWebSocket.cs | \
sed 's/System.Diagnostics.Debug.WriteLine/Console.WriteLine/g' > OKXFutureWebSocket.cs

# Bybit
sed 's/namespace BinanceFutureTracker/namespace CryptoFutureMonitor.Services;/g' /mnt/user-data/uploads/BybitFutureWebSocket.cs | \
sed 's/System.Diagnostics.Debug.WriteLine/Console.WriteLine/g' > BybitFutureWebSocket.cs

# HTX
sed 's/namespace BinanceFutureTracker/namespace CryptoFutureMonitor.Services;/g' /mnt/user-data/uploads/HTXFutureWebSocket.cs | \
sed 's/System.Diagnostics.Debug.WriteLine/Console.WriteLine/g' > HTXFutureWebSocket.cs

# MEXC
sed 's/namespace BinanceFutureTracker/namespace CryptoFutureMonitor.Services;/g' /mnt/user-data/uploads/MEXCFutureWebSocket.cs | \
sed 's/System.Diagnostics.Debug.WriteLine/Console.WriteLine/g' > MEXCFutureWebSocket.cs

echo "All WebSocket services converted!"
