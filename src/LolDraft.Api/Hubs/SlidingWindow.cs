namespace LolDraft.Api.Hubs;

/// <summary>
/// Kayan pencere sayacı: son <c>window</c> süresinde en fazla <c>limit</c> olay.
/// Her olayın zamanı kuyrukta tutulur; pencereden çıkanlar her denemede atılır.
/// Örnek (limit 5, pencere 1 sn): 0,0 · 0,1 · 0,2 · 0,3 · 0,4 sn → 5 olay kabul; 0,5 sn → ret;
/// 1,05 sn → 0,0'daki olay pencereden çıktı, kabul.
/// Zamanlayıcı yok (bağlantı başına arka plan işi açmaz); kuyruk en fazla <c>limit</c> uzunlukta.
/// Bağlantı başına tek iş parçacığından çağrılır (SignalR bir bağlantının çağrılarını sırayla işler); kilit yok.
/// </summary>
internal sealed class SlidingWindow(int limit, TimeSpan window, TimeProvider time)
{
    private readonly Queue<long> _events = new();

    /// <summary>Olay pencereye sığıyorsa kaydeder ve true döner; sığmıyorsa hiçbir şey kaydetmez.</summary>
    public bool TryAdd()
    {
        var now = time.GetTimestamp();
        while (_events.TryPeek(out var oldest) && time.GetElapsedTime(oldest, now) >= window)
            _events.Dequeue();

        if (_events.Count >= limit)
            return false;

        _events.Enqueue(now);
        return true;
    }
}
