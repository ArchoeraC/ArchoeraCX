import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/instances/installed_mod.dart';
import '../../stores/providers.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../../utils/format.dart';
import '../common/confirm.dart';

Future<void> showInstanceModsDialog(
  BuildContext context,
  GameInstance instance,
) {
  return showDialog<void>(
    context: context,
    builder: (_) => InstanceModsDialog(instance: instance),
  );
}

class InstanceModsDialog extends ConsumerStatefulWidget {
  const InstanceModsDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<InstanceModsDialog> createState() => _InstanceModsDialogState();
}

class _InstanceModsDialogState extends ConsumerState<InstanceModsDialog> {
  List<InstalledMod> _mods = const [];
  bool _loading = true;
  String? _error;
  String? _busyFile;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() {
        _loading = false;
        _error = AppLocalizations.of(context).commonCoreNotConnected;
      });
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final items = await client.listInstanceMods(widget.instance.id);
      if (!mounted) return;
      setState(() {
        _mods = items;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = AppLocalizations.of(context).instanceModsLoadFailed('$error');
      });
    }
  }

  Future<void> _toggle(InstalledMod mod, bool enabled) async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final l10n = AppLocalizations.of(context);
    setState(() => _busyFile = mod.fileName);
    try {
      final updated = await client.setInstanceModEnabled(
        instanceId: widget.instance.id,
        fileName: mod.fileName,
        enabled: enabled,
      );
      if (!mounted) return;
      if (updated == null) {
        setState(() => _error = l10n.instanceModsToggleFailed);
        return;
      }
      setState(() {
        _mods = [
          for (final item in _mods)
            if (item.fileName == mod.fileName) updated else item,
        ]..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
        _error = null;
      });
    } catch (error) {
      if (mounted) setState(() => _error = l10n.instanceModsToggleFailed);
    } finally {
      if (mounted) setState(() => _busyFile = null);
    }
  }

  Future<void> _remove(InstalledMod mod) async {
    final l10n = AppLocalizations.of(context);
    final confirmed = await confirmDestructive(
      context,
      l10n,
      title: l10n.instanceModDelete,
      body: l10n.instanceModDeleteConfirm(mod.name),
      confirmLabel: l10n.commonDelete,
    );
    if (confirmed != true || !mounted) return;
    final client = ref.read(coreClientProvider);
    if (client == null) return;

    setState(() => _busyFile = mod.fileName);
    try {
      final removed = await client.removeInstanceMod(
        instanceId: widget.instance.id,
        fileName: mod.fileName,
      );
      if (!mounted) return;
      if (!removed) {
        setState(() => _error = l10n.instanceModsRemoveFailed);
        return;
      }
      setState(() {
        _mods = _mods
            .where((item) => item.fileName != mod.fileName)
            .toList(growable: false);
        _error = null;
      });
    } catch (error) {
      if (mounted) setState(() => _error = l10n.instanceModsRemoveFailed);
    } finally {
      if (mounted) setState(() => _busyFile = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Dialog(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: 760,
          maxHeight: MediaQuery.sizeOf(context).height * 0.78,
        ),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.instanceModsTitle,
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '${widget.instance.name} · '
                          '${l10n.instanceMods(_mods.length)}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: _loading ? null : _load,
                    tooltip: l10n.refresh,
                    icon: const Icon(AppIcons.refresh),
                  ),
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    tooltip: l10n.commonClose,
                    icon: const Icon(AppIcons.close),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.sm),
              Divider(height: 1, color: tokens.border),
              if (_error != null) ...[
                const SizedBox(height: AppSpacing.sm),
                Text(
                  _error!,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              const SizedBox(height: AppSpacing.xs),
              Flexible(
                child: _loading
                    ? const Center(
                        child: Padding(
                          padding: EdgeInsets.all(AppSpacing.xl),
                          child: CircularProgressIndicator(),
                        ),
                      )
                    : _mods.isEmpty
                    ? Center(
                        child: Padding(
                          padding: const EdgeInsets.all(AppSpacing.xl),
                          child: Text(
                            l10n.instanceModsEmpty,
                            textAlign: TextAlign.center,
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(color: tokens.textSecondary),
                          ),
                        ),
                      )
                    : ListView.separated(
                        shrinkWrap: true,
                        itemCount: _mods.length,
                        separatorBuilder: (_, _) =>
                            Divider(height: 1, color: tokens.border),
                        itemBuilder: (context, index) {
                          final mod = _mods[index];
                          return _ModRow(
                            mod: mod,
                            l10n: l10n,
                            busy: _busyFile == mod.fileName,
                            onToggle: (value) => _toggle(mod, value),
                            onRemove: () => _remove(mod),
                          );
                        },
                      ),
              ),
              const SizedBox(height: AppSpacing.sm),
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  onPressed: () => Navigator.of(context).pop(),
                  child: Text(l10n.commonClose),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ModRow extends StatelessWidget {
  const _ModRow({
    required this.mod,
    required this.l10n,
    required this.busy,
    required this.onToggle,
    required this.onRemove,
  });

  final InstalledMod mod;
  final AppLocalizations l10n;
  final bool busy;
  final ValueChanged<bool> onToggle;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Icon(
            mod.enabled ? AppIcons.mod : AppIcons.modDisabled,
            size: 20,
            color: mod.enabled ? tokens.accent : tokens.textTertiary,
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  mod.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                    color: mod.enabled ? null : tokens.textTertiary,
                  ),
                ),
                Text(
                  mod.enabled
                      ? formatBytes(mod.size)
                      : '${l10n.instanceModDisabled} · ${formatBytes(mod.size)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
          ),
          if (busy)
            const Padding(
              padding: EdgeInsets.all(AppSpacing.xs),
              child: SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            )
          else ...[
            Switch(value: mod.enabled, onChanged: onToggle),
            IconButton(
              onPressed: onRemove,
              tooltip: l10n.instanceModDelete,
              icon: Icon(AppIcons.delete, size: 19, color: tokens.danger),
            ),
          ],
        ],
      ),
    );
  }
}
