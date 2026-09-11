using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Pages.Categories;

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
        await categoryMappingService.SaveCategoryConfigurationAsync(command, cancellationToken);

        return RedirectToPage(new
        {
            sourceId = command.SelectedSourceId,
            contentType = command.SelectedContentType
        });
    }
}
