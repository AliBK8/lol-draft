using LolDraft.Domain;
using static LolDraft.Domain.Tests.DraftTestKit;

namespace LolDraft.Domain.Tests;

/// <summary>Maç sonucu lobi üzerinden seriye yazılır (DESIGN.md §8.3: tek kapı Lobby).</summary>
public sealed class LobbyRecordGameTests
{
    private static readonly LobbyCode Code = LobbyCode.FromBytes(new byte[16]);

    private readonly PlayerId _host = PlayerId.FromBytes(Enumerable.Repeat((byte)1, 16).ToArray());
    private readonly PlayerId _guest = PlayerId.FromBytes(Enumerable.Repeat((byte)2, 16).ToArray());

    [Fact]
    public void Biten_draftin_sonucu_yazilir_ve_sonraki_mac_icin_hazir_denir()
    {
        var lobby = DraftedLobby();
        var match = NewMatch();
        var before = lobby.Revision;

        // İlk maçta A mavi (firstBlue = A); Kırmızı kazanırsa B'ye yazılır.
        Assert.True(lobby.RecordGame(Side.Red, match, T0).IsSuccess);

        Assert.Equal(1, lobby.Series.Wins(TeamSlot.B));
        var game = Assert.Single(lobby.Series.Games);
        Assert.Equal((TeamSlot.A, TeamSlot.B, match), (game.Blue, game.Winner, game.Match));
        Assert.Null(lobby.Series.CurrentDraft);
        Assert.Equal(before + 1, lobby.Revision);
        // Renkler değişti: 2. maçta B mavi. Yeni draft yeniden "hazır"la başlar.
        Assert.Equal(TeamSlot.B, lobby.Series.BlueTeam);
        Assert.True(lobby.Ready(_host, Action(), DraftTimings.Default, T0).IsSuccess);
    }

    [Fact]
    public void Draft_bitmeden_sonuc_yazilamaz()
    {
        var lobby = DraftedLobby(lock_: false);
        var before = lobby.Revision;

        Assert.Equal(SeriesErrors.DraftNotCompleted, lobby.RecordGame(Side.Blue, NewMatch(), T0).Error);
        Assert.Equal(before, lobby.Revision);
    }

    /// <summary>İki oyuncu bağlı ve hazır; 20 adım oynandı; lock_ ise iki taraf da dizilimi kilitledi.</summary>
    private Lobby DraftedLobby(bool lock_ = true)
    {
        var lobby = Lobby.Create(Code, _host, SeriesFormat.Bo3, fearless: false, Pool, TeamSlot.A, T0).Value;
        lobby.Join(_guest, T0);
        lobby.Ready(_host, Action(), DraftTimings.Default, T0);
        lobby.Ready(_guest, Action(), DraftTimings.Default, T0);

        var draft = lobby.Series.CurrentDraft!;
        while (draft.CurrentStep is { } step)
        {
            var player = step.Side == Side.Blue ? _host : _guest;
            var champion = Pool.First(c => draft.Actions.All(a => a.Champion != c));
            var result = step.Kind == DraftActionKind.Ban
                ? lobby.Ban(player, Action(), champion, T0)
                : lobby.Pick(player, Action(), champion, T0);
            Assert.True(result.IsSuccess, result.Error?.Code);
        }

        if (lock_)
        {
            lobby.LockLineup(_host, Action(), T0);
            lobby.LockLineup(_guest, Action(), T0);
            Assert.NotNull(draft.Outcome);
        }

        return lobby;
    }

    private static ActionId Action() => ActionId.Create(Guid.NewGuid()).Value;
}
