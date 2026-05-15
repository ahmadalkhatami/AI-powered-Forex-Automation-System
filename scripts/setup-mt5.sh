#!/usr/bin/env bash
# =============================================================================
# setup-mt5.sh — Setup PENUH otomatis ForexAI Bridge di MT5 (Monex Demo)
#
# Fitur:
#   - Simpan password sekali ke macOS Keychain (aman, tidak plain-text)
#   - Auto-login ke Monex-Demo via AppleScript keystroke
#   - Chart EURUSD.m,M15 otomatis terbuka dengan EA ForexAI_Bridge sudah terpasang
#
# Pertama kali:
#   ./scripts/setup-mt5.sh --save-password
#
# Selanjutnya (cukup):
#   ./scripts/setup-mt5.sh
# =============================================================================

set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'
BOLD='\033[1m'; RESET='\033[0m'
info()    { echo -e "${BLUE}ℹ${RESET}  $*"; }
success() { echo -e "${GREEN}✅${RESET} $*"; }
warn()    { echo -e "${YELLOW}⚠${RESET}  $*"; }
error()   { echo -e "${RED}❌${RESET} $*"; exit 1; }
step()    { echo -e "\n${BOLD}── $* ──${RESET}"; }

# ── Konfigurasi ───────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
MT5_APP="/Applications/MetaTrader 5.app"
MT5_DATA="$HOME/Library/Application Support/net.metaquotes.wine.metatrader5"
MT5_DRIVE="$MT5_DATA/drive_c/Program Files/MetaTrader 5"
EA_DIR="$MT5_DRIVE/MQL5/Experts"
CONFIG_INI="$MT5_DRIVE/config/common.ini"
CHART_FILE="$MT5_DRIVE/MQL5/Profiles/Charts/Default/chart01.chr"

EA_MQ5="$PROJECT_DIR/mql5/ForexAI_Bridge.mq5"
EA_EX5="$PROJECT_DIR/mql5/ForexAI_Bridge.ex5"

MT5_SERVER="Monex-Demo"
MT5_LOGIN="1009043415"
KEYCHAIN_SERVICE="ForexAI-MT5"

echo ""
echo -e "${BOLD}╔══════════════════════════════════════════════════╗${RESET}"
echo -e "${BOLD}║   ForexAI Bridge — MT5 Auto Setup                ║${RESET}"
echo -e "${BOLD}║   Monex Demo · EURUSD.m · M15                    ║${RESET}"
echo -e "${BOLD}╚══════════════════════════════════════════════════╝${RESET}"
echo ""

# ── Simpan password ke Keychain (jalankan sekali dengan --save-password) ──────
if [[ "${1:-}" == "--save-password" ]]; then
    step "Simpan password ke macOS Keychain"
    echo -n "Masukkan password Monex Demo (tidak akan ditampilkan): "
    read -rs MT5_PASS
    echo ""
    # Hapus entry lama kalau ada
    security delete-generic-password -a "$MT5_LOGIN" -s "$KEYCHAIN_SERVICE" 2>/dev/null || true
    # Simpan ke Keychain
    security add-generic-password \
        -a "$MT5_LOGIN" \
        -s "$KEYCHAIN_SERVICE" \
        -w "$MT5_PASS" \
        -T "" \
        -U
    success "Password tersimpan di Keychain (service: $KEYCHAIN_SERVICE)"
    echo ""
fi

# ── Ambil password dari Keychain ──────────────────────────────────────────────
MT5_PASS=$(security find-generic-password -a "$MT5_LOGIN" -s "$KEYCHAIN_SERVICE" -w 2>/dev/null || true)
if [[ -z "$MT5_PASS" ]]; then
    warn "Password belum tersimpan. Jalankan sekali:"
    echo ""
    echo -e "  ${BOLD}./scripts/setup-mt5.sh --save-password${RESET}"
    echo ""
    error "Setup dibatalkan — password tidak ditemukan di Keychain"
fi
success "Password ditemukan di Keychain"

# Log files — dipakai di seluruh script
BE_LOG="/tmp/forexai-backend.log"
FE_LOG="/tmp/forexai-fe.log"

# ── Helper: kill process(es) yang nge-hold port tertentu ─────────────────────
kill_port() {
    local port=$1
    local label=$2
    local pids
    pids=$(lsof -ti tcp:"$port" 2>/dev/null || true)
    if [[ -n "$pids" ]]; then
        info "Menghentikan $label di port $port (PID: $(echo "$pids" | tr '\n' ' '))..."
        echo "$pids" | xargs kill -TERM 2>/dev/null || true
        # Tunggu sampai port free atau max 8 detik, baru SIGKILL
        for i in $(seq 1 8); do
            sleep 1
            if ! lsof -ti tcp:"$port" > /dev/null 2>&1; then
                success "$label dihentikan"
                return 0
            fi
        done
        # Masih ada — paksa kill
        lsof -ti tcp:"$port" 2>/dev/null | xargs kill -KILL 2>/dev/null || true
        sleep 1
        success "$label dipaksa-stop (SIGKILL)"
    fi
}

# ── Start Backend (selalu restart jika sudah jalan) ──────────────────────────
step "Step 0a — Backend (ForexAI API)"
if curl -s --max-time 2 http://localhost:8080/api/mifx/status > /dev/null 2>&1; then
    info "Backend sedang berjalan — restart untuk pick up perubahan terbaru"
    kill_port 8080 "Backend"
fi

info "Build & start backend..."
cd "$PROJECT_DIR"
# Build dulu (--no-incremental supaya perubahan terbaru selalu masuk)
dotnet build ForexAI.sln --nologo -v quiet --no-incremental 2>&1 | tail -2
nohup dotnet run --project src/ForexAI.API/ForexAI.API.csproj --no-build \
    > "$BE_LOG" 2>&1 &
BE_PID=$!
echo -n "  Menunggu backend siap"
for i in $(seq 1 20); do
    sleep 1; echo -n "."
    if curl -s --max-time 1 http://localhost:8080/api/mifx/status > /dev/null 2>&1; then
        break
    fi
done
echo ""
if curl -s --max-time 2 http://localhost:8080/api/mifx/status > /dev/null 2>&1; then
    success "Backend aktif di http://localhost:8080 (PID $BE_PID)"
else
    warn "Backend lambat start — log: $BE_LOG"
fi

# ── Start Frontend (selalu restart jika sudah jalan) ─────────────────────────
step "Step 0b — Frontend (Dashboard)"
FE_DIR="$PROJECT_DIR/frontend"
if curl -s --max-time 2 http://localhost:3000 > /dev/null 2>&1; then
    info "Frontend sedang berjalan — restart untuk pick up perubahan terbaru"
    kill_port 3000 "Frontend"
fi

info "Build & start frontend..."
cd "$FE_DIR"
# Build Next.js (jika .next/BUILD_ID tidak ada atau stale)
if [ ! -f "$FE_DIR/.next/BUILD_ID" ] || \
   find "$FE_DIR/src" -newer "$FE_DIR/.next/BUILD_ID" -name "*.tsx" -o -name "*.ts" 2>/dev/null | grep -q .; then
    info "Build frontend (30-60 detik)..."
    if npm run build > /tmp/forexai-fe-build.log 2>&1; then
        success "Frontend build OK"
    else
        warn "Build error — lihat /tmp/forexai-fe-build.log"
    fi
else
    info "Build sudah up-to-date — skip rebuild"
fi
nohup npm start > "$FE_LOG" 2>&1 &
FE_PID=$!
echo -n "  Menunggu frontend siap"
for i in $(seq 1 20); do
    sleep 1; echo -n "."
    if curl -s --max-time 1 http://localhost:3000 > /dev/null 2>&1; then
        break
    fi
done
echo ""
if curl -s --max-time 2 http://localhost:3000 > /dev/null 2>&1; then
    success "Dashboard aktif di http://localhost:3000 (PID $FE_PID)"
else
    warn "Frontend lambat start — log: $FE_LOG"
fi
cd "$PROJECT_DIR"

# ── Cek dependensi ────────────────────────────────────────────────────────────
step "Step 1 — Cek instalasi MT5"
[ -d "$MT5_APP" ]  || error "MetaTrader 5.app tidak ditemukan di /Applications/"
[ -f "$EA_MQ5" ]   || error "EA source tidak ditemukan: $EA_MQ5"
[ -d "$MT5_DATA" ] || error "Data MT5 tidak ditemukan — jalankan MT5 minimal sekali"

# Jika .ex5 belum ada di project, coba ambil dari MT5 (hasil compile sebelumnya)
if [ ! -f "$EA_EX5" ]; then
    MT5_EX5="$EA_DIR/ForexAI_Bridge.ex5"
    if [ -f "$MT5_EX5" ]; then
        cp "$MT5_EX5" "$EA_EX5"
        success "ForexAI_Bridge.ex5 diambil dari MT5 → disimpan ke project"
    else
        # Compile otomatis via MetaEditor CLI
        warn ".ex5 tidak ditemukan — mencoba compile via MetaEditor..."
        WINE="$MT5_APP/Contents/SharedSupport/wine/bin/wine64"
        METAEDITOR="$MT5_DRIVE/metaeditor64.exe"
        EA_WIN="C:\\Program Files\\MetaTrader 5\\MQL5\\Experts\\ForexAI_Bridge.mq5"
        cp "$EA_MQ5" "$EA_DIR/ForexAI_Bridge.mq5"
        WINEPREFIX="$MT5_DATA" "$WINE" "$METAEDITOR" /compile:"$EA_WIN" /log:"C:\\compile.log" 2>/dev/null &
        sleep 8
        if [ -f "$MT5_EX5" ]; then
            cp "$MT5_EX5" "$EA_EX5"
            success "Compile berhasil — .ex5 tersimpan ke project"
        else
            error "Compile gagal. Buka MT5 → MetaEditor (F4) → tekan F7 untuk compile ForexAI_Bridge\nLalu jalankan script ini kembali."
        fi
    fi
fi
success "Semua dependensi OK"

# ── Matikan MT5 secara graceful ───────────────────────────────────────────────
step "Step 2 — Matikan MT5 (graceful)"
if pgrep -f "terminal64.exe" > /dev/null 2>&1; then
    info "MT5 sedang berjalan, mematikan gracefully..."
    # Coba quit lewat osascript dulu (MT5 simpan state sebelum exit)
    osascript -e 'tell application "MetaTrader 5" to quit' 2>/dev/null || true
    sleep 4
    # Kalau masih ada, force kill
    if pgrep -f "terminal64.exe" > /dev/null 2>&1; then
        pkill -f "terminal64.exe" 2>/dev/null || true
        sleep 2
    fi
    success "MT5 dihentikan"
else
    info "MT5 tidak berjalan"
fi

# ── Install EA ────────────────────────────────────────────────────────────────
EA_VERSION=$(grep -o '#property version.*"[0-9.]*"' "$EA_MQ5" | grep -o '[0-9.]*' | head -1)
step "Step 3 — Install EA v${EA_VERSION}"
cp "$EA_MQ5" "$EA_DIR/ForexAI_Bridge.mq5"

# Cek apakah .ex5 yang ada cocok dengan versi .mq5 terbaru
# v1.15 menambahkan indikator MA/RSI/SR — perlu compile ulang
NEED_COMPILE=false
if [ ! -f "$EA_EX5" ]; then
    NEED_COMPILE=true
    info ".ex5 belum ada — perlu compile"
else
    # Cek apakah .mq5 lebih baru dari .ex5 (source diupdate)
    if [ "$EA_MQ5" -nt "$EA_EX5" ]; then
        NEED_COMPILE=true
        info "Source .mq5 lebih baru dari .ex5 — perlu compile ulang"
    fi
fi

if [ "$NEED_COMPILE" = true ]; then
    warn "EA v${EA_VERSION} perlu di-compile. Mencoba auto-compile via MetaEditor..."
    WINE="$MT5_APP/Contents/SharedSupport/wine/bin/wine64"
    METAEDITOR="$MT5_DRIVE/metaeditor64.exe"
    EA_WIN="C:\\Program Files\\MetaTrader 5\\MQL5\\Experts\\ForexAI_Bridge.mq5"
    MT5_EX5="$EA_DIR/ForexAI_Bridge.ex5"

    # Catat timestamp SEBELUM compile — ini yang kita pakai untuk verifikasi sukses
    COMPILE_BEFORE=$(date +%s)

    # Hapus .ex5 lama di MT5 dir agar tidak salah dianggap hasil compile baru
    rm -f "$MT5_EX5"

    WINEPREFIX="$MT5_DATA" "$WINE" "$METAEDITOR" /compile:"$EA_WIN" /log:"C:\\compile.log" 2>/dev/null &
    COMPILE_PID=$!

    echo -n "  Compiling v${EA_VERSION}"
    COMPILED=false
    for i in $(seq 1 25); do
        sleep 1; echo -n "."
        # Sukses: .ex5 BARU ada di MT5 dir (dibuat setelah kita mulai compile)
        if [ -f "$MT5_EX5" ]; then
            EX5_TIME=$(stat -f %m "$MT5_EX5" 2>/dev/null || stat -c %Y "$MT5_EX5" 2>/dev/null)
            if [ "${EX5_TIME:-0}" -ge "$COMPILE_BEFORE" ]; then
                COMPILED=true; break
            fi
        fi
    done
    echo ""
    wait $COMPILE_PID 2>/dev/null || true

    # Cek sekali lagi setelah proses selesai
    if [ "$COMPILED" = false ] && [ -f "$MT5_EX5" ]; then
        EX5_TIME=$(stat -f %m "$MT5_EX5" 2>/dev/null || stat -c %Y "$MT5_EX5" 2>/dev/null)
        if [ "${EX5_TIME:-0}" -ge "$COMPILE_BEFORE" ]; then
            COMPILED=true
        fi
    fi

    if [ "$COMPILED" = true ]; then
        cp "$MT5_EX5" "$EA_EX5"
        success "Compile v${EA_VERSION} berhasil — .ex5 tersimpan ke project"
    else
        echo ""
        warn "Auto-compile gagal (MetaEditor CLI tidak reliable di macOS)."
        warn "Lakukan MANUAL — hanya perlu sekali:"
        echo ""
        echo -e "  ${BOLD}1. MT5 akan dibuka di Step 6 — tunggu sampai terbuka penuh${RESET}"
        echo -e "  ${BOLD}2. Tekan F4 untuk buka MetaEditor${RESET}"
        echo -e "  ${BOLD}3. File → Open → pilih 'ForexAI_Bridge' dari list Experts${RESET}"
        echo -e "  ${BOLD}4. Tekan F7 untuk Compile — harus ada '0 errors, 0 warnings'${RESET}"
        echo -e "  ${BOLD}5. Drag EA 'ForexAI_Bridge' dari Navigator ke chart EURUSD.m,M15${RESET}"
        echo ""
        warn "Setelah compile, tekan Trigger Analysis di dashboard — analisa akan real dari Monex."
        echo ""
        # Lanjut tanpa ex5 (MT5 akan re-attach EA setelah compile manual)
        # Tidak copy ex5 lama agar MT5 tahu perlu compile ulang
    fi
else
    cp "$EA_EX5" "$EA_DIR/ForexAI_Bridge.ex5"
    success "EA v${EA_VERSION} ter-install (tidak perlu compile ulang)"
fi
success "EA source ter-install di $EA_DIR"

# ── Update chart01.chr — set symbol EURUSD.m + TradePair EURUSD.m ─────────────
step "Step 4 — Konfigurasi chart EURUSD.m,M15 + EA"
if [ -f "$CHART_FILE" ]; then
    python3 - "$CHART_FILE" << 'PYEOF'
import sys, re

path = sys.argv[1]
with open(path, 'rb') as f:
    raw = f.read()

# Detect BOM
if raw[:2] == b'\xff\xfe':
    content = raw[2:].decode('utf-16-le')
    bom = b'\xff\xfe'
else:
    content = raw.decode('utf-16-le', errors='replace')
    bom = b'\xff\xfe'

# Update symbol dan TradePair ke EURUSD.m
content = re.sub(r'^symbol=EURUSD$', 'symbol=EURUSD.m', content, flags=re.MULTILINE)
content = re.sub(r'^TradePair=EURUSD$', 'TradePair=EURUSD.m', content, flags=re.MULTILINE)

# Pastikan <expert> section ada
if '<expert>' not in content:
    expert_block = """
<expert>
name=ForexAI_Bridge
path=Experts\\ForexAI_Bridge.ex5
expertmode=0
<inputs>
BackendUrl=http://127.0.0.1:5033
TradePair=EURUSD.m
TimerMs=1000
MagicNumber=20260101
</inputs>
</expert>

"""
    content = content.replace('<window>', expert_block + '<window>', 1)
    print("  <expert> section ditambahkan")
else:
    print("  <expert> section diupdate")

with open(path, 'wb') as f:
    f.write(bom + content.encode('utf-16-le'))
PYEOF
    success "chart01.chr → symbol=EURUSD.m, TradePair=EURUSD.m"
else
    warn "chart01.chr tidak ditemukan — chart akan perlu di-setup manual sekali"
fi

# ── Update common.ini ─────────────────────────────────────────────────────────
step "Step 5 — Konfigurasi common.ini"
python3 - "$CONFIG_INI" "$MT5_SERVER" "$MT5_LOGIN" << 'PYEOF'
import sys, re
path, server, login = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, 'rb') as f:
    raw = f.read()
bom = b'\xff\xfe' if raw[:2] == b'\xff\xfe' else b'\xfe\xff'
enc = 'utf-16-le' if bom == b'\xff\xfe' else 'utf-16-be'
content = raw[2:].decode(enc)

def set_key(text, key, value):
    p = re.compile(r'^(' + key + r'\s*=).*$', re.MULTILINE)
    return p.sub(r'\g<1>' + value, text) if p.search(text) else text.replace('[Common]', '[Common]\n' + key + '=' + value, 1)

content = set_key(content, 'Server', server)
content = set_key(content, 'Login',  login)
with open(path, 'wb') as f:
    f.write(bom + content.encode(enc))
print(f"  Server={server}, Login={login}")
PYEOF
success "common.ini dikonfigurasi"

# ── Launch MT5 ────────────────────────────────────────────────────────────────
step "Step 6 — Launch MT5"
open -a "MetaTrader 5"
success "MT5 diluncurkan"

# ── Auto-login via AppleScript ────────────────────────────────────────────────
step "Step 7 — Auto-login Monex Demo"
info "Menunggu MT5 & dialog login (maks 30 detik)..."

python3 - "$MT5_PASS" << 'PYEOF'
import subprocess, sys, time

password = sys.argv[1]

# Tunggu MT5 muncul di layar
for attempt in range(30):
    time.sleep(1)
    result = subprocess.run(
        ['osascript', '-e',
         'tell application "System Events" to (name of processes) contains "MetaTrader 5"'],
        capture_output=True, text=True
    )
    if result.stdout.strip() == 'true':
        break
else:
    print("  ⚠  MT5 tidak terdeteksi dalam 30 detik")
    sys.exit(0)

# Tunggu dialog login — dicoba dengan keystroke sequence
# MT5 login dialog: Login field sudah terisi dari common.ini
# Tab → ke Password → ketik password → Enter
time.sleep(6)  # tunggu dialog render

script = f'''
tell application "MetaTrader 5" to activate
delay 1
tell application "System Events"
    -- Klik area tengah layar untuk fokus ke dialog
    key code 48  -- Tab ke field berikutnya (password)
    delay 0.3
    -- Clear field dulu (select all + delete)
    keystroke "a" using command down
    delay 0.2
    key code 51  -- Delete
    delay 0.2
    -- Ketik password
    keystroke "{password}"
    delay 0.3
    key code 36  -- Return/Enter
end tell
'''
result = subprocess.run(['osascript', '-e', script], capture_output=True, text=True)
if result.returncode == 0:
    print("  Keystroke login dikirim")
else:
    print(f"  ⚠  AppleScript error: {result.stderr.strip()}")
PYEOF

# ── Tunggu & verifikasi koneksi ───────────────────────────────────────────────
step "Step 8 — Verifikasi koneksi ke backend"
info "Menunggu EA terhubung ke backend (maks 20 detik)..."

for i in $(seq 1 20); do
    STATUS=$(curl -s --max-time 2 http://localhost:8080/api/mifx/status 2>/dev/null || echo '{}')
    CONNECTED=$(echo "$STATUS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('connected', False))" 2>/dev/null || echo "False")
    BALANCE=$(echo "$STATUS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('accountBalance', 0))" 2>/dev/null || echo "0")

    if [[ "$CONNECTED" == "True" ]] && (( $(echo "$BALANCE > 0" | python3 -c "import sys; print(eval(sys.stdin.read()))") )); then
        echo ""
        success "Koneksi berhasil!"
        success "Balance: \$$BALANCE | Pair: $(echo "$STATUS" | python3 -c "import sys,json; print(json.load(sys.stdin).get('pair','?'))" 2>/dev/null)"
        BID=$(echo "$STATUS" | python3 -c "import sys,json; print(json.load(sys.stdin).get('bid','?'))" 2>/dev/null)
        success "Harga live: $BID"
        break
    fi
    echo -n "."
    sleep 1
done

echo ""
echo -e "${BOLD}╔══════════════════════════════════════════════════╗${RESET}"
echo -e "${BOLD}║   Setup selesai! Status sistem:                  ║${RESET}"
echo -e "${BOLD}╚══════════════════════════════════════════════════╝${RESET}"
echo ""

# ── Status Backend ────────────────────────────────────────────────────────────
if curl -s --max-time 2 http://localhost:8080/api/mifx/status > /dev/null 2>&1; then
    MIFX_STATUS=$(curl -s http://localhost:8080/api/mifx/status 2>/dev/null)
    MT5_CONN=$(echo "$MIFX_STATUS" | python3 -c "import sys,json; d=json.load(sys.stdin); print('✅ Terhubung' if d.get('connected') else '❌ Tidak terhubung')" 2>/dev/null || echo "?")
    MT5_BID=$(echo  "$MIFX_STATUS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('bid','?'))"  2>/dev/null || echo "?")
    MT5_BAL=$(echo  "$MIFX_STATUS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('accountBalance','?'))" 2>/dev/null || echo "?")
    echo -e "  ${GREEN}Backend  :${RESET} http://localhost:8080"
    echo -e "  ${GREEN}MT5 EA   :${RESET} $MT5_CONN | Bid: $MT5_BID | Balance: \$$MT5_BAL"
else
    echo -e "  ${RED}Backend  : tidak aktif${RESET} — log: $BE_LOG"
fi

# ── Status Frontend ───────────────────────────────────────────────────────────
if curl -s --max-time 2 http://localhost:3000 > /dev/null 2>&1; then
    echo -e "  ${GREEN}Dashboard:${RESET} http://localhost:3000"
else
    echo -e "  ${RED}Dashboard: tidak aktif${RESET} — log: $FE_LOG"
fi

# ── Status Account ────────────────────────────────────────────────────────────
if curl -s --max-time 2 http://localhost:8080/api/account > /dev/null 2>&1; then
    ACC=$(curl -s http://localhost:8080/api/account 2>/dev/null)
    EQUITY=$(echo "$ACC" | python3 -c "import sys,json; print(json.load(sys.stdin).get('equity','?'))"  2>/dev/null || echo "?")
    SOURCE=$(echo "$ACC" | python3 -c "import sys,json; print(json.load(sys.stdin).get('source','?'))" 2>/dev/null || echo "?")
    echo -e "  ${GREEN}Equity   :${RESET} \$$EQUITY ($SOURCE)"
fi

echo ""

# ── Deteksi apakah EA sudah v1.15 ────────────────────────────────────────────
EA_INSTALLED_VER=$(grep -o '#property version.*"[0-9.]*"' "$EA_DIR/ForexAI_Bridge.mq5" 2>/dev/null | grep -o '[0-9.]*' | head -1 || echo "?")
if [[ "$EA_INSTALLED_VER" == "1.15" ]]; then
    MIFX_TICK=$(curl -s --max-time 2 http://localhost:8080/api/mifx/status 2>/dev/null || echo '{}')
    HAS_IND=$(echo "$MIFX_TICK" | python3 -c "
import sys,json
d=json.load(sys.stdin)
# Cek apakah tick punya field indikator (belum ada di response status, tapi cek connected)
print('Terhubung' if d.get('connected') else 'Belum terhubung')
" 2>/dev/null || echo "?")
    echo -e "  ${GREEN}EA v1.15 ter-install${RESET} — $HAS_IND ke MT5"
    echo -e "  ${YELLOW}Buka Dashboard → tekan Trigger Analysis untuk analisa real${RESET}"
else
    echo -e "  ${YELLOW}EA v${EA_INSTALLED_VER} ter-install (bukan v1.15)${RESET}"
    echo -e "  ${YELLOW}Langkah compile v1.15:${RESET}"
    echo -e "    1. Tunggu MT5 terbuka penuh"
    echo -e "    2. Tekan ${BOLD}F4${RESET} → MetaEditor"
    echo -e "    3. Buka ForexAI_Bridge.mq5 → tekan ${BOLD}F7${RESET} untuk compile"
    echo -e "    4. Drag EA ke chart EURUSD.m,M15"
    echo -e "    5. Buka Dashboard → Trigger Analysis"
fi

echo ""
echo -e "  ${YELLOW}Tip:${RESET} Kalau MT5 perlu login ulang, jalankan script ini lagi."
echo -e "  ${YELLOW}Tip:${RESET} Jangan paksa-quit MT5 — tutup normal agar state tersimpan."
echo -e "  ${YELLOW}Log :${RESET} Backend → $BE_LOG | Frontend → $FE_LOG"
echo ""
