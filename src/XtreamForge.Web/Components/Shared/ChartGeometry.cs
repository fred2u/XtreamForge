using System.Globalization;
using System.Text;

namespace XtreamForge.Web.Components.Shared;

/// <summary>Pure computations of <see cref="LineChart"/>: the vertical scale and the SVG coordinates of a series.</summary>
public static class ChartGeometry
{
    /// <summary>Smallest "round" value (1, 2, 2.5 or 5 times a power of ten) greater than or equal to <paramref name="value"/>; at least 1.</summary>
    public static double NiceMaximum(double value)
    {
        if (value <= 1)
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in (ReadOnlySpan<double>)[1, 2, 2.5, 5, 10])
        {
            if (step * magnitude >= value)
                return step * magnitude;
        }

        return 10 * magnitude;
    }

    /// <summary>X coordinate of the point at <paramref name="index"/>, the points being spread over <paramref name="width"/>.</summary>
    public static double X(int index, int count, double width) =>
        count <= 1 ? width : index * width / (count - 1);

    /// <summary>Y coordinate of <paramref name="value"/>, 0 being at the bottom of a chart of <paramref name="height"/>.</summary>
    public static double Y(double value, double maximum, double height) =>
        height - (Math.Clamp(value, 0, maximum) / maximum * height);

    /// <summary>"x,y x,y ..." for the <c>points</c> attribute of an SVG polyline.</summary>
    public static string ToPoints(IReadOnlyList<double> values, double maximum, double width, double height)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
                builder.Append(' ');

            builder.Append(Format(X(index, values.Count, width))).Append(',').Append(Format(Y(values[index], maximum, height)));
        }

        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
