namespace ai_harness_tui;

/// <summary>プラグイン 1 件の表示行。</summary>
/// <param name="Name">PluginName。</param>
/// <param name="Enabled">対象プロジェクトの <c>common.yml</c> で有効か。lib 一覧表示時は <c>null</c>。</param>
internal readonly record struct PluginRow(string Name, bool? Enabled);
