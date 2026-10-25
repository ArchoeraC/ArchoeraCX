import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/java/java_scan.dart';
import '../../stores/java.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_menu_button.dart';

/// Dropdown attached to the Java path field that lists scanned runtimes.
///
/// The field itself stays a plain [TextField]: a runtime the scanner cannot
/// find (a portable JRE, a wrapper script) is still saved verbatim, while the
/// menu offers the entries the shared scanner already reported.
class InstanceJavaRuntimePicker extends ConsumerWidget {
  const InstanceJavaRuntimePicker({
    super.key,
    required this.l10n,
    required this.selectedPath,
    required this.enabled,
    required this.onSelected,
  });

  final AppLocalizations l10n;
  final String selectedPath;
  final bool enabled;
  final ValueChanged<String> onSelected;

  static const _rescan = '\u0000rescan';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final runtimes = ref.watch(javaRuntimesProvider);
    return AppMenuButton<String>(
      key: const ValueKey('instance-java-picker'),
      tooltip: l10n.instanceSettingsJavaPick,
      enabled: enabled,
      icon: AppIcons.chevronDown,
      iconSize: 20,
      constraints: const BoxConstraints(
        minWidth: 380,
        maxWidth: 520,
        maxHeight: 420,
      ),
      onSelected: (value) {
        if (value == _rescan) {
          ref.read(javaRuntimesProvider.notifier).rescan();
          return;
        }
        onSelected(value);
      },
      itemBuilder: (context) => _entries(context, ref, runtimes),
    );
  }

  List<PopupMenuEntry<String>> _entries(
    BuildContext context,
    WidgetRef ref,
    AsyncValue<List<JavaRuntime>> runtimes,
  ) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = runtimes.value ?? const <JavaRuntime>[];

    return [
      if (selectedPath.isNotEmpty)
        PopupMenuItem<String>(
          value: '',
          child: Row(
            children: [
              const Icon(AppIcons.reset, size: 18),
              const SizedBox(width: AppSpacing.xs),
              Flexible(child: Text(l10n.instanceSettingsJavaFollowGlobal)),
            ],
          ),
        ),
      for (final runtime in items)
        PopupMenuItem<String>(
          value: runtime.path,
          height: 56,
          child: Row(
            children: [
              Icon(
                runtime.path == selectedPath ? AppIcons.check : AppIcons.java,
                size: 18,
                color: runtime.path == selectedPath ? tokens.accent : null,
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(runtime.displayVersion, style: textTheme.labelLarge),
                    Text(
                      runtime.path,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      if (runtimes.isLoading)
        PopupMenuItem<String>(
          enabled: false,
          child: Row(
            children: [
              const SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
              const SizedBox(width: AppSpacing.xs),
              Text(l10n.instanceSettingsJavaScanning),
            ],
          ),
        )
      else if (runtimes.hasError)
        PopupMenuItem<String>(
          value: _rescan,
          child: Row(
            children: [
              Icon(AppIcons.error, size: 18, color: tokens.danger),
              const SizedBox(width: AppSpacing.xs),
              Flexible(child: Text(l10n.instanceSettingsJavaScanFailed)),
            ],
          ),
        )
      else if (items.isEmpty)
        PopupMenuItem<String>(
          enabled: false,
          child: Row(
            children: [
              Icon(AppIcons.info, size: 18, color: tokens.textTertiary),
              const SizedBox(width: AppSpacing.xs),
              Flexible(child: Text(l10n.instanceSettingsJavaNone)),
            ],
          ),
        ),
      const PopupMenuDivider(),
      PopupMenuItem<String>(
        value: _rescan,
        child: Row(
          children: [
            const Icon(AppIcons.refresh, size: 18),
            const SizedBox(width: AppSpacing.xs),
            Flexible(child: Text(l10n.instanceSettingsJavaRescan)),
          ],
        ),
      ),
    ];
  }
}
