using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Admin.Sources;

public class SourcesPostEndpoint(SourceAdminService sourceAdminService, XtreamCategoryDiscoveryAdminService xtreamCategoryDiscoveryAdminService, XtreamProviderValidator xtreamProviderValidator)
{
    public async Task<IResult> PostAsync(XtreamSourceCreateRequest request, CancellationToken cancellationToken = default)
    {
        var errors = Validate(request, out var uri);
        if (errors.Count > 0 || uri is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var protocol = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();

        if (await sourceAdminService.ExistsAsync(protocol, host, uri.Port, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        // username and password are only used to discover the categories, they are not persisted
        var source = await xtreamCategoryDiscoveryAdminService.DiscoverAsync(protocol, host, uri.Port, request.Username, request.Password, cancellationToken);
        if (source is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status502BadGateway);
        }

        var dto = new XtreamSourceDto(source.Id, source.Protocol, source.Host, source.Port);

        return TypedResults.Created($"/api/admin/sources/{source.Id}", dto);
    }

    private Dictionary<string, string[]> Validate(XtreamSourceCreateRequest request, out Uri? uri)
    {
        var errors = new Dictionary<string, string[]>();

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out uri))
        {
            errors[nameof(XtreamSourceCreateRequest.Url)] = ["Url must be an absolute http or https URL."];
        }
        else
        {
            var providerValidationResult = xtreamProviderValidator.Validate(uri.Scheme, uri.Host, uri.Port);
            if (providerValidationResult.Error is not null)
            {
                errors[nameof(XtreamSourceCreateRequest.Url)] = [providerValidationResult.Error];
            }
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors[nameof(XtreamSourceCreateRequest.Username)] = ["Username is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(XtreamSourceCreateRequest.Password)] = ["Password is required."];
        }

        return errors;
    }
}
