part of '../accounts_page.dart';

/// Microsoft sign-in dialog. It starts the authorization-code flow, opens the
/// system browser, and waits for the C# module to complete the token chain.
/// When the backend falls back to device code, the code UI is shown instead.
/// Closing it cancels the flow.
class DeviceLoginDialog extends ConsumerStatefulWidget {
  const DeviceLoginDialog({
    super.key,
    required this.client,
    this.openAuthorizationUrl,
  });

  final CoreClient client;

  /// Opens the authorization page. Defaults to the native `system.openUrl`
  /// path; tests inject a no-op so they never launch a real browser.
  final Future<bool> Function(String url)? openAuthorizationUrl;

  @override
  ConsumerState<DeviceLoginDialog> createState() => _DeviceLoginDialogState();
}

class _DeviceLoginDialogState extends ConsumerState<DeviceLoginDialog> {
  MicrosoftDeviceCodeStart? _start;
  String? _error;
  String? _flowId;
  int? _pollTaskId;
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
        unawaited(_cancelFlow(start.flowId));
        return;
      }
      setState(() {
        _start = start;
        _flowId = start.flowId;
      });
      if (_completed) return;

      // Start the poll before opening the browser and do not await the browser
      // launch. The flow must be listening even when the browser is slow, and
      // a cancel must never wait for the browser channel.
      final poll = widget.client.pollMicrosoftLogin(
        start.flowId,
        onTaskStarted: (taskId) {
          _pollTaskId = taskId;
          if (_completed) _cancelPollTask(taskId);
        },
      );
      if (start.isAuthorizationCode) {
        unawaited(_openAuthorizationUrl(start));
      }
      final account = await poll;
      if (!mounted || _completed) return;
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
      // The user may have cancelled while the poll was unwinding; `_cancel`
      // already popped this route. A second pop would remove the page below.
      if (!mounted || _completed) return;
      if (error is CoreException && error.code == 'E_CANCELLED') {
        Navigator.of(context).pop();
        return;
      }
      setState(() {
        _error = _message(error);
      });
    }
  }

  /// Opens the authorization URL in the system browser.
  ///
  /// The native `system.openUrl` work runs on a core worker, so it never
  /// blocks the UI. If the desktop portal is unavailable the URL stays visible
  /// and the user can copy it; a Flutter-side `url_launcher` fallback is
  /// deliberately not used here because its GTK call runs on the platform
  /// thread and can stall input while the browser starts.
  Future<void> _openAuthorizationUrl(MicrosoftDeviceCodeStart start) async {
    final authorizationUrl = start.authorizationUrl;
    if (authorizationUrl == null || authorizationUrl.isEmpty) return;
    var opened = false;
    try {
      final opener = widget.openAuthorizationUrl;
      opened = opener != null
          ? await opener(authorizationUrl)
          : await widget.client.openExternalUrl(authorizationUrl);
    } catch (_) {
      opened = false;
    }
    if (!mounted || _completed) return;
    setState(() => _browserLaunchFailed = !opened);
  }

  Future<void> _cancelFlow(String flowId) async {
    try {
      await widget.client.cancelMicrosoftLogin(flowId);
    } catch (_) {
      // The flow may already have completed or expired.
    }
  }

  void _cancelPollTask(int taskId) {
    try {
      widget.client.cancel(taskId);
    } catch (_) {
      // The task may already have settled; cancel is idempotent.
    }
  }

  /// Runs [action] after the current frame so the dialog close is painted
  /// before any native call. Cancellation stays local-first: the user sees the
  /// dialog disappear even if a worker thread or socket is slow to unwind.
  /// `Navigator.pop` always schedules that frame.
  void _afterFrame(void Function() action) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      try {
        action();
      } catch (_) {
        // The app may be shutting down; cleanup is best effort.
      }
    });
  }

  void _cancel() {
    if (_completed) return;
    // Close the dialog first. The native cancel request is fire-and-forget:
    // the UI must never wait for a loopback callback or a token request that
    // is already in flight.
    _completed = true;
    final flowId = _flowId;
    final pollTaskId = _pollTaskId;
    if (mounted) Navigator.of(context).pop();
    // The flow cancel is a cheap async task submission; send it even if the
    // app exits before the next frame. The native task cancel waits for the
    // frame that paints the dialog away.
    if (flowId != null) unawaited(_cancelFlow(flowId));
    if (pollTaskId != null) {
      _afterFrame(() => _cancelPollTask(pollTaskId));
    }
  }

  @override
  void dispose() {
    if (!_completed) {
      _completed = true;
      final flowId = _flowId;
      final pollTaskId = _pollTaskId;
      if (flowId != null) unawaited(_cancelFlow(flowId));
      if (pollTaskId != null) _cancelPollTask(pollTaskId);
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
          onPressed: _completed ? null : _cancel,
          child: Text(l10n.accountCancelSignIn),
        ),
      ],
    );
  }
}
