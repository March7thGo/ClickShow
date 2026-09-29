using ClickShow;
using System.Diagnostics;
using System.Text.Json;

int checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
long At(int ms) => (long)(ms * Stopwatch.Frequency / 1000d);
var settings = new Settings();
var tracker = new ClickTracker();
foreach (int ms in new[] { 499, 500, 501 })
{
    Check(tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings) is { Small: false, Duration: 400 }, "按下立即出现普通波纹");
    Check((tracker.Handle(MouseButton.Left, false, 0, 0, At(ms), settings) is not null) == (ms > 500), $"时间边界 {ms}");
}
foreach (int px in new[] { 199, 200, 201 })
{
    tracker.Handle(MouseButton.Right, true, -1000, -500, 0, settings);
    Check((tracker.Handle(MouseButton.Right, false, -1000 + px, -500, At(300), settings) is not null) == (px > 200), $"距离边界 {px}");
}
tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings);
Check(tracker.Handle(MouseButton.Left, false, 300, 0, At(700), settings) is { Small: true, Duration: 200 }, "同时满足只返回一次");
Check(tracker.Handle(MouseButton.Left, false, 300, 0, At(800), settings) is null, "松开后清理状态");
tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings);
Check(tracker.Handle(MouseButton.Left, false, 0, 0, At(400), settings) is null, "返回起点不触发");
foreach (var button in Enum.GetValues<MouseButton>()) tracker.Handle(button, true, 0, 0, 0, settings);
foreach (var button in Enum.GetValues<MouseButton>()) Check(tracker.Handle(button, false, 0, 0, At(700), settings) is not null, "五键独立");
tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings); tracker.Clear();
Check(tracker.Handle(MouseButton.Left, false, 0, 0, At(700), settings) is null, "重置没有残留");
Check(tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings with { Enabled = false }) is null, "停用无输入");
Check(tracker.Handle(MouseButton.Left, false, 0, 0, At(700), settings) is null, "恢复无旧状态");
tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings);
tracker.Handle(MouseButton.Right, true, 0, 0, 0, settings);
tracker.Clear(1 << (int)MouseButton.Left);
Check(tracker.Handle(MouseButton.Left, false, 0, 0, At(700), settings) is null, "仅清理被禁用按键");
Check(tracker.Handle(MouseButton.Right, false, 0, 0, At(700), settings) is not null, "其他按键仍保持按下状态");
var disabled = settings.Buttons.ToArray(); disabled[0] = disabled[0] with { Enabled = false };
Check(tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings with { Buttons = disabled }) is null, "单键停用");
Check(settings.IsValid() && !((settings with { Diameter = 52 }).IsValid()) && !((settings with { Buttons = [] }).IsValid()), "配置校验");
Check(!((settings with { Easing = (RippleEasing)99 }).IsValid()), "动画曲线校验");
Check(JsonSerializer.Deserialize<Settings>("{\"Version\":1}")?.Easing == RippleEasing.Linear, "旧配置缺少动画曲线时使用默认线性");
Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings with { Easing = RippleEasing.EaseInOut }))?.Easing == RippleEasing.EaseInOut, "动画曲线保存读取");
Check(tracker.Handle(MouseButton.Left, true, 0, 0, 0, settings with { Easing = RippleEasing.Linear })?.Easing == RippleEasing.Linear, "点击保存当前动画曲线");
Check(Settings.ValidColor("#a1B2c3") && !Settings.ValidColor("#GG0000") && !Settings.ValidColor("#1234"), "RGB 校验");
var queue = new RippleQueue();
for (int i = 0; i < 10000; i++) Check(queue.Push(new(i, 0, 0, 100, "#3B82F6", false)) == (i == 0), "仅空队列需要唤醒");
int count = 0;
while (queue.TryPop(out var item)) { Check(item.X == 9900 + count, "淘汰最旧请求"); count++; }
Check(count == 100, "队列有界");
Check(queue.Push(new()), "清空后再次唤醒"); queue.Clear(); Check(!queue.TryPop(out _), "清空队列");
var area = PixelRect.Around(100, 100, 53.5f);
Check(area == new PixelRect(46, 46, 154, 154), "波纹外接框向外取整");
Check(area.Intersect(new(0, 0, 100, 200)) == new PixelRect(46, 46, 100, 154), "跨屏左侧裁剪");
Check(area.Intersect(new(100, 0, 200, 200)) == new PixelRect(100, 46, 154, 154), "跨屏右侧裁剪");
var corner = PixelRect.Around(0, 0, 53);
var quadrants = new[] { new PixelRect(-100, -100, 0, 0), new PixelRect(0, -100, 100, 0), new PixelRect(-100, 0, 0, 100), new PixelRect(0, 0, 100, 100) };
Check(quadrants.All(screen => corner.Intersect(screen) is { Width: 53, Height: 53 }), "四屏交点与负坐标");
Check(PixelRect.Around(150, 50, 20).Intersect(new(0, 0, 100, 100)).IsEmpty, "屏幕间隙不生成窗口");
Console.WriteLine($"通过 {checks} 项断言（时间/距离边界、五键、禁用、动画曲线配置、有界队列、跨屏裁剪）。");
