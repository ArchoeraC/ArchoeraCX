part of '../settings_page.dart';

/// "Check for updates" row in the About panel.
///
/// The check only asks the core (`update.check`) and never downloads or
/// installs anything; the release page opens through the core too, so the UI
/// stays free of direct network access.
class _UpdateCheckRow extends ConsumerStatefulWidget {
  const _UpdateCheckRow();

  @override
  ConsumerState<_UpdateCheckRow> createState() => _UpdateCheckRowState();
}

class _UpdateCheckRowState extends ConsumerState<_UpdateCheckRow> {
  bool _checking = false;

  Future<void> _check() async {
    if (_checking) return;
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    if (client == null) {
      toast(l10n.updateCheckFailed, type: ToastType.error);
      return;
    }

    setState(() => _checking = true);
    try {
      final result = await client.checkForUpdate();
      if (!mounted) return;
      if (result.hasUpdate) {
        await _showUpdateDialog(client, result);
      } else {
        toast(l10n.updateUpToDate(result.current), type: ToastType.success);
      }
    } on CoreException catch (error) {
      if (mounted) {
        toast(
          '${l10n.updateCheckFailed} (${error.code})',
          type: ToastType.error,
        );
      }
    } catch (error) {
      if (mounted) {
        toast(
          '${l10n.updateCheckFailed} (${error.runtimeType})',
          type: ToastType.error,
        );
      }
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  /// Shows the release summary and opens the release page on request.
  Future<void> _showUpdateDialog(
    CoreClient client,
    UpdateCheckResult result,
  ) async {
    final l10n = AppLocalizations.of(context);
    final notes = result.notes?.trim();
    final openRelease = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        final tokens = dialogContext.tokens;
        return AlertDialog(
          title: Text(l10n.updateAvailableTitle),
          content: SizedBox(
            width: 420,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(l10n.updateAvailableBody(result.current, result.latest)),
                if (notes != null && notes.isNotEmpty) ...[
                  const SizedBox(height: AppSpacing.sm),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxHeight: 160),
                    child: SingleChildScrollView(
                      child: Text(
                        notes,
                        style: Theme.of(dialogContext).textTheme.bodySmall
                            ?.copyWith(color: tokens.textSecondary),
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: Text(l10n.commonClose),
            ),
            FilledButton.icon(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              icon: const Icon(AppIcons.externalLink, size: 17),
              label: Text(l10n.updateOpenRelease),
            ),
          ],
        );
      },
    );
    if (openRelease != true || !mounted) return;

    final url = result.url;
    if (url == null || url.isEmpty) {
      toast(l10n.settingsAboutOpenFailed, type: ToastType.error);
      return;
    }
    // The release URL is gated by the C++ core (scheme whitelist inside
    // `openExternalUrl`), so no Dart-side launchWebUrl wrapper is needed.
    final opened = await client.openExternalUrl(url);
    if (!opened && mounted) {
      toast(l10n.settingsAboutOpenFailed, type: ToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: _checking ? null : _check,
        borderRadius: BorderRadius.circular(tokens.radiusS),
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.xs,
          ),
          child: Row(
            children: [
              Icon(AppIcons.refresh, size: 18, color: tokens.textSecondary),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.settingsCheckUpdate,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      l10n.settingsCheckUpdateDesc,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              if (_checking)
                Tooltip(
                  message: l10n.updateChecking,
                  child: SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: tokens.accent,
                    ),
                  ),
                )
              else
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      l10n.updateCurrentVersion,
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      AppInfo.version,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textSecondary),
                    ),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }
}
