using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class WindowPlacementTests
{
    private static WindowPlacement Fit(WindowPlacement p) => p.Fit(0, 0, 1920, 1040, 460, 420);

    [Fact]
    public void A_placement_inside_the_area_is_kept()
    {
        var p = new WindowPlacement(100, 80, 560, 620);
        Assert.Equal(p, Fit(p));
    }

    [Fact]
    public void A_window_left_on_a_removed_monitor_is_moved_back_in()
    {
        var fitted = Fit(new WindowPlacement(3000, -500, 560, 620));
        Assert.Equal(new WindowPlacement(1360, 0, 560, 620), fitted);
    }

    [Fact]
    public void A_window_hanging_off_the_bottom_right_is_moved_up_and_left()
    {
        var fitted = Fit(new WindowPlacement(1800, 1000, 560, 620));
        Assert.Equal(1360, fitted.Left);
        Assert.Equal(420, fitted.Top);
    }

    [Fact]
    public void An_oversized_window_is_limited_to_the_area()
    {
        var fitted = Fit(new WindowPlacement(-50, -50, 5000, 5000));
        Assert.Equal(new WindowPlacement(0, 0, 1920, 1040), fitted);
    }

    [Fact]
    public void A_tiny_window_is_raised_to_the_minimum_size()
    {
        var fitted = Fit(new WindowPlacement(10, 10, 100, 100));
        Assert.Equal(460, fitted.Width);
        Assert.Equal(420, fitted.Height);
    }

    [Fact]
    public void A_smaller_work_area_wins_over_the_minimum_size()
    {
        var fitted = new WindowPlacement(0, 0, 560, 620).Fit(0, 0, 400, 300, 460, 420);
        Assert.Equal(new WindowPlacement(0, 0, 400, 300), fitted);
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100)]
    [InlineData(0, double.PositiveInfinity, 100, 100)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, -1)]
    public void Non_finite_or_empty_placements_are_unusable(double left, double top, double width, double height)
    {
        Assert.False(new WindowPlacement(left, top, width, height).IsUsable);
    }

    [Fact]
    public void A_normal_placement_is_usable()
    {
        Assert.True(new WindowPlacement(0, 0, 100, 100).IsUsable);
    }
}
