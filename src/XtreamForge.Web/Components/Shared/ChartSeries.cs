namespace XtreamForge.Web.Components.Shared;

/// <summary>A series of <see cref="LineChart"/>; <see cref="CssClass"/> sets its color through <c>--chart-series-color</c>.</summary>
public sealed record ChartSeries(string Name, string CssClass, IReadOnlyList<double> Values);
