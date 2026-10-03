using System.Globalization;
using System.Text.Json;

namespace Shadowverse.Engine.Simulation;

/// <summary>
/// **跨进程的实时进度**：命令行跑批量对局时把进度写到一个固定文件，
/// 界面每秒钟读一次显示在自己的进度栏里。
/// <para>
/// 为什么要走文件：命令行（Shadowverse.Console）和界面（Shadowverse.DeckEditor）是
/// 两个独立进程，没有共享内存；而用户要的是"我在命令行跑，你在界面上也能看见"。
/// 文件是这里最简单、最不引入依赖的通道。
/// </para>
/// <para>
/// 路径固定在仓库根的 <c>outputs/live-progress.json</c>（和卡组库一样，
/// 从程序集位置向上找 <c>ShadowversePrototype.sln</c> 定位）。
/// </para>
/// </summary>
public static class LiveProgress
{
    /// <summary>进度快照。字段都用短名，减少文件体积与解析开销。</summary>
    public sealed record Snapshot(
        string Label,
        int Done,
        int Total,
        string FirstName,
        int FirstWins,
        string SecondName,
        int SecondWins,
        double ElapsedSeconds,
        double RemainingSeconds,
        string UpdatedAt,
        long UpdatedMs);

    public static string FilePath { get; } = ResolvePath();

    /// <summary>写一次进度。任何异常都吞掉 —— 进度显示失败绝不能影响对局本身。</summary>
    public static void Report(
        string label,
        int done,
        int total,
        string firstName,
        int firstWins,
        string secondName,
        int secondWins,
        TimeSpan elapsed,
        TimeSpan remaining)
    {
        try
        {
            var snapshot = new Snapshot(
                label,
                done,
                total,
                firstName,
                firstWins,
                secondName,
                secondWins,
                Math.Round(elapsed.TotalSeconds, 1),
                Math.Round(remaining.TotalSeconds, 1),
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(snapshot),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            // 进度是附加信息，写失败不影响对局。
        }
    }

    /// <summary>收尾：把进度文件删掉，界面就会回到空闲显示。</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception)
        {
            // 同上。
        }
    }

    /// <summary>
    /// 进度超过这个时长没更新，就认为那一侧已经不在跑了（进程被关掉等）。
    /// <para>
    /// 取 30 秒。**这里踩过一次**：原来取 10 秒，而命令行最初只在"每 N 局"的 tick 上写文件，
    /// 两次 tick 之间可能几十秒 → 界面判定过期 → 把进度隐藏 → 下次 tick 又显示。
    /// 用户看到的现象是"**一会看得到一会看不到**"。
    /// </para>
    /// <para>现在命令行**每局都写文件**，30 秒足够容纳最慢的一局。</para>
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    /// <summary>读当前进度；没有在跑、或已经过期就返回 null。</summary>
    public static Snapshot? Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var text = File.ReadAllText(FilePath);
            var snapshot = string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize<Snapshot>(text);
            if (snapshot is null)
            {
                return null;
            }

            // 过期判定：命令行那边被关掉时不会有"收尾"机会，所以这里按时间戳兜底。
            var age = DateTimeOffset.Now.ToUnixTimeMilliseconds() - snapshot.UpdatedMs;
            return age > StaleAfter.TotalMilliseconds ? null : snapshot;
        }
        catch (Exception)
        {
            // 正在写的时候读到半个文件是正常的 —— 当作"这一拍没有进度"。
            return null;
        }
    }

    private static string ResolvePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShadowversePrototype.sln")))
            {
                return Path.Combine(directory.FullName, "outputs", "live-progress.json");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "outputs", "live-progress.json");
    }
}
