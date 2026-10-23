part of '../shell.dart';

/// Full-width footer command bar.
///
/// Navigation lives on the left, account and launch actions on the right. This
/// keeps the middle of the window free for content and avoids a floating dock
/// competing with page cards.
class _CommandBar extends StatelessWidget {
  const _CommandBar({
    required this.contentMaxWidth,
    required this.destinations,
    required this.currentIndex,
    required this.account,
    required this.selectedInstance,
    required this.onSelected,
    required this.onAccount,
    required this.onChooseInstance,
    required this.onLaunch,
  });

  final double contentMaxWidth;
  final List<AppDestination> destinations;
  final int currentIndex;
  final Account? account;
  final GameInstance? selectedInstance;
  final ValueChanged<int> onSelected;
  final VoidCallback onAccount;
  final VoidCallback onChooseInstance;
  final VoidCallback onLaunch;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);

    return SafeArea(
      top: false,
      child: AnimatedContainer(
        duration: context.tokens.normal,
        curve: Curves.easeOutCubic,
        height: currentIndex == 0
            ? AppLayout.homeBarHeight
            : AppLayout.bottomBarHeight,
        decoration: BoxDecoration(
          color: tokens.surface1.withValues(alpha: 0.86),
          border: Border(top: BorderSide(color: tokens.border)),
        ),
        child: Center(
          child: ConstrainedBox(
            constraints: BoxConstraints(maxWidth: contentMaxWidth),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final compactNav = constraints.maxWidth < 820;
                  final compactAccount = constraints.maxWidth < 900;
                  final compactLaunch = constraints.maxWidth < 680;

                  return Material(
                    color: Colors.transparent,
                    child: Row(
                      children: [
                        Expanded(
                          child: SingleChildScrollView(
                            scrollDirection: Axis.horizontal,
                            child: Row(
                              children: [
                                for (final destination in destinations)
                                  Padding(
                                    padding: const EdgeInsets.only(
                                      right: AppSpacing.xxs,
                                    ),
                                    child: _NavItem(
                                      key: ValueKey('nav-${destination.path}'),
                                      destination: destination,
                                      selected:
                                          destination.branchIndex ==
                                          currentIndex,
                                      compact: compactNav,
                                      onTap: () =>
                                          onSelected(destination.branchIndex),
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        ),
                        const SizedBox(width: AppSpacing.md),
                        Container(width: 1, height: 30, color: tokens.border),
                        const SizedBox(width: AppSpacing.md),
                        _AccountButton(
                          account: account,
                          compact: compactAccount,
                          onTap: onAccount,
                        ),
                        // The launch action only belongs to the non-home bar;
                        // slide + fade it so switching pages does not pop it in.
                        AnimatedSwitcher(
                          duration: tokens.normal,
                          switchInCurve: Curves.easeOutCubic,
                          switchOutCurve: Curves.easeInCubic,
                          transitionBuilder: (child, animation) =>
                              SizeTransition(
                                sizeFactor: animation,
                                axis: Axis.horizontal,
                                alignment: Alignment.centerLeft,
                                child: FadeTransition(
                                  opacity: animation,
                                  child: child,
                                ),
                              ),
                          layoutBuilder: (currentChild, previousChildren) =>
                              Stack(
                                alignment: Alignment.centerRight,
                                children: [...previousChildren, ?currentChild],
                              ),
                          child: currentIndex == 0
                              ? const SizedBox.shrink(
                                  key: ValueKey('launch-action-hidden'),
                                )
                              : Padding(
                                  key: const ValueKey('launch-action'),
                                  padding: const EdgeInsets.only(
                                    left: AppSpacing.sm,
                                  ),
                                  child: _LaunchButton(
                                    key: const ValueKey('command-bar-launch'),
                                    compact: compactLaunch,
                                    hasInstance: selectedInstance != null,
                                    onPressed: selectedInstance == null
                                        ? onChooseInstance
                                        : onLaunch,
                                    label: selectedInstance == null
                                        ? l10n.selectInstance
                                        : l10n.launch,
                                    tooltip: selectedInstance == null
                                        ? l10n.selectInstance
                                        : l10n.launchWithInstance(
                                            selectedInstance!.name,
                                          ),
                                  ),
                                ),
                        ),
                      ],
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    super.key,
    required this.destination,
    required this.selected,
    required this.compact,
    required this.onTap,
  });

  final AppDestination destination;
  final bool selected;
  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final foreground = selected ? tokens.accent : tokens.textSecondary;

    return Tooltip(
      message: destination.label,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          height: 40,
          padding: EdgeInsets.symmetric(
            horizontal: compact ? AppSpacing.sm : AppSpacing.md,
          ),
          decoration: BoxDecoration(
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
              Icon(
                selected ? destination.selectedIcon : destination.icon,
                size: 18,
                color: foreground,
              ),
              if (!compact) ...[
                const SizedBox(width: AppSpacing.xs),
                Text(
                  destination.label,
                  maxLines: 1,
                  style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: foreground,
                    fontWeight: selected ? FontWeight.w500 : FontWeight.w500,
                    letterSpacing: 0,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _AccountButton extends StatelessWidget {
  const _AccountButton({
    required this.account,
    required this.compact,
    required this.onTap,
  });

  final Account? account;
  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);
    final current = account;
    final name = current?.displayName ?? l10n.addAccount;
    final detail = current?.kind.labelFor(l10n) ?? l10n.accountNoAccounts;
    final initial = current == null ? '+' : name.characters.first.toUpperCase();

    return Tooltip(
      message: name,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.xxs,
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (current != null)
                AccountAvatar(account: current, size: 30)
              else
                CircleAvatar(
                  radius: 15,
                  backgroundColor: tokens.accentSoft,
                  child: Text(
                    initial,
                    style: TextStyle(
                      color: tokens.accent,
                      fontSize: 12,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
              if (!compact) ...[
                const SizedBox(width: AppSpacing.xs),
                ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 150),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.labelMedium,
                      ),
                      Text(
                        detail,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                          letterSpacing: 0,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _LaunchButton extends StatelessWidget {
  const _LaunchButton({
    super.key,
    required this.compact,
    required this.hasInstance,
    required this.onPressed,
    required this.label,
    required this.tooltip,
  });

  final bool compact;
  final bool hasInstance;
  final VoidCallback onPressed;
  final String label;
  final String tooltip;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Tooltip(
      message: tooltip,
      child: compact
          ? IconButton.filled(
              onPressed: onPressed,
              style: IconButton.styleFrom(
                backgroundColor: tokens.accent,
                foregroundColor: tokens.onAccent,
                minimumSize: const Size(38, 38),
              ),
              icon: Icon(
                hasInstance ? AppIcons.play : AppIcons.gridView,
                size: 20,
              ),
            )
          : FilledButton.icon(
              onPressed: onPressed,
              style: FilledButton.styleFrom(
                minimumSize: const Size(0, 38),
                padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
              ),
              icon: Icon(
                hasInstance ? AppIcons.play : AppIcons.gridView,
                size: 19,
              ),
              label: Text(label),
            ),
    );
  }
}
