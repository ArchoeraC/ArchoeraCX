part of '../java_install_page.dart';

/// Flat CVE section for the selected build. It never becomes a modal: the
/// advisories sit in the same page and the acknowledgement checkbox is part of
/// the install form.
class _CvePanel extends StatelessWidget {
  const _CvePanel({
    required this.report,
    required this.loading,
    required this.error,
    required this.acknowledge,
    required this.enabled,
    required this.onAcknowledgeChanged,
    required this.l10n,
  });

  final JavaCveReport? report;
  final bool loading;
  final String? error;
  final bool acknowledge;
  final bool enabled;
  final ValueChanged<bool> onAcknowledgeChanged;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final current = report;
    final advisories = current?.advisories ?? const <JavaCveAdvisory>[];
    final unavailable =
        error != null || (current != null && !current.available);

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.security, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallCveTitle,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const Spacer(),
              if (!loading && advisories.isNotEmpty)
                _Badge(label: '${advisories.length}', color: tokens.danger),
            ],
          ),
          const SizedBox(height: AppSpacing.xs),
          if (loading)
            _CveStatus(
              icon: null,
              color: tokens.textTertiary,
              message: l10n.javaInstallCveChecking,
            )
          else if (unavailable)
            _CveStatus(
              icon: AppIcons.offline,
              color: tokens.textTertiary,
              message: l10n.javaInstallCveUnavailable,
            )
          else if (current != null && advisories.isEmpty)
            _CveStatus(
              icon: AppIcons.check,
              color: tokens.success,
              message: l10n.javaInstallCveNone,
            )
          else if (advisories.isNotEmpty) ...[
            Row(
              children: [
                Icon(AppIcons.warning, size: 16, color: tokens.warning),
                const SizedBox(width: AppSpacing.xxs),
                Expanded(
                  child: Text(
                    l10n.javaInstallCveCount(advisories.length),
                    style: textTheme.bodyMedium?.copyWith(
                      color: tokens.warning,
                    ),
                  ),
                ),
              ],
            ),
            if (current != null && current.sources.isNotEmpty) ...[
              const SizedBox(height: 2),
              Text(
                current.sources
                    .map(
                      (source) => source == 'circl'
                          ? 'NVD/CIRCL'
                          : source.toUpperCase(),
                    )
                    .join(' · '),
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
            const SizedBox(height: AppSpacing.xs),
            _OptionRow(
              value: acknowledge,
              enabled: enabled,
              title: l10n.javaInstallCveAcknowledge,
              hint: l10n.javaInstallCveAcknowledgeHint,
              activeColor: tokens.danger,
              titleColor: tokens.danger,
              hintColor: tokens.danger,
              onChanged: onAcknowledgeChanged,
            ),
            const SizedBox(height: AppSpacing.xs),
            for (var index = 0; index < advisories.length; index++) ...[
              if (index > 0) Divider(height: 1, color: tokens.border),
              _CveRow(advisory: advisories[index], l10n: l10n),
            ],
          ],
        ],
      ),
    );
  }
}

class _CveStatus extends StatelessWidget {
  const _CveStatus({
    required this.icon,
    required this.color,
    required this.message,
  });

  final IconData? icon;
  final Color color;
  final String message;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        if (icon != null)
          Icon(icon, size: 15, color: color)
        else
          SizedBox(
            width: 15,
            height: 15,
            child: CircularProgressIndicator(strokeWidth: 2, color: color),
          ),
        const SizedBox(width: AppSpacing.xxs),
        Expanded(
          child: Text(
            message,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: color),
          ),
        ),
      ],
    );
  }
}

class _CveRow extends StatelessWidget {
  const _CveRow({required this.advisory, required this.l10n});

  final JavaCveAdvisory advisory;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = _severityColor(tokens, advisory.severity);
    final score = advisory.score;
    final url = advisory.url;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _Badge(label: advisory.severity.toUpperCase(), color: color),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Tooltip(
                  message: advisory.aliases.isEmpty
                      ? advisory.id
                      : '${advisory.id}\n${advisory.aliases.join(', ')}',
                  child: Text(
                    advisory.id,
                    style: textTheme.bodyMedium?.copyWith(
                      color: tokens.textPrimary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ),
              if (score != null) ...[
                const SizedBox(width: AppSpacing.xs),
                Text(
                  l10n.javaInstallCveScore(score.toStringAsFixed(1)),
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
              const Spacer(),
              if (url != null)
                IconButton(
                  onPressed: () => launchWebUrl(url),
                  tooltip: l10n.javaInstallCveOpen,
                  visualDensity: VisualDensity.compact,
                  icon: Icon(
                    AppIcons.openExternal,
                    size: 16,
                    color: tokens.textTertiary,
                  ),
                ),
            ],
          ),
          if (advisory.summary.isNotEmpty)
            Text(
              advisory.summary,
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
            ),
        ],
      ),
    );
  }

  static Color _severityColor(AppTokens tokens, String severity) {
    switch (severity) {
      case 'critical':
      case 'high':
        return tokens.danger;
      case 'medium':
        return tokens.warning;
      case 'low':
        return tokens.textSecondary;
      default:
        return tokens.textTertiary;
    }
  }
}
