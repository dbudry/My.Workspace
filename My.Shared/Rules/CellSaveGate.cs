namespace My.Shared.Rules;

/// <summary>
/// One save at a time per grid cell. While a create is on the wire the cell has no
/// TaskId yet, so a second save (Enter, then blur) would decide "Create" again and add
/// a duplicate row. The second request waits here and runs once after the first ends.
/// </summary>
public sealed class CellSaveGate
{
    private bool inFlight;
    private bool resaveRequested;

    public bool InFlight => inFlight;

    /// <summary>
    /// Returns true when the caller may save now. Returns false when another save is
    /// running; that save will report <see cref="End"/> = true so the caller saves again.
    /// </summary>
    public bool TryBegin()
    {
        if (inFlight)
        {
            resaveRequested = true;
            return false;
        }

        inFlight = true;
        return true;
    }

    /// <summary>Marks the running save finished. True means a save was asked for meanwhile.</summary>
    public bool End()
    {
        inFlight = false;
        var again = resaveRequested;
        resaveRequested = false;
        return again;
    }
}
