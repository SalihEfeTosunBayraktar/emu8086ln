using Emu8086.Core.Editing;

namespace Emu8086.Tests;

public class AutoSavePolicyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    private static EditStats Edit(int chars, int lineBreaks, DateTime at)
    {
        var stats = new EditStats();
        stats.Record(chars, 0, lineBreaks, at);
        return stats;
    }

    [Fact]
    public void OffNeverSaves()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Off };
        Assert.False(policy.ShouldSave(Edit(500, 5, T0), T0.AddHours(1), AutoSaveTrigger.FocusLost));
    }

    [Fact]
    public void NoChangesNeverSaves()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Smart };
        Assert.False(policy.ShouldSave(new EditStats(), T0.AddHours(1), AutoSaveTrigger.FocusLost));
    }

    [Fact]
    public void IntervalSavesOnlyAfterTheInterval()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Interval, Interval = TimeSpan.FromSeconds(30) };
        var stats = Edit(1, 0, T0);
        Assert.False(policy.ShouldSave(stats, T0.AddSeconds(29), AutoSaveTrigger.Tick));
        Assert.True(policy.ShouldSave(stats, T0.AddSeconds(30), AutoSaveTrigger.Tick));
    }

    [Fact]
    public void SmartWaitsWhileTyping()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Smart };
        var stats = Edit(100, 3, T0);
        Assert.False(policy.ShouldSave(stats, T0.AddSeconds(1), AutoSaveTrigger.Tick));
        Assert.True(policy.ShouldSave(stats, T0.AddSeconds(2), AutoSaveTrigger.Tick));
    }

    [Fact]
    public void SmartSavesLineChangesButNotTinyEditsUntilTheyAge()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Smart, Interval = TimeSpan.FromSeconds(60) };
        Assert.True(policy.ShouldSave(Edit(1, 1, T0), T0.AddSeconds(3), AutoSaveTrigger.Tick));   // new line
        var tiny = Edit(3, 0, T0);
        Assert.False(policy.ShouldSave(tiny, T0.AddSeconds(3), AutoSaveTrigger.Tick));           // 3 chars, recent
        Assert.True(policy.ShouldSave(tiny, T0.AddSeconds(60), AutoSaveTrigger.Tick));           // old enough
        Assert.True(policy.ShouldSave(Edit(40, 0, T0), T0.AddSeconds(3), AutoSaveTrigger.Tick)); // big enough
    }

    [Fact]
    public void SmartSavesImmediatelyWhenFocusLeaves()
    {
        var policy = new AutoSavePolicy { Mode = AutoSaveMode.Smart };
        Assert.True(policy.ShouldSave(Edit(1, 0, T0), T0, AutoSaveTrigger.FocusLost));
    }
}
