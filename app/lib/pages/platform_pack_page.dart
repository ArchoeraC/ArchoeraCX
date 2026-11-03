import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/core_transport.dart';
import '../services/core/packs/platform_pack_models.dart';
import '../stores/downloads.dart';
import '../stores/providers.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/confirm.dart';
import '../widgets/common/toast.dart';

/// Full-page version picker for one platform pack.
///
/// It mirrors the Modrinth resource detail page instead of opening a dialog,
/// so the catalogue remains mounted underneath and desktop navigation behaves
/// consistently across sources.
class PlatformPackPage extends ConsumerStatefulWidget {
  const PlatformPackPage({
    super.key,
    required this.provider,
    required this.packId,
    required this.name,
    this.summary = '',
    this.pack,
  });

  final String provider;
  final String packId;
  final String name;
  final String summary;
  final PlatformPackSummary? pack;

  @override
  ConsumerState<PlatformPackPage> createState() => _PlatformPackPageState();
}

class _PlatformPackPageState extends ConsumerState<PlatformPackPage> {
  List<PlatformPackVersion> _versions = const [];
  bool _loading = true;
  String? _error;

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
      final versions = await client.platformPackVersions(
        provider: widget.provider,
        packId: widget.packId,
      );
      if (!mounted) return;
      setState(() {
        _versions = versions;
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

  Future<void> _install(PlatformPackVersion version) async {
    final l10n = AppLocalizations.of(context);
    if (version.directArchive) {
      final confirmed = await confirmDestructive(
        context,
        l10n,
        title: l10n.platformDirectArchiveTitle,
        body: l10n.platformDirectArchiveWarning,
        confirmLabel: l10n.platformDirectArchiveConfirm,
      );
      if (confirmed != true || !mounted) return;
    }

    ref
        .read(downloadsProvider.notifier)
        .installModpackFromPlatform(
          provider: widget.provider,
          packId: widget.packId,
          versionId: version.id,
          label: '${widget.name} · ${version.name}',
          successMessage: l10n.packInstallDone,
          failureMessage: l10n.packInstallFailed,
          cancelledMessage: l10n.downloadCancelled(widget.name),
          describeError: (error) => switch (error) {
            CoreException(
              code: 'PLATFORM_UNSUPPORTED' ||
                  'PACK_UNSUPPORTED' ||
                  'PACK_ARCHIVE_INVALID' ||
                  'PACK_ARCHIVE_BOMB' ||
                  'PACK_SYMLINK_ENTRY' ||
                  'PACK_PATH_ESCAPE' ||
                  'PACK_TOO_LARGE' ||
                  'PACK_DUPLICATE_FILE',
            ) =>
              l10n.platformInstallUnsupported,
            CoreException(
              code: 'PLATFORM_METADATA_INVALID' ||
                  'PACK_LOADER_METADATA_FAILED',
            ) =>
              l10n.platformInstallMetadataFailed,
            CoreException(code: 'PLATFORM_NETWORK_FAILED') =>
              l10n.platformInstallNetworkFailed,
            CoreException(code: 'PACK_LOADER_UNSUPPORTED') =>
              l10n.packLoaderUnsupported,
            CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
              l10n.packJavaMissing,
            _ => null,
          },
        );
    toast(l10n.downloadQueued(widget.name));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final summary = widget.summary.trim().split('\n').first;
    final subtitle = summary.length > 180
        ? '${summary.substring(0, 180)}…'
        : summary;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AppPageHeader(
          title: widget.name,
          subtitle: subtitle.isEmpty ? null : subtitle,
          actions: [
            TextButton.icon(
              onPressed: () => context.pop(),
              icon: const Icon(AppIcons.back, size: 16),
              label: Text(l10n.back),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator())
              : _error != null
              ? AppEmptyState(
                  icon: AppIcons.error,
                  title: l10n.platformLoadFailed,
                  message: _error!,
                  primaryAction: FilledButton(
                    onPressed: _load,
                    child: Text(l10n.refresh),
                  ),
                )
              : _versions.isEmpty
              ? AppEmptyState(
                  icon: AppIcons.modpack,
                  title: l10n.platformVersionsEmpty,
                  message: l10n.platformEmptyHint,
                )
              : ListView.separated(
                  itemCount: _versions.length,
                  separatorBuilder: (_, _) =>
                      Divider(height: 1, color: tokens.border),
                  itemBuilder: (context, index) {
                    final version = _versions[index];
                    final loader = version.loaderVersion.isEmpty
                        ? version.loader
                        : '${version.loader} ${version.loaderVersion}';
                    return ListTile(
                      title: Text(version.name),
                      subtitle: Text(
                        [
                          if (version.gameVersion.isNotEmpty)
                            version.gameVersion,
                          if (loader.isNotEmpty && loader != 'unknown') loader,
                          if (version.directArchive)
                            l10n.platformDirectArchiveBadge,
                        ].join(' · '),
                      ),
                      trailing: FilledButton(
                        onPressed: () => _install(version),
                        child: Text(l10n.platformInstall),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}
