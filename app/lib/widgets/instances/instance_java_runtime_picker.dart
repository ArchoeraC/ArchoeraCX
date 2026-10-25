import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/java/java_scan.dart';
import '../../stores/java.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

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

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final runtimes = ref.watch(javaRuntimesProvider);
    final tokens = context.tokens;
    return MenuAnchor(
      style: MenuStyle(
        alignment: AlignmentDirectional.bottomEnd,
        minimumSize: const WidgetStatePropertyAll(Size(380, 0)),
        maximumSize: const WidgetStatePropertyAll(Size(520, 420)),
        backgroundColor: WidgetStatePropertyAll(tokens.surface1),
        surfaceTintColor: const WidgetStatePropertyAll(Colors.transparent),
        shape: WidgetStatePropertyAll(
          BeveledRectangleBorder(
            side: BorderSide(color: tokens.border),
            borderRadius: BorderRadius.only(
              topRight: Radius.circular(tokens.bevelL),
              bottomLeft: Radius.circular(tokens.bevelS),
            ),
          ),
        ),
      ),
      menuChildren: _entries(context, ref, runtimes),
      builder: (context, controller, child) => IconButton(
        key: const ValueKey('instance-java-picker'),
        tooltip: l10n.instanceSettingsJavaPick,
        onPressed: enabled
            ? () => controller.isOpen ? controller.close() : controller.open()
            : null,
        icon: const Icon(AppIcons.chevronDown, size: 20),
      ),
    );
  }

  List<Widget> _entries(
    BuildContext context,
    WidgetRef ref,
    AsyncValue<List<JavaRuntime>> runtimes,
  ) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = runtimes.value ?? const <JavaRuntime>[];

    return [
      if (selectedPath.isNotEmpty)
        MenuItemButton(
          onPressed: () => onSelected(''),
          leadingIcon: const Icon(AppIcons.reset, size: 18),
          child: Text(l10n.instanceSettingsJavaFollowGlobal),
        ),
      for (final runtime in items)
        MenuItemButton(
          onPressed: () => onSelected(runtime.path),
          leadingIcon: Icon(
            runtime.path == selectedPath ? AppIcons.check : AppIcons.java,
            size: 18,
            color: runtime.path == selectedPath ? tokens.accent : null,
          ),
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
      if (runtimes.isLoading)
        MenuItemButton(
          onPressed: null,
          leadingIcon: const SizedBox(
            width: 16,
            height: 16,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
          child: Text(l10n.instanceSettingsJavaScanning),
        )
      else if (runtimes.hasError)
        MenuItemButton(
          onPressed: () => ref.read(javaRuntimesProvider.notifier).rescan(),
          leadingIcon: Icon(AppIcons.error, size: 18, color: tokens.danger),
          child: Text(l10n.instanceSettingsJavaScanFailed),
        )
      else if (items.isEmpty)
        MenuItemButton(
          onPressed: null,
          leadingIcon: Icon(
            AppIcons.info,
            size: 18,
            color: tokens.textTertiary,
          ),
          child: Text(l10n.instanceSettingsJavaNone),
        ),
      const Divider(height: 1),
      MenuItemButton(
        onPressed: () => ref.read(javaRuntimesProvider.notifier).rescan(),
        leadingIcon: const Icon(AppIcons.refresh, size: 18),
        child: Text(l10n.instanceSettingsJavaRescan),
      ),
    ];
  }
}
