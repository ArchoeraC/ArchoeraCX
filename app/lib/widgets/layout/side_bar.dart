import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

class AppDestination {
  const AppDestination({
    required this.path,
    required this.label,
    required this.icon,
    required this.selectedIcon,
  });

  final String path;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
}

class AppSidebar extends StatelessWidget {
  const AppSidebar({
    super.key,
    required this.destinations,
    required this.currentIndex,
    required this.collapsed,
    required this.onSelected,
    required this.coreStatusLabel,
    required this.coreStatusHint,
    required this.version,
  });

  final List<AppDestination> destinations;
  final int currentIndex;
  final bool collapsed;
  final ValueChanged<int> onSelected;
  final String coreStatusLabel;
  final String coreStatusHint;
  final String version;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return AnimatedContainer(
      duration: tokens.normal,
      curve: tokens.curve,
      width: collapsed
          ? AppLayout.sidebarCollapsedWidth
          : AppLayout.sidebarWidth,
      decoration: BoxDecoration(
        color: tokens.surface1.withValues(alpha: 0.72),
        border: Border(right: BorderSide(color: tokens.border)),
      ),
      child: SafeArea(
        top: false,
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SizedBox(height: AppSpacing.md),
            _Brand(collapsed: collapsed),
            const SizedBox(height: AppSpacing.lg),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
                children: [
                  for (var i = 0; i < destinations.length; i++)
                    Padding(
                      padding: const EdgeInsets.only(bottom: AppSpacing.xxs),
                      child: _NavItem(
                        destination: destinations[i],
                        selected: i == currentIndex,
                        collapsed: collapsed,
                        onTap: () => onSelected(i),
                      ),
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(AppSpacing.sm),
              child: _CoreStatus(
                collapsed: collapsed,
                label: coreStatusLabel,
                hint: coreStatusHint,
                version: version,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Brand extends StatelessWidget {
  const _Brand({required this.collapsed});

  final bool collapsed;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: EdgeInsets.symmetric(horizontal: collapsed ? 0 : AppSpacing.md),
      child: Row(
        mainAxisAlignment: collapsed
            ? MainAxisAlignment.center
            : MainAxisAlignment.start,
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [tokens.accent, tokens.accentPressed],
              ),
              borderRadius: BorderRadius.circular(tokens.radiusS),
            ),
            child: Icon(
              Icons.grid_view_rounded,
              size: 18,
              color: tokens.onAccent,
            ),
          ),
          if (!collapsed) ...[
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: Text(
                'ArchoeraCX',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _NavItem extends StatefulWidget {
  const _NavItem({
    required this.destination,
    required this.selected,
    required this.collapsed,
    required this.onTap,
  });

  final AppDestination destination;
  final bool selected;
  final bool collapsed;
  final VoidCallback onTap;

  @override
  State<_NavItem> createState() => _NavItemState();
}

class _NavItemState extends State<_NavItem> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final selected = widget.selected;
    final background = selected
        ? tokens.accentSoft
        : (_hovered ? tokens.surface2 : Colors.transparent);
    final foreground = selected ? tokens.accent : tokens.textSecondary;

    final content = AnimatedContainer(
      duration: tokens.fast,
      curve: tokens.curve,
      height: 44,
      padding: EdgeInsets.symmetric(
        horizontal: widget.collapsed ? 0 : AppSpacing.sm,
      ),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(tokens.radiusM),
      ),
      child: Row(
        mainAxisAlignment: widget.collapsed
            ? MainAxisAlignment.center
            : MainAxisAlignment.start,
        children: [
          if (selected)
            Container(
              width: 3,
              height: 18,
              margin: EdgeInsets.only(
                right: widget.collapsed ? 0 : AppSpacing.xs,
              ),
              decoration: BoxDecoration(
                color: tokens.accent,
                borderRadius: BorderRadius.circular(999),
              ),
            ),
          Icon(
            selected
                ? widget.destination.selectedIcon
                : widget.destination.icon,
            size: 20,
            color: foreground,
          ),
          if (!widget.collapsed) ...[
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: Text(
                widget.destination.label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: foreground,
                  fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                ),
              ),
            ),
          ],
        ],
      ),
    );

    final interactive = MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onTap,
        child: content,
      ),
    );

    if (widget.collapsed) {
      return Tooltip(message: widget.destination.label, child: interactive);
    }
    return interactive;
  }
}

class _CoreStatus extends StatelessWidget {
  const _CoreStatus({
    required this.collapsed,
    required this.label,
    required this.hint,
    required this.version,
  });

  final bool collapsed;
  final String label;
  final String hint;
  final String version;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    if (collapsed) {
      return Tooltip(
        message: '$label\n$hint',
        child: Container(
          height: 44,
          decoration: BoxDecoration(
            color: tokens.surface2,
            borderRadius: BorderRadius.circular(tokens.radiusM),
          ),
          child: Icon(
            Icons.cloud_off_rounded,
            size: 18,
            color: tokens.textTertiary,
          ),
        ),
      );
    }

    return Container(
      padding: const EdgeInsets.all(AppSpacing.sm),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: tokens.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                Icons.cloud_off_rounded,
                size: 14,
                color: tokens.textTertiary,
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelMedium,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.xxs),
          Text(
            hint,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary, fontSize: 11),
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'v$version',
            style: Theme.of(context).textTheme.labelSmall
                ?.copyWith(color: tokens.textTertiary, fontFamily: 'monospace'),
          ),
        ],
      ),
    );
  }
}
