//+------------------------------------------------------------------+
//|  ForexAI_Bridge.mq5                                              |
//|  Menghubungkan MT5 (MIFX) dengan ForexAI backend                 |
//|                                                                  |
//|  SETUP:                                                          |
//|  1. Buka MetaEditor → File → Open → pilih file ini              |
//|  2. Compile (F7)                                                 |
//|  3. MT5 → Tools → Options → Expert Advisors →                   |
//|     centang "Allow WebRequest for listed URL" →                  |
//|     tambah: http://localhost:8080                                |
//|  4. Drag EA ke chart EURUSD.m,M15                               |
//+------------------------------------------------------------------+
#property copyright "ForexAI"
#property version   "1.19"
#property description "ForexAI price bridge + indicators + order executor + close-order sync"

//--- Input parameters
input string   BackendUrl  = "http://127.0.0.1:5033";  // URL backend ForexAI
input string   TradePair   = "EURUSD.m";               // Pair yang di-track (Monex pakai .m suffix)
input int      TimerMs     = 1000;                     // Interval kirim tick (ms)
input int      MagicNumber = 20260101;                 // Magic number untuk order EA ini
input int      SRLookback  = 20;                       // Lookback bars untuk Support/Resistance

//--- Indicator handles (dibuat sekali di OnInit)
int g_ma20m15 = INVALID_HANDLE;
int g_ma50m15 = INVALID_HANDLE;
int g_ma20h1  = INVALID_HANDLE;
int g_ma50h1  = INVALID_HANDLE;
int g_rsi14   = INVALID_HANDLE;
int g_atr14   = INVALID_HANDLE;
int g_adx14   = INVALID_HANDLE;

//--- State
string   g_pendingCommandId = "";

//+------------------------------------------------------------------+
int OnInit()
{
   // Pastikan simbol ada di Market Watch agar tick mengalir dari broker
   if(!SymbolSelect(TradePair, true))
      Print("[ForexAI] Peringatan: gagal subscribe simbol ", TradePair, " ke Market Watch");
   else
      Print("[ForexAI] Simbol ", TradePair, " aktif di Market Watch");

   // Log status akun
   long acctLogin = AccountInfoInteger(ACCOUNT_LOGIN);
   Print("[ForexAI] Account login: ", acctLogin,
         " | server: ", AccountInfoString(ACCOUNT_SERVER),
         " | balance: ", AccountInfoDouble(ACCOUNT_BALANCE));

   // Buat indicator handles
   g_ma20m15 = iMA(TradePair, PERIOD_M15, 20, 0, MODE_SMA, PRICE_CLOSE);
   g_ma50m15 = iMA(TradePair, PERIOD_M15, 50, 0, MODE_SMA, PRICE_CLOSE);
   g_ma20h1  = iMA(TradePair, PERIOD_H1,  20, 0, MODE_SMA, PRICE_CLOSE);
   g_ma50h1  = iMA(TradePair, PERIOD_H1,  50, 0, MODE_SMA, PRICE_CLOSE);
   g_rsi14   = iRSI(TradePair, PERIOD_M15, 14, PRICE_CLOSE);
   g_atr14   = iATR(TradePair, PERIOD_M15, 14);
   g_adx14   = iADX(TradePair, PERIOD_M15, 14);

   if(g_ma20m15 == INVALID_HANDLE || g_ma50m15 == INVALID_HANDLE ||
      g_ma20h1  == INVALID_HANDLE || g_ma50h1  == INVALID_HANDLE ||
      g_rsi14   == INVALID_HANDLE || g_atr14   == INVALID_HANDLE ||
      g_adx14   == INVALID_HANDLE)
   {
      Print("[ForexAI] ERROR: Gagal membuat indicator handles — pastikan simbol tersedia");
      return INIT_FAILED;
   }

   Print("[ForexAI] Indicator handles berhasil dibuat (MA20/MA50 M15+H1, RSI14, ATR14, ADX14)");

   if(!EventSetMillisecondTimer(TimerMs))
   {
      Print("[ForexAI] Gagal set timer");
      return INIT_FAILED;
   }

   Print("[ForexAI] Bridge aktif v1.19 — pair: ", TradePair, " | backend: ", BackendUrl,
         " | balance: ", AccountInfoDouble(ACCOUNT_BALANCE),
         " | equity: ", AccountInfoDouble(ACCOUNT_EQUITY));

   SendStatus("CONNECTED");
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();

   // Lepas indicator handles
   if(g_ma20m15 != INVALID_HANDLE) IndicatorRelease(g_ma20m15);
   if(g_ma50m15 != INVALID_HANDLE) IndicatorRelease(g_ma50m15);
   if(g_ma20h1  != INVALID_HANDLE) IndicatorRelease(g_ma20h1);
   if(g_ma50h1  != INVALID_HANDLE) IndicatorRelease(g_ma50h1);
   if(g_rsi14   != INVALID_HANDLE) IndicatorRelease(g_rsi14);
   if(g_atr14   != INVALID_HANDLE) IndicatorRelease(g_atr14);
   if(g_adx14   != INVALID_HANDLE) IndicatorRelease(g_adx14);

   SendStatus("DISCONNECTED");
   Print("[ForexAI] Bridge berhenti.");
}

//+------------------------------------------------------------------+
//| Timer: kirim tick + indikator, cek perintah order setiap TimerMs |
//+------------------------------------------------------------------+
void OnTimer()
{
   CheckForCommand();
   SendLatestTick();
}

//+------------------------------------------------------------------+
//| Kirim harga + indikator lengkap ke backend                       |
//+------------------------------------------------------------------+
void SendLatestTick()
{
   double bid = SymbolInfoDouble(TradePair, SYMBOL_BID);
   double ask = SymbolInfoDouble(TradePair, SYMBOL_ASK);

   // Skip kirim jika harga belum tersedia dari broker
   if(bid <= 0.0 || ask <= 0.0) return;

   // Gunakan waktu lokal sistem
   long   time    = (long)TimeLocal();
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);

   // ── Ambil nilai indikator dari handles ──────────────────────────
   double ma20m15_buf[1], ma50m15_buf[1];
   double ma20h1_buf[1],  ma50h1_buf[1];
   double rsi_buf[2];
   double atr_buf[1];
   double adx_buf[1];

   // CopyBuffer: index 0 = bar terbaru, index 1 = bar sebelumnya
   if(CopyBuffer(g_ma20m15, 0, 0, 1, ma20m15_buf) <= 0) return;
   if(CopyBuffer(g_ma50m15, 0, 0, 1, ma50m15_buf) <= 0) return;
   if(CopyBuffer(g_ma20h1,  0, 0, 1, ma20h1_buf)  <= 0) return;
   if(CopyBuffer(g_ma50h1,  0, 0, 1, ma50h1_buf)  <= 0) return;
   if(CopyBuffer(g_rsi14,   0, 0, 2, rsi_buf)     < 2)  return;
   if(CopyBuffer(g_atr14,   0, 0, 1, atr_buf)     <= 0) return;
   if(CopyBuffer(g_adx14,   0, 0, 1, adx_buf)     <= 0) return;  // buffer 0 = ADX main line

   double ma20m15 = ma20m15_buf[0];
   double ma50m15 = ma50m15_buf[0];
   double ma20h1  = ma20h1_buf[0];
   double ma50h1  = ma50h1_buf[0];
   double rsi14   = rsi_buf[0];
   int    rsiDir  = (rsi_buf[0] > rsi_buf[1]) ? 1 : 0;  // 1=rising, 0=falling
   double atr14   = atr_buf[0];  // ATR(14) M15 dalam satuan harga (misal 0.00120 = 12 pip)
   double adx14   = adx_buf[0];  // ADX(14) M15 trend strength (0-100; <20=ranging, 25+=trending)

   // ── Support & Resistance: high/low tertinggi/terendah N bar M15 ─
   double highs[], lows[];
   double support = 0.0, resistance = 0.0;

   // CopyHigh/CopyLow start_pos=1 agar skip bar saat ini yang masih terbentuk
   if(CopyHigh(TradePair, PERIOD_M15, 1, SRLookback, highs) == SRLookback &&
      CopyLow (TradePair, PERIOD_M15, 1, SRLookback, lows)  == SRLookback)
   {
      support    = lows [ArrayMinimum(lows,  0, WHOLE_ARRAY)];
      resistance = highs[ArrayMaximum(highs, 0, WHOLE_ARRAY)];
   }

   // ── Format JSON dan kirim ke backend ────────────────────────────
   string positions = BuildPositionsJson();
   string json = StringFormat(
      "{\"pair\":\"%s\",\"bid\":%.5f,\"ask\":%.5f,\"time\":%d,"
      "\"balance\":%.2f,\"equity\":%.2f,"
      "\"ma20m15\":%.5f,\"ma50m15\":%.5f,"
      "\"ma20h1\":%.5f,\"ma50h1\":%.5f,"
      "\"rsi14\":%.2f,\"rsiDir\":%d,"
      "\"atr14\":%.5f,\"adx14\":%.2f,"
      "\"support\":%.5f,\"resistance\":%.5f,"
      "\"positions\":%s}",
      TradePair, bid, ask, time, balance, equity,
      ma20m15, ma50m15, ma20h1, ma50h1,
      rsi14, rsiDir, atr14, adx14, support, resistance,
      positions);

   string headers = "Content-Type: application/json\r\n";
   char   body[], resp[];
   string respHeaders;
   StringToCharArray(json, body, 0, StringLen(json));

   int code = WebRequest("POST",
      BackendUrl + "/api/mifx/tick",
      headers, 3000, body, resp, respHeaders);

   if(code < 0)
      Print("[ForexAI] SendTick error: ", GetLastError());
}

//+------------------------------------------------------------------+
//| Polling perintah order dari backend                              |
//+------------------------------------------------------------------+
void CheckForCommand()
{
   string headers = "Content-Type: application/json\r\n";
   char   body[], resp[];
   string respHeaders;

   int code = WebRequest("GET",
      BackendUrl + "/api/mifx/command",
      headers, 3000, body, resp, respHeaders);

   if(code == 200 && ArraySize(resp) > 5)
   {
      string json = CharArrayToString(resp);
      Print("[ForexAI] Perintah diterima: ", json);
      DispatchCommandFromJson(json);
   }
}

//+------------------------------------------------------------------+
//| Route command OPEN/CLOSE dari backend                            |
//+------------------------------------------------------------------+
void DispatchCommandFromJson(string json)
{
   string commandId = JsonGetString(json, "commandId");
   string action    = JsonGetString(json, "action");

   if(action == "" || action == "OPEN")
   {
      ExecuteOrderFromJson(json);
      return;
   }

   if(action == "CLOSE")
   {
      ClosePositionFromJson(json);
      return;
   }

   Print("[ForexAI] Action tidak dikenal: ", action, " | ", json);
   ReportOrderResult(commandId, "FAILED", "", 0.0, -10);
}

//+------------------------------------------------------------------+
//| Parse JSON perintah dan eksekusi order open di MT5               |
//+------------------------------------------------------------------+
void ExecuteOrderFromJson(string json)
{
   string commandId = JsonGetString(json, "commandId");
   string direction = JsonGetString(json, "direction");
   double lots      = JsonGetDouble(json, "lots");
   double sl        = JsonGetDouble(json, "stopLoss");
   double tp        = JsonGetDouble(json, "takeProfit");

   if(commandId == "" || lots <= 0)
   {
      Print("[ForexAI] Perintah tidak valid: ", json);
      return;
   }

   bool isBuy = (direction == "BUY");

   MqlTradeRequest req  = {};
   MqlTradeResult  res  = {};

   // Auto-detect filling mode yang didukung broker untuk simbol ini
   // MIFX (dan banyak broker retail) tidak support IOC — deteksi otomatis mencegah error
   int fillFlags = (int)SymbolInfoInteger(TradePair, SYMBOL_FILLING_MODE);
   ENUM_ORDER_TYPE_FILLING fillMode;
   if     ((fillFlags & 1) != 0) fillMode = ORDER_FILLING_FOK;     // bit 0 = FOK
   else if((fillFlags & 2) != 0) fillMode = ORDER_FILLING_IOC;     // bit 1 = IOC
   else                          fillMode = ORDER_FILLING_RETURN;   // fallback
   Print("[ForexAI] Filling mode: ", EnumToString(fillMode), " (flags=", fillFlags, ")");

   req.action       = TRADE_ACTION_DEAL;
   req.symbol       = TradePair;
   req.volume       = lots;
   req.type         = isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
   req.price        = isBuy
                      ? SymbolInfoDouble(TradePair, SYMBOL_ASK)
                      : SymbolInfoDouble(TradePair, SYMBOL_BID);
   req.sl           = sl;
   req.tp           = tp;
   req.deviation    = 20;
   req.magic        = MagicNumber;
   req.comment      = "ForexAI";
   req.type_filling = fillMode;

   bool ok = OrderSend(req, res);

   string status  = ok ? "FILLED" : "FAILED";
   string orderId = ok ? IntegerToString((long)res.order) : "";

   Print("[ForexAI] Order ", status, " | code=", res.retcode,
         " | orderId=", orderId, " | price=", res.price);

   ReportOrderResult(commandId, status, orderId, res.price, res.retcode);
}

//+------------------------------------------------------------------+
//| Parse JSON perintah dan close posisi by ticket                   |
//+------------------------------------------------------------------+
void ClosePositionFromJson(string json)
{
   string commandId = JsonGetString(json, "commandId");
   long   ticketRaw = JsonGetLong(json, "ticket");

   if(commandId == "" || ticketRaw <= 0)
   {
      Print("[ForexAI] Perintah CLOSE tidak valid: ", json);
      ReportOrderResult(commandId, "FAILED", "", 0.0, -20);
      return;
   }

   ulong ticket = (ulong)ticketRaw;
   if(!PositionSelectByTicket(ticket))
   {
      Print("[ForexAI] Ticket tidak ditemukan untuk CLOSE: ", ticketRaw);
      ReportOrderResult(commandId, "FAILED", IntegerToString(ticketRaw), 0.0, -21);
      return;
   }

   string symbol  = PositionGetString(POSITION_SYMBOL);
   int    posType = (int)PositionGetInteger(POSITION_TYPE);
   double volume  = PositionGetDouble(POSITION_VOLUME);
   bool   wasBuy  = (posType == POSITION_TYPE_BUY);

   int fillFlags = (int)SymbolInfoInteger(symbol, SYMBOL_FILLING_MODE);
   ENUM_ORDER_TYPE_FILLING fillMode;
   if     ((fillFlags & 1) != 0) fillMode = ORDER_FILLING_FOK;
   else if((fillFlags & 2) != 0) fillMode = ORDER_FILLING_IOC;
   else                          fillMode = ORDER_FILLING_RETURN;

   MqlTradeRequest req = {};
   MqlTradeResult  res = {};

   req.action       = TRADE_ACTION_DEAL;
   req.position     = ticket;
   req.symbol       = symbol;
   req.volume       = volume;
   req.type         = wasBuy ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
   req.price        = wasBuy
                      ? SymbolInfoDouble(symbol, SYMBOL_BID)
                      : SymbolInfoDouble(symbol, SYMBOL_ASK);
   req.deviation    = 20;
   req.magic        = MagicNumber;
   req.comment      = "ForexAI close";
   req.type_filling = fillMode;

   bool ok = OrderSend(req, res);
   string status = ok ? "CLOSED" : "FAILED";
   double price  = (res.price > 0.0) ? res.price : req.price;

   Print("[ForexAI] Close ", status, " | ticket=", ticketRaw,
         " | code=", res.retcode, " | price=", price);

   ReportOrderResult(commandId, status, IntegerToString(ticketRaw), price, res.retcode);
}

//+------------------------------------------------------------------+
//| Kirim hasil eksekusi order ke backend                            |
//+------------------------------------------------------------------+
void ReportOrderResult(string commandId, string status,
                       string orderId, double price, int retcode)
{
   string json = StringFormat(
      "{\"commandId\":\"%s\",\"status\":\"%s\",\"orderId\":\"%s\",\"price\":%.5f,\"retcode\":%d}",
      commandId, status, orderId, price, retcode
   );

   string headers = "Content-Type: application/json\r\n";
   char   body[], resp[];
   string respHeaders;
   StringToCharArray(json, body, 0, StringLen(json));

   WebRequest("POST",
      BackendUrl + "/api/mifx/order-result",
      headers, 3000, body, resp, respHeaders);
}

//+------------------------------------------------------------------+
//| Kirim status koneksi EA ke backend                               |
//+------------------------------------------------------------------+
void SendStatus(string status)
{
   string json = StringFormat("{\"status\":\"%s\",\"pair\":\"%s\"}", status, TradePair);
   string headers = "Content-Type: application/json\r\n";
   char   body[], resp[];
   string respHeaders;
   StringToCharArray(json, body, 0, StringLen(json));
   WebRequest("POST", BackendUrl + "/api/mifx/status",
      headers, 3000, body, resp, respHeaders);
}

//+------------------------------------------------------------------+
//| Build JSON array posisi open EA ini (filter by MagicNumber)      |
//+------------------------------------------------------------------+
string BuildPositionsJson()
{
   string result = "[";
   bool   first  = true;
   int    total  = PositionsTotal();

   for(int i = total - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;

      string posSymbol = PositionGetString(POSITION_SYMBOL);
      int    posType   = (int)PositionGetInteger(POSITION_TYPE);  // 0=BUY, 1=SELL
      double posLots   = PositionGetDouble(POSITION_VOLUME);
      double posOpen   = PositionGetDouble(POSITION_PRICE_OPEN);
      double posProfit = PositionGetDouble(POSITION_PROFIT);
      double posCur    = PositionGetDouble(POSITION_PRICE_CURRENT);

      // Pip size standard (0.0001 untuk pasangan non-JPY)
      double pipSize = 0.0001;
      double pipsRaw = (posType == 0) ? (posCur - posOpen) : (posOpen - posCur);
      int    pips    = (int)MathRound(pipsRaw / pipSize);

      if(!first) result += ",";
      first = false;

      result += StringFormat(
         "{\"ticket\":%s,\"type\":\"%s\",\"symbol\":\"%s\",\"lots\":%.2f,"
         "\"openPrice\":%.5f,\"profit\":%.2f,\"pips\":%d}",
         IntegerToString((long)ticket),
         (posType == 0) ? "BUY" : "SELL",
         posSymbol, posLots, posOpen, posProfit, pips);
   }

   result += "]";
   return result;
}

//+------------------------------------------------------------------+
//| Helper: ambil string dari JSON sederhana                         |
//+------------------------------------------------------------------+
string JsonGetString(string json, string key)
{
   string search = "\"" + key + "\":\"";
   int pos = StringFind(json, search);
   if(pos < 0) return "";
   pos += StringLen(search);
   int end = StringFind(json, "\"", pos);
   if(end < 0) return "";
   return StringSubstr(json, pos, end - pos);
}

//+------------------------------------------------------------------+
//| Helper: ambil double dari JSON sederhana                         |
//+------------------------------------------------------------------+
double JsonGetDouble(string json, string key)
{
   string search = "\"" + key + "\":";
   int pos = StringFind(json, search);
   if(pos < 0) return 0.0;
   pos += StringLen(search);
   int end = pos;
   while(end < StringLen(json))
   {
      ushort c = StringGetCharacter(json, end);
      if(c != '.' && c != '-' && !(c >= '0' && c <= '9')) break;
      end++;
   }
   return StringToDouble(StringSubstr(json, pos, end - pos));
}

//+------------------------------------------------------------------+
//| Helper: ambil long dari JSON sederhana                           |
//+------------------------------------------------------------------+
long JsonGetLong(string json, string key)
{
   string search = "\"" + key + "\":";
   int pos = StringFind(json, search);
   if(pos < 0) return 0;
   pos += StringLen(search);
   int end = pos;
   while(end < StringLen(json))
   {
      ushort c = StringGetCharacter(json, end);
      if(!(c >= '0' && c <= '9')) break;
      end++;
   }
   return (long)StringToInteger(StringSubstr(json, pos, end - pos));
}
