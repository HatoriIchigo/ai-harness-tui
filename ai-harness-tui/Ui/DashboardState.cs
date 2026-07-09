namespace ai_harness_tui;

/// <summary>
/// 画面が表示している内容。<c>ai-harness-main</c> の呼び出しは全てここに集約し、描画側は読むだけにする。
///
/// 対象リストの先頭は常にハーネス実行体自身（<see cref="HarnessTarget"/>＝<c>null</c>）で、
/// 以降が daemon のメモリ上にあるプロジェクト。
/// </summary>
internal sealed class DashboardState
{
    /// <summary>実行体自身を指す擬似対象。プロジェクトルートの代わりに <c>null</c>。</summary>
    public const string? HarnessTarget = null;

    /// <summary>表示上の実行体自身の名前。</summary>
    public const string HarnessLabel = "(ai-harness-main)";

    private readonly List<string?> _targets = [HarnessTarget];

    /// <summary>選択中の対象（実行体自身なら <c>null</c>）。</summary>
    public string? Selected => _targets[Index];

    /// <summary>対象一覧（先頭は実行体自身）。</summary>
    public IReadOnlyList<string?> Targets => _targets;

    /// <summary>選択位置。</summary>
    public int Index { get; private set; }

    /// <summary>daemon が稼働しているか。</summary>
    public bool DaemonRunning { get; private set; }

    /// <summary>選択中の対象のプラグイン。</summary>
    public IReadOnlyList<PluginRow> Plugins { get; private set; } = [];

    /// <summary>選択中の対象のログ（新しい順）。</summary>
    public IReadOnlyList<LogRow> Logs { get; private set; } = [];

    /// <summary>ログの重大度フィルタ。</summary>
    public LogFilter Filter { get; private set; } = LogFilter.All;

    /// <summary>取得件数。端末の高さに合わせて描画側が更新する。</summary>
    public int LogCapacity { get; set; } = 20;

    /// <summary>
    /// 直近の取得で起きた失敗（実行体が消えた等）。成功すると <c>null</c> に戻る。
    /// 取得は子プロセス起動なので失敗し得る。画面を落とさず、理由を出して次の周期で回復させる。
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// プロジェクト一覧を取り直し、選択中の対象の詳細も更新する。
    /// 回収などで選択中のプロジェクトが消えていたら実行体自身へ戻す。
    /// </summary>
    public void Reload() => Guard(() =>
    {
        var projects = HarnessQuery.QueryProjects();
        DaemonRunning = projects.DaemonRunning;

        var previous = Selected;
        _targets.Clear();
        _targets.Add(HarnessTarget);
        _targets.AddRange(projects.Roots.Cast<string?>());

        var restored = _targets.IndexOf(previous);
        Index = restored >= 0 ? restored : 0;
        LoadDetail();
    });

    /// <summary>選択中の対象のプラグインとログだけを取り直す。</summary>
    public void ReloadDetail() => Guard(LoadDetail);

    /// <summary>選択を <paramref name="delta"/> 件動かす（範囲外へは出ない）。動いたら詳細を取り直す。</summary>
    public void Move(int delta)
    {
        var next = Math.Clamp(Index + delta, 0, _targets.Count - 1);
        if (next == Index)
        {
            return;
        }
        Index = next;
        ReloadDetail();
    }

    /// <summary>フィルタを次の候補へ進め、ログを取り直す。</summary>
    public void CycleFilter()
    {
        Filter = Filter.Next();
        Guard(LoadLogs);
    }

    private void LoadDetail()
    {
        Plugins = HarnessQuery.QueryPlugins(Selected);
        LoadLogs();
    }

    private void LoadLogs() => Logs = HarnessQuery.QueryLogs(Selected, LogCapacity, Filter);

    /// <summary>取得の失敗を <see cref="Error"/> に畳む。直前の表示内容はそのまま残す。</summary>
    private void Guard(Action load)
    {
        try
        {
            load();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
