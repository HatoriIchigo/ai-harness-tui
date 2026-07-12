using Spectre.Console;
using Spectre.Console.Rendering;

namespace ai_harness_tui;

/// <summary>
/// ログのビュー。1 行おきに地色を変える（既定の地＝黒 ⇄ 濃い灰）ゼブラで、長い行を目で追えるようにする。
///
/// Spectre の <c>Table</c> は<b>セルの中身にしか</b>スタイルが乗らず、列間やセルの余白は地色のままになる。
/// 縞が途切れて見えるので、テーブルを使わず 1 行＝1 本の文字列として組み、行末まで空白で埋めてから
/// 地色を掛ける（ステータスラインと同じ手）。桁は自前で持つ。
/// </summary>
internal static class LogView
{
    /// <summary>奇数行の地色。既定の地（多くの端末で黒）と交互になる。</summary>
    private const string StripeStyle = "on grey11";

    /// <summary><c>MM-dd HH:mm:ss</c> の桁数。</summary>
    private const int TimeWidth = 14;

    /// <summary>レベル名の最大桁（<c>error</c>／<c>debug</c>／<c>trace</c>）。</summary>
    private const int LevelWidth = 5;

    /// <summary>左余白 1 ＋ 列間 2 ＋ 右余白 1。</summary>
    private const int Margins = 4;

    /// <summary>パネルの左右の枠。</summary>
    private const int PanelBorders = 2;

    private const int MinContentsWidth = 10;

    public static IRenderable Render(DashboardState state)
    {
        var inner = Term.Width - PanelBorders;
        var contentsWidth = Math.Max(MinContentsWidth, inner - TimeWidth - LevelWidth - Margins);

        var rows = new List<IRenderable> { Head(inner, contentsWidth) };
        var index = 0;
        foreach (var log in state.Logs.Skip(state.Scroll).Take(state.LogCapacity))
        {
            rows.Add(Row(log, inner, contentsWidth, stripe: index++ % 2 == 1));
        }

        var scrolled = state.Scroll > 0 ? $", +{state.Scroll}" : "";
        return new Panel(new Rows(rows))
            .Header($"logs (newest first, {state.Filter.Label()}{scrolled})")
            .Padding(0, 0)
            .Expand();
    }

    /// <summary>見出し行。縞は掛けない。</summary>
    private static IRenderable Head(int inner, int contentsWidth)
    {
        var head = Compose("time", "level", "contents");
        return Line($"[grey]{Markup.Escape(head + Filler(head, inner))}[/]");
    }

    private static IRenderable Row(LogRow log, int inner, int contentsWidth, bool stripe)
    {
        var time = ShortTime(log.Time);
        var contents = Term.Truncate(log.Contents, contentsWidth);

        // 行末まで地色を乗せるための空白。本文に全角が混ざるので、文字数ではなく桁数から数える。
        var filler = Filler(Compose(time, log.Level, contents), inner);

        var body = $" [grey]{Markup.Escape(time.PadRight(TimeWidth))}[/]"
            + $" [{LevelColor(log.Level)}]{Markup.Escape(log.Level.PadRight(LevelWidth))}[/]"
            + $" {Markup.Escape(contents)}{filler}";

        // 地色は外側に掛ける。内側の前景色（レベル）は上書きされず、地色だけが行末まで乗る。
        return Line(stripe ? $"[{StripeStyle}]{body}[/]" : body);
    }

    /// <summary>桁を数えるための素の 1 行（マークアップと同じ並び・同じ空白）。</summary>
    private static string Compose(string time, string level, string contents) =>
        $" {time.PadRight(TimeWidth)} {level.PadRight(LevelWidth)} {contents}";

    /// <summary><paramref name="line"/> を <paramref name="inner"/> 桁まで埋める空白。</summary>
    private static string Filler(string line, int inner) =>
        new(' ', Math.Max(0, inner - Term.CellLength(line)));

    /// <summary>行が折り返すと下の行を押し出して件数が合わなくなる。はみ出したら切り落とす。</summary>
    private static IRenderable Line(string markup) => new Markup(markup).Overflow(Overflow.Crop);

    private static string LevelColor(string level) => level switch
    {
        "error" => "red",
        "warn" => "yellow",
        "info" => "default",
        _ => "grey",
    };

    /// <summary>年を落として <c>MM-dd HH:mm:ss</c> にする。狭い端末で本文に幅を譲るため。</summary>
    private static string ShortTime(string time) => time.Length >= 19 ? time[5..] : time;
}
