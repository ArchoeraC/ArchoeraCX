part of '../game_log_dialog.dart';

class _Header extends StatelessWidget {
  const _Header({
    required this.title,
    required this.subtitle,
    required this.running,
    required this.forceStopping,
    required this.onForceStop,
    required this.onOpenFolder,
    required this.onRefresh,
    required this.onClose,
  });

  final String title;
  final String subtitle;
  final bool running;
  final bool forceStopping;
  final VoidCallback onForceStop;
  final VoidCallback? onOpenFolder;
  final VoidCallback? onRefresh;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleLarge),
                  const SizedBox(width: AppSpacing.xs),
                  _StatusChip(running: running),
                ],
              ),
              const SizedBox(height: 2),
              Text(
                subtitle,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
        ),
        if (running)
          IconButton(
            onPressed: forceStopping ? null : onForceStop,
            tooltip: l10n.gameLogForceStop,
            icon: forceStopping
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Icon(AppIcons.cancelled, color: tokens.danger),
          ),
        IconButton(
          onPressed: onOpenFolder,
          tooltip: l10n.gameLogOpenFolder,
          icon: const Icon(AppIcons.folder),
        ),
        IconButton(
          onPressed: onRefresh,
          tooltip: l10n.refresh,
          icon: const Icon(AppIcons.refresh),
        ),
        IconButton(
          onPressed: onClose,
          tooltip: l10n.commonClose,
          icon: const Icon(AppIcons.close),
        ),
      ],
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({required this.running});

  final bool running;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final color = running ? tokens.success : tokens.textTertiary;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: ShapeDecoration(
        color: color.withValues(alpha: 0.12),
        shape: BeveledRectangleBorder(
          side: BorderSide(color: color.withValues(alpha: 0.4)),
          borderRadius: BorderRadius.circular(tokens.radiusS),
        ),
      ),
      child: Text(
        running ? l10n.gameLogRunning : l10n.gameLogStopped,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: color, fontWeight: FontWeight.w600),
      ),
    );
  }
}

class _FilePicker extends StatelessWidget {
  const _FilePicker({
    required this.list,
    required this.selected,
    required this.l10n,
    required this.onChanged,
  });

  final LaunchLogList? list;
  final String? selected;
  final AppLocalizations l10n;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    final logs = list?.logs ?? const <LaunchLogFile>[];
    final value = logs.any((file) => file.fileName == selected)
        ? selected
        : null;

    return InputDecorator(
      decoration: const InputDecoration(
        isDense: true,
        border: OutlineInputBorder(),
        contentPadding: EdgeInsets.symmetric(
          horizontal: AppSpacing.sm,
          vertical: AppSpacing.xs,
        ),
      ),
      child: DropdownButtonHideUnderline(
        child: DropdownButton<String>(
          value: value,
          isExpanded: true,
          isDense: true,
          hint: Text(l10n.gameLogFileLabel),
          items: [
            for (final file in logs)
              DropdownMenuItem(
                value: file.fileName,
                child: Text(
                  '${file.fileName}  ·  ${formatBytes(file.size)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
          ],
          onChanged: logs.isEmpty ? null : onChanged,
        ),
      ),
    );
  }
}

class _FollowToggle extends StatelessWidget {
  const _FollowToggle({
    required this.following,
    required this.l10n,
    required this.onChanged,
  });

  final bool following;
  final AppLocalizations l10n;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Tooltip(
      message: l10n.gameLogFollowHint,
      child: FilterChip(
        selected: following,
        showCheckmark: false,
        avatar: Icon(
          following ? AppIcons.pause : AppIcons.play,
          size: 16,
          color: following ? tokens.accent : tokens.textSecondary,
        ),
        label: Text(l10n.gameLogFollow),
        onSelected: onChanged,
      ),
    );
  }
}

class _LogLine extends StatelessWidget {
  const _LogLine({required this.text, required this.tokens});

  final String text;
  final AppTokens tokens;

  @override
  Widget build(BuildContext context) {
    return Text(
      text.isEmpty ? ' ' : text,
      style: TextStyle(
        fontFamily: 'monospace',
        fontFamilyFallback: const ['NotoSansSC', 'DejaVu Sans Mono'],
        fontSize: 12,
        height: 1.35,
        color: _color,
      ),
    );
  }

  Color get _color {
    final line = text.toUpperCase();
    if (line.contains('ERROR') ||
        line.contains('FATAL') ||
        line.contains('SEVERE') ||
        line.contains('EXCEPTION')) {
      return tokens.danger;
    }
    if (line.contains('WARN')) return tokens.warning;
    if (line.startsWith('AT ') || line.startsWith('\tat ')) {
      return tokens.textTertiary;
    }
    return tokens.textSecondary;
  }
}
