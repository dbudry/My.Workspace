namespace My.Client.Components.Layout;

/// <summary>One option in a <see cref="SegmentedTabBar{TValue}"/> (Tasks All/Week/Project, Stopwatch Work items/Day).</summary>
public readonly record struct SegmentedTab<TValue>(TValue Value, string Label);
