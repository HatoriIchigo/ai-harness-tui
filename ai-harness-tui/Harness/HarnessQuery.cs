namespace ai_harness_tui;

/// <summary>
/// <c>ai-harness-main</c> のサブコマンドを型付きで叩く。
///
/// 対象（<c>target</c>）が <c>null</c> のときはハーネス実行体そのもの
/// （<c>--logs</c> ならグローバルログ、<c>--plugin</c> なら <c>lib/</c> のインストール一覧）を指す。
///
/// 読み取りのほかに、プラグインの有効化（<see cref="SetPluginEnabled"/>）だけを書き込みとして持つ。
/// 書き換えるのは main であり、TUI は <c>common.yml</c> を直接触らない。
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
    /// プラグイン一覧。3 列目は <paramref name="target"/> 指定時が <c>enabled</c>、
    /// 無指定時は <c>description</c>。どちらか一方しか得られない。
    /// </summary>
    public static List<PluginRow> QueryPlugins(string? target)
    {
        var result = target is null
            ? HarnessCli.Run("--plugin")
            : HarnessCli.Run("--plugin", target);

        return TableParser.ParseRows(result.Lines, 3)
            .Select(cells => target is null
                ? new PluginRow(cells[1], null, cells[2])
                : new PluginRow(cells[1], cells[2] == "true", ""))
            .ToList();
    }

    /// <summary>プラグインの有効化／無効化の結果。</summary>
    /// <param name="Ok">切り替えられたか。</param>
    /// <param name="Reason">拒否・失敗の理由（<paramref name="Ok"/> が <c>true</c> なら空）。</param>
    internal readonly record struct Toggle(bool Ok, string Reason);

    /// <summary>
    /// <paramref name="target"/> のプロジェクトで <paramref name="name"/> を有効化／無効化する
    /// （<c>--plugin &lt;プロジェクト&gt; --enable|--disable &lt;名&gt;</c>）。<c>common.yml</c> を書き換えるのは main で、
    /// 設定はホットリロードされるため daemon の再起動は要らない。
    ///
    /// main は「有効化するとフェイルクローズ（そのプロジェクトの hook が全 deny）になる」場合、書き込まずに
    /// 非 0 で拒否する。その理由を拾って呼び出し側へ返す。
    /// </summary>
    public static Toggle SetPluginEnabled(string target, string name, bool enable)
    {
        var result = HarnessCli.Run("--plugin", target, enable ? "--enable" : "--disable", name);
        return result.ExitCode == 0
            ? new Toggle(true, "")
            : new Toggle(false, Reason(result.Error, enable));
    }

    /// <summary>
    /// 拒否の stderr（複数行）から 1 行ぶんの理由を作る。main は見出しに続けて
    /// <c>- &lt;プラグイン名&gt;: 理由</c> を並べるため、その明細行を優先して拾う。
    /// </summary>
    private static string Reason(string stderr, bool enable)
    {
        var lines = stderr.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        var detail = lines.FirstOrDefault(line => line.StartsWith("- ", StringComparison.Ordinal));
        return detail?[2..]
            ?? lines.FirstOrDefault()
            ?? $"{(enable ? "有効化" : "無効化")}に失敗しました。";
    }

    /// <summary>
    /// LSP の状況。<paramref name="target"/> 無指定は <see cref="LspCatalog"/> の対応言語・候補サーバ一覧
    /// （<c>language | server</c> の 2 列。<see cref="LspRow.Status"/>／<see cref="LspRow.Error"/> は <c>null</c>）、
    /// 指定時は <c>common.yml</c> の宣言と daemon 上の実際の稼働状況（4 列）。
    /// </summary>
    public static List<LspRow> QueryLsp(string? target)
    {
        var result = target is null
            ? HarnessCli.Run("--lsp")
            : HarnessCli.Run("--lsp", target);

        return target is null
            ? TableParser.ParseRows(result.Lines, 2)
                .Select(cells => new LspRow(cells[0], cells[1], null, null))
                .ToList()
            : TableParser.ParseRows(result.Lines, 4)
                .Select(cells => new LspRow(cells[0], cells[1], cells[2], cells[3]))
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
