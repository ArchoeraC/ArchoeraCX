import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/launch_options.dart';
import '../render/app_shaders.dart';
import '../render/dissolve_mask.dart';
import '../services/core/settings/app_settings.dart';
import '../services/core/core_client.dart';
import '../services/core/system/system_memory.dart';
import '../services/core/downloads/update_check.dart';
import '../stores/account_policy.dart';
import '../stores/appearance.dart';
import '../stores/instances.dart';
import '../stores/locale.dart';
import '../stores/platform.dart';
import '../stores/preferences.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/absolute_path.dart';
import '../utils/window_mode.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/safe_url.dart';
import '../utils/app_info.dart';
import '../utils/format.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/confirm.dart';
import '../widgets/common/toast.dart';

import 'package:go_router/go_router.dart';

import '../widgets/common/app_page_header.dart';

part 'settings/common.dart';
part 'settings/controls.dart';
part 'settings/accent.dart';
part 'settings/appearance_panel.dart';
part 'settings/download_panel.dart';
part 'settings/storage_panel.dart';
part 'settings/memory_panel.dart';
part 'settings/launch_panel.dart';
part 'settings/performance_panel.dart';
part 'settings/native_panel.dart';
part 'settings/tasks_panel.dart';
part 'settings/about_panel.dart';
part 'settings/update_check_row.dart';
part 'settings/nav.dart';
part 'settings/section_switcher.dart';

class SettingsPage extends ConsumerStatefulWidget {
  const SettingsPage({super.key});

  @override
  ConsumerState<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends ConsumerState<SettingsPage> {
  _SettingsSection _section = _SettingsSection.appearance;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final settings = ref.watch(appSettingsProvider).value;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AppPageHeader(
          title: l10n.settingsTitle,
          subtitle: l10n.settingsSubtitle,
        ),
        const SizedBox(height: AppSpacing.md),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 820;
              final content = _SectionSwitcher(
                duration: tokens.normal,
                child: KeyedSubtree(
                  key: ValueKey(_section),
                  child: _sectionContent(context, l10n, settings),
                ),
              );

              if (wide) {
                // Center the navigation + content block and use more of a
                // fullscreen window; 920 px left the right half empty.
                final contentWidth =
                    (constraints.maxWidth - 208 - AppSpacing.lg).clamp(
                      640.0,
                      1500.0,
                    );
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    SizedBox(
                      width: 208,
                      child: _SettingsNav(
                        selected: _section,
                        onSelected: (value) => setState(() => _section = value),
                      ),
                    ),
                    const SizedBox(width: AppSpacing.lg),
                    Expanded(
                      child: SingleChildScrollView(
                        padding: const EdgeInsets.only(bottom: AppSpacing.xl),
                        child: Align(
                          alignment: Alignment.topCenter,
                          child: ConstrainedBox(
                            constraints: BoxConstraints(maxWidth: contentWidth),
                            child: content,
                          ),
                        ),
                      ),
                    ),
                  ],
                );
              }

              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _SettingsTabs(
                    selected: _section,
                    onSelected: (value) => setState(() => _section = value),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  Expanded(
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
                      child: content,
                    ),
                  ),
                ],
              );
            },
          ),
        ),
      ],
    );
  }

  Widget _sectionContent(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) {
    final locale = ref.watch(localeProvider);
    final appearance = ref.watch(appearanceProvider);
    final systemAppearance = ref.watch(systemAppearanceProvider).value;
    final systemAccent = systemAppearance?.accent;
    return switch (_section) {
      _SettingsSection.appearance => _appearanceContent(
        context,
        l10n,
        appearance,
        systemAccent,
        locale,
      ),
      _SettingsSection.launch => _LaunchSettingsPanel(settings: settings),
      _SettingsSection.download => _downloadContent(context, l10n, settings),
      _SettingsSection.storage => _storageContent(context, l10n, settings),
      _SettingsSection.java => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Java selection (auto/manual) and the current runtime live on the
          // dedicated Java page; this section only links to it and keeps the
          // memory/GC/JVM/game argument panel.
          _SettingRow(
            icon: AppIcons.java,
            title: l10n.javaPageManage,
            subtitle: l10n.javaPageManageHint,
            onTap: () => context.go('/settings/java'),
          ),
          const SizedBox(height: AppSpacing.md),
          _RuntimeSettingsPanel(settings: settings),
        ],
      ),
      _SettingsSection.tasks => const _TasksSettingsPanel(),
      _SettingsSection.notifications => const _NotificationsSettingsPanel(),
      _SettingsSection.about => _AboutPanel(settings: settings),
    };
  }
}
