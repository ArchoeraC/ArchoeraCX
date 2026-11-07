import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/diagnostics/crash_analysis.dart';
import '../../theme/icons/app_icons.dart';
import '../common/toast.dart';

/// Shows the automatic diagnosis produced after a game process exits.
Future<void> showCrashReportDialog(
  BuildContext context,
  CrashAnalysis analysis,
) {
  final l10n = AppLocalizations.of(context);
  final finding = analysis.primary;
  final text = finding == null ? null : _findingText(l10n, finding);
  final evidence = finding?.evidence ?? const <String>[];

  return showDialog<void>(
    context: context,
    builder: (context) => AlertDialog(
      title: Row(
        children: [
          const Icon(Icons.warning_amber_rounded, size: 22),
          const SizedBox(width: 10),
          Expanded(child: Text(l10n.crashDialogTitle)),
        ],
      ),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              if (analysis.exitCode != null) ...[
                Text(
                  l10n.crashDialogExitCode(analysis.exitCode!),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                if (_isUserTerminationSignal(analysis.exitCode))
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(
                      l10n.crashDialogForceStopHint,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
              if (text != null) ...[
                const SizedBox(height: 12),
                Text(text.title, style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 6),
                Text(text.detail),
                const SizedBox(height: 10),
                Text(text.fix, style: Theme.of(context).textTheme.bodySmall),
              ] else
                Text(l10n.crashDialogNoDetails),
              const SizedBox(height: 14),
              _ShareHint(text: l10n.crashDialogShareHint),
              if (evidence.isNotEmpty) ...[
                const SizedBox(height: 14),
                ExpansionTile(
                  tilePadding: EdgeInsets.zero,
                  childrenPadding: EdgeInsets.zero,
                  title: Text(l10n.crashDialogEvidence),
                  children: [
                    for (final line in evidence)
                      Align(
                        alignment: Alignment.centerLeft,
                        child: SelectableText(
                          line,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ),
                  ],
                ),
              ],
              if (analysis.logPath != null) ...[
                const SizedBox(height: 10),
                SelectableText(
                  analysis.logPath!,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () async {
            await Clipboard.setData(
              ClipboardData(text: _clipboardText(l10n, analysis, text)),
            );
            if (context.mounted) {
              toast(l10n.crashDialogCopied, type: ToastType.success);
            }
          },
          child: Text(l10n.crashDialogCopy),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.crashDialogClose),
        ),
      ],
    ),
  );
}

/// POSIX reports a signal death as 128 + signal, and .NET uses -1 for
/// `TerminateProcess` on Windows. Only user-sent signals get the "ignore if
/// you did this" note: SIGSEGV/SIGABRT (139/134) are real crashes.
bool _isUserTerminationSignal(int? code) =>
    code == -1 || // Windows TerminateProcess
    code == 129 || // SIGHUP
    code == 130 || // SIGINT
    code == 137 || // SIGKILL, the launcher's force stop
    code == 143; // SIGTERM

/// The classic launcher reminder: ask for help with the log, not a screenshot
/// of this dialog.
class _ShareHint extends StatelessWidget {
  const _ShareHint({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(
        color: colors.secondaryContainer,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(AppIcons.info, size: 18, color: colors.onSecondaryContainer),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: colors.error,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

String _clipboardText(
  AppLocalizations l10n,
  CrashAnalysis analysis,
  ({String title, String detail, String fix})? finding,
) {
  final buffer = StringBuffer()
    ..writeln(l10n.crashDialogTitle)
    ..writeln('outcome: ${analysis.outcome}');
  if (analysis.exitCode != null) {
    buffer.writeln('exitCode: ${analysis.exitCode}');
  }
  if (finding != null) {
    buffer
      ..writeln()
      ..writeln(finding.title)
      ..writeln(finding.detail)
      ..writeln(finding.fix);
  }
  for (final item in analysis.findings) {
    if (item.evidence.isEmpty) continue;
    buffer.writeln();
    buffer.writeln('[${item.ruleId}]');
    for (final line in item.evidence) {
      buffer.writeln(line);
    }
  }
  if (analysis.logPath != null) {
    buffer
      ..writeln()
      ..writeln('log: ${analysis.logPath}');
  }
  return buffer.toString().trimRight();
}

({String title, String detail, String fix}) _findingText(
  AppLocalizations l10n,
  CrashFinding finding,
) {
  String arg(String name) => finding.args[name] ?? '';
  return switch (finding.ruleId) {
    'java.too_old' => (
      title: l10n.crashRuleJavaTooOldTitle,
      detail: l10n.crashRuleJavaTooOldDetail(arg('needed'), arg('current')),
      fix: l10n.crashRuleJavaTooOldFix(arg('needed')),
    ),
    'java.too_new' => (
      title: l10n.crashRuleJavaTooNewTitle,
      detail: l10n.crashRuleJavaTooNewDetail,
      fix: l10n.crashRuleJavaTooNewFix,
    ),
    'jvm.heap' => (
      title: l10n.crashRuleHeapTitle,
      detail: l10n.crashRuleHeapDetail,
      fix: l10n.crashRuleHeapFix,
    ),
    'mixin.failed' => (
      title: l10n.crashRuleMixinTitle,
      detail: l10n.crashRuleMixinDetail(arg('config')),
      fix: l10n.crashRuleMixinFix,
    ),
    'deps.missing' => (
      title: l10n.crashRuleDependencyTitle,
      detail: l10n.crashRuleDependencyDetail,
      fix: l10n.crashRuleDependencyFix,
    ),
    'loader.main_class' => (
      title: l10n.crashRuleMainClassTitle,
      detail: l10n.crashRuleMainClassDetail(arg('cls')),
      fix: l10n.crashRuleMainClassFix,
    ),
    'gfx.glfw' => (
      title: l10n.crashRuleGraphicsTitle,
      detail: l10n.crashRuleGraphicsDetail,
      fix: l10n.crashRuleGraphicsFix,
    ),
    'mods.entrypoint' => (
      title: l10n.crashRuleModTitle,
      detail: l10n.crashRuleModDetail(arg('mod'), arg('stage')),
      fix: l10n.crashRuleModFix,
    ),
    'io.file_locked' => (
      title: l10n.crashRuleFileLockedTitle,
      detail: l10n.crashRuleFileLockedDetail,
      fix: l10n.crashRuleFileLockedFix,
    ),
    'gfx.driver_crash' => (
      title: l10n.crashRuleDriverTitle,
      detail: l10n.crashRuleDriverDetail(arg('lib')),
      fix: l10n.crashRuleDriverFix,
    ),
    'jvm.native_crash' => (
      title: l10n.crashRuleNativeTitle,
      detail: l10n.crashRuleNativeDetail(arg('frame')),
      fix: l10n.crashRuleNativeFix,
    ),
    'crash.report' => (
      title: l10n.crashRuleReportTitle,
      detail: l10n.crashRuleReportDetail(arg('description')),
      fix: l10n.crashRuleReportFix,
    ),
    _ => (
      title: l10n.crashRuleUnknownTitle,
      detail: l10n.crashRuleUnknownDetail(arg('code')),
      fix: l10n.crashRuleUnknownFix,
    ),
  };
}
