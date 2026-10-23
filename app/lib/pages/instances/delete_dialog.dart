part of '../instances_page.dart';

/// First confirmation before deleting an instance. Pops `true` when the user
/// also wants the local files removed, `false` for registry-only deletion and
/// `null` on cancel.
class _InstanceDeleteDialog extends StatefulWidget {
  const _InstanceDeleteDialog({required this.l10n, required this.instance});

  final AppLocalizations l10n;
  final GameInstance instance;

  @override
  State<_InstanceDeleteDialog> createState() => _InstanceDeleteDialogState();
}

class _InstanceDeleteDialogState extends State<_InstanceDeleteDialog> {
  // Off by default: deleting files is the irreversible part of this action.
  bool _deleteFiles = false;

  bool get _canDeleteFiles =>
      !widget.instance.missing && widget.instance.rootPath.isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final tokens = context.tokens;
    final instance = widget.instance;

    return AlertDialog(
      title: Text(l10n.instanceDeleteConfirm(instance.name)),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(l10n.instanceDeleteBody),
            const SizedBox(height: AppSpacing.sm),
            CheckboxListTile(
              value: _deleteFiles,
              onChanged: _canDeleteFiles
                  ? (value) => setState(() => _deleteFiles = value ?? false)
                  : null,
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              dense: true,
              title: Text(l10n.instanceDeleteFiles),
              subtitle: Text(l10n.instanceDeleteFilesHint),
            ),
            if (instance.rootPath.isNotEmpty)
              Text(
                instance.rootPath,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: tokens.danger,
            foregroundColor: tokens.onAccent,
          ),
          onPressed: () => Navigator.of(context).pop(_deleteFiles),
          child: Text(l10n.commonDelete),
        ),
      ],
    );
  }
}
