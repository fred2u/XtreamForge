using Microsoft.FluentUI.AspNetCore.Components;

namespace XtreamForge.Web.Features.Dashboard;

/// <summary>A dashboard counter; <paramref name="Href"/> is null when no admin page manages it yet.</summary>
public sealed record DashboardMetric(string Label, int Value, Icon Icon, string? Href);

/// <summary>Dashboard counters displayed together under a title.</summary>
public sealed record DashboardMetricGroup(string Title, string Description, IReadOnlyList<DashboardMetric> Metrics);
