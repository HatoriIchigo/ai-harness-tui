namespace ai_harness_tui;

/// <summary>
/// <c>ai-harness-main</c> が出力する等幅テーブル（<c>ヘッダ | ヘッダ</c>）を行の配列へ分解する。
///
/// 区切りは <c>" | "</c>。列数を上限に分割するため、<b>最終列に区切り文字が含まれていても壊れない</b>
/// （ログ本文に <c>" | "</c> が現れ得る）。改行は main 側がエスケープ済みで、1 レコード＝1 行。
/// </summary>
internal static class TableParser
{
    private const string Separator = " | ";

    /// <summary>
    /// 1 行目をヘッダとみなし、以降を <paramref name="columns"/> 列のセルへ分解して返す。
    /// 列数に満たない行（想定外の出力）は捨てる。
    /// </summary>
    public static List<string[]> ParseRows(IReadOnlyList<string> lines, int columns)
    {
        var rows = new List<string[]>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var cells = line.Split(Separator, columns, StringSplitOptions.None);
            if (cells.Length != columns)
            {
                continue;
            }
            // 整形のパディングを落とす。最終列は本文なので前後の余白のみ除去される。
            rows.Add([.. cells.Select(c => c.Trim())]);
        }
        return rows;
    }
}
