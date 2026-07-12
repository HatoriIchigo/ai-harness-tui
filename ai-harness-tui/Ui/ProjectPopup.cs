using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// <c>p</c> で開くプロジェクト選択ポップアップ。
///
/// Spectre の <c>Layout</c> は本当の意味でのオーバーレイを持たないため、開いている間だけ本体の領域を
/// このポップアップで置き換え、中央に寄せて浮かせて見せる。キーもポップアップが先に食う（モーダル）。
///
/// 先頭は常にハーネス実行体自身。選ぶと <c>lib/</c> のインストール済みプラグインと実行体自身の
/// ライフサイクルログに切り替わる。
/// </summary>
internal static class ProjectPopup
{
    /// <summary>フルパスに割く桁数（端末幅の半分まで）。</summary>
    private const int PathWidthDivisor = 2;

    private const int MinPathWidth = 20;

    public static IRenderable Render(DashboardState state)
    {
        var width = Math.Max(MinPathWidth, Term.Width / PathWidthDivisor);

        var rows = new List<IRenderable>();
        for (var i = 0; i < state.Targets.Count; i++)
        {
            rows.Add(new Markup(Row(state, i, width)));
        }
        if (state.Targets.Count == 1)
        {
            // 実行体自身しか無い＝daemon がプロジェクトを 1 つも展開していない。
            rows.Add(new Markup("[grey]（メモリ上のプロジェクトはありません）[/]"));
        }
        rows.Add(new Markup("[grey]jk 選択   Enter 決定   Esc 取消[/]"));

        var panel = new Panel(new Rows(rows))
            .Header("projects")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        return new Align(panel, HorizontalAlignment.Center, VerticalAlignment.Middle);
    }

    /// <summary>
    /// 1 行＝ディレクトリ名＋薄いフルパス。カーソル行は反転し、いま表示中の対象には <c>*</c> を付ける
    /// （同名のディレクトリが並んでも取り違えないようフルパスを併記する）。
    /// </summary>
    private static string Row(DashboardState state, int index, int width)
    {
        var target = state.Targets[index];
        var name = Markup.Escape(Term.ShortName(target));
        var path = target is null ? "" : Markup.Escape(Term.Truncate(target, width));
        var current = index == state.Index ? "*" : " ";

        return index == state.PopupIndex
            ? $"[invert] {current} {name} [/]  [grey]{path}[/]"
            : $" {current} {name}   [grey]{path}[/]";
    }
}
