part of '../settings_page.dart';

class _RuntimeSettingsPanel extends ConsumerStatefulWidget {
  const _RuntimeSettingsPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_RuntimeSettingsPanel> createState() =>
      _RuntimeSettingsPanelState();
}

class _RuntimeSettingsPanelState extends ConsumerState<_RuntimeSettingsPanel> {
  final _min = TextEditingController();
  final _max = TextEditingController();
  final _jvm = TextEditingController();
  final _minFocus = FocusNode();
  final _maxFocus = FocusNode();
  final _jvmFocus = FocusNode();

  Timer? _debounce;
  String _gcMode = 'auto';
  String? _error;
  bool _saving = false;
  bool _saveInFlight = false;
  bool _saveRequested = false;
  SystemMemoryInfo? _memory;
  bool _memoryLoading = true;

  @override
  void initState() {
    super.initState();
    _syncControllers();
    _loadMemory();
    for (final focus in [_minFocus, _maxFocus, _jvmFocus]) {
      focus.addListener(() {
        if (!focus.hasFocus) _saveNow();
      });
    }
  }

  @override
  void didUpdateWidget(covariant _RuntimeSettingsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.settings != widget.settings) _syncControllers();
  }

  void _syncControllers() {
    final settings = widget.settings;
    if (!_minFocus.hasFocus) _min.text = '${settings?.memoryMinMb ?? 512}';
    if (!_maxFocus.hasFocus) _max.text = '${settings?.memoryMaxMb ?? 2048}';
    if (!_jvmFocus.hasFocus) _jvm.text = settings?.jvmArgs ?? '';
    _gcMode = settings?.gcMode ?? 'auto';
  }

  /// Switches between automatic sizing and the manual `-Xms`/`-Xmx` pair.
  Future<void> _setMemoryMode(bool auto) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(memoryMode: auto ? 'auto' : 'manual');
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _loadMemory() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      if (mounted) setState(() => _memoryLoading = false);
      return;
    }
    if (mounted && !_memoryLoading) setState(() => _memoryLoading = true);
    try {
      final memory = await client.getSystemMemory();
      if (mounted) {
        setState(() {
          _memory = memory;
          _memoryLoading = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _memoryLoading = false);
    }
  }

  void _scheduleSave() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), _saveNow);
  }

  Future<void> _saveNow() async {
    if (!mounted) return;
    if (_saveInFlight) {
      _saveRequested = true;
      return;
    }

    final l10n = AppLocalizations.of(context);
    final min = int.tryParse(_min.text.trim());
    final max = int.tryParse(_max.text.trim());
    final memoryAuto = widget.settings?.memoryAuto ?? true;
    // In automatic mode the manual numbers are kept but not applied, so a
    // half-typed value must not block saving the JVM/GC fields.
    if (!memoryAuto &&
        (min == null || max == null || min < 128 || max > 65536 || min > max)) {
      setState(() => _error = l10n.settingsMemoryInvalid);
      return;
    }

    _saveInFlight = true;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(
            memoryMinMb: memoryAuto ? null : min,
            memoryMaxMb: memoryAuto ? null : max,
            jvmArgs: _jvm.text.trim(),
            gcMode: _gcMode,
          );
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      _saveInFlight = false;
      if (mounted) {
        setState(() => _saving = false);
        if (_saveRequested) {
          _saveRequested = false;
          unawaited(_saveNow());
        }
      }
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _min.dispose();
    _max.dispose();
    _jvm.dispose();
    _minFocus.dispose();
    _maxFocus.dispose();
    _jvmFocus.dispose();
    super.dispose();
  }

  String _formatMb(int mb) =>
      mb >= 1024 ? '${(mb / 1024).toStringAsFixed(1)} GB' : '$mb MB';

  List<(String, String)> _gcOptions(AppLocalizations l10n) => [
    ('auto', l10n.settingsGcAuto),
    ('g1gc', l10n.settingsGcG1),
    ('zgc', l10n.settingsGcZgc),
    ('parallel', l10n.settingsGcParallel),
    ('serial', l10n.settingsGcSerial),
    ('shenandoah', l10n.settingsGcShenandoah),
    ('epsilon', l10n.settingsGcEpsilon),
  ];

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final memory = _memory;
    final total = memory?.totalMb ?? 0;
    final available = memory?.availableMb ?? 0;
    final memoryAuto = widget.settings?.memoryAuto ?? true;
    final maxMb = int.tryParse(_max.text.trim()) ?? 2048;
    final estimated = maxMb + 512;
    final overAvailable =
        !memoryAuto && memory?.isKnown == true && estimated > available;
    final ratio = total > 0 ? (estimated / total).clamp(0.0, 1.0) : 0.0;

    return _SettingsCard(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.sm,
            AppSpacing.xs,
            AppSpacing.sm,
            AppSpacing.sm,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(AppIcons.memory, size: 19, color: tokens.textSecondary),
                  const SizedBox(width: AppSpacing.xs),
                  Text(
                    l10n.settingsMemorySystem,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const Spacer(),
                  if (_memoryLoading)
                    const SizedBox(
                      width: 15,
                      height: 15,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  else
                    IconButton(
                      onPressed: _loadMemory,
                      tooltip: l10n.refresh,
                      icon: const Icon(AppIcons.refresh, size: 18),
                    ),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.settingsMemoryAuto,
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          l10n.settingsMemoryAutoDesc,
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Switch(
                    key: const ValueKey('settings-memory-auto'),
                    value: memoryAuto,
                    onChanged: _setMemoryMode,
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Wrap(
                spacing: AppSpacing.lg,
                runSpacing: AppSpacing.xs,
                children: [
                  _MemoryMetric(
                    label: l10n.settingsMemoryTotal,
                    value: memory?.isKnown == true
                        ? _formatMb(total)
                        : l10n.settingsMemoryUnavailable,
                  ),
                  _MemoryMetric(
                    label: l10n.settingsMemoryAvailable,
                    value: memory?.isKnown == true
                        ? _formatMb(available)
                        : l10n.settingsMemoryUnavailable,
                  ),
                  if (!memoryAuto)
                    _MemoryMetric(
                      label: l10n.settingsMemoryEstimated,
                      value: _formatMb(estimated),
                      color: overAvailable ? tokens.warning : null,
                    ),
                ],
              ),
              if (!memoryAuto && memory?.isKnown == true) ...[
                const SizedBox(height: AppSpacing.sm),
                AppProgressBar(
                  value: ratio,
                  height: 5,
                  trackColor: tokens.surface3,
                  color: overAvailable ? tokens.warning : tokens.accent,
                ),
              ],
              if (overAvailable) ...[
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  l10n.settingsMemoryOverAvailable,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.warning),
                ),
              ],
              const SizedBox(height: AppSpacing.xxs),
              Text(
                l10n.settingsRuntimeHint,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  if (!memoryAuto)
                    SizedBox(
                      width: 150,
                      child: TextField(
                        controller: _min,
                        focusNode: _minFocus,
                        keyboardType: TextInputType.number,
                        onChanged: (_) {
                          setState(() {});
                          _scheduleSave();
                        },
                        onSubmitted: (_) => _saveNow(),
                        decoration: InputDecoration(
                          labelText: l10n.settingsMemoryMin,
                        ),
                      ),
                    ),
                  if (!memoryAuto)
                    SizedBox(
                      width: 150,
                      child: TextField(
                        controller: _max,
                        focusNode: _maxFocus,
                        keyboardType: TextInputType.number,
                        onChanged: (_) {
                          setState(() {});
                          _scheduleSave();
                        },
                        onSubmitted: (_) => _saveNow(),
                        decoration: InputDecoration(
                          labelText: l10n.settingsMemoryMax,
                        ),
                      ),
                    ),
                  SizedBox(
                    width: 240,
                    child: DropdownButtonFormField<String>(
                      key: ValueKey('gc-$_gcMode'),
                      initialValue: _gcMode,
                      isExpanded: true,
                      decoration: InputDecoration(labelText: l10n.settingsGc),
                      items: [
                        for (final item in _gcOptions(l10n))
                          DropdownMenuItem(
                            value: item.$1,
                            child: Text(item.$2),
                          ),
                      ],
                      onChanged: (value) {
                        if (value == null) return;
                        setState(() => _gcMode = value);
                        _saveNow();
                      },
                    ),
                  ),
                ],
              ),
              if (memoryAuto) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.settingsMemoryAutoHint,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: _jvm,
                focusNode: _jvmFocus,
                onChanged: (_) => _scheduleSave(),
                onSubmitted: (_) => _saveNow(),
                decoration: InputDecoration(
                  labelText: l10n.settingsJvmArgs,
                  hintText: l10n.settingsJvmArgsHint,
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  _error!,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              if (_saving)
                const Padding(
                  padding: EdgeInsets.only(top: AppSpacing.xs),
                  child: Align(
                    alignment: Alignment.centerRight,
                    child: SizedBox(
                      width: 14,
                      height: 14,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}

class _MemoryMetric extends StatelessWidget {
  const _MemoryMetric({required this.label, required this.value, this.color});

  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: 1),
        Text(
          value,
          style: Theme.of(context).textTheme.titleSmall
              ?.copyWith(color: color ?? tokens.textPrimary),
        ),
      ],
    );
  }
}
