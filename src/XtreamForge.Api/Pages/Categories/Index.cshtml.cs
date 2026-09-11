using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Pages.Categories;

[ValidateAntiForgeryToken]
public sealed class IndexModel(
    XtreamCategoryMappingService categoryMappingService,
    CategoryRuleService categoryRuleService) : PageModel
{
    public CategoryPageViewModel ViewModel { get; private set; } =
        new(new([], null, ContentType.Vod, [], []), new([], null, null));

    [BindProperty]
    public CategoryRuleEditorCommand NewRule { get; set; } =
        new(null, 0, ContentType.Vod, CategoryRuleAction.Include, CategoryRuleOperator.Contains, null, false, true);

    public async Task OnGetAsync(
        int? sourceId,
        ContentType contentType = ContentType.Vod,
        string? previewCategoryName = null,
        CancellationToken cancellationToken = default)
    {
        await LoadViewModelAsync(sourceId, contentType, previewCategoryName, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CategoryConfigurationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryMappingService.SaveCategoryConfigurationAsync(command, cancellationToken);

            return RedirectToPage(new
            {
                sourceId = result.SourceId,
                contentType = result.ContentType
            });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCreateRuleAsync(CategoryRuleEditorCommand command, CancellationToken cancellationToken)
    {
        if (!ValidateRuleCommand(command))
        {
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            NewRule = command;
            return Page();
        }

        try
        {
            var result = await categoryRuleService.CreateRuleAsync(command, cancellationToken);
            return RedirectToPage(new { sourceId = result.SourceId, contentType = result.ContentType });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            NewRule = command;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateRuleAsync(CategoryRuleEditorCommand command, CancellationToken cancellationToken)
    {
        if (!ValidateRuleCommand(command))
        {
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }

        try
        {
            var result = await categoryRuleService.UpdateRuleAsync(command, cancellationToken);
            return RedirectToPage(new { sourceId = result.SourceId, contentType = result.ContentType });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteRuleAsync(CategoryRuleDeleteCommand command, CancellationToken cancellationToken)
    {
        if (!command.ConfirmDelete)
        {
            ModelState.AddModelError(string.Empty, "Confirm delete before removing a rule.");
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }

        try
        {
            var result = await categoryRuleService.DeleteRuleAsync(command, cancellationToken);
            return RedirectToPage(new { sourceId = result.SourceId, contentType = result.ContentType });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMoveRuleUpAsync(CategoryRuleIdentityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.MoveRuleAsync(command, CategoryRuleMoveDirection.Up, cancellationToken);
            return RedirectToPage(new { sourceId = result.SourceId, contentType = result.ContentType });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMoveRuleDownAsync(CategoryRuleIdentityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.MoveRuleAsync(command, CategoryRuleMoveDirection.Down, cancellationToken);
            return RedirectToPage(new { sourceId = result.SourceId, contentType = result.ContentType });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadViewModelAsync(command.SelectedSourceId, command.SelectedContentType, null, cancellationToken);
            return Page();
        }
    }

    private async Task LoadViewModelAsync(int? sourceId, ContentType contentType, string? previewCategoryName, CancellationToken cancellationToken)
    {
        var categoryView = await categoryMappingService.GetAdministrationViewAsync(sourceId, contentType, cancellationToken);
        var ruleView = await categoryRuleService.GetAdministrationViewAsync(categoryView.SelectedSourceId, contentType, previewCategoryName, cancellationToken);
        ViewModel = new CategoryPageViewModel(categoryView, ruleView);

        if (categoryView.SelectedSourceId is int selectedSourceId)
        {
            NewRule = NewRule with { SelectedSourceId = selectedSourceId, SelectedContentType = contentType };
        }
    }

    private bool ValidateRuleCommand(CategoryRuleEditorCommand command)
    {
        if (command.SelectedSourceId <= 0)
        {
            ModelState.AddModelError(string.Empty, "A source must be selected.");
        }

        if (string.IsNullOrWhiteSpace(command.Pattern))
        {
            ModelState.AddModelError(nameof(command.Pattern), "Pattern is required.");
        }
        else if (command.Pattern.Trim().Length > CategoryRuleService.MaxPatternLength)
        {
            ModelState.AddModelError(nameof(command.Pattern), $"Pattern must be {CategoryRuleService.MaxPatternLength} characters or fewer.");
        }

        return ModelState.IsValid;
    }
}

public sealed record CategoryPageViewModel(
    CategoryAdministrationView Categories,
    CategoryRulesAdministrationView Rules);
