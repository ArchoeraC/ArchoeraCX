import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/core_client.dart';
import '../../theme/tokens.dart';
import 'app_card.dart';

/// A small, zero-business card that verifies the native core ABI.
///
/// The card owns its [CoreClient] for its lifetime. A loader can be injected
/// for tests, which lets the unavailable path render without a native library.
class CoreStatusCard extends StatefulWidget {
  const CoreStatusCard({
    super.key,
    this.clientFactory = CoreClient.openDefault,
  });

  final CoreClient Function() clientFactory;

  @override
  State<CoreStatusCard> createState() => _CoreStatusCardState();
}

enum _CoreCardState { idle, connecting, connected, unavailable }

class _CoreStatusCardState extends State<CoreStatusCard> {
  CoreClient? _client;
  _CoreCardState _state = _CoreCardState.idle;
  PingResult? _ping;
  String? _reason;
  String? _details;
  bool _pingFailed = false;
  int _progressDone = 0;
  int _progressTotal = 0;

  @override
  void initState() {
    super.initState();
    _probe();
  }

  @override
  void dispose() {
    _client?.close();
    _client = null;
    super.dispose();
  }

  void _probe() {
    try {
      _client = widget.clientFactory();
      _state = _CoreCardState.idle;
    } catch (error) {
      _setUnavailable(error);
    }
  }

  Future<void> _pingCore() async {
    if (_state == _CoreCardState.connecting) return;

    var client = _client;
    if (client == null) {
      setState(() {
        _state = _CoreCardState.connecting;
        _reason = null;
        _details = null;
        _pingFailed = false;
        _progressDone = 0;
        _progressTotal = 0;
      });

      try {
        client = widget.clientFactory();
        _client = client;
      } catch (error) {
        if (!mounted) return;
        setState(() => _setUnavailable(error, pingFailed: true));
        return;
      }
    }

    final activeClient = client;

    setState(() {
      _state = _CoreCardState.connecting;
      _reason = null;
      _details = null;
      _pingFailed = false;
      _progressDone = 0;
      _progressTotal = 0;
    });

    try {
      final result = await activeClient.pingAsync(
        onProgress: (done, total) {
          if (!mounted) return;
          setState(() {
            _progressDone = done;
            _progressTotal = total;
          });
        },
      );
      if (!mounted) return;
      setState(() {
        _state = _CoreCardState.connected;
        _ping = result;
        _reason = null;
        _details = null;
        _pingFailed = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() => _setUnavailable(error, pingFailed: true));
    }
  }

  void _setUnavailable(Object error, {bool pingFailed = false}) {
    _state = _CoreCardState.unavailable;
    _ping = null;
    _pingFailed = pingFailed;

    if (error is CoreUnavailableException) {
      _reason = error.message;
      _details = error.summary;
    } else if (error is CoreException) {
      _reason = error.message;
      _details = error.toString();
    } else {
      _reason = error.toString();
      _details = error.toString();
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final ping = _ping;

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.memory_rounded, size: 18, color: tokens.accent),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(l10n.coreStatusTitle, style: textTheme.titleMedium),
              ),
              _StatusPill(state: _state, l10n: l10n),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (_state == _CoreCardState.idle)
            _MutedText(
              l10n.coreStatusIdleHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          if (_state == _CoreCardState.connecting) ...[
            _MutedText(
              _progressTotal > 0
                  ? l10n.coreStatusProgress(_progressDone, _progressTotal)
                  : l10n.coreStatusPingingHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
            const SizedBox(height: AppSpacing.xs),
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(
                value: _progressTotal > 0
                    ? (_progressDone / _progressTotal).clamp(0.0, 1.0)
                    : null,
                minHeight: 4,
                backgroundColor: tokens.surface2,
                color: tokens.accent,
              ),
            ),
          ],
          if (_state == _CoreCardState.unavailable) ...[
            _MutedText(
              _pingFailed && _reason != null
                  ? l10n.coreStatusPingFailed(_reason!)
                  : (_reason ?? l10n.coreStatusUnavailableHint),
              maxLines: 2,
              style: textTheme.bodySmall?.copyWith(color: tokens.danger),
            ),
            if (_reason == null) ...[
              const SizedBox(height: AppSpacing.xxs),
              _MutedText(
                l10n.coreStatusUnavailableHint,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
          ],
          if (_state == _CoreCardState.connected && ping != null) ...[
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(AppSpacing.sm),
              decoration: BoxDecoration(
                color: tokens.surface2,
                borderRadius: BorderRadius.circular(tokens.radiusM),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    l10n.coreStatusPong(ping.summary),
                    style: textTheme.labelLarge?.copyWith(
                      color: tokens.success,
                    ),
                  ),
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    l10n.coreStatusMethod(ping.method),
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    l10n.coreStatusAbiVersion(_client?.abiVersion ?? 0),
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                  if (_client?.buildInfo.isNotEmpty ?? false) ...[
                    const SizedBox(height: AppSpacing.xxs),
                    Tooltip(
                      message: _client!.buildInfo,
                      child: Text(
                        l10n.coreStatusBuildInfo(_client!.buildInfo),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ],
          const SizedBox(height: AppSpacing.sm),
          Row(
            children: [
              FilledButton.icon(
                onPressed: _state == _CoreCardState.connecting
                    ? null
                    : _pingCore,
                icon: const Icon(Icons.wifi_tethering_rounded, size: 18),
                label: Text(
                  _state == _CoreCardState.connecting
                      ? l10n.coreStatusConnecting
                      : l10n.coreStatusPing,
                ),
              ),
              if (_details != null) ...[
                const SizedBox(width: AppSpacing.xs),
                Tooltip(
                  message: _details!,
                  child: Icon(
                    Icons.info_outline_rounded,
                    size: 18,
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _StatusPill extends StatelessWidget {
  const _StatusPill({required this.state, required this.l10n});

  final _CoreCardState state;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final (color, label) = switch (state) {
      _CoreCardState.idle => (tokens.textTertiary, l10n.coreStatusIdle),
      _CoreCardState.connecting => (tokens.warning, l10n.coreStatusConnecting),
      _CoreCardState.connected => (tokens.success, l10n.coreStatusConnected),
      _CoreCardState.unavailable => (tokens.danger, l10n.coreStatusUnavailable),
    };

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xxs,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: AppSpacing.xxs),
          Text(
            label,
            style: Theme.of(context).textTheme.labelMedium
                ?.copyWith(color: color),
          ),
        ],
      ),
    );
  }
}

class _MutedText extends StatelessWidget {
  const _MutedText(this.text, {required this.style, this.maxLines});

  final String text;
  final TextStyle? style;
  final int? maxLines;

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      maxLines: maxLines,
      overflow: maxLines == null ? null : TextOverflow.ellipsis,
      style: style,
    );
  }
}
