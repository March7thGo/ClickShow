using System.Diagnostics;
using System.Globalization;

namespace ClickShow;

public enum MouseButton { Left, Middle, Right, Back, Forward }
public enum RippleEasing { Linear, EaseIn, EaseOut, EaseInOut }
public record ButtonSettings(bool Enabled, string Color);
public record Settings
{
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public bool Startup { get; init; }
    public bool Elevated { get; init; }
    public int Diameter { get; init; } = 100;
    public RippleEasing Easing { get; init; } = RippleEasing.Linear;
    public ButtonSettings[] Buttons { get; init; } = Defaults();
    public static ButtonSettings[] Defaults() => [new(true, "#3B82F6"), new(true, "#A855F7"), new(true, "#F97316"), new(true, "#22C55E"), new(true, "#EC4899")];
    public static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    public bool IsValid() => Version == 1 && Diameter is >= 50 and <= 200 && Diameter % 5 == 0 && Enum.IsDefined(Easing) && Buttons is { Length: 5 } && Buttons.All(b => b is not null && ValidColor(b.Color));
}

public readonly record struct RippleRequest(int X, int Y, long Timestamp, int Diameter, string Color, bool Small, RippleEasing Easing = RippleEasing.Linear)
{
    public int Duration => Small ? 200 : 400;
}

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static PixelRect Around(int x, int y, float extent) => new((int)Math.Floor(x - extent), (int)Math.Floor(y - extent), (int)Math.Ceiling(x + extent), (int)Math.Ceiling(y + extent));
    public PixelRect Intersect(PixelRect other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
}

// 只有按下、松开进入状态机，移动事件完全不参与判定。
public sealed class ClickTracker
{
    private readonly (bool Down, int X, int Y, long Time)[] pressed = new (bool, int, int, long)[5];
    public void Clear() => Array.Clear(pressed);
    public void Clear(int mask) { for (int i = 0; i < pressed.Length; i++) if ((mask & (1 << i)) != 0) pressed[i] = default; }
    public RippleRequest? Handle(MouseButton button, bool down, int x, int y, long now, Settings settings)
    {
        int index = (int)button;
        if (!settings.Enabled || !settings.Buttons[index].Enabled) { pressed[index] = default; return null; }
        if (down)
        {
            pressed[index] = (true, x, y, now);
            return new(x, y, now, settings.Diameter, settings.Buttons[index].Color, false, settings.Easing);
        }
        var start = pressed[index];
        pressed[index] = default;
        long dx = (long)x - start.X, dy = (long)y - start.Y;
        return start.Down && (Stopwatch.GetElapsedTime(start.Time, now).TotalMilliseconds > 500 || dx * dx + dy * dy > 200L * 200)
            ? new(x, y, now, settings.Diameter, settings.Buttons[index].Color, true, settings.Easing) : null;
    }
}

// 有界缓冲淘汰最旧输入；只在从空变为非空时唤醒绘制线程。
public sealed class RippleQueue
{
    private readonly Queue<RippleRequest> queue = new(100);
    public bool Push(RippleRequest item)
    {
        lock (queue)
        {
            bool wake = queue.Count == 0;
            if (queue.Count == 100) queue.Dequeue();
            queue.Enqueue(item);
            return wake;
        }
    }
    public bool TryPop(out RippleRequest item) { lock (queue) return queue.TryDequeue(out item); }
    public void Clear() { lock (queue) queue.Clear(); }
}
