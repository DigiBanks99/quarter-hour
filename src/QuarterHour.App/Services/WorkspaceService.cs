using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;
using QuarterHour.Infrastructure.Agents;
using QuarterHour.Infrastructure.Calendar;
using QuarterHour.Infrastructure.Git;
using QuarterHour.Infrastructure.Storage;

namespace QuarterHour.App.Services;

/// <summary>
/// The app's single source of truth: the configuration and journal, the cached calendar, agent and commit scans per period,
/// and the timesheets built from them. Pages edit through it and listen to <see cref="Changed"/>.
/// </summary>
public sealed class WorkspaceService
{
    private readonly WorkspaceStore _store;
    private readonly CalendarCache _calendarCache;
    private readonly AgentSessionScanner _agentScanner;
    private readonly ICommitSource _commits;
    private readonly GraphAuthService _graphAuth;
    private readonly IHttpClientFactory _httpClients;
    private readonly TimeProvider _time;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private readonly Dictionary<string, AgentScanSummary> _agentScans = [];
    private readonly Dictionary<string, CommitScanResult> _commitScans = [];
    private readonly Dictionary<string, PeriodTimesheet> _sheets = [];
    private WorkspaceConfig _config;
    private Journal _journal;
    private long _version;

    public WorkspaceService(WorkspaceStore store, CalendarCache calendarCache, AgentSessionScanner agentScanner, ICommitSource commits,
        GraphAuthService graphAuth, IHttpClientFactory httpClients, TimeProvider time, ILogger<WorkspaceService> logger, IConfiguration configuration)
    {
        _store = store;
        _calendarCache = calendarCache;
        _agentScanner = agentScanner;
        _commits = commits;
        _graphAuth = graphAuth;
        _httpClients = httpClients;
        _time = time;
        _logger = logger;
        _config = store.LoadConfig();
        _journal = store.LoadJournal();
        ApplyGraphDefaults(_config.Calendar.Graph, configuration.GetSection("QuarterHour:Graph"));
    }

    /// <summary>
    /// Fills the Graph app registration from configuration (e.g. environment variables the AppHost injects) when
    /// Settings → Calendar hasn't set it. Values saved in the workspace win.
    /// </summary>
    private static void ApplyGraphDefaults(GraphSettings graph, IConfiguration section)
    {
        if (string.IsNullOrWhiteSpace(graph.ClientId) && !string.IsNullOrWhiteSpace(section["ClientId"]))
        {
            graph.ClientId = section["ClientId"]!.Trim();
        }

        if ((string.IsNullOrWhiteSpace(graph.TenantId) || graph.TenantId == "organizations") && !string.IsNullOrWhiteSpace(section["TenantId"]))
        {
            graph.TenantId = section["TenantId"]!.Trim();
        }
    }

    /// <summary>Raised after anything changes: configuration, journal, calendar sync or agent scan.</summary>
    public event Action? Changed;

    /// <summary>The live configuration. Edit it, then call <see cref="SaveConfig"/>.</summary>
    public WorkspaceConfig Config => _config;

    public Journal Journal => _journal;
    public string DataDirectory => _store.DataRoot;
    public GraphAuthService GraphAuth => _graphAuth;
    public long Version => Interlocked.Read(ref _version);

    public TimeZoneInfo TimeZone => _config.General.ResolveTimeZone();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZone).DateTime);

    public TimesheetPeriod PeriodContaining(DateOnly date) => TimesheetPeriod.Containing(date, _config.General.PeriodStartDay);

    public TimesheetPeriod CurrentPeriod => PeriodContaining(Today);

    public IReadOnlyList<Category> ActiveCategories =>
        [.. _config.Categories.Where(category => !category.Archived).OrderBy(category => category.Group).ThenBy(category => category.Name)];

    public WorkspaceConfig ConfigSnapshot()
    {
        lock (_gate)
        {
            return JsonFiles.Parse<WorkspaceConfig>(JsonFiles.Serialize(_config))!.Normalize();
        }
    }

    public void SaveConfig()
    {
        lock (_gate)
        {
            _config.Normalize();
            _store.SaveConfig(_config);
        }

        Invalidate();
    }

    public void UpdateConfig(Action<WorkspaceConfig> change)
    {
        lock (_gate)
        {
            change(_config);
            _config.Normalize();
            _store.SaveConfig(_config);
        }

        Invalidate();
    }

    public void ReplaceConfig(WorkspaceConfig config)
    {
        lock (_gate)
        {
            _config = config.Normalize();
            _store.SaveConfig(_config);
        }

        Invalidate();
    }

    public void UpdateJournal(Action<Journal> change)
    {
        lock (_gate)
        {
            change(_journal);
            foreach (string key in _journal.Overrides.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList())
            {
                _journal.Overrides.Remove(key);
            }

            foreach (string key in _journal.Days.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList())
            {
                _journal.Days.Remove(key);
            }

            foreach (string key in _journal.Lines.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList())
            {
                _journal.Lines.Remove(key);
            }

            _store.SaveJournal(_journal);
        }

        Invalidate();
    }

    public async Task<PeriodTimesheet> GetTimesheetAsync(TimesheetPeriod period, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_sheets.TryGetValue(period.Key, out PeriodTimesheet? cached) && cached.Period == period)
            {
                return cached;
            }
        }

        return await Telemetry.TraceAsync("timesheet build", async activity =>
        {
            activity?.SetTag("quarterhour.period", period.Key);
            return await BuildTimesheetAsync(period, cancellationToken);
        });
    }

    private async Task<PeriodTimesheet> BuildTimesheetAsync(TimesheetPeriod period, CancellationToken cancellationToken)
    {
        await _buildLock.WaitAsync(cancellationToken);
        try
        {
            AgentScanSummary agents = await AgentScanAsync(period, force: false, cancellationToken);
            CommitScanResult commits = await CommitScanAsync(period, agents, force: false, cancellationToken);
            WorkspaceConfig config;
            Journal journal;
            long version;
            lock (_gate)
            {
                if (_sheets.TryGetValue(period.Key, out PeriodTimesheet? cached) && cached.Period == period)
                {
                    return cached;
                }

                config = JsonFiles.Parse<WorkspaceConfig>(JsonFiles.Serialize(_config))!.Normalize();
                journal = JsonFiles.Parse<Journal>(JsonFiles.Serialize(_journal))!.Normalize();
                version = _version;
            }

            TimeZoneInfo timeZone = config.General.ResolveTimeZone();
            List<Activity> activities = [];
            CalendarSnapshot? calendar = _calendarCache.Load(period.Key);
            if (calendar is not null)
            {
                activities.AddRange(calendar.Events.Select(calendarEvent => CalendarActivities.ToActivity(calendarEvent, timeZone)));
            }

            TimeSpan idleGap = TimeSpan.FromMinutes(Math.Max(1, config.Agents.IdleGapMinutes));
            TimeSpan padding = TimeSpan.FromMinutes(Math.Max(0, config.Agents.PaddingMinutes));
            IReadOnlyDictionary<string, string> friendlyNames = config.FriendlyProjectNames();
            List<Activity> agentWork = [.. agents.Sessions.SelectMany(session => AgentActivities.ToActivities(session, idleGap, padding, timeZone, friendlyNames))];
            activities.AddRange(config.Agents.DescribeWithCommits
                ? CandidateAttribution.Attribute(agentWork, commits.Commits, idleGap, timeZone).Activities
                : agentWork);

            PeriodTimesheet sheet = new TimesheetBuilder().Build(period, config, journal, activities, Today);
            lock (_gate)
            {
                if (version == _version)
                {
                    _sheets[period.Key] = sheet;
                }
            }

            return sheet;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public CalendarSnapshot? CalendarSnapshot(TimesheetPeriod period) => _calendarCache.Load(period.Key);

    public AgentScanSummary? LastAgentScan(TimesheetPeriod period)
    {
        lock (_gate)
        {
            return _agentScans.GetValueOrDefault(period.Key);
        }
    }

    /// <summary>The period's last commit scan, or null before the first timesheet build.</summary>
    public CommitScanResult? LastCommitScan(TimesheetPeriod period)
    {
        lock (_gate)
        {
            return _commitScans.GetValueOrDefault(period.Key);
        }
    }

    public async Task<AgentScanSummary> RescanAgentsAsync(TimesheetPeriod period, CancellationToken cancellationToken = default)
    {
        AgentScanSummary summary = await AgentScanAsync(period, force: true, cancellationToken);
        await CommitScanAsync(period, summary, force: true, cancellationToken);
        Invalidate();
        return summary;
    }

    /// <summary>Fetches the period's events from the configured calendar and caches them.</summary>
    public Task<CalendarSnapshot> SyncCalendarAsync(TimesheetPeriod period, bool interactive, Func<string, Task>? deviceCode, CancellationToken cancellationToken = default) =>
        Telemetry.TraceAsync("calendar sync", async activity =>
        {
            activity?.SetTag("quarterhour.period", period.Key);
            activity?.SetTag("quarterhour.calendar.interactive", interactive);
            CalendarSnapshot snapshot = await FetchCalendarAsync(period, interactive, deviceCode, cancellationToken);
            activity?.SetTag("quarterhour.calendar.source", snapshot.Source);
            activity?.SetTag("quarterhour.calendar.events", snapshot.Events.Count);
            return snapshot;
        });

    private async Task<CalendarSnapshot> FetchCalendarAsync(TimesheetPeriod period, bool interactive, Func<string, Task>? deviceCode, CancellationToken cancellationToken)
    {
        WorkspaceConfig config = ConfigSnapshot();
        (DateTimeOffset from, DateTimeOffset to) = Range(period, config.General.ResolveTimeZone());
        ICalendarProvider provider = config.Calendar.Provider switch
        {
            CalendarProviderKind.MicrosoftGraph => new GraphCalendarProvider(
                _httpClients.CreateClient(QuarterHourSetup.GraphClient),
                token => _graphAuth.GetAccessTokenAsync(config.Calendar.Graph, interactive, deviceCode, token)),
            CalendarProviderKind.OutlookDesktop => new OutlookCalendarProvider(),
            _ => throw new InvalidOperationException("No calendar source is set up. Choose one in Settings → Calendar, or import an .ics file."),
        };

        IReadOnlyList<CalendarEvent> events = await provider.GetEventsAsync(from, to, cancellationToken);
        CalendarSnapshot snapshot = new()
        {
            FetchedAt = _time.GetUtcNow(),
            Source = provider.Kind == CalendarProviderKind.MicrosoftGraph ? "Microsoft Graph" : "Outlook",
            Events = [.. events],
        };
        _calendarCache.Save(period.Key, snapshot);
        _logger.LogInformation("Synced {Count} calendar events for {Period} from {Source}", events.Count, period.Key, snapshot.Source);
        Invalidate();
        return snapshot;
    }

    public CalendarSnapshot ImportIcs(TimesheetPeriod period, string ics, string fileName) =>
        Telemetry.Trace("calendar import", activity =>
        {
            activity?.SetTag("quarterhour.period", period.Key);
            activity?.SetTag("quarterhour.calendar.file", fileName);
            (DateTimeOffset from, DateTimeOffset to) = Range(period, TimeZone);
            IReadOnlyList<CalendarEvent> events = IcsCalendarReader.Read(ics, from, to);
            CalendarSnapshot snapshot = new() { FetchedAt = _time.GetUtcNow(), Source = $"Imported {fileName}", Events = [.. events] };
            _calendarCache.Save(period.Key, snapshot);
            activity?.SetTag("quarterhour.calendar.events", events.Count);
            Invalidate();
            return snapshot;
        });

    public void ClearCalendar(TimesheetPeriod period)
    {
        _calendarCache.Delete(period.Key);
        Invalidate();
    }

    public static (DateTimeOffset From, DateTimeOffset To) Range(TimesheetPeriod period, TimeZoneInfo timeZone)
    {
        DateTime start = period.Start.ToDateTime(TimeOnly.MinValue);
        DateTime end = period.End.ToDateTime(TimeOnly.MinValue);
        return (new DateTimeOffset(start, timeZone.GetUtcOffset(start)), new DateTimeOffset(end, timeZone.GetUtcOffset(end)));
    }

    private async Task<AgentScanSummary> AgentScanAsync(TimesheetPeriod period, bool force, CancellationToken cancellationToken)
    {
        if (!force)
        {
            lock (_gate)
            {
                if (_agentScans.TryGetValue(period.Key, out AgentScanSummary? cached))
                {
                    return cached;
                }
            }
        }

        return await Telemetry.TraceAsync("agent scan", async activity =>
        {
            activity?.SetTag("quarterhour.period", period.Key);
            WorkspaceConfig config = ConfigSnapshot();
            TimeZoneInfo timeZone = config.General.ResolveTimeZone();
            (DateTimeOffset from, DateTimeOffset to) = Range(period, timeZone);
            AgentScanSummary summary = await _agentScanner.ScanAsync(new AgentScanRequest(from, to, config.Agents, timeZone), cancellationToken);
            foreach (string problem in summary.Problems)
            {
                _logger.LogWarning("Agent scan: {Problem}", problem);
            }

            activity?.SetTag("quarterhour.agents.sessions", summary.Sessions.Count);
            activity?.SetTag("quarterhour.agents.problems", summary.Problems.Count);
            lock (_gate)
            {
                _agentScans[period.Key] = summary;
            }

            return summary;
        });
    }

    /// <summary>Reads the commits in the projects the period's agent sessions worked in, once per period unless forced.</summary>
    private async Task<CommitScanResult> CommitScanAsync(TimesheetPeriod period, AgentScanSummary agents, bool force, CancellationToken cancellationToken)
    {
        WorkspaceConfig config = ConfigSnapshot();
        if (!config.Agents.DescribeWithCommits)
        {
            return CommitScanResult.Empty;
        }

        if (!force)
        {
            lock (_gate)
            {
                if (_commitScans.TryGetValue(period.Key, out CommitScanResult? cached))
                {
                    return cached;
                }
            }
        }

        return await Telemetry.TraceAsync("commit scan", async activity =>
        {
            activity?.SetTag("quarterhour.period", period.Key);
            (DateTimeOffset from, DateTimeOffset to) = Range(period, config.General.ResolveTimeZone());
            List<ProjectFolders> projects = agents.Sessions
                .Where(session => !string.IsNullOrWhiteSpace(session.ProjectPath))
                .GroupBy(session => ProjectNaming.Key(session.ProjectPath, session.Repository))
                .Select(group => new ProjectFolders(group.Key, [.. group.Select(session => session.ProjectPath!).Distinct(StringComparer.OrdinalIgnoreCase)]))
                .ToList();
            CommitScanResult result;
            try
            {
                result = await _commits.ScanAsync(new CommitScanRequest(from, to, projects, config.Agents.CommitEmails), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new CommitScanResult([], [], [exception.Message]);
            }

            foreach (string problem in result.Problems)
            {
                _logger.LogWarning("Commit scan: {Problem}", problem);
            }

            activity?.SetTag("quarterhour.commits.projects", projects.Count);
            activity?.SetTag("quarterhour.commits.found", result.Commits.Count);
            lock (_gate)
            {
                _commitScans[period.Key] = result;
            }

            return result;
        });
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _sheets.Clear();
            _version++;
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A listener failed while handling a change.");
        }
    }

    /// <summary>Forgets scans made with old agent settings, so the next build rescans.</summary>
    public void ForgetAgentScans()
    {
        lock (_gate)
        {
            _agentScans.Clear();
            _commitScans.Clear();
        }

        Invalidate();
    }
}
