namespace My.Client.Pages.Tyme;

/// <summary>Main Tasks page view: full list, week, or single-project week grid.</summary>
public enum TasksViewMode
{
    Grid,
    Weekly,
    Project
}

/// <summary>Week view sub-layout: classic list vs day columns (like Project view).</summary>
public enum WeeklyLayoutMode
{
    List,
    Day
}
