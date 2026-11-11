namespace LolDraft.Domain;

/// <summary>
/// Lobi (DESIGN.md §2, §8.4): iki takım koltuğu ve oynanacak seri.
/// Açan oyuncu A koltuğuna oturur, linkle gelen B'ye; boş koltuk bir kez alınır.
/// Koltuk renk değildir: renk her maç seriden gelir (<see cref="Series.SideOf"/>).
/// Oyuncunun hamleleri buradan geçer: yetki (lobide mi), koltuk → renk çevirisi ve tekrar koruması burada,
/// draft kuralları <see cref="DraftSession"/>'da. Zamana bağlı kararlar (zaman aşımı, hükmen galibiyet,
/// kapanış) <see cref="Advance"/>'ta.
/// </summary>
public sealed class Lobby
{
    /// <summary>Seri başladıktan sonra bu kadar kesintisiz bağlı olmayan oyuncu seriyi hükmen kaybeder (karar 2026-11-10).</summary>
    public static readonly TimeSpan ForfeitAfter = TimeSpan.FromMinutes(2);

    /// <summary>Kimse bağlı değilse lobi bu kadar sonra kapanır (karar 2026-11-11).</summary>
    public static readonly TimeSpan EmptyCloseAfter = TimeSpan.FromMinutes(5);

    /// <summary>Bu kadar hareketsiz kalan lobi kapanır, bağlı oyuncu olsa da (DESIGN.md §8.6).</summary>
    public static readonly TimeSpan IdleCloseAfter = TimeSpan.FromMinutes(30);

    // Son 64 hamle kimliği hatırlanır: çift tıklama milisaniyeler içinde gelir, bir draft ~30 hamle.
    private const int RecentActionCapacity = 64;

    private readonly Dictionary<TeamSlot, PlayerId> _seats = [];
    private readonly HashSet<TeamSlot> _ready = [];
    private readonly Dictionary<(PlayerId, ActionId), Result> _recentActions = [];
    private readonly Queue<(PlayerId, ActionId)> _recentOrder = new();

    // Bağlantı takibi: koltuk başına açık bağlantı sayısı (aynı oyuncu iki sekme açabilir) ve
    // sayı sıfıra düştüğü an. Oturmuş ama bağlı olmayan koltuk "yok" sayılır.
    private readonly Dictionary<TeamSlot, int> _connections = [];
    private readonly Dictionary<TeamSlot, Instant> _absentSince = [];

    private Lobby(LobbyCode code, Series series, PlayerId host, Instant now)
    {
        Code = code;
        Series = series;
        Seat(TeamSlot.A, host, now);
        LastActivity = now;
    }

    /// <summary>
    /// Seri ayarları lobi açılırken doğrulanır: havuza sığmayan kombinasyon (ör. 40'lık havuzda
    /// fearless bo5) daha lobi oluşmadan reddedilir, rakip davet edildikten sonra değil.
    /// </summary>
    public static Result<Lobby> Create(
        LobbyCode code, PlayerId host, SeriesFormat format, bool fearless, IEnumerable<ChampionId> pool,
        TeamSlot firstBlue, Instant now)
    {
        var series = Series.Start(format, fearless, pool, firstBlue);
        return series.IsSuccess
            ? Result<Lobby>.Ok(new Lobby(code, series.Value, host, now))
            : Result<Lobby>.Fail(series.Error!);
    }

    public LobbyCode Code { get; }

    public Series Series { get; }

    public bool IsFull => _seats.Count == Enum.GetValues<TeamSlot>().Length;

    /// <summary>Son hamle, katılma, bağlanma ya da kopma anı (hareketsizlik kapanışı buna bakar).</summary>
    public Instant LastActivity { get; private set; }

    /// <summary>
    /// Görünür durumun sürümü: her değişimde (oturma, bağlanma, kopma, başarılı yeni hamle, saatin değiştirdiği tik)
    /// bir artar. Görüntü kilit altında bu sayıyla üretilir ama yayın kilidin dışında gider; iki yayın ağda yer
    /// değiştirirse istemci elindekinden küçük ya da eşit sürümü atar (DESIGN.md §8.12).
    /// Örnek: "A hazır" (7) ve "draft başladı" (8) ters gelirse istemci 8'i gösterir, 7'yi atar.
    /// </summary>
    public long Revision { get; private set; }

    public PlayerId? PlayerAt(TeamSlot seat) => _seats.TryGetValue(seat, out var player) ? player : null;

    /// <summary>Sıradaki draft için hazır olan koltuklar.</summary>
    public IReadOnlySet<TeamSlot> ReadySeats => _ready;

    public bool IsConnected(TeamSlot seat) => _connections.GetValueOrDefault(seat) > 0;

    /// <summary>Oturmuş ama bağlı olmayan koltuğun ne zamandan beri yok olduğu; bağlıysa ya da boşsa null.</summary>
    public Instant? AbsentSince(TeamSlot seat) => _absentSince.TryGetValue(seat, out var since) ? since : null;

    /// <summary>Oyuncunun koltuğu; lobide değilse null. Yetki kontrolünün dayanağı.</summary>
    public TeamSlot? SeatOf(PlayerId player)
    {
        foreach (var (seat, seated) in _seats)
        {
            if (seated == player)
                return seat;
        }

        return null;
    }

    /// <summary>Oyuncuyu ilk boş koltuğa oturtur. Bağlanana kadar "yok" sayılır.</summary>
    public Result<TeamSlot> Join(PlayerId player, Instant now)
    {
        if (SeatOf(player) is not null)
            return Result<TeamSlot>.Fail(LobbyErrors.AlreadyInLobby);

        foreach (var seat in Enum.GetValues<TeamSlot>())
        {
            if (!_seats.ContainsKey(seat))
            {
                Seat(seat, player, now);
                LastActivity = now;
                return Result<TeamSlot>.Ok(seat);
            }
        }

        return Result<TeamSlot>.Fail(LobbyErrors.Full);
    }

    /// <summary>Oyuncunun bir bağlantısı açıldı (hub). Birden fazla sekme ayrı ayrı sayılır.</summary>
    public Result Connect(PlayerId player, Instant now)
    {
        if (SeatOf(player) is not { } seat)
            return Result.Fail(LobbyErrors.NotInLobby);

        _connections[seat] = _connections.GetValueOrDefault(seat) + 1;
        _absentSince.Remove(seat);
        LastActivity = now;
        Revision++;
        return Result.Ok();
    }

    /// <summary>Oyuncunun bir bağlantısı kapandı. Son bağlantısıysa "yok" sayacı o an başlar.</summary>
    public Result Disconnect(PlayerId player, Instant now)
    {
        if (SeatOf(player) is not { } seat)
            return Result.Fail(LobbyErrors.NotInLobby);

        var remaining = Math.Max(0, _connections.GetValueOrDefault(seat) - 1);
        _connections[seat] = remaining;
        if (remaining == 0)
            _absentSince[seat] = now;
        LastActivity = now;
        Revision++;
        return Result.Ok();
    }

    /// <summary>
    /// Oyuncu sıradaki maç için hazır. İki koltuk da hazır olunca draft başlar ve hazırlar sıfırlanır
    /// (serinin sonraki maçında yeniden "hazır" denir).
    /// </summary>
    public Result Ready(PlayerId player, ActionId action, DraftTimings timings, Instant now) =>
        Once(player, action, now, seat =>
        {
            if (!IsFull)
                return Result.Fail(LobbyErrors.NotFull);
            if (Series.IsFinished)
                return Result.Fail(SeriesErrors.SeriesFinished);
            if (Series.CurrentDraft is not null)
                return Result.Fail(SeriesErrors.GameInProgress);

            _ready.Add(seat);
            if (_ready.Count < _seats.Count)
                return Result.Ok();

            var draft = Series.StartDraft(timings, now);
            if (draft.IsFailure)
                return Result.Fail(draft.Error!);

            _ready.Clear();
            return Result.Ok();
        });

    public Result Ban(PlayerId player, ActionId action, ChampionId champion, Instant now) =>
        OnDraft(player, action, now, (draft, side) => draft.Ban(side, champion, now));

    public Result Pick(PlayerId player, ActionId action, ChampionId champion, Instant now) =>
        OnDraft(player, action, now, (draft, side) => draft.Pick(side, champion, now));

    public Result SwapRoles(PlayerId player, ActionId action, Role a, Role b, Instant now) =>
        OnDraft(player, action, now, (draft, side) => draft.SwapRoles(side, a, b, now));

    public Result LockLineup(PlayerId player, ActionId action, Instant now) =>
        OnDraft(player, action, now, (draft, side) => draft.LockLineup(side, now));

    /// <summary>
    /// Draft'ı biten maçın sonucunu seriye yazar (DESIGN.md §8.3: dışarıdan tek kapı). Simülasyonu dışarısı yapar
    /// (Domain motoru bilmez): draft bitince Application maçı oynatır, kazanan rengi ve maçın kimliğini buraya verir.
    /// Seri skoru takıma çevirir, renkleri değiştirir, fearless kilitlerini günceller; sonraki maç için yeniden "hazır" denir.
    /// </summary>
    public Result RecordGame(Side winner, MatchId match, Instant now)
    {
        var result = Series.RecordGame(winner, match);
        if (result.IsFailure)
            return result;

        LastActivity = now;
        Revision++;
        return result;
    }

    /// <summary>
    /// Lobi saatinin tiki (sunucunun arka plan döngüsü çağırır): zamana bağlı ne varsa yürütür, sırayla:
    /// <list type="number">
    /// <item>Kapanış: kimse bağlı değilse son çıkıştan 5 dk sonra; ya da 30 dk hiç hareket yoksa.</item>
    /// <item>Hükmen galibiyet: seri başladıktan sonra bir oyuncu 2 dk kesintisiz yoksa ve rakibi bağlıysa.
    /// İkisi birden yoksa kimse kazanmaz; lobi 1. maddeyle kapanır.</item>
    /// <item>Zaman aşımı: draft adımının süresi dolduysa boş ban / rastgele pick / swap biter.</item>
    /// </list>
    /// Hamleyle yarışmaz: ikisi de lobinin kilidinden geçer; süre dolduktan sonra gelen hamleyi
    /// <see cref="DraftSession"/> zaten reddeder (turn_expired), süre dolmadan zaman aşımı olmaz.
    /// </summary>
    public LobbyTick Advance(Instant now, IDraftRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (ShouldClose(now))
            return LobbyTick.Closed;

        var changed = TryForfeit(now)
                      || (Series.CurrentDraft is { Deadline: { } deadline } draft && now >= deadline
                          && draft.Timeout(now, random).IsSuccess);
        if (!changed)
            return LobbyTick.Unchanged;

        Revision++;
        return LobbyTick.Changed;
    }

    private bool ShouldClose(Instant now)
    {
        if (now >= LastActivity + IdleCloseAfter)
            return true;

        // Kimse bağlı değil: en son çıkan da 5 dk'dır yok.
        var everyoneAway = _seats.Keys.All(seat => _absentSince.ContainsKey(seat));
        return everyoneAway && now >= _absentSince.Values.Max() + EmptyCloseAfter;
    }

    private bool TryForfeit(Instant now)
    {
        if (!Series.HasStarted || Series.IsFinished)
            return false;

        foreach (var (seat, since) in _absentSince)
        {
            // Rakip de yoksa hükmen yok: iki taraf da kaçtıysa kazanan olmaz, lobi kapanır.
            if (now >= since + ForfeitAfter && IsConnected(seat.Opponent()))
                return Series.Forfeit(seat).IsSuccess;
        }

        return false;
    }

    private void Seat(TeamSlot seat, PlayerId player, Instant now)
    {
        _seats[seat] = player;
        _connections[seat] = 0;
        // Oturan oyuncu henüz bağlanmadı: bağlanana kadar "yok" (lobiyi açıp hiç gelmeyen için kapanış sayacı).
        _absentSince[seat] = now;
        Revision++;
    }

    // Koltuk → bu maçtaki renk. Token renk taşımaz; renk her maç seriden okunur.
    private Result OnDraft(PlayerId player, ActionId action, Instant now, Func<DraftSession, Side, Result> act) =>
        Once(player, action, now, seat => Series.CurrentDraft is { } draft
            ? act(draft, Series.SideOf(seat))
            : Result.Fail(DraftErrors.PhaseClosed));

    /// <summary>
    /// Hamleyi bir kez uygular. Aynı oyuncudan aynı kimlikle ikinci istek gelirse hamle tekrarlanmaz,
    /// ilk sonuç (başarılı ya da hatalı) aynen döner.
    /// </summary>
    private Result Once(PlayerId player, ActionId action, Instant now, Func<TeamSlot, Result> act)
    {
        // Lobide olmayanın hamlesi hatırlanmaz: başkasının kimlik listesini dolduramasın.
        if (SeatOf(player) is not { } seat)
            return Result.Fail(LobbyErrors.NotInLobby);

        LastActivity = now;

        var key = (player, action);
        if (_recentActions.TryGetValue(key, out var previous))
            return previous;

        var result = act(seat);
        // Başarısız hamle durumu değiştirmez; tekrar gelen kimlik buraya hiç ulaşmaz (yukarıda döndü).
        if (result.IsSuccess)
            Revision++;

        _recentActions[key] = result;
        _recentOrder.Enqueue(key);
        if (_recentOrder.Count > RecentActionCapacity)
            _recentActions.Remove(_recentOrder.Dequeue());

        return result;
    }
}

/// <summary>Lobi saatinin bir tikinin sonucu.</summary>
public enum LobbyTick
{
    Unchanged,

    /// <summary>Durum değişti: yeni durum yayınlanır.</summary>
    Changed,

    /// <summary>Lobi kapanmalı: depodan silinir, bağlı olanlara haber verilir.</summary>
    Closed,
}
