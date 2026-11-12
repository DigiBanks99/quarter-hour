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

    public TimesheetPeriod Period
    {
        get
        {
            TimesheetPeriod period = _period ?? _workspace.CurrentPeriod;
            int startDay = _workspace.Config.General.PeriodStartDay;
            return period.StartDay == startDay ? period : new TimesheetPeriod(period.Year, period.Month, startDay);
        }
    }

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
