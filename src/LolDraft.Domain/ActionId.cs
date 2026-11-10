namespace LolDraft.Domain;

/// <summary>
/// İstemcinin her hamleye eklediği kimlik (DESIGN.md §8.6): aynı kimlikle gelen ikinci istek
/// (çift tıklama, yeniden bağlanınca tekrar gönderme) hamleyi ikinci kez uygulamaz.
/// </summary>
public readonly record struct ActionId
{
    private ActionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static Result<ActionId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<ActionId>.Fail(LobbyErrors.InvalidActionId)
            : Result<ActionId>.Ok(new ActionId(value));

    public override string ToString() => Value.ToString();
}
