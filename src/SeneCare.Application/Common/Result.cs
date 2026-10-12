using System.Diagnostics.CodeAnalysis;

namespace SeneCare.Application.Common;

/// <summary>
/// Expected failure of a use case. <see cref="Code"/> is a stable key translated by the UI
/// (resource « Error_{Code} »), e.g. « otp.cooldown ».
/// </summary>
public sealed record AppError(string Code, DateTimeOffset? RetryAt = null);

public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public AppError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

#pragma warning disable CA1000 // Factory methods on a generic result type are the clearest API here.
    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(AppError error) => new(default, error);

    public static Result<T> Failure(string code) => new(default, new AppError(code));
#pragma warning restore CA1000

    public static implicit operator Result<T>(AppError error) => Failure(error);
}
