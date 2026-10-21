import 'package:flutter/material.dart';

import '../../theme/tokens.dart';
import '../common/app_panel.dart';

/// Settings destinations shared by the wide side navigation and the narrow
/// horizontal tab strip.
enum SettingsSection { appearance, storage, java, network, compliance, about }

class SettingsNavEntry {
  const SettingsNavEntry({
    required this.section,
    required this.label,
    required this.icon,
  });

  final SettingsSection section;
  final String label;
  final IconData icon;
}

/// Vertical settings navigation used by the focused two-pane layout.
class SettingsSideNav extends StatelessWidget {
  const SettingsSideNav({
    super.key,
    required this.entries,
    required this.selected,
    required this.headerLabel,
    required this.onSelected,
  });

  final List<SettingsNavEntry> entries;
  final SettingsSection selected;
  final String headerLabel;
  final ValueChanged<SettingsSection> onSelected;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return AppPanel(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
              AppSpacing.md,
              AppSpacing.xs,
              AppSpacing.md,
              AppSpacing.sm,
            ),
            child: Row(
              children: [
                Container(width: 16, height: 2, color: tokens.accent),
                const SizedBox(width: AppSpacing.xs),
                Text(
                  headerLabel,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
          ),
          for (final entry in entries)
            _SideNavTile(
              entry: entry,
              selected: entry.section == selected,
              onTap: () => onSelected(entry.section),
            ),
        ],
      ),
    );
  }
}

class _SideNavTile extends StatelessWidget {
  const _SideNavTile({
    required this.entry,
    required this.selected,
    required this.onTap,
  });

  final SettingsNavEntry entry;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final foreground = selected ? tokens.accent : tokens.textPrimary;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          height: 42,
          decoration: BoxDecoration(
            color: selected ? tokens.accentSoft : null,
            border: Border(
              left: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
          child: Row(
            children: [
              Icon(
                entry.icon,
                size: 18,
                color: selected ? tokens.accent : tokens.textSecondary,
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  entry.label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: foreground,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Horizontal settings navigation used below the two-pane breakpoint.
class SettingsTabStrip extends StatelessWidget {
  const SettingsTabStrip({
    super.key,
    required this.entries,
    required this.selected,
    required this.onSelected,
  });

  final List<SettingsNavEntry> entries;
  final SettingsSection selected;
  final ValueChanged<SettingsSection> onSelected;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: tokens.border)),
      ),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            for (final entry in entries)
              _TabTile(
                entry: entry,
                selected: entry.section == selected,
                onTap: () => onSelected(entry.section),
              ),
          ],
        ),
      ),
    );
  }
}

class _TabTile extends StatelessWidget {
  const _TabTile({
    required this.entry,
    required this.selected,
    required this.onTap,
  });

  final SettingsNavEntry entry;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final foreground = selected ? tokens.accent : tokens.textSecondary;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          height: 46,
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
          decoration: BoxDecoration(
            color: selected ? tokens.accentSoft : null,
            border: Border(
              bottom: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(entry.icon, size: 17, color: foreground),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                entry.label,
                maxLines: 1,
                style: Theme.of(context).textTheme.labelLarge
                    ?.copyWith(color: foreground, fontWeight: FontWeight.w500),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
