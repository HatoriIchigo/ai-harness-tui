namespace ai_harness_tui;

/// <summary>
/// <c>ai-harness-main</c> の情報表示サブコマンドを型付きで叩く。
///
/// 対象（<c>target</c>）が <c>null</c> のときはハーネス実行体そのもの
/// （<c>--logs</c> ならグローバルログ、<c>--plugin</c> なら <c>lib/</c> のインストール一覧）を指す。
/// </summary>
internal static class HarnessQuery
{
    /// <summary><c>--project</c> の結果。</summary>
    /// <param name="Roots">メモリ上のプロジェクトルート。</param>
    /// <param name="DaemonRunning">daemon が稼働しているか。</param>
    internal readonly record struct Projects(IReadOnlyList<string> Roots, bool DaemonRunning);

    /// <summary>
    /// メモリ上のプロジェクト一覧を取る。daemon 未起動のとき main は stderr に注記を出して
    /// ヘッダだけを返すため、stderr が空かどうかで稼働を判定する（main は他に stderr へ書かない）。
    /// </summary>
    public static Projects QueryProjects()
    {
        var result = HarnessCli.Run("--project");
        var roots = TableParser.ParseRows(result.Lines, 2).Select(cells => cells[1]).ToList();
        return new Projects(roots, result.Error.Length == 0);
    }

    /// <summary>
    /// プラグイン一覧。<paramref name="target"/> 指定時は <c>enabled</c> 列、
    /// 無指定時は <c>description</c> 列が返るので、有効状態は <c>null</c> になる。
    /// </summary>
    public static List<PluginRow> QueryPlugins(string? target)
    {
        var result = target is null
            ? HarnessCli.Run("--plugin")
            : HarnessCli.Run("--plugin", target);

        return TableParser.ParseRows(result.Lines, 3)
            .Select(cells => new PluginRow(cells[1], target is null ? null : cells[2] == "true"))
            .ToList();
    }

    /// <summary>新しい順のログを最大 <paramref name="take"/> 件取る。</summary>
    public static List<LogRow> QueryLogs(string? target, int take, LogFilter filter)
    {
        var args = new List<string> { "--logs" };
        if (target is not null)
        {
            args.Add(target);
        }
        args.Add("--n");
        args.Add(take.ToString());
        if (filter.Argument() is { } levels)
        {
            args.Add("--filter");
            args.Add(levels);
        }

        var result = HarnessCli.Run([.. args]);
        return TableParser.ParseRows(result.Lines, 3)
            .Select(cells => new LogRow(cells[0], cells[1], cells[2]))
            .ToList();
    }
}
