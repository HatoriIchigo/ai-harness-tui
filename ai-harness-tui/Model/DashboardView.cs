namespace ai_harness_tui;

/// <summary>
/// 本体に出すビュー。上部のボタン（タブ）で切り替える単一表示で、同時に 2 つは出さない。
/// </summary>
internal enum DashboardView
{
    /// <summary>選択中の対象のプラグイン。</summary>
    Plugins,

    /// <summary>選択中の対象の LSP 稼働状況（無指定なら対応言語・候補サーバの一覧）。</summary>
    Lsp,

    /// <summary>選択中の対象のログ。</summary>
    Log,
}

/// <summary><see cref="DashboardView"/> の表示名と切り替え。</summary>
internal static class DashboardViews
{
    /// <summary>上部ボタンに出す名前。</summary>
    public static string Label(this DashboardView view) => view switch
    {
        DashboardView.Plugins => "plugins",
        DashboardView.Lsp => "lsp",
        _ => "log",
    };

    /// <summary>次のビュー（plugins → lsp → log → plugins の順に巡回する）。</summary>
    public static DashboardView Other(this DashboardView view) => view switch
    {
        DashboardView.Plugins => DashboardView.Lsp,
        DashboardView.Lsp => DashboardView.Log,
        _ => DashboardView.Plugins,
    };
}
