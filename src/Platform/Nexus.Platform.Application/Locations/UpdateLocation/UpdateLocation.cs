using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.Locations.UpdateLocation;

/// <summary><c>PUT /v1/locations/{id}</c> (<c>updateLocation</c>), full replacement guarded by <c>If-Match</c> (<see cref="ExpectedVersion"/>).</summary>
public sealed record UpdateLocationCommand(
    Guid Id,
    uint ExpectedVersion,
    string? Code,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Region,
    string CountryCode) : ICommand<LocationDetails>;

internal sealed class UpdateLocationValidator : AbstractValidator<UpdateLocationCommand>
{
    public UpdateLocationValidator()
    {
        RuleFor(c => c.Name).MustBeNonBlank();
        RuleFor(c => c.CountryCode).MustBeNonBlank().Matches("^[A-Z]{2}$").WithMessage("'countryCode' must be an ISO 3166-1 alpha-2 code.");
    }
}

internal sealed class UpdateLocationHandler : ICommandHandler<UpdateLocationCommand, LocationDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILocationRepository _locations;

    public UpdateLocationHandler(IRequestTransaction transaction, ILocationRepository locations)
    {
        _transaction = transaction;
        _locations = locations;
    }

    public async Task<Result<LocationDetails>> Handle(UpdateLocationCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _locations.GetByIdAsync,
            _locations.GetMetadata,
            command.Id,
            command.ExpectedVersion,
            LocationErrors.NotFound(command.Id),
            LocationErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<LocationDetails>(loaded.Error!);
        }

        var location = loaded.Value;
        location.Update(command.Code, command.Name, command.Street, command.PostalCode, command.City, command.Region, command.CountryCode);

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(LocationDetails.From(location, _locations.GetMetadata(location)));
    }
}
