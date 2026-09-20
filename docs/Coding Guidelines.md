# Coding guidelines

Apply these coding guidelines when writing or modifying C# in this
repository, while keeping the existing behavior and the project's architecture
and financial rules intact.

## Layout and control flow

- Put object initializers with multiple properties on separate lines. Place the
  opening brace on the next line, write one property per line, and keep the
  closing brace on its own line.
- Use a braced block for an `if` body, including a single statement. Do not
  compress a condition and its body onto one line.
- Separate setup, conditional blocks, calculated values, and loops with blank
  lines when that makes the method easier to scan.
- In nested UI construction, give each control and its properties enough space
  to read without scanning across a long line.

For example:

```csharp
var body = new StackPanel
{
    Spacing = 1
};

if (items.Count == 0)
{
    body.Children.Add(QuietText("No items.", 12));
}
```

