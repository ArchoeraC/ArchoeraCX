part of '../accounts_page.dart';

/// Microsoft sign-in dialog. It starts the authorization-code flow, opens the
/// system browser, and waits for the C# module to complete the token chain.
/// When the backend falls back to device code, the code UI is shown instead.
/// Closing it cancels the flow.
class DeviceLoginDialog extends ConsumerStatefulWidget {
  const DeviceLoginDialog({super.key, required this.client});

  final CoreClient client;

  @override
  ConsumerState<DeviceLoginDialog> createState() => _DeviceLoginDialogState();
}

class _DeviceLoginDialogState extends ConsumerState<DeviceLoginDialog> {
  MicrosoftDeviceCodeStart? _start;
  String? _error;
  String? _flowId;
  bool _busy = true;
  bool _completed = false;
  bool _browserLaunchFailed = false;

  @override
  void initState() {
    super.initState();
    unawaited(_begin());
  }

  Future<void> _begin() async {
    try {
      final start = await widget.client.startMicrosoftLogin(
        flow: 'authorization_code',
      );
      if (!mounted) {
        await _cancelFlow(start.flowId);
        return;
      }
      setState(() {
        _start = start;
        _flowId = start.flowId;
        _busy = false;
      });
      if (start.isAuthorizationCode) {
        await _openAuthorizationUrl(start);
      }
      final account = await widget.client.pollMicrosoftLogin(start.flowId);
      if (!mounted) return;
      final l10n = AppLocalizations.of(context);
      if (ref.read(preferencesProvider).desktopNotifications) {
        unawaited(
          widget.client.showSystemNotification(
            title: l10n.accountLoginSuccessNotificationTitle,
            body: l10n.accountLoginSuccessNotificationBody(account.displayName),
          ),
        );
      }
      _completed = true;
      Navigator.of(context).pop(account);
    } catch (error) {
      if (!mounted) return;
      if (error is CoreException && error.code == 'E_CANCELLED') {
        Navigator.of(context).pop();
        return;
      }
      setState(() {
        _busy = false;
        _error = _message(error);
      });
    }
  }

  /// Opens the authorization URL in the system browser. Failures are shown in
  /// the dialog instead of thrown, and the URL stays visible for manual use.
  Future<void> _openAuthorizationUrl(MicrosoftDeviceCodeStart start) async {
    final authorizationUrl = start.authorizationUrl;
    if (authorizationUrl == null || authorizationUrl.isEmpty) return;
    var opened = false;
    try {
      opened = await widget.client.openExternalUrl(authorizationUrl);
    } catch (_) {
      opened = false;
    }
    if (!opened) {
      // Older cores or an unavailable desktop portal fall back to Flutter's
      // system-browser bridge instead of leaving the user without a browser.
      try {
        opened = await launchWebUrl(
          authorizationUrl,
          mode: LaunchMode.externalApplication,
        );
      } catch (_) {
        opened = false;
      }
    }
    if (!mounted) return;
    setState(() => _browserLaunchFailed = !opened);
  }

  Future<void> _cancelFlow(String flowId) async {
    try {
      await widget.client.cancelMicrosoftLogin(flowId);
    } catch (_) {
      // The flow may already have completed or expired.
    }
  }

  Future<void> _cancel() async {
    _completed = true;
    final flowId = _flowId;
    if (flowId != null) {
      await _cancelFlow(flowId);
    }
    if (mounted) Navigator.of(context).pop();
  }

  @override
  void dispose() {
    if (!_completed && _flowId != null) {
      unawaited(_cancelFlow(_flowId!));
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final start = _start;

    return AlertDialog(
      title: Text(l10n.accountMicrosoftTitle),
      content: SizedBox(
        width: 380,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (start == null && _error == null)
              Row(
                children: [
                  const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(child: Text(l10n.accountDeviceCodeWaiting)),
                ],
              )
            else if (start != null && start.isAuthorizationCode) ...[
              Text(
                l10n.accountBrowserSignInHint,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: AppSpacing.md),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: tokens.surface2,
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: SelectableText(
                  start.authorizationUrl ?? '',
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.accent),
                ),
              ),
              if (_browserLaunchFailed) ...[
                const SizedBox(height: AppSpacing.sm),
                Text(
                  l10n.accountBrowserOpenFailed,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              const SizedBox(height: AppSpacing.md),
              Row(
                children: [
                  const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Text(
                      l10n.accountBrowserWaitingCallback,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ] else if (start != null) ...[
              Text(
                l10n.accountDeviceCodeHint(start.verificationUri),
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: AppSpacing.md),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: tokens.surface2,
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: SelectableText(
                  start.userCode,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w500, letterSpacing: 4),
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              SelectableText(
                start.verificationUri,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.accent),
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.accountDeviceCodeExpires((start.expiresIn / 60).ceil()),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.md),
              Row(
                children: [
                  const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Text(
                      l10n.accountDeviceCodeWaiting,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                _error!,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.danger),
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (start != null && start.isAuthorizationCode)
          TextButton.icon(
            onPressed: () => _openAuthorizationUrl(start),
            icon: const Icon(AppIcons.externalLink, size: 18),
            label: Text(l10n.accountOpenBrowser),
          ),
        if (start != null && !start.isAuthorizationCode)
          TextButton.icon(
            onPressed: () async {
              await Clipboard.setData(ClipboardData(text: start.userCode));
              if (context.mounted) {
                toast(l10n.accountDeviceCodeCopied, type: ToastType.success);
              }
            },
            icon: const Icon(AppIcons.copy, size: 18),
            label: Text(l10n.commonCopy),
          ),
        TextButton(
          onPressed: _busy && _start == null ? null : _cancel,
          child: Text(l10n.accountCancelSignIn),
        ),
      ],
    );
  }
}
