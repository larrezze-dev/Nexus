using System.Numerics;

namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// Guards against technical invariant violations (not business rules - those use
/// <see cref="Result"/>/<see cref="Error"/>). These represent programming errors, so they
/// throw instead of returning a <see cref="Result"/>.
/// </summary>
public static class Guard
{
    public static IGuardClause Against { get; } = new GuardClause();

    private sealed class GuardClause : IGuardClause
    {
        public T Null<T>(T? value, string parameterName)
            where T : class
        {
            if (value is null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return value;
        }

        public string NullOrWhiteSpace(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName);
            }

            return value;
        }

        public TNumber NegativeOrZero<TNumber>(TNumber value, string parameterName)
            where TNumber : struct, INumber<TNumber>
        {
            if (value <= TNumber.Zero)
            {
                throw new ArgumentException("Value must be greater than zero.", parameterName);
            }

            return value;
        }

        public TNumber Negative<TNumber>(TNumber value, string parameterName)
            where TNumber : struct, INumber<TNumber>
        {
            if (value < TNumber.Zero)
            {
                throw new ArgumentException("Value must not be negative.", parameterName);
            }

            return value;
        }
    }
}

/// <summary>The guard clauses exposed by <see cref="Guard.Against"/>.</summary>
public interface IGuardClause
{
    /// <summary>Throws <see cref="ArgumentNullException"/> if <paramref name="value"/> is null.</summary>
    T Null<T>(T? value, string parameterName)
        where T : class;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if <paramref name="value"/> is null, empty, or
    /// whitespace-only.
    /// </summary>
    string NullOrWhiteSpace(string? value, string parameterName);

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="value"/> is zero or negative.</summary>
    TNumber NegativeOrZero<TNumber>(TNumber value, string parameterName)
        where TNumber : struct, INumber<TNumber>;

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="value"/> is negative.</summary>
    TNumber Negative<TNumber>(TNumber value, string parameterName)
        where TNumber : struct, INumber<TNumber>;
}
