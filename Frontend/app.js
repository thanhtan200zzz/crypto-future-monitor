// Configuration - auto-detect if running locally or on Railway
const API_BASE = window.location.hostname === 'localhost' ? 'http://localhost:5000' : window.location.origin;
const HUB_URL = `${API_BASE}/priceHub`;

// State
let connection = null;
let symbols = new Map();
let uiUpdateTimer = null;

// DOM Elements
const symbolInput = document.getElementById('symbolInput');
const btnAdd = document.getElementById('btnAdd');
const btnRemoveAll = document.getElementById('btnRemoveAll');
const symbolsContainer = document.getElementById('symbolsContainer');

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    initSignalR();
    loadActiveSymbols();
    startUIUpdateTimer();
    
    btnAdd.addEventListener('click', addSymbol);
    btnRemoveAll.addEventListener('click', removeAllSymbols);
    symbolInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') addSymbol();
    });
});

// SignalR Connection
async function initSignalR() {
    console.log('🔌 Initializing SignalR...');
    
    connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL)
        .withAutomaticReconnect()
        .build();

    connection.on('ReceivePriceUpdate', (data) => {
        updateSymbolData(data);
    });

    connection.onreconnected(() => {
        resubscribeSymbols();
    });

    try {
        await connection.start();
        console.log('✅ SignalR Connected!');
    } catch (err) {
        console.error('❌ SignalR Error:', err);
        setTimeout(initSignalR, 5000);
    }
}

async function resubscribeSymbols() {
    for (const symbol of symbols.keys()) {
        await connection.invoke('SubscribeToSymbol', symbol);
    }
}

// Load active symbols
async function loadActiveSymbols() {
    try {
        const response = await fetch(`${API_BASE}/api/symbol`);
        const activeSymbols = await response.json();
        for (const symbol of activeSymbols) {
            await subscribeToSymbol(symbol);
        }
    } catch (err) {
        console.error('Error loading symbols:', err);
    }
}

// Add symbol
async function addSymbol() {
    let symbol = symbolInput.value.trim().toUpperCase();
    if (!symbol) return alert('Vui lòng nhập symbol');
    if (!symbol.includes('USDT')) symbol += 'USDT';
    if (symbols.has(symbol)) return alert('Symbol đã tồn tại');
    
    try {
        const response = await fetch(`${API_BASE}/api/symbol`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ symbol })
        });
        if (response.ok) {
            await subscribeToSymbol(symbol);
            symbolInput.value = '';
        }
    } catch (err) {
        console.error('Error:', err);
    }
}

// Subscribe to symbol
async function subscribeToSymbol(symbol) {
    if (symbols.has(symbol)) return;
    
    symbols.set(symbol, {
        binance: { price: 0, funding: 0, nextTime: null },
        gate: { price: 0, funding: 0, nextTime: null },
        okx: { price: 0, funding: 0, nextTime: null },
        bybit: { price: 0, funding: 0, nextTime: null },
        htx: { price: 0, funding: 0, nextTime: null },
        mexc: { price: 0, funding: 0, nextTime: null }
    });
    
    createSymbolCard(symbol);
    await connection.invoke('SubscribeToSymbol', symbol);
}

// Create symbol card
function createSymbolCard(symbol) {
    const card = document.createElement('div');
    card.className = 'symbol-card';
    card.id = `card-${symbol}`;
    card.innerHTML = `
        <div class="card-header">
            <div class="symbol-name">${symbol}</div>
            <button class="btn-delete" onclick="removeSymbol('${symbol}')">✖</button>
        </div>
        <div class="card-content">
            <div class="exchange-row"><span class="exchange-label binance">Binance:</span><span class="exchange-data" id="binance-${symbol}">Loading...</span></div>
            <div class="exchange-row"><span class="exchange-label gate">Gate.io:</span><span class="exchange-data" id="gate-${symbol}">Loading...</span></div>
            <div class="exchange-row"><span class="exchange-label okx">OKX:</span><span class="exchange-data" id="okx-${symbol}">Loading...</span></div>
            <div class="exchange-row"><span class="exchange-label bybit">Bybit:</span><span class="exchange-data" id="bybit-${symbol}">Loading...</span></div>
            <div class="exchange-row"><span class="exchange-label htx">HTX:</span><span class="exchange-data" id="htx-${symbol}">Loading...</span></div>
            <div class="exchange-row"><span class="exchange-label mexc">MEXC:</span><span class="exchange-data" id="mexc-${symbol}">Loading...</span></div>
            <div class="best-section"><div class="best-header">Best:</div><div id="best-${symbol}"></div></div>
        </div>
    `;
    symbolsContainer.appendChild(card);
}

// Update symbol data from SignalR
function updateSymbolData(data) {
    const symbol = data.symbol;
    if (!symbols.has(symbol)) return;
    
    const s = symbols.get(symbol);
    if (data.hasBinance) { s.binance.price = data.binancePrice; s.binance.funding = data.binanceFundingRate; s.binance.nextTime = data.binanceNextFunding ? new Date(data.binanceNextFunding) : null; }
    if (data.hasGate) { s.gate.price = data.gatePrice; s.gate.funding = data.gateFundingRate; s.gate.nextTime = data.gateNextFunding ? new Date(data.gateNextFunding) : null; }
    if (data.hasOKX) { s.okx.price = data.okxPrice; s.okx.funding = data.okxFundingRate; s.okx.nextTime = data.okxNextFunding ? new Date(data.okxNextFunding) : null; }
    if (data.hasBybit) { s.bybit.price = data.bybitPrice; s.bybit.funding = data.bybitFundingRate; s.bybit.nextTime = data.bybitNextFunding ? new Date(data.bybitNextFunding) : null; }
    if (data.hasHTX) { s.htx.price = data.htxPrice; s.htx.funding = data.htxFundingRate; s.htx.nextTime = data.htxNextFunding ? new Date(data.htxNextFunding) : null; }
    if (data.hasMEXC) { s.mexc.price = data.mexcPrice; s.mexc.funding = data.mexcFundingRate; s.mexc.nextTime = data.mexcNextFunding ? new Date(data.mexcNextFunding) : null; }
    
    updateSymbolUI(symbol);
}

// Update UI
function updateSymbolUI(symbol) {
    const s = symbols.get(symbol);
    if (!s) return;
    
    ['binance', 'gate', 'okx', 'bybit', 'htx', 'mexc'].forEach(ex => updateExchangeUI(symbol, ex, s[ex]));
    calculateBestPairs(symbol);
}

// Update exchange UI
function updateExchangeUI(symbol, exchange, data) {
    const el = document.getElementById(`${exchange}-${symbol}`);
    if (!el || data.price === 0) { if(el) el.innerHTML = 'N/A'; return; }
    
    const price = formatPrice(data.price);
    const funding = (data.funding * 100).toFixed(4) + '%';
    const fundingClass = data.funding > 0 ? 'price-negative' : data.funding < 0 ? 'price-positive' : '';
    
    let time = 'Funding now';
    if (data.nextTime && data.nextTime - new Date() > 0) {
        const diff = data.nextTime - new Date();
        const h = String(Math.floor(diff / 3600000)).padStart(2, '0');
        const m = String(Math.floor((diff % 3600000) / 60000)).padStart(2, '0');
        const sec = String(Math.floor((diff % 60000) / 1000)).padStart(2, '0');
        time = `${h}:${m}:${sec}`;
    }
    
    el.innerHTML = `${price} | <span class="${fundingClass}">${funding}</span> | ${time}`;
}

// Format price
function formatPrice(p) {
    if (p === 0) return '0.00';
    if (p >= 1000) return p.toFixed(2);
    if (p >= 10) return p.toFixed(3);
    if (p >= 1) return p.toFixed(4);
    if (p >= 0.01) return p.toFixed(4);
    if (p >= 0.0001) return p.toFixed(6);
    return p.toFixed(8);
}

// Calculate Best Pairs - EXACT C# LOGIC
function calculateBestPairs(symbol) {
    const s = symbols.get(symbol);
    const ex = [];
    if (s.binance.price > 0) ex.push({ n: 'BIN', p: s.binance.price, f: s.binance.funding });
    if (s.gate.price > 0) ex.push({ n: 'GATE', p: s.gate.price, f: s.gate.funding });
    if (s.okx.price > 0) ex.push({ n: 'OKX', p: s.okx.price, f: s.okx.funding });
    if (s.bybit.price > 0) ex.push({ n: 'BYB', p: s.bybit.price, f: s.bybit.funding });
    if (s.htx.price > 0) ex.push({ n: 'HTX', p: s.htx.price, f: s.htx.funding });
    if (s.mexc.price > 0) ex.push({ n: 'MEXC', p: s.mexc.price, f: s.mexc.funding });
    
    const bestEl = document.getElementById(`best-${symbol}`);
    if (!bestEl || ex.length < 2) { if(bestEl) bestEl.innerHTML = 'N/A'; return; }
    
    const pairs = [];
    for (let i = 0; i < ex.length; i++) {
        for (let j = i + 1; j < ex.length; j++) {
            let ps = Math.abs((ex[j].p - ex[i].p) / ex[i].p) * 100;
            if (ps === 0) ps = 0.0001;
            const fs = Math.abs(ex[j].f - ex[i].f) * 100;
            const score = fs / ps;
            pairs.push({ e1: ex[i].n, e2: ex[j].n, f1: ex[i].f, f2: ex[j].f, ps, fs, score });
        }
    }
    
    pairs.sort((a, b) => b.score - a.score);
    const top3 = pairs.slice(0, 3);
    
    let html = '';
    top3.forEach((p, i) => {
        const color = p.score >= 10 ? '#16A085' : p.score >= 5 ? '#E67E22' : '#C0392B';
        html += `<div class="best-pair" style="color:${color}">`;
        html += `#${i+1}: ${p.e1}(${(p.f1*100).toFixed(2)}%) ${p.e2}(${(p.f2*100).toFixed(2)}%) | P:${p.ps.toFixed(3)}% F:${p.fs.toFixed(2)}% ★${p.score.toFixed(1)}`;
        html += '</div>';
    });
    bestEl.innerHTML = html;
}

// Timer - update UI every second
function startUIUpdateTimer() {
    uiUpdateTimer = setInterval(() => {
        symbols.forEach((_, symbol) => updateSymbolUI(symbol));
    }, 1000);
}

// Remove symbol
async function removeSymbol(symbol) {
    if (!confirm(`Xóa ${symbol}?`)) return;
    try {
        const res = await fetch(`${API_BASE}/api/symbol/${symbol}`, { method: 'DELETE' });
        if (res.ok) {
            await connection.invoke('UnsubscribeFromSymbol', symbol);
            symbols.delete(symbol);
            document.getElementById(`card-${symbol}`)?.remove();
        }
    } catch (err) {
        console.error(err);
    }
}

// Remove all
async function removeAllSymbols() {
    if (!confirm('Xóa tất cả?')) return;
    try {
        const res = await fetch(`${API_BASE}/api/symbol/all`, { method: 'DELETE' });
        if (res.ok) {
            for (const symbol of symbols.keys()) await connection.invoke('UnsubscribeFromSymbol', symbol);
            symbols.clear();
            symbolsContainer.innerHTML = '';
        }
    } catch (err) {
        console.error(err);
    }
}

window.addEventListener('beforeunload', () => {
    if (uiUpdateTimer) clearInterval(uiUpdateTimer);
});
