"""Riot API için ortak istemci (01-03 betikleri kullanır).

- Anahtar RIOT_API_KEY ortam değişkeninden okunur, hiçbir yere yazılmaz.
- 429: Retry-After kadar bekleyip tekrar dener.
- 5xx ve ağ hataları: artan beklemeyle tekrar dener.
- 401/403: anahtar yok ya da süresi dolmuş, betik durur.
- Hız sınırlayıcı: her istekten önce development anahtarının limitlerine bakar,
  gerekirse bekler; böylece 429'a düşmeyiz.
"""
import os
import sys
import time
from collections import deque

import requests

PLATFORM = "euw1"   # league-v4 gibi platform uç noktaları
REGION = "europe"   # match-v5 gibi bölge uç noktaları

_MAX_RETRIES = 5
_session = None

# Development anahtarı limitleri: (istek sayısı, pencere sn)
_LIMITS = [(20, 1.0), (100, 120.0)]
_MARGIN = 0.1  # Riot'un saatiyle küçük farklar için pay (sn)
_sent = deque()  # gönderilen isteklerin zamanları (monotonic), eskiden yeniye


def _throttle():
    """Her iki pencerede de yer açılana kadar bekler, sonra isteği kaydeder."""
    longest = max(window for _, window in _LIMITS)
    while True:
        now = time.monotonic()
        while _sent and now - _sent[0] >= longest:
            _sent.popleft()
        wait = 0.0
        for limit, window in _LIMITS:
            recent = [t for t in _sent if now - t < window]
            if len(recent) >= limit:
                # Son `limit` isteğin en eskisi pencereden çıkınca yer açılır.
                wait = max(wait, recent[-limit] + window - now + _MARGIN)
        if wait <= 0:
            _sent.append(now)
            return
        time.sleep(wait)


def _get_session() -> requests.Session:
    global _session
    if _session is None:
        key = os.environ.get("RIOT_API_KEY", "").strip()
        if not key:
            sys.exit("RIOT_API_KEY ortam değişkeni tanımlı değil. Anahtarı developer.riotgames.com'dan al.")
        _session = requests.Session()
        _session.headers["X-Riot-Token"] = key
    return _session


def _riot_message(r: requests.Response) -> str:
    """Riot hata gövdesindeki mesaj: {"status": {"message": "Unknown apikey", ...}}"""
    try:
        return r.json()["status"]["message"]
    except (ValueError, KeyError, TypeError):
        return r.text[:100] or "mesaj yok"


def get(host: str, path: str, params: dict | None = None):
    """GET https://{host}.api.riotgames.com{path} → JSON. 404'te None döner."""
    url = f"https://{host}.api.riotgames.com{path}"
    session = _get_session()
    for attempt in range(_MAX_RETRIES):
        _throttle()
        try:
            r = session.get(url, params=params, timeout=15)
        except requests.RequestException as e:
            wait = 2 ** attempt
            print(f"  ağ hatası ({type(e).__name__}), {wait} sn sonra tekrar")
            time.sleep(wait)
            continue

        if r.status_code == 200:
            return r.json()
        if r.status_code == 404:
            return None
        if r.status_code == 429:
            wait = int(r.headers.get("Retry-After", "10"))
            print(f"  429 rate limit, {wait} sn bekleniyor")
            time.sleep(wait)
            continue
        if r.status_code in (401, 403):
            sys.exit(f"Riot API {r.status_code} ({_riot_message(r)}): anahtar geçersiz ya da süresi dolmuş, developer.riotgames.com'dan yenile.")
        if r.status_code >= 500:
            wait = 2 ** attempt
            print(f"  sunucu hatası {r.status_code}, {wait} sn sonra tekrar")
            time.sleep(wait)
            continue
        r.raise_for_status()

    raise RuntimeError(f"{path}: {_MAX_RETRIES} denemede cevap alınamadı")
