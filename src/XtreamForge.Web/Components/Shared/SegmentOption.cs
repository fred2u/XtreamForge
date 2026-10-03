using Microsoft.FluentUI.AspNetCore.Components;

namespace XtreamForge.Web.Components.Shared;

public sealed record SegmentOption<TValue>(TValue Value, string Label, Icon? Icon = null);
