using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// 下部 2 行。1 行目が neovim 風のステータスライン（黄緑の帯）、2 行目がキーの案内。
///
/// 帯には「いま何を見ているか」（対象・ブランチ・フルパス）と「ハーネスがどうなっているか」
/// （版・daemon・展開中のプロジェクト数）を集約する。上部にはタブしか置かない。
///
/// 帯を端まで塗るため、Spectre の <c>Grid</c>（列の隙間に背景が乗らない）ではなく 1 本の文字列を
/// 自前で組む。左群・パス・右群を並べ、余りを空白で埋める。幅が足りないときは、優先度の低い
/// パス → 右群の順に落として左群（対象名）を必ず残す。
/// </summary>
internal static class StatusLine
{
    /// <summary>帯の配色。黄緑地に黒。</summary>
    private const string BarStyle = "black on greenyellow";

    /// <summary>daemon が止まっているときだけ、帯の中で前景色を変えて目立たせる。</summary>
    private const string StoppedStyle = "red1 bold";

    /// <summary>パスを出すために最低限必要な余白。これを割るならパスは落とす。</summary>
    private const int MinPathBudget = 12;

    public static IRenderable Render(DashboardState state, string version)
    {
        // 折り返すと下部に確保した 2 行に収まらず、レイアウトごと崩れる。桁を数え違えたときは切り落とす。
        return new Rows(
            Bar(state, version).Overflow(Overflow.Crop),
            new Markup(Hint(state)).Overflow(Overflow.Crop));
    }

    private static Markup Bar(DashboardState state, string version)
    {
        var width = Term.Width;
        var left = Left(state);
        var right = Right(state, version);

        // 桁は文字数ではなく端末の桁数で数える（プロジェクト名やパスに全角が混ざり得る）。
        var used = Term.CellLength(left) + Term.CellLength(right);

        // 入り切らないぶんは、対象名（左群）を残す方向で削る。
        var path = Path(state, width - used);
        if (used + Term.CellLength(path) > width)
        {
            right = "";
            used = Term.CellLength(left);
        }
        var gap = Math.Max(1, width - used - Term.CellLength(path));

        return new Markup($"[{BarStyle}]{Markup.Escape(left + path)}{new string(' ', gap)}"
            + $"{RightMarkup(state, right)}[/]");
    }

    /// <summary>左群＝対象名とブランチ。常に出す。</summary>
    private static string Left(DashboardState state)
    {
        var name = Term.ShortName(state.Selected);
        return state.Branch is { } branch ? $" {name}  {branch} " : $" {name} ";
    }

    /// <summary>右群＝版・daemon・展開中のプロジェクト数。ハーネス自身の状態。</summary>
    private static string Right(DashboardState state, string version)
    {
        var daemon = state.DaemonRunning ? "running" : "stopped";
        var projects = state.Targets.Count - 1;
        return $" {version}  daemon: {daemon}  memory: {projects} project(s) ";
    }

    /// <summary>
    /// 左群のディレクトリ名だけでは同名のプロジェクトを見分けられないため、余裕があればフルパスを添える。
    /// </summary>
    private static string Path(DashboardState state, int budget)
    {
        if (state.Selected is not { } root || budget < MinPathBudget)
        {
            return "";
        }
        // 前後の空白ぶん（対象名との間隔と右端の余白）を引いた桁に収める。
        return $" {Term.Truncate(root, budget - 3)} ";
    }

    /// <summary>
    /// 右群に色を差す。帯全体を包む <see cref="BarStyle"/> の内側なので、前景だけ上書きされ地色は残る。
    /// </summary>
    private static string RightMarkup(DashboardState state, string right)
    {
        var escaped = Markup.Escape(right);
        if (right.Length == 0 || state.DaemonRunning)
        {
            return escaped;
        }
        return escaped.Replace("stopped", $"[{StoppedStyle}]stopped[/]", StringComparison.Ordinal);
    }

    /// <summary>
    /// 2 行目。取得に失敗している間、および切り替えを拒否されたときは、案内の代わりに理由を赤字で出す
    /// （画面は落とさず、次の周期で回復させる）。案内はビューごとに要るキーだけを並べる。
    /// </summary>
    private static string Hint(DashboardState state)
    {
        // 取得の失敗を優先する（画面の内容そのものが古い可能性を先に伝える）。
        if ((state.Error ?? state.Notice) is { } message)
        {
            return $"[red]{Markup.Escape(Term.Truncate(message, Term.Width - 2))}[/]";
        }

        return state.View == DashboardView.Plugins
            ? " [bold]p[/] 対象   [bold]Tab[/] 切替   [bold]jk[/] 選択   [bold]Space[/] 有効/無効   "
                + "[bold]r[/] 再取得   [bold]u[/] 更新   [bold]q[/] 終了"
            : $" [bold]p[/] 対象   [bold]Tab[/] 切替   [bold]jk[/] スクロール   "
                + $"[bold]f[/] フィルタ: {state.Filter.Label()}   [bold]r[/] 再取得   [bold]u[/] 更新   [bold]q[/] 終了";
    }
}
