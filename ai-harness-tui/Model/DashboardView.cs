namespace ai_harness_tui;

/// <summary>
/// 本体に出すビュー。上部のボタン（タブ）で切り替える単一表示で、同時に 2 つは出さない。
/// </summary>
internal enum DashboardView
{
    /// <summary>選択中の対象のプラグイン。</summary>
    Plugins,

    /// <summary>選択中の対象のログ。</summary>
    Log,
}

/// <summary><see cref="DashboardView"/> の表示名と切り替え。</summary>
internal static class DashboardViews
{
    /// <summary>上部ボタンに出す名前。</summary>
    public static string Label(this DashboardView view) =>
        view == DashboardView.Plugins ? "plugins" : "log";

    /// <summary>もう一方のビュー（ボタンは 2 つなので切り替えは反転）。</summary>
    public static DashboardView Other(this DashboardView view) =>
        view == DashboardView.Plugins ? DashboardView.Log : DashboardView.Plugins;
}
