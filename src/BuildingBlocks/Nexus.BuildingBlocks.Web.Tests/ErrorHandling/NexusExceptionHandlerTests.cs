using System.Text.Json;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.ErrorHandling;

public sealed class NexusExceptionHandlerTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusProblemDetails();

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext NewHttpContext(ServiceProvider provider) => new()
    {
        RequestServices = provider,
        Response = { Body = new MemoryStream() },
    };

    [Fact]
    public async Task TryHandleAsync_Maps_FluentValidation_Exception_To_400_With_Field_Errors()
    {
        using var provider = BuildServices();
        var handler = provider.GetRequiredService<IEnumerable<IExceptionHandler>>()
            .OfType<NexusExceptionHandler>()
            .Single();
        var httpContext = NewHttpContext(provider);

        var exception = new FluentValidation.ValidationException(new[]
        {
            new ValidationFailure("code", "'code' must not be empty."),
        });

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(400);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(
            httpContext.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = document.RootElement;

        root.GetProperty("status").GetInt32().ShouldBe(400);
        root.GetProperty("code").GetString().ShouldBe("VALIDATION");
        var errors = root.GetProperty("errors");
        errors.GetArrayLength().ShouldBe(1);
        errors[0].GetProperty("field").GetString().ShouldBe("code");
    }

    [Fact]
    public async Task TryHandleAsync_Maps_NotFoundException_To_404()
    {
        using var provider = BuildServices();
        var handler = provider.GetRequiredService<IEnumerable<IExceptionHandler>>()
            .OfType<NexusExceptionHandler>()
            .Single();
        var httpContext = NewHttpContext(provider);

        var handled = await handler.TryHandleAsync(
            httpContext,
            new NotFoundException("No existe un asset type con ese id."),
            CancellationToken.None);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(404);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(
            httpContext.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        document.RootElement.GetProperty("code").GetString().ShouldBe("NOT_FOUND");
    }

    [Fact]
    public async Task TryHandleAsync_Ignores_Unknown_Exceptions()
    {
        using var provider = BuildServices();
        var handler = provider.GetRequiredService<IEnumerable<IExceptionHandler>>()
            .OfType<NexusExceptionHandler>()
            .Single();
        var httpContext = NewHttpContext(provider);

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("boom"),
            CancellationToken.None);

        handled.ShouldBeFalse();
    }
}
