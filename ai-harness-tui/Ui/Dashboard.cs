using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// 画面本体。左に対象（実行体＋メモリ上のプロジェクト）、右上にプラグイン、右下にログを置き、
/// <c>Live</c> で同じ領域を再描画し続ける。
///
/// 一定間隔で <c>ai-harness-main</c> を叩き直すので、daemon がプロジェクトを回収したり
/// 新しいプロジェクトが hook で立ち上がったりすると、そのまま画面に反映される。
/// </summary>
internal static class Dashboard
{
    /// <summary>自動再取得の間隔。</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>キー入力待ちのポーリング間隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public static int Run()
    {
        // 端末が無いとカーソル制御もキー入力（Console.KeyAvailable）も例外になる。
        // リダイレクト下では画面も意味を成さないため、理由を示して早期に降りる。
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(
                "ai-harness-tui は端末で実行してください（stdin/stdout がリダイレクトされています）。"
                + " 出力を取り込みたい場合は ai-harness-main --project / --logs / --plugin を使ってください。");
            return 1;
        }

        if (!HarnessCli.IsAvailable(out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var state = new DashboardState { LogCapacity = LogCapacity() };
        state.Reload();

        var console = AnsiConsole.Console;
        if (console.Profile.Capabilities.Ansi)
        {
            // 代替画面バッファ（nvim や less と同じ）。抜けると元の画面がそのまま戻る。
            console.AlternateScreen(() => RunLive(state));
        }
        else
        {
            // ANSI 非対応の端末（旧 cmd.exe 等）は代替画面を持たないので、せめて描画跡を消す。
            RunLive(state);
            AnsiConsole.Clear();
        }
        return 0;
    }

    private static void RunLive(DashboardState state)
    {
        var layout = BuildLayout();
        AnsiConsole.Cursor.Hide();
        try
        {
            AnsiConsole.Live(layout).Start(context => Loop(context, layout, state));
        }
        finally
        {
            AnsiConsole.Cursor.Show();
        }
    }

    private static Layout BuildLayout() =>
        new Layout("root").SplitRows(
            new Layout("header").Size(4),
            new Layout("body").SplitColumns(
                new Layout("targets").Ratio(1),
                new Layout("detail").Ratio(2).SplitRows(
                    new Layout("plugins").Ratio(1),
                    new Layout("logs").Ratio(2))),
            new Layout("footer").Size(3));

    private static void Loop(LiveDisplayContext context, Layout layout, DashboardState state)
    {
        var sinceRefresh = Stopwatch.StartNew();
        while (true)
        {
            state.LogCapacity = LogCapacity();
            Render(layout, state);
            context.Refresh();

            if (Console.KeyAvailable)
            {
                if (!HandleKey(Console.ReadKey(intercept: true).Key, state))
                {
                    return;
                }
                sinceRefresh.Restart();
                continue;
            }

            if (sinceRefresh.Elapsed >= RefreshInterval)
            {
                state.Reload();
                sinceRefresh.Restart();
                continue;
            }
            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>キーを処理する。終了したいとき <c>false</c>。</summary>
    private static bool HandleKey(ConsoleKey key, DashboardState state)
    {
        switch (key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                return false;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                state.Move(-1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                state.Move(1);
                return true;
            case ConsoleKey.F:
                state.CycleFilter();
                return true;
            case ConsoleKey.R:
                state.Reload();
                return true;
            default:
                return true;
        }
    }

    // ---- 描画 ----

    private static void Render(Layout layout, DashboardState state)
    {
        layout["header"].Update(Header(state));
        layout["targets"].Update(Targets(state));
        layout["plugins"].Update(Plugins(state));
        layout["logs"].Update(Logs(state));
        layout["footer"].Update(Footer(state));
    }

    /// <summary>左欄はディレクトリ名だけに切り詰めるので、選択中の対象はここでフルパスを示す。</summary>
    private static IRenderable Header(DashboardState state)
    {
        var daemon = state.DaemonRunning ? "[green]running[/]" : "[red]stopped[/]";
        var projects = state.Targets.Count - 1;
        var selected = Truncate(state.Selected ?? DashboardState.HarnessLabel, WindowWidth() - 6);

        var rows = new Rows(
            new Markup($"daemon: {daemon}   memory: {projects} project(s)"),
            new Markup($"[grey]{Markup.Escape(selected)}[/]"));
        return new Panel(rows).Header("ai-harness").Expand();
    }

    private static IRenderable Targets(DashboardState state)
    {
        var rows = new List<IRenderable>();
        for (var i = 0; i < state.Targets.Count; i++)
        {
            var text = Markup.Escape(ShortName(state.Targets[i]));
            rows.Add(new Markup(i == state.Index ? $"[invert]{text}[/]" : text));
        }
        return new Panel(new Rows(rows)).Header("targets").Expand();
    }

    /// <summary>
    /// プロジェクトルートの末尾セグメント（ディレクトリ名）。区切りは <c>\</c> と <c>/</c> の両方を受ける。
    /// ドライブ直下など末尾を取り出せない場合はフルパスのまま返す。
    /// </summary>
    private static string ShortName(string? target)
    {
        if (target is null)
        {
            return DashboardState.HarnessLabel;
        }
        var name = Path.GetFileName(target.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : target;
    }

    private static IRenderable Plugins(DashboardState state)
    {
        var table = new Table().Border(TableBorder.None).Expand();
        table.AddColumn("name");
        table.AddColumn(state.Selected is null ? "installed" : "enabled");

        foreach (var plugin in state.Plugins)
        {
            table.AddRow(Markup.Escape(plugin.Name), EnabledMark(plugin.Enabled));
        }
        return new Panel(table).Header("plugins").Expand();
    }

    /// <summary>lib 一覧（有効状態を持たない）では導入済みを示すだけ。</summary>
    private static string EnabledMark(bool? enabled) => enabled switch
    {
        true => "[green]true[/]",
        false => "[grey]false[/]",
        null => "[green]yes[/]",
    };

    private static IRenderable Logs(DashboardState state)
    {
        var table = new Table().Border(TableBorder.None).Expand();
        table.AddColumn("time");
        table.AddColumn("level");
        table.AddColumn("contents");

        var width = ContentsWidth();
        foreach (var log in state.Logs.Take(state.LogCapacity))
        {
            table.AddRow(
                Markup.Escape(ShortTime(log.Time)),
                $"[{LevelColor(log.Level)}]{Markup.Escape(log.Level)}[/]",
                Markup.Escape(Truncate(log.Contents, width)));
        }
        return new Panel(table).Header($"logs (newest first, {state.Filter.Label()})").Expand();
    }

    private static IRenderable Footer(DashboardState state) =>
        new Panel(new Markup(
            $"[bold]↑↓/jk[/] 選択   [bold]f[/] フィルタ: {state.Filter.Label()}   [bold]r[/] 再取得   [bold]q[/] 終了"))
            .Expand();

    private static string LevelColor(string level) => level switch
    {
        "error" => "red",
        "warn" => "yellow",
        "info" => "default",
        _ => "grey",
    };

    /// <summary>年を落として <c>MM-dd HH:mm:ss</c> にする。狭い端末で本文に幅を譲るため。</summary>
    private static string ShortTime(string time) => time.Length >= 19 ? time[5..] : time;

    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : string.Concat(text.AsSpan(0, Math.Max(0, width - 1)), "…");

    // ---- 端末サイズ ----

    /// <summary>枠・ヘッダ・フッタ・プラグイン欄を除いた、ログに使える行数の概算。</summary>
    private static int LogCapacity() => Math.Max(5, WindowHeight() - 18);

    /// <summary>ログ本文に使える桁数の概算（左欄と time／level 列を除く）。</summary>
    private static int ContentsWidth() => Math.Max(20, (WindowWidth() * 2 / 3) - 30);

    private static int WindowHeight() => Safe(() => Console.WindowHeight, 30);

    private static int WindowWidth() => Safe(() => Console.WindowWidth, 120);

    /// <summary>端末に接続していない（リダイレクト等）場合は既定値へ倒す。</summary>
    private static int Safe(Func<int> read, int fallback)
    {
        try
        {
            var value = read();
            return value > 0 ? value : fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }
}
