using QuarterHour.Core.Time;

namespace QuarterHour.App.Services;

/// <summary>Per-browser-tab state: which period is being looked at.</summary>
public sealed class UiState
{
    private readonly WorkspaceService _workspace;
    private TimesheetPeriod? _period;

    public UiState(WorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public event Action? PeriodChanged;

    public TimesheetPeriod Period => _period ?? _workspace.CurrentPeriod;

    public void SetPeriod(TimesheetPeriod period)
    {
        if (_period == period)
        {
            return;
        }

        _period = period;
        PeriodChanged?.Invoke();
    }

    public void ShowDate(DateOnly date)
    {
        if (!Period.Contains(date))
        {
            SetPeriod(_workspace.PeriodContaining(date));
        }
    }
}
