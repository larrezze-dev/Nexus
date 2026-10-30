using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.Locations;

namespace Nexus.Platform.Application.Locations.CreateLocation;

/// <summary>
/// <c>POST /v1/locations</c> (<c>createLocation</c>): idempotent create (client-supplied
/// UUIDv7 <see cref="Id"/>); identical content on a reused id replays (200), different content
/// conflicts (409). No <c>Party</c> involved (Location is not a party).
/// </summary>
public sealed record CreateLocationCommand(
    Guid? Id,
    string? Code,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Region,
    string CountryCode) : ICommand<CreateLocationResult>;

/// <summary>The location and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateLocationResult(LocationDetails Location, bool Created);

internal sealed class CreateLocationValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.Name).MustBeNonBlank();
        RuleFor(c => c.CountryCode).MustBeNonBlank().Matches("^[A-Z]{2}$").WithMessage("'countryCode' must be an ISO 3166-1 alpha-2 code.");
    }
}

internal sealed class CreateLocationHandler : ICommandHandler<CreateLocationCommand, CreateLocationResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILocationRepository _locations;
    private readonly IIdGenerator _ids;

    public CreateLocationHandler(IRequestTransaction transaction, ILocationRepository locations, IIdGenerator ids)
    {
        _transaction = transaction;
        _locations = locations;
        _ids = ids;
    }

    public async Task<Result<CreateLocationResult>> Handle(CreateLocationCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _locations.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var locationResult = Location.Create(
            command.Id ?? _ids.NewId(),
            command.Code,
            command.Name,
            command.Street,
            command.PostalCode,
            command.City,
            command.Region,
            command.CountryCode);
        if (locationResult.IsFailure)
        {
            return Result.Failure<CreateLocationResult>(locationResult.Error!);
        }

        var location = locationResult.Value;
        _locations.Add(location);

        // The code's uniqueness is enforced by the database at this point; its violation is
        // translated by the web layer (LocationErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateLocationResult(
            LocationDetails.From(location, _locations.GetMetadata(location)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateLocationResult>> ReplayAsync(
        CreateLocationCommand command, Location existing, CancellationToken cancellationToken)
    {
        var identical =
            string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && string.Equals(existing.Name, command.Name, StringComparison.Ordinal)
            && string.Equals(existing.Street, command.Street, StringComparison.Ordinal)
            && string.Equals(existing.PostalCode, command.PostalCode, StringComparison.Ordinal)
            && string.Equals(existing.City, command.City, StringComparison.Ordinal)
            && string.Equals(existing.Region, command.Region, StringComparison.Ordinal)
            && string.Equals(existing.CountryCode, command.CountryCode, StringComparison.Ordinal);

        if (!identical)
        {
            return Result.Failure<CreateLocationResult>(LocationErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateLocationResult(
            LocationDetails.From(existing, _locations.GetMetadata(existing)), Created: false));
    }
}
