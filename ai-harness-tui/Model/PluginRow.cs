namespace ai_harness_tui;

/// <summary>プラグイン 1 件の表示行。</summary>
/// <param name="Name">PluginName。</param>
/// <param name="Enabled">
/// 対象プロジェクトの <c>common.yml</c> で有効か。lib の一覧表示時は <c>null</c>
/// （<c>--plugin</c> がプロジェクト無指定のとき enabled 列を返さないため）。
/// </param>
/// <param name="Description">
/// プラグインの 1 行説明。lib の一覧表示時のみ得られる（プロジェクト指定時は enabled 列に置き換わる）。
/// 説明を実装していないプラグインは <c>-</c>。
/// </param>
internal readonly record struct PluginRow(string Name, bool? Enabled, string Description);
