import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/core_transport.dart';
import 'launch_progress_dialog.dart';

/// Shows the structured launch diagnostics returned by the C# module. The
/// details payload is optional, so older cores still get a normal failure
/// dialog with the localized message.
Future<void> showLaunchFailureDialog(
  BuildContext context, {
  required AppLocalizations l10n,
  required String message,
  required CoreException error,
}) => showDialog<void>(
  context: context,
  builder: (context) =>
      LaunchFailureDialog(l10n: l10n, message: message, error: error),
);

class LaunchFailureDialog extends StatefulWidget {
  const LaunchFailureDialog({
    super.key,
    required this.l10n,
    required this.message,
    required this.error,
  });

  final AppLocalizations l10n;
  final String message;
  final CoreException error;

  @override
  State<LaunchFailureDialog> createState() => _LaunchFailureDialogState();
}

class _LaunchFailureDialogState extends State<LaunchFailureDialog> {
  late final Map<String, Object?>? _details = _decodeDetails(
    widget.error.detail,
  );
  bool _copied = false;

  static Map<String, Object?>? _decodeDetails(String? raw) {
    if (raw == null || raw.isEmpty) return null;
    try {
      final decoded = jsonDecode(raw);
      if (decoded is Map) return decoded.cast<String, Object?>();
    } catch (_) {
      // Older cores may put a human string in `detail`; ignore it here.
    }
    return null;
  }

  String? get _stage => _details?['stage']?.toString();
  String? get _javaPath => _details?['javaPath']?.toString();
  String? get _javaVersion => _details?['javaVersion']?.toString();
  String? get _logPath => _details?['logPath']?.toString();
  int? get _javaRequired => (_details?['javaRequired'] as num?)?.toInt();
  bool get _retryable => _details?['retryable'] == true;

  bool get _needsJava =>
      widget.error.code == 'JAVA_MISSING' ||
      widget.error.code == 'JAVA_VERSION_MISMATCH';

  String get _diagnostics => [
    'ArchoeraCX launch failure',
    'stage: ${_stage ?? ''}',
    'code: ${widget.error.code}',
    'message: ${widget.error.message}',
    if (_javaPath != null) 'javaPath: $_javaPath',
    if (_javaVersion != null) 'javaVersion: $_javaVersion',
    if (_javaRequired != null) 'javaRequired: $_javaRequired',
    if (_logPath != null) 'logPath: $_logPath',
  ].join('\n');

  Future<void> _copy() async {
    await Clipboard.setData(ClipboardData(text: _diagnostics));
    if (mounted) setState(() => _copied = true);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final theme = Theme.of(context);
    return AlertDialog(
      title: Text(l10n.launchFailureTitle),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(widget.message, style: theme.textTheme.bodyMedium),
            const SizedBox(height: 16),
            if (_stage != null)
              _LabelValue(
                label: l10n.launchFailureStage,
                value: launchStageTokenLabel(l10n, _stage),
              ),
            _LabelValue(
              label: l10n.launchFailureCode,
              value: widget.error.code,
            ),
            if (_javaPath != null)
              _LabelValue(
                label: l10n.launchFailureJava,
                value: _javaVersion == null
                    ? _javaPath!
                    : '$_javaPath ($_javaVersion)',
              ),
            if (_javaRequired != null)
              _LabelValue(
                label: l10n.launchFailureJavaRequired,
                value: 'Java $_javaRequired',
              ),
            if (_logPath != null)
              _LabelValue(label: l10n.launchFailureLog, value: _logPath!),
            if (_retryable) ...[
              const SizedBox(height: 8),
              Text(
                l10n.launchFailureRetryable,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.primary,
                ),
              ),
            ],
            if (_copied) ...[
              const SizedBox(height: 8),
              Text(
                l10n.launchFailureCopied,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.primary,
                ),
              ),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: _copy, child: Text(l10n.launchFailureCopy)),
        if (_needsJava)
          TextButton(
            key: const ValueKey('launch-install-java'),
            onPressed: () {
              Navigator.of(context).pop();
              final major = _javaRequired;
              context.push(
                '/settings/java/install'
                '${major == null ? '' : '?major=$major'}',
              );
            },
            child: Text(l10n.launchFailureInstallJava),
          ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.commonClose),
        ),
      ],
    );
  }
}

class _LabelValue extends StatelessWidget {
  const _LabelValue({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 84,
            child: Text(
              label,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ),
          Expanded(
            child: SelectableText(
              value,
              style: theme.textTheme.bodySmall?.copyWith(
                fontFamily: 'monospace',
              ),
            ),
          ),
        ],
      ),
    );
  }
}
