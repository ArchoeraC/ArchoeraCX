part of '../java_install_page.dart';

/// Install location. The chosen folder is the JDK home itself; the backend
/// refuses a non-empty folder rather than merging into an existing runtime.
class _DirectoryPanel extends StatelessWidget {
  const _DirectoryPanel({
    required this.selected,
    required this.targetPath,
    required this.custom,
    required this.enabled,
    required this.onChoose,
    required this.onUseDefault,
    required this.l10n,
  });

  final JavaDistributionVersion? selected;
  final String? targetPath;
  final bool custom;
  final bool enabled;
  final VoidCallback onChoose;
  final VoidCallback onUseDefault;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.folder, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallDirectory,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const Spacer(),
              _Badge(
                label: custom
                    ? l10n.javaInstallCustomDirectory
                    : l10n.javaInstallDefaultDirectory,
                color: custom ? tokens.accent : tokens.textSecondary,
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: AppSpacing.xs,
            ),
            decoration: BoxDecoration(
              color: tokens.surface2,
              borderRadius: BorderRadius.circular(tokens.radiusS),
              border: Border.all(color: tokens.border),
            ),
            child: SelectableText(
              targetPath ?? '—',
              maxLines: 3,
              style: textTheme.bodySmall?.copyWith(
                color: selected == null && !custom
                    ? tokens.textTertiary
                    : tokens.textPrimary,
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            l10n.javaInstallDirectoryHint,
            style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
          ),
          const SizedBox(height: AppSpacing.sm),
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              OutlinedButton.icon(
                onPressed: enabled ? onChoose : null,
                icon: const Icon(AppIcons.folder, size: 16),
                label: Text(l10n.javaInstallChooseDirectory),
              ),
              if (custom)
                TextButton.icon(
                  onPressed: enabled ? onUseDefault : null,
                  icon: const Icon(AppIcons.reset, size: 16),
                  label: Text(l10n.javaInstallUseDefault),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Archive handling: extraction is on by default, and the verified archive is
/// deleted after a successful extraction unless the user opts out.
class _InstallOptions extends StatelessWidget {
  const _InstallOptions({
    required this.autoExtract,
    required this.autoDelete,
    required this.enabled,
    required this.onAutoExtractChanged,
    required this.onAutoDeleteChanged,
    required this.showOutdatedOption,
    required this.allowOutdated,
    required this.onAllowOutdatedChanged,
    required this.l10n,
  });

  final bool autoExtract;
  final bool autoDelete;
  final bool enabled;
  final ValueChanged<bool> onAutoExtractChanged;
  final ValueChanged<bool> onAutoDeleteChanged;
  final bool showOutdatedOption;
  final bool allowOutdated;
  final ValueChanged<bool> onAllowOutdatedChanged;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.download, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallExtractOptions,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.xs),
          _OptionRow(
            value: autoExtract,
            enabled: enabled,
            title: l10n.javaInstallAutoExtract,
            hint: l10n.javaInstallAutoExtractHint,
            onChanged: onAutoExtractChanged,
          ),
          _OptionRow(
            value: autoDelete,
            enabled: enabled && autoExtract,
            title: l10n.javaInstallAutoDelete,
            hint: l10n.javaInstallAutoDeleteHint,
            onChanged: onAutoDeleteChanged,
          ),
          if (showOutdatedOption) ...[
            const SizedBox(height: AppSpacing.xs),
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.xs,
                vertical: AppSpacing.xs,
              ),
              decoration: BoxDecoration(
                color: tokens.warning.withValues(alpha: 0.08),
                border: Border(
                  left: BorderSide(color: tokens.warning, width: 2),
                ),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(AppIcons.warning, size: 16, color: tokens.warning),
                  const SizedBox(width: AppSpacing.xxs),
                  Expanded(
                    child: Text(
                      l10n.javaInstallOutdatedSelected,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.warning,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            _OptionRow(
              value: allowOutdated,
              enabled: enabled,
              title: l10n.javaInstallAllowOutdated,
              hint: l10n.javaInstallAllowOutdatedHint,
              onChanged: onAllowOutdatedChanged,
            ),
          ],
        ],
      ),
    );
  }
}

class _OptionRow extends StatelessWidget {
  const _OptionRow({
    required this.value,
    required this.enabled,
    required this.title,
    required this.hint,
    required this.onChanged,
    this.activeColor,
    this.titleColor,
    this.hintColor,
  });

  final bool value;
  final bool enabled;
  final String title;
  final String hint;
  final ValueChanged<bool> onChanged;

  /// Checked fill and outline. Used by the CVE acknowledgement so the control
  /// reads as a safety gate instead of a normal preference.
  final Color? activeColor;
  final Color? titleColor;
  final Color? hintColor;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = activeColor;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Checkbox(
          value: value,
          activeColor: color,
          checkColor: color == null ? null : tokens.onAccent,
          side: color == null ? null : BorderSide(color: color, width: 1.6),
          onChanged: enabled
              ? (next) {
                  if (next != null) onChanged(next);
                }
              : null,
        ),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.only(top: 10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: textTheme.bodyMedium?.copyWith(
                    color:
                        titleColor ??
                        (enabled ? tokens.textPrimary : tokens.textTertiary),
                  ),
                ),
                Text(
                  hint,
                  style: textTheme.bodySmall?.copyWith(
                    color: hintColor ?? tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// Progress/result footer. It stays on the page instead of closing a dialog
/// because the download and extraction are both long-running.
class _InstallBar extends StatelessWidget {
  const _InstallBar({
    required this.installing,
    required this.cancelling,
    required this.canCancel,
    required this.autoExtract,
    required this.done,
    required this.total,
    required this.rate,
    required this.phase,
    required this.error,
    required this.installed,
    required this.foreign,
    required this.platform,
    required this.canInstall,
    required this.onInstall,
    required this.onCancel,
    required this.onUseRuntime,
    required this.onBack,
    required this.l10n,
    required this.tokens,
  });

  final bool installing;
  final bool cancelling;
  final bool canCancel;
  final bool autoExtract;
  final int done;
  final int total;
  final int rate;
  final String? phase;
  final String? error;
  final JavaDistributionInstallResult? installed;
  final bool foreign;
  final JavaDistributionPlatformInfo? platform;
  final bool canInstall;
  final VoidCallback onInstall;
  final VoidCallback onCancel;
  final VoidCallback onUseRuntime;
  final VoidCallback onBack;
  final AppLocalizations l10n;
  final AppTokens tokens;

  @override
  Widget build(BuildContext context) {
    final result = installed;
    if (result != null) {
      final extracted = result.extracted;
      final path = extracted ? result.executable : (result.archive ?? '');
      return AppSection(
        padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(
                  extracted ? AppIcons.completed : AppIcons.fileDownload,
                  size: 18,
                  color: extracted ? tokens.success : tokens.accent,
                ),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Text(
                    extracted
                        ? l10n.javaInstallSuccess(result.version)
                        : l10n.javaInstallArchiveOnly,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: AppSpacing.xs),
            SelectableText(
              path,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textSecondary),
            ),
            if (foreign && platform != null) ...[
              const SizedBox(height: AppSpacing.xs),
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(AppIcons.warning, size: 16, color: tokens.danger),
                  const SizedBox(width: AppSpacing.xxs),
                  Expanded(
                    child: Text(
                      l10n.javaInstallForeignResult(
                        platform!.os,
                        platform!.arch,
                      ),
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.danger),
                    ),
                  ),
                ],
              ),
            ],
            if (extracted && result.archive != null) ...[
              const SizedBox(height: AppSpacing.xs),
              SelectableText(
                l10n.javaInstallArchiveKept(result.archive!),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
            const SizedBox(height: AppSpacing.md),
            Wrap(
              spacing: AppSpacing.xs,
              runSpacing: AppSpacing.xs,
              alignment: WrapAlignment.end,
              children: [
                OutlinedButton.icon(
                  onPressed: onBack,
                  icon: const Icon(AppIcons.back, size: 16),
                  label: Text(l10n.javaInstallBackToJava),
                ),
                if (extracted && !foreign)
                  FilledButton.icon(
                    onPressed: onUseRuntime,
                    icon: const Icon(AppIcons.check, size: 16),
                    label: Text(l10n.javaInstallUseRuntime),
                  ),
              ],
            ),
          ],
        ),
      );
    }

    final progress = total > 0 ? (done / total).clamp(0.0, 1.0) : null;
    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (installing) ...[
            Row(
              children: [
                Expanded(
                  child: Text(
                    phase ?? l10n.javaInstallInstalling,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                ),
                if (total > 0 || rate > 0)
                  Text(
                    total > 0
                        ? '${formatBytes(done)} / ${formatBytes(total)}'
                              '${rate > 0 ? ' · ${formatBytes(rate)}/s' : ''}'
                        : '${formatBytes(rate)}/s',
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
              ],
            ),
            const SizedBox(height: AppSpacing.xs),
            AppProgressBar(value: progress, height: 4),
            const SizedBox(height: AppSpacing.md),
          ],
          if (error != null) ...[
            _InlineError(message: l10n.javaInstallFailed, detail: error!),
            const SizedBox(height: AppSpacing.sm),
          ],
          Align(
            alignment: Alignment.centerRight,
            child: installing
                ? OutlinedButton.icon(
                    onPressed: canCancel && !cancelling ? onCancel : null,
                    style: OutlinedButton.styleFrom(
                      foregroundColor: tokens.danger,
                      side: BorderSide(
                        color: tokens.danger.withValues(
                          alpha: canCancel ? 0.7 : 0.3,
                        ),
                      ),
                    ),
                    icon: cancelling || !canCancel
                        ? SizedBox(
                            width: 14,
                            height: 14,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: tokens.danger,
                            ),
                          )
                        : const Icon(AppIcons.close, size: 16),
                    label: Text(
                      cancelling
                          ? l10n.javaInstallCancelling
                          : canCancel
                          ? l10n.commonCancel
                          : l10n.javaInstallInstalling,
                    ),
                  )
                : FilledButton.icon(
                    onPressed: canInstall ? onInstall : null,
                    icon: const Icon(AppIcons.download, size: 16),
                    label: Text(
                      autoExtract
                          ? l10n.javaInstallStart
                          : l10n.javaInstallDownloadOnlyAction,
                    ),
                  ),
          ),
        ],
      ),
    );
  }
}

/// Compact segmented control used for vendors and Java majors.
class _SelectChip extends StatelessWidget {
  const _SelectChip({
    required this.label,
    required this.selected,
    required this.enabled,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final bool enabled;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(tokens.radiusS),
        child: AnimatedContainer(
          duration: tokens.fast,
          curve: tokens.curve,
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.xs,
          ),
          decoration: BoxDecoration(
            color: selected
                ? tokens.accentSoft
                : (enabled ? tokens.surface2 : Colors.transparent),
            borderRadius: BorderRadius.circular(tokens.radiusS),
            border: Border.all(color: selected ? tokens.accent : tokens.border),
          ),
          child: Text(
            label,
            style: textTheme.labelMedium?.copyWith(
              color: selected
                  ? tokens.accent
                  : (enabled ? tokens.textSecondary : tokens.textTertiary),
              fontWeight: FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}

class _Badge extends StatelessWidget {
  const _Badge({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
      decoration: BoxDecoration(
        border: Border.all(color: color.withValues(alpha: 0.6)),
        borderRadius: BorderRadius.circular(3),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: color, fontSize: 10),
      ),
    );
  }
}

class _InlineError extends StatelessWidget {
  const _InlineError({
    required this.message,
    required this.detail,
    this.onRetry,
  });

  final String message;
  final String detail;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(AppIcons.error, size: 16, color: tokens.danger),
        const SizedBox(width: AppSpacing.xxs),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                message,
                style: textTheme.bodyMedium?.copyWith(color: tokens.danger),
              ),
              const SizedBox(height: 2),
              Text(
                detail,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
          ),
        ),
        if (onRetry != null)
          TextButton(
            onPressed: onRetry,
            child: Text(AppLocalizations.of(context).refresh),
          ),
      ],
    );
  }
}
