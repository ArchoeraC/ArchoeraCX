import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/core_client.dart';
import '../../stores/downloads.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/toast.dart';

/// Copies an instance (folder + metadata) under a new name.
///
/// Returns `true` when the core created the copy, `null` when the user backed
/// out. The caller only needs to react to the toast, so the result is mostly
/// informational.
Future<bool?> showInstanceDuplicateDialog(
  BuildContext context,
  GameInstance instance,
) {
  return showDialog<bool>(
    context: context,
    barrierDismissible: false,
    builder: (_) => InstanceDuplicateDialog(instance: instance),
  );
}

class InstanceDuplicateDialog extends ConsumerStatefulWidget {
  const InstanceDuplicateDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<InstanceDuplicateDialog> createState() =>
      _InstanceDuplicateDialogState();
}

class _InstanceDuplicateDialogState
    extends ConsumerState<InstanceDuplicateDialog> {
  final _nameController = TextEditingController();

  String? _error;
  bool _saving = false;
  bool _defaultApplied = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_defaultApplied) return;
    _defaultApplied = true;
    // A suffix keeps the default unique-ish without asking the backend; the
    // user can always overwrite it.
    _nameController.text =
        '${widget.instance.name} '
        '${AppLocalizations.of(context).instanceDuplicateSuffix}';
    _nameController.addListener(_onNameChanged);
  }

  @override
  void dispose() {
    _nameController
      ..removeListener(_onNameChanged)
      ..dispose();
    super.dispose();
  }

  void _onNameChanged() {
    if (_error != null) setState(() => _error = null);
  }

  Future<void> _duplicate(AppLocalizations l10n) async {
    if (_saving) return;
    final name = _nameController.text.trim();
    if (name.isEmpty) {
      _fail(l10n.instanceDuplicateInvalidName);
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });

    // The copy runs in the background: a large instance is minutes of file
    // work, and the dialog should not be the thing that owns it.
    try {
      await ref
          .read(downloadsProvider.notifier)
          .duplicateInstance(
            widget.instance,
            name: name,
            successMessage: l10n.instanceDuplicateDone(name),
            failureMessage: l10n.instanceDuplicateFailed,
            cancelledMessage: l10n.downloadCancelled(name),
          );
    } catch (error) {
      if (!mounted) return;
      setState(() => _saving = false);
      _fail(
        error is CoreException
            ? '${l10n.instanceDuplicateFailed} ${error.message}'
            : '${l10n.instanceDuplicateFailed} $error',
      );
      return;
    }

    if (!mounted) return;
    Navigator.of(context).pop(true);
    toast(l10n.downloadQueued(name), type: ToastType.success);
  }

  void _fail(String message) {
    setState(() => _error = message);
    toast(message, type: ToastType.error);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Dialog(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.lg,
                AppSpacing.md,
                AppSpacing.sm,
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.instanceDuplicateTitle,
                          style: textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          widget.instance.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: _saving
                        ? null
                        : () => Navigator.of(context).pop(),
                    tooltip: l10n.commonClose,
                    icon: const Icon(AppIcons.close),
                  ),
                ],
              ),
            ),
            Divider(height: 1, color: tokens.border),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.md,
                AppSpacing.lg,
                AppSpacing.md,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    l10n.instanceDuplicateHint,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  TextField(
                    controller: _nameController,
                    enabled: !_saving,
                    autofocus: true,
                    textInputAction: TextInputAction.done,
                    onSubmitted: (_) => _duplicate(l10n),
                    decoration: InputDecoration(
                      labelText: l10n.instanceDuplicateName,
                      hintText: l10n.instanceDuplicateNameHint,
                      prefixIcon: const Icon(AppIcons.copy, size: 18),
                      errorText: _error,
                      errorMaxLines: 2,
                    ),
                  ),
                ],
              ),
            ),
            Divider(height: 1, color: tokens.border),
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: _saving
                        ? null
                        : () => Navigator.of(context).pop(),
                    child: Text(l10n.commonCancel),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  FilledButton.icon(
                    onPressed: _saving ? null : () => _duplicate(l10n),
                    icon: _saving
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(AppIcons.copy, size: 18),
                    label: Text(l10n.instanceDuplicateAction),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
