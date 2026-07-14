using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// 画面本体。上部に <c>plugins</c>／<c>log</c> のボタン（それだけ）、中央にそのどちらか 1 つ、下部に
/// neovim 風のステータスライン（対象・ブランチ・版・daemon）を置き、<c>Live</c> で再描画し続ける。
///
/// 状態を語るのはステータスライン（<see cref="StatusLine"/>）に一本化し、上部は「いまどのビューにいるか」
/// だけを示す。対象（実行体＋メモリ上のプロジェクト）も画面に常駐させず、<c>p</c> のポップアップで選ぶ。
/// これで本体の 1 ビューに端末の幅と行数を全部渡せる。
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

    /// <summary>起動時に <c>ai-harness-main --version</c> から得た版（ボタン行の右に出す）。</summary>
    private static string _version = "ai-harness";

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

        if (!HarnessCli.IsAvailable(out var version, out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }
        _version = version;

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
            new Layout("tabs").Size(TabsHeight),
            new Layout("body"),
            new Layout("status").Size(StatusHeight));

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
        // ポップアップは前面。開いている間は本体のキーを食わせない（nvim のモーダルと同じ）。
        if (state.PopupOpen)
        {
            return HandlePopupKey(key, state);
        }

        switch (key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                return false;
            case ConsoleKey.P:
                state.OpenPopup();
                return true;
            case ConsoleKey.Tab:
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
            case ConsoleKey.H:
            case ConsoleKey.L:
                state.ToggleView();
                return true;
            case ConsoleKey.D1:
                state.Show(DashboardView.Plugins);
                return true;
            case ConsoleKey.D2:
                state.Show(DashboardView.Log);
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                Move(state, -1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                Move(state, 1);
                return true;
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                state.TogglePlugin();
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

    /// <summary>
    /// <c>jk</c> の意味はビューで変わる。<c>plugins</c> では切り替える行のカーソル、<c>log</c> ではスクロール。
    /// </summary>
    private static void Move(DashboardState state, int delta)
    {
        if (state.View == DashboardView.Plugins)
        {
            state.MovePlugin(delta);
            return;
        }
        state.ScrollBy(delta);
    }

    /// <summary>ポップアップ表示中のキー。確定するまで対象は動かない。</summary>
    private static bool HandlePopupKey(ConsoleKey key, DashboardState state)
    {
        switch (key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.P:
            case ConsoleKey.Q:
                state.ClosePopup();
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                state.CommitPopup();
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                state.MovePopup(-1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                state.MovePopup(1);
                return true;
            default:
                return true;
        }
    }

    // ---- 描画 ----

    private static void Render(Layout layout, DashboardState state)
    {
        layout["tabs"].Update(Tabs(state));
        layout["body"].Update(state.PopupOpen ? ProjectPopup.Render(state) : Body(state));
        layout["status"].Update(StatusLine.Render(state, _version));
    }

    /// <summary>
    /// 上部のボタン行。タブだけを置く。版・daemon・プロジェクト数はステータスラインへ集約したので、
    /// ここは「いまどのビューにいるか」しか語らない。
    /// </summary>
    private static IRenderable Tabs(DashboardState state) =>
        new Markup($" {Button(state, DashboardView.Plugins)} {Button(state, DashboardView.Log)}");

    /// <summary>選択中のボタンはステータスラインと同じ黄緑で塗り、両者が同じ画面の一部だと分かるようにする。</summary>
    private static string Button(DashboardState state, DashboardView view)
    {
        var label = view.Label();
        return state.View == view ? $"[black on greenyellow] {label} [/]" : $"[grey] {label} [/]";
    }

    /// <summary>本体。ボタンで選ばれている 1 ビューだけを出す。</summary>
    private static IRenderable Body(DashboardState state) =>
        state.View == DashboardView.Plugins ? Plugins(state) : LogView.Render(state);

    /// <summary>
    /// 実行体自身を選んでいるときは lib のインストール一覧なので、有効状態の代わりに説明を出す
    /// （どのプロジェクトの話でもないため、そこに enabled は存在しない）。
    ///
    /// プロジェクトを選んでいるときは、カーソル行を <c>Space</c> で有効／無効に切り替えられる
    /// （<see cref="DashboardState.TogglePlugin"/>）。カーソルは lib 一覧では出さない。
    /// </summary>
    private static IRenderable Plugins(DashboardState state)
    {
        var libView = state.Selected is null;
        var table = new Table().Border(TableBorder.None).Expand();
        table.AddColumn("name");
        table.AddColumn(libView ? "description" : "enabled");

        var width = PluginDescriptionWidth();
        for (var i = 0; i < state.Plugins.Count; i++)
        {
            var plugin = state.Plugins[i];
            var second = libView
                ? Markup.Escape(Term.Truncate(plugin.Description, width))
                : EnabledMark(plugin.Enabled);
            var onCursor = !libView && i == state.PluginIndex;
            table.AddRow(NameCell(plugin.Name, onCursor), second);
        }
        return new Panel(table).Header(libView ? "plugins (lib)" : "plugins").Expand();
    }

    /// <summary>
    /// カーソル行の名前を、選択中のタブと同じ黄緑で塗る（画面全体で「いま選んでいるもの」の色を揃える）。
    /// 行頭 2 桁はカーソルの有無で変わらないよう、非カーソル行は空白で字下げする。
    /// </summary>
    private static string NameCell(string name, bool onCursor)
    {
        var escaped = Markup.Escape(name);
        return onCursor ? $"[black on greenyellow]▸ {escaped} [/]" : $"  {escaped}";
    }

    private static string EnabledMark(bool? enabled) =>
        enabled == true ? "[green]true[/]" : "[grey]false[/]";

    // ---- 端末サイズ ----
    //
    // BuildLayout の Size から逆算する。値をここに集約し、レイアウトを変えたらここだけ直せば済むように
    // する（Spectre は確定した割り当て幅を教えてくれない）。本体は単一ビューなので、幅も高さも丸ごと使える。

    /// <summary>tabs の Size（ボタン行 1 行）。</summary>
    private const int TabsHeight = 1;

    /// <summary>status の Size（ステータスライン＋キー案内）。</summary>
    private const int StatusHeight = 2;

    /// <summary>パネルの上下枠 2 行＋テーブルのヘッダ 1 行。</summary>
    private const int PanelChrome = 3;

    /// <summary>plugins テーブルの name 列と、テーブル・パネルの余白の合計。</summary>
    private const int PluginNameColumn = 32;

    private const int MinLogRows = 5;
    private const int MinTextWidth = 10;

    /// <summary>本体（単一ビュー）に収まる行数。ログの桁は <see cref="LogView"/> が自前で持つ。</summary>
    private static int LogCapacity() =>
        Math.Max(MinLogRows, Term.Height - TabsHeight - StatusHeight - PanelChrome);

    /// <summary>プラグインの説明に使える桁数。</summary>
    private static int PluginDescriptionWidth() => Math.Max(MinTextWidth, Term.Width - PluginNameColumn);
}
