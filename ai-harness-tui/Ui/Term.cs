namespace ai_harness_tui;

/// <summary>
/// 端末サイズと表示用の文字列整形。描画側（<see cref="Dashboard"/>／<see cref="ProjectPopup"/>）の
/// 共通処理をここに置く。
/// </summary>
internal static class Term
{
    private const int FallbackWidth = 120;
    private const int FallbackHeight = 30;

    /// <summary>端末の桁数。端末に接続していない（リダイレクト等）場合は既定値。</summary>
    public static int Width => Safe(() => Console.WindowWidth, FallbackWidth);

    /// <summary>端末の行数。端末に接続していない（リダイレクト等）場合は既定値。</summary>
    public static int Height => Safe(() => Console.WindowHeight, FallbackHeight);

    /// <summary>
    /// プロジェクトルートの末尾セグメント（ディレクトリ名）。区切りは <c>\</c> と <c>/</c> の両方を受ける。
    /// 対象がハーネス実行体自身（<c>null</c>）ならその表示名。ドライブ直下など末尾を取り出せない場合は
    /// フルパスのまま返す。
    /// </summary>
    public static string ShortName(string? target)
    {
        if (target is null)
        {
            return DashboardState.HarnessLabel;
        }
        var name = Path.GetFileName(target.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : target;
    }

    /// <summary>
    /// 端末に占める桁数。日本語・絵文字などの全角は 2 桁と数える。
    ///
    /// 空白で行を埋めて地色を行末まで乗せる（ゼブラ・ステータスライン）ため、文字数ではなく桁数が要る。
    /// Spectre 自身の桁計算（<c>Cell</c>）は internal で呼べないので、East Asian Width の主要な範囲を
    /// 自前で持つ。
    /// </summary>
    public static int CellLength(string text)
    {
        var cells = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            cells += RuneWidth(rune.Value);
        }
        return cells;
    }

    /// <summary><paramref name="width"/> 桁に収まるよう末尾を省略する（桁は <see cref="CellLength"/>）。</summary>
    public static string Truncate(string text, int width)
    {
        if (CellLength(text) <= width)
        {
            return text;
        }
        if (width <= 1)
        {
            return "…";
        }

        // 省略記号 1 桁ぶんを残して詰める。全角は 2 桁なので、境界で切ると 1 桁余ることがある。
        var builder = new System.Text.StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = RuneWidth(rune.Value);
            if (used + cells > width - 1)
            {
                break;
            }
            builder.Append(rune);
            used += cells;
        }
        return builder.Append('…').ToString();
    }

    /// <summary>East Asian Width が Wide／Fullwidth なら 2 桁。</summary>
    private static int RuneWidth(int codePoint) => codePoint switch
    {
        >= 0x1100 and <= 0x115F => 2,   // ハングル字母
        >= 0x2E80 and <= 0x303E => 2,   // CJK 部首・記号
        >= 0x3041 and <= 0x33FF => 2,   // かな・ハングル・CJK 互換
        >= 0x3400 and <= 0x4DBF => 2,   // CJK 拡張 A
        >= 0x4E00 and <= 0x9FFF => 2,   // CJK 統合漢字
        >= 0xA000 and <= 0xA4CF => 2,   // イ文字
        >= 0xAC00 and <= 0xD7A3 => 2,   // ハングル音節
        >= 0xF900 and <= 0xFAFF => 2,   // CJK 互換漢字
        >= 0xFE30 and <= 0xFE6F => 2,   // CJK 互換形・小字形
        >= 0xFF00 and <= 0xFF60 => 2,   // 全角英数・記号
        >= 0xFFE0 and <= 0xFFE6 => 2,   // 全角記号
        >= 0x1F300 and <= 0x1F9FF => 2, // 絵文字
        >= 0x20000 and <= 0x3FFFD => 2, // CJK 拡張 B 以降
        _ => 1,
    };

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
