using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CellSaveGateTests
{
    [Fact]
    public void First_save_may_start()
    {
        var gate = new CellSaveGate();

        Assert.True(gate.TryBegin());
        Assert.True(gate.InFlight);
    }

    [Fact]
    public void Second_save_waits_while_the_first_is_in_flight()
    {
        // Enter saves at once, then blur asks again before the create has replied.
        var gate = new CellSaveGate();
        gate.TryBegin();

        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void End_reports_that_a_save_was_requested_meanwhile()
    {
        var gate = new CellSaveGate();
        gate.TryBegin();
        gate.TryBegin();

        Assert.True(gate.End());
        Assert.False(gate.InFlight);
    }

    [Fact]
    public void End_reports_nothing_when_no_second_save_was_asked_for()
    {
        var gate = new CellSaveGate();
        gate.TryBegin();

        Assert.False(gate.End());
    }

    [Fact]
    public void Gate_opens_again_after_end_and_forgets_the_old_request()
    {
        var gate = new CellSaveGate();
        gate.TryBegin();
        gate.TryBegin();
        gate.End();

        Assert.True(gate.TryBegin());
        Assert.False(gate.End());
    }
}
