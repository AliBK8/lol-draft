namespace LolDraft.Domain;

/// <summary>Beklenen bir iş hatası (DESIGN.md §8.3). Kod istemciye gider, mesaj insan içindir.</summary>
public sealed record Error(string Code, string Message);

/// <summary>Değer döndürmeyen işlemin sonucu. Beklenen hatalar exception değil, <see cref="Error"/> taşır.</summary>
public sealed class Result
{
    private static readonly Result Success = new(null);

    private Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Ok() => Success;

    public static Result Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }
}

/// <summary>Başarılıysa bir değer, değilse <see cref="Error"/> taşır.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    /// <summary>Başarısız sonucun değerini okumak programcı hatasıdır.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Başarısız sonucun değeri yok: {Error!.Code}");

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
