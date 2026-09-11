using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Pages.Categories;

[ValidateAntiForgeryToken]
public sealed class IndexModel(XtreamCategoryMappingService categoryMappingService) : PageModel
{
    public CategoryAdministrationView ViewModel { get; private set; } =
        new([], null, ContentType.Vod, [], []);

    public async Task OnGetAsync(int? sourceId, ContentType contentType = ContentType.Vod, CancellationToken cancellationToken = default)
    {
        ViewModel = await categoryMappingService.GetAdministrationViewAsync(sourceId, contentType, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CategoryConfigurationCommand command, CancellationToken cancellationToken)
    {
        var result = await categoryMappingService.SaveCategoryConfigurationAsync(command, cancellationToken);

        return RedirectToPage(new
        {
            sourceId = result.SourceId,
            contentType = result.ContentType
        });
    }
}
