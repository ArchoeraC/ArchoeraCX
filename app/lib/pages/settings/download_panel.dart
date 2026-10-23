part of '../settings_page.dart';

/// Sentinel for "follow the system proxy" in the proxy dialog.
const Object _clearProxy = Object();

/// Parses `http(s)://host[:port]` (a bare `host:port` is treated as http).
Uri? _parseProxy(String value) {
  final text = value.contains('://') ? value : 'http://$value';
  final uri = Uri.tryParse(text);
  if (uri == null || uri.host.isEmpty) return null;
  if (!const {'http', 'https', 'socks5', 'socks5h'}.contains(uri.scheme)) {
    return null;
  }
  return uri;
}

/// Download section content, the proxy editor and the high-concurrency guard.
extension _DownloadPanelX on _SettingsPageState {
  Widget _downloadContent(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) {
    final notifier = ref.read(appSettingsProvider.notifier);
    final tokens = context.tokens;
    final source = settings?.downloadSource ?? 'auto';
    final concurrency = settings?.downloadConcurrency ?? 16;
    final perHost = settings?.downloadPerHost ?? 16;
    final proxy = settings?.downloadProxy;
    final chunkKb = settings?.downloadChunkSizeKb ?? 512;
    final rangeThresholdKb = settings?.downloadRangeThresholdKb ?? 1024;

    // `auto` follows the detected region; show what it actually resolves to.
    final region = ref.watch(regionPolicyProvider).value;
    final autoResolved = region == null
        ? null
        : (region.region == 'CN' ? 'mirror' : 'official');
    final autoSubtitle = switch (autoResolved) {
      'mirror' => l10n.settingsDownloadAutoResolvedMirror,
      'official' => l10n.settingsDownloadAutoResolvedOfficial,
      _ => l10n.settingsDownloadAutoDetecting,
    };

    // Beyond 32 streams the cost grows faster than the benefit: warn once when
    // crossing the line and keep a persistent reminder while it stays high.
    final highConcurrency = concurrency > 32 || perHost > 32;

    Future<void> applyConcurrency(int value) async {
      if (value > 32 && concurrency <= 32) {
        final accepted = await _confirmHighConcurrency(context, l10n);
        if (accepted != true || !context.mounted) return;
      }
      await notifier.setDownloadPreferences(downloadConcurrency: value);
      if (context.mounted) {
        _notify(context, l10n.settingsSaved, type: ToastType.success);
      }
    }

    Future<void> applyPerHost(int value) async {
      if (value > 32 && perHost <= 32) {
        final accepted = await _confirmHighConcurrency(context, l10n);
        if (accepted != true || !context.mounted) return;
      }
      await notifier.setDownloadPreferences(downloadPerHost: value);
      if (context.mounted) {
        _notify(context, l10n.settingsSaved, type: ToastType.success);
      }
    }

    final sourceGroup = _SettingsGroup(
      title: l10n.settingsDownload,
      note: l10n.settingsDownloadSourceDesc,
      children: [
        _AppearanceTile(
          icon: AppIcons.source,
          title: l10n.settingsDownloadSource,
          // Only `auto` needs an explanation: it hides a region decision.
          subtitle: source == 'auto' ? autoSubtitle : null,
          trailing: _SegmentedControl<String>(
            selected: source,
            options: [
              _SegmentedOption('auto', l10n.settingsDownloadSourceAuto),
              _SegmentedOption('official', l10n.settingsDownloadSourceOfficial),
              _SegmentedOption('mirror', l10n.settingsDownloadSourceMirror),
            ],
            onChanged: (value) async {
              await notifier.setDownloadPreferences(downloadSource: value);
              if (context.mounted) {
                _notify(context, l10n.settingsSaved, type: ToastType.success);
              }
            },
          ),
        ),
      ],
    );

    final tuningGroup = _SettingsGroup(
      title: l10n.settingsDownloadTuning,
      note: l10n.settingsDownloadNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.throughput,
          title: l10n.settingsDownloadConcurrency,
          subtitle: l10n.settingsDownloadConcurrencyDesc,
          trailing: _NumberStepper(
            key: const ValueKey('download-concurrency-stepper'),
            value: concurrency,
            min: 1,
            max: 64,
            onChanged: applyConcurrency,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.perHost,
          title: l10n.settingsDownloadPerHost,
          subtitle: l10n.settingsDownloadPerHostDesc,
          trailing: _NumberStepper(
            key: const ValueKey('download-per-host-stepper'),
            value: perHost,
            min: 0,
            max: 64,
            onChanged: applyPerHost,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.throughput,
          title: l10n.settingsDownloadRangeThreshold,
          subtitle: l10n.settingsDownloadRangeThresholdDesc,
          trailing: DropdownButton<int>(
            value: rangeThresholdKb,
            isDense: true,
            underline: const SizedBox.shrink(),
            borderRadius: BorderRadius.circular(tokens.radiusM),
            items: [
              DropdownMenuItem(
                value: 0,
                child: Text(l10n.settingsRangeDisabled),
              ),
              const DropdownMenuItem(value: 1024, child: Text('1 MiB')),
              const DropdownMenuItem(value: 4096, child: Text('4 MiB')),
              const DropdownMenuItem(value: 8192, child: Text('8 MiB')),
            ],
            onChanged: (value) async {
              if (value == null) return;
              await notifier.setDownloadPreferences(
                downloadRangeThresholdKb: value,
              );
              if (context.mounted) {
                _notify(context, l10n.settingsSaved, type: ToastType.success);
              }
            },
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.download,
          title: l10n.settingsDownloadChunk,
          subtitle: l10n.settingsDownloadChunkDesc,
          trailing: DropdownButton<int>(
            value: chunkKb,
            isDense: true,
            underline: const SizedBox.shrink(),
            borderRadius: BorderRadius.circular(tokens.radiusM),
            items: const [
              DropdownMenuItem(value: 256, child: Text('256 KiB')),
              DropdownMenuItem(value: 512, child: Text('512 KiB')),
              DropdownMenuItem(value: 1024, child: Text('1 MiB')),
              DropdownMenuItem(value: 2048, child: Text('2 MiB')),
            ],
            onChanged: (value) async {
              if (value == null) return;
              await notifier.setDownloadPreferences(downloadChunkSizeKb: value);
              if (context.mounted) {
                _notify(context, l10n.settingsSaved, type: ToastType.success);
              }
            },
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.proxy,
          title: l10n.settingsDownloadProxy,
          subtitle: proxy == null || proxy.isEmpty
              ? l10n.settingsDownloadProxySystem
              : proxy,
          trailing: Icon(
            AppIcons.chevronRight,
            size: 18,
            color: context.tokens.textTertiary,
          ),
          onTap: () => _editProxy(context, l10n),
        ),
      ],
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        sourceGroup,
        const SizedBox(height: AppSpacing.lg),
        tuningGroup,
        if (highConcurrency) ...[
          const SizedBox(height: AppSpacing.xs),
          _HighConcurrencyNotice(
            message: l10n.settingsDownloadHighConcurrencyActive(
              concurrency,
              perHost,
            ),
          ),
        ],
      ],
    );
  }

  /// Manual proxy for downloads; empty input follows the system proxy.
  Future<void> _editProxy(BuildContext context, AppLocalizations l10n) async {
    final current = ref.read(appSettingsProvider).value?.downloadProxy;
    final result = await showDialog<Object?>(
      context: context,
      builder: (_) => _DownloadProxyDialog(l10n: l10n, initial: current),
    );
    if (result == null || !context.mounted) return;

    await ref
        .read(appSettingsProvider.notifier)
        .setDownloadPreferences(
          downloadProxy: result == _clearProxy ? null : result as String,
        );
    if (!context.mounted) return;
    _notify(context, l10n.settingsSaved, type: ToastType.success);
  }

  /// Explicit acknowledgement before going past 32 streams.
  Future<bool?> _confirmHighConcurrency(
    BuildContext context,
    AppLocalizations l10n,
  ) {
    return confirmDestructive(
      context,
      l10n,
      title: l10n.settingsDownloadHighConcurrencyTitle,
      body: l10n.settingsDownloadHighConcurrencyWarning,
      confirmLabel: l10n.settingsDownloadHighConcurrencyContinue,
    );
  }
}

/// Manual download proxy editor. Owns its controller so the dialog's exit
/// animation never touches a disposed controller.
class _DownloadProxyDialog extends StatefulWidget {
  const _DownloadProxyDialog({required this.l10n, this.initial});

  final AppLocalizations l10n;
  final String? initial;

  @override
  State<_DownloadProxyDialog> createState() => _DownloadProxyDialogState();
}

class _DownloadProxyDialogState extends State<_DownloadProxyDialog> {
  late final TextEditingController _controller = TextEditingController(
    text: widget.initial ?? '',
  );
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _save() {
    final value = _controller.text.trim();
    if (value.isNotEmpty && _parseProxy(value) == null) {
      setState(() => _error = widget.l10n.settingsDownloadProxyInvalid);
      return;
    }
    Navigator.of(context).pop(value);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final tokens = context.tokens;
    return AlertDialog(
      title: Text(l10n.settingsDownloadProxy),
      content: SizedBox(
        width: 380,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextField(
              controller: _controller,
              autofocus: true,
              decoration: InputDecoration(
                labelText: l10n.settingsDownloadProxy,
                hintText: 'http://127.0.0.1:7890',
                errorText: _error,
              ),
              onChanged: (_) => setState(() => _error = null),
              onSubmitted: (_) => _save(),
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              l10n.settingsDownloadProxyHint,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(_clearProxy),
          child: Text(l10n.settingsDownloadProxySystem),
        ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(onPressed: _save, child: Text(l10n.commonSave)),
      ],
    );
  }
}
