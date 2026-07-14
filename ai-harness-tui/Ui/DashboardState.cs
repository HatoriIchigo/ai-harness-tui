namespace ai_harness_tui;

/// <summary>
/// 画面が表示している内容。<c>ai-harness-main</c> の呼び出しは全てここに集約し、描画側は読むだけにする。
///
/// 対象リストの先頭は常にハーネス実行体自身（<see cref="HarnessTarget"/>＝<c>null</c>）で、
/// 以降が daemon のメモリ上にあるプロジェクト。対象は画面に常駐させず、<c>p</c> のポップアップで選ぶ。
/// 本体には <see cref="View"/> の 1 ビューだけを出す。
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

    /// <summary>対象一覧（先頭は実行体自身）。ポップアップの選択肢。</summary>
    public IReadOnlyList<string?> Targets => _targets;

    /// <summary>選択位置。</summary>
    public int Index { get; private set; }

    /// <summary>本体に出しているビュー（上部ボタン）。</summary>
    public DashboardView View { get; private set; } = DashboardView.Log;

    /// <summary>プロジェクト選択ポップアップを開いているか。</summary>
    public bool PopupOpen { get; private set; }

    /// <summary>ポップアップ内のカーソル位置（確定するまで <see cref="Index"/> は動かさない）。</summary>
    public int PopupIndex { get; private set; }

    /// <summary>daemon が稼働しているか。</summary>
    public bool DaemonRunning { get; private set; }

    /// <summary>選択中の対象の git ブランチ。git 管理外・実行体自身なら <c>null</c>。</summary>
    public string? Branch { get; private set; }

    /// <summary>選択中の対象のプラグイン。</summary>
    public IReadOnlyList<PluginRow> Plugins { get; private set; } = [];

    /// <summary>plugins ビューのカーソル位置（有効化を切り替える行）。</summary>
    public int PluginIndex { get; private set; }

    /// <summary>選択中の対象のログ（新しい順）。<see cref="Scroll"/> 件目から表示する。</summary>
    public IReadOnlyList<LogRow> Logs { get; private set; } = [];

    /// <summary>ログの重大度フィルタ。</summary>
    public LogFilter Filter { get; private set; } = LogFilter.All;

    /// <summary>ログの表示開始位置（新しい順に何件読み飛ばすか）。</summary>
    public int Scroll { get; private set; }

    /// <summary>本体に収まるログの行数。端末の高さに合わせて描画側が更新する。</summary>
    public int LogCapacity { get; set; } = 20;

    /// <summary>
    /// 直近の取得で起きた失敗（実行体が消えた等）。成功すると <c>null</c> に戻る。
    /// 取得は子プロセス起動なので失敗し得る。画面を落とさず、理由を出して次の周期で回復させる。
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// 直近の切り替えが拒否された理由（成功時は <c>null</c>）。2 秒ごとの再取得では消さない
    /// ＝読む前に流れないよう、次の切り替えかビュー・対象の変更まで残す。
    /// </summary>
    public string? Notice { get; private set; }

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
        PopupIndex = Math.Clamp(PopupIndex, 0, _targets.Count - 1);
        LoadDetail();
    });

    /// <summary>本体のビューを切り替える（上部ボタン）。</summary>
    public void Show(DashboardView view)
    {
        if (View == view)
        {
            return;
        }
        View = view;
        Scroll = 0;
        Notice = null;
    }

    /// <summary>plugins ビューのカーソルを動かす（範囲外へは出ない）。</summary>
    public void MovePlugin(int delta)
    {
        if (Plugins.Count == 0)
        {
            return;
        }
        PluginIndex = Math.Clamp(PluginIndex + delta, 0, Plugins.Count - 1);
    }

    /// <summary>
    /// カーソル行のプラグインの有効／無効を反転する（<c>--plugin --enable|--disable</c>）。
    ///
    /// 実行体自身を選んでいるときは <c>lib/</c> のインストール一覧＝どのプロジェクトの話でもないため、
    /// 有効化という概念が無い。<c>p</c> でプロジェクトを選ぶよう促して何もしない。
    ///
    /// main が拒否した場合（有効化するとそのプロジェクトの hook が全 deny になる等）は、書き換わって
    /// いないので取り直さず、理由だけを <see cref="Notice"/> に残す。
    /// </summary>
    public void TogglePlugin()
    {
        if (View != DashboardView.Plugins || Plugins.Count == 0)
        {
            return;
        }
        if (Selected is not { } target)
        {
            Notice = "有効化はプロジェクトごとの設定です。p でプロジェクトを選んでください。";
            return;
        }

        var plugin = Plugins[PluginIndex];
        var enable = plugin.Enabled != true;

        Guard(() =>
        {
            var toggle = HarnessQuery.SetPluginEnabled(target, plugin.Name, enable);
            Notice = toggle.Ok ? null : toggle.Reason;
            if (toggle.Ok)
            {
                LoadDetail();
            }
        });
    }

    /// <summary>もう一方のビューへ切り替える。</summary>
    public void ToggleView() => Show(View.Other());

    /// <summary>プロジェクト選択ポップアップを開く。カーソルは現在の選択に合わせる。</summary>
    public void OpenPopup()
    {
        PopupOpen = true;
        PopupIndex = Index;
    }

    /// <summary>選択を確定せずポップアップを閉じる。</summary>
    public void ClosePopup() => PopupOpen = false;

    /// <summary>ポップアップのカーソルを動かす（範囲外へは出ない）。</summary>
    public void MovePopup(int delta) =>
        PopupIndex = Math.Clamp(PopupIndex + delta, 0, _targets.Count - 1);

    /// <summary>ポップアップの選択を確定し、対象を切り替えて詳細を取り直す。</summary>
    public void CommitPopup()
    {
        PopupOpen = false;
        if (PopupIndex == Index)
        {
            return;
        }
        Index = PopupIndex;
        Scroll = 0;
        PluginIndex = 0;
        Notice = null;
        ReloadDetail();
    }

    /// <summary>選択中の対象のプラグインとログだけを取り直す。</summary>
    public void ReloadDetail() => Guard(LoadDetail);

    /// <summary>
    /// ログの表示位置を <paramref name="delta"/> 件動かす。
    /// 取得件数に届いていない（これ以上古いログが無い）方向へは進めない。
    /// </summary>
    public void ScrollBy(int delta)
    {
        if (View != DashboardView.Log)
        {
            return;
        }
        var next = Math.Max(0, Scroll + delta);
        if (next > Scroll && Logs.Count <= Scroll + LogCapacity)
        {
            return;   // 末尾まで見えている。これ以上は無い。
        }
        if (next == Scroll)
        {
            return;
        }
        Scroll = next;
        Guard(LoadLogs);
    }

    /// <summary>フィルタを次の候補へ進め、ログを取り直す。</summary>
    public void CycleFilter()
    {
        Filter = Filter.Next();
        Scroll = 0;
        Guard(LoadLogs);
    }

    private void LoadDetail()
    {
        Branch = GitBranch.Resolve(Selected);
        Plugins = HarnessQuery.QueryPlugins(Selected);
        // プラグインが増減してもカーソルが表からはみ出さないようにする。
        PluginIndex = Plugins.Count == 0 ? 0 : Math.Clamp(PluginIndex, 0, Plugins.Count - 1);
        LoadLogs();
    }

    /// <summary>読み飛ばす分も含めて取る（<c>--logs</c> は新しい順に上位 N 件しか返さないため）。</summary>
    private void LoadLogs() => Logs = HarnessQuery.QueryLogs(Selected, Scroll + LogCapacity, Filter);

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
