import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/packs/platform_pack_models.dart';
import '../../stores/providers.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_empty_state.dart';
import '../common/app_network_image.dart';

/// Download-page catalogue for platforms with a public, key-free API.
class PlatformPacksPanel extends ConsumerStatefulWidget {
  const PlatformPacksPanel({super.key, required this.provider});

  /// Platform id (`ftb`, `technic`, `atlauncher`), chosen by the page source
  /// selector rather than a second dropdown inside the panel.
  final String provider;

  @override
  ConsumerState<PlatformPacksPanel> createState() => _PlatformPacksPanelState();
}

class _PlatformPacksPanelState extends ConsumerState<PlatformPacksPanel> {
  final _query = TextEditingController();
  List<PlatformPackSummary> _items = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void didUpdateWidget(covariant PlatformPacksPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.provider != widget.provider) {
      _items = const [];
      _load();
    }
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
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
      final result = await client.searchPlatformPacks(
        provider: widget.provider,
        query: _query.text.trim(),
      );
      if (!mounted) return;
      setState(() {
        _items = result.items;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = error.toString();
      });
    }
  }

  void _openPack(PlatformPackSummary pack) {
    final location = Uri(
      path:
          '/download/platform/${Uri.encodeComponent(pack.provider)}/${Uri.encodeComponent(pack.id)}',
      queryParameters: {'name': pack.name},
    ).toString();
    unawaited(context.push(location, extra: pack));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _query,
                decoration: InputDecoration(
                  hintText: l10n.platformSearchHint,
                  prefixIcon: const Icon(AppIcons.search, size: 18),
                ),
                onSubmitted: (_) => _load(),
              ),
            ),
            const SizedBox(width: AppSpacing.xs),
            FilledButton(
              onPressed: _loading ? null : _load,
              child: Text(l10n.searchDownload),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        if (_loading)
          const Expanded(child: Center(child: CircularProgressIndicator()))
        else if (_error != null)
          Expanded(
            child: AppEmptyState(
              icon: AppIcons.error,
              title: l10n.platformLoadFailed,
              message: _error!,
              primaryAction: FilledButton(
                onPressed: _load,
                child: Text(l10n.refresh),
              ),
            ),
          )
        else if (_items.isEmpty)
          Expanded(
            child: AppEmptyState(
              icon: AppIcons.modpack,
              title: l10n.platformEmpty,
              message: l10n.platformEmptyHint,
            ),
          )
        else
          Expanded(
            child: ListView.separated(
              itemCount: _items.length,
              separatorBuilder: (_, _) =>
                  Divider(height: 1, color: tokens.border),
              itemBuilder: (context, index) {
                final pack = _items[index];
                return ListTile(
                  leading: SizedBox(
                    width: 44,
                    height: 44,
                    child: AppNetworkImage(
                      url: pack.iconUrl ?? '',
                      fit: BoxFit.cover,
                      fallbackIcon: AppIcons.modpack,
                    ),
                  ),
                  title: Text(pack.name),
                  subtitle: Text(
                    pack.summary,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  trailing: const Icon(AppIcons.chevronRight, size: 18),
                  onTap: () => _openPack(pack),
                );
              },
            ),
          ),
      ],
    );
  }
}
