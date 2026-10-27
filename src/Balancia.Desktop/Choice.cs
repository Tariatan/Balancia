namespace Balancia.Desktop;

/// <summary>A list or combo item that shows and announces <paramref name="Label"/>; <c>ToString</c> is what screen readers read for a row.</summary>
internal sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
