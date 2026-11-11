using Microsoft.AspNetCore.Components;
using QuarterHour.App.Services;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.App.Components.Shared;

/// <summary>A page that shows the selected period's timesheet and refreshes whenever anything changes.</summary>
public abstract class PeriodPageBase : ComponentBase, IDisposable
{
    [Inject]
    protected WorkspaceService Workspace { get; set; } = default!;

    [Inject]
    protected UiState UiState { get; set; } = default!;

    protected PeriodTimesheet? Sheet { get; private set; }
    protected string? LoadError { get; private set; }
    protected bool Loading { get; private set; }
    protected WorkspaceConfig Config => Workspace.Config;

    protected override async Task OnInitializedAsync()
    {
        Workspace.Changed += OnWorkspaceChanged;
        UiState.PeriodChanged += OnWorkspaceChanged;
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        Loading = true;
        try
        {
            Sheet = await Workspace.GetTimesheetAsync(UiState.Period);
            LoadError = null;
            await OnSheetLoadedAsync();
        }
        catch (Exception exception)
        {
            LoadError = exception.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    protected virtual Task OnSheetLoadedAsync() => Task.CompletedTask;

    protected Category? CategoryOf(string? id) => Workspace.Config.FindCategory(id);

    private void OnWorkspaceChanged() => InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    public virtual void Dispose()
    {
        Workspace.Changed -= OnWorkspaceChanged;
        UiState.PeriodChanged -= OnWorkspaceChanged;
        GC.SuppressFinalize(this);
    }
}
