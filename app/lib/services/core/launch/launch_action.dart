import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/desktop_integration.dart';
import '../../../l10n/generated/app_localizations.dart';
import '../../../models/account.dart';
import '../../../models/game_instance.dart';
import '../../../stores/account_policy.dart';
import '../../../stores/accounts.dart';
import '../../../stores/diagnostics.dart';
import '../../../stores/instances.dart';
import '../../../stores/preferences.dart';
import '../../../stores/providers.dart';
import '../../../theme/tokens.dart';
import '../core_client.dart';
import 'launch_memory.dart';
import 'launch_post_actions.dart';
import 'launch_result.dart';
import 'welcome_back.dart';
import '../../../widgets/common/toast.dart';
import '../../../widgets/launch/launch_failure_dialog.dart';
import '../../../widgets/launch/launch_progress_dialog.dart';
import 'launch_progress.dart';

/// Build and start a basic offline launch. UI code only calls this helper;
/// business decisions remain in the C# launch module.
Future<void> launchInstance(
  BuildContext context,
  WidgetRef ref,
  GameInstance instance,
) async {
  final l10n = AppLocalizations.of(context);
  final policy = ref.read(accountPolicyProvider);
  final account = ref.read(currentAccountProvider);
  if (policy.microsoftOnly && account?.kind != AccountKind.microsoft) {
    toast(
      account == null ? l10n.launchNoAccount : l10n.accountMicrosoftOnly,
      type: ToastType.error,
    );
    return;
  }
  if (!policy.microsoftOnly &&
      policy.requirePremium &&
      !policy.hasPremium &&
      account?.kind != AccountKind.authlib) {
    toast(l10n.accountPremiumRequired, type: ToastType.error);
    return;
  }
  final client = ref.read(coreClientProvider);

  if (client == null) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
    return;
  }

  // Decide before `launch.start`: a successful start records a fresh timestamp,
  // so checking afterwards would always hide the easter egg. The greeting itself
  // waits until the process is running, so it can never delay the game.
  final greetReturningPlayer = shouldShowWelcomeBack(
    ref.read(instancesProvider).value ?? const <GameInstance>[],
    now: DateTime.now(),
  );

  // Snapshot the desktop launch switches before the first await: the widget
  // may be gone by the time the game starts, and the window actions below must
  // still run for the launch that was requested.
  final uiPrefs = ref.read(preferencesProvider);
  // The C# launch module records the session's start time itself; refresh the
  // mirrored list afterwards so the "last played" label and ordering follow.
  final instances = ref.read(instancesProvider.notifier);

  // Automatic sizing can still be tight on a small machine, and a manual value
  // can sit far below what the instance needs. Warn once, then let the user
  // decide; the native module never silently clamps a manual heap.
  final memory = await _readMemory(client, instance.id);
  if (memory != null && memory.hasWarning) {
    if (!context.mounted) return;
    final proceed = await _confirmLowMemory(context, l10n, memory);
    if (proceed != true) return;
  }

  // The hardware snapshot comes from the C++ core; the launch module decides
  // whether it is worth preferring the dedicated GPU. Probe unconditionally:
  // the C# side already gates on the persisted gameOptimization value, and
  // relying on the Dart async settings cache here silently skipped offload
  // when the provider was still loading (or had a stale value).
  Map<String, Object?>? hardware;
  try {
    hardware = await client.getSystemGpu();
  } catch (_) {
    // Probing is best effort; the launch still works without it.
  }

  toast(l10n.launchStarting, duration: const Duration(seconds: 4));
  if (!context.mounted) return;

  // Show the stage checklist before the call so the first report is not lost.
  // The dialog is modal but does not block the launch task itself.
  final progress = LaunchProgressController();
  final progressReady = Completer<void>();
  final progressDialog = showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (context) {
      if (!progressReady.isCompleted) progressReady.complete();
      return LaunchProgressDialog(
        instanceName: instance.name,
        progress: progress,
      );
    },
  );
  await progressReady.future;

  try {
    final result = await client.launchInstance(
      instance.id,
      mode: account == null || account.kind == AccountKind.offline
          ? 'offline'
          : 'online',
      requirePremium:
          policy.requirePremium && account?.kind != AccountKind.authlib,
      requireMicrosoft: policy.microsoftOnly,
      hardware: hardware,
      onProgress: progress.report,
      label: instance.name,
    );
    if (context.mounted) await _dismissProgress(context, progressDialog);
    if (context.mounted) {
      toast(l10n.launchStarted(result.pid), type: ToastType.success);
    }

    // Focus mode is best effort: a desktop without a usable system API must
    // never turn a successful launch into a failure.
    if (uiPrefs.focusMode) {
      unawaited(client.setFocusMode(true));
    }

    // `launch.start` already persisted the timestamp; refresh the mirror so the
    // card immediately shows "played today" instead of the previous session.
    unawaited(instances.refresh());

    // The game is already running; this only delays the launcher window
    // actions below, never the gameplay. Hiding waits until the greeting is
    // dismissed so the easter egg is not lost behind a hidden window.
    if (greetReturningPlayer && context.mounted) {
      await _showWelcomeBack(context, ref, l10n);
    }

    // Window actions and exit diagnostics run after the success toast and never
    // extend the launch call. The crash watcher is always started: an abnormal
    // exit should be explained even when the user did not ask to quit.
    final plan = postLaunchPlan(
      succeeded: true,
      hideAfterLaunch: uiPrefs.hideAfterLaunch,
      quitAfterGameExit: uiPrefs.quitAfterGameExit,
    );
    if (plan.hideWindow) await _hideLauncherAfterStart();
    unawaited(
      _watchLaunchExit(
        ref,
        client,
        instance,
        result,
        quitWhenDone: plan.monitorGameExit,
        focusMode: uiPrefs.focusMode,
      ),
    );
  } catch (error) {
    if (context.mounted) await _dismissProgress(context, progressDialog);
    if (!context.mounted) return;
    final message = _launchErrorMessage(l10n, error);
    if (error is CoreException) {
      await showLaunchFailureDialog(
        context,
        l10n: l10n,
        message: message,
        error: error,
      );
      return;
    }
    toast(message, type: ToastType.error, duration: const Duration(seconds: 4));
  }
}

String _launchErrorMessage(
  AppLocalizations l10n,
  Object error,
) => switch (error) {
  CoreException(code: 'ACCOUNT_REQUIRED') => l10n.launchNoAccount,
  CoreException(code: 'AUTH_REQUIRED') => l10n.launchOnlineSessionExpired,
  CoreException(code: 'MICROSOFT_ACCOUNT_ONLY') => l10n.accountMicrosoftOnly,
  CoreException(code: 'MICROSOFT_ACCOUNT_REQUIRED') =>
    l10n.launchMicrosoftAccountRequired,
  CoreException(code: 'MICROSOFT_CLIENT_ID_MISSING') =>
    l10n.launchOnlineNotConfigured,
  CoreException(code: 'ONLINE_LAUNCH_NOT_READY') => l10n.launchOnlineNotReady,
  CoreException(code: 'PREMIUM_ACCOUNT_REQUIRED') =>
    l10n.accountPremiumRequired,
  CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
    l10n.launchJavaMissing,
  CoreException(code: 'LOADER_NOT_INSTALLED') => l10n.launchLoaderNotInstalled,
  CoreException(code: 'AUTO_REPAIR_FAILED') => l10n.launchAutoRepairFailed,
  CoreException(code: 'AUTO_REPAIR_BLOCKED') => l10n.launchAutoRepairBlocked,
  _ => error.toString(),
};

/// Pops the progress dialog and waits for its route to finish so the next
/// dialog (welcome back or failure) cannot stack behind it.
Future<void> _dismissProgress(BuildContext context, Future<void> dialog) async {
  if (!context.mounted) return;
  final navigator = Navigator.of(context, rootNavigator: true);
  if (navigator.canPop()) navigator.pop();
  await dialog;
}

/// Shows the "welcome back" easter egg. Names a Microsoft account first, then
/// falls back to the currently selected account.
Future<void> _showWelcomeBack(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
) async {
  final accounts = ref.read(accountsProvider).value ?? const <Account>[];
  final current = ref.read(currentAccountProvider);
  final name = welcomeBackAccountName(accounts, current: current);

  await showDialog<void>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(l10n.welcomeBackTitle),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(l10n.welcomeBackBody),
          const SizedBox(height: AppSpacing.sm),
          Text(
            name == null
                ? l10n.welcomeBackGreetingAnonymous
                : l10n.welcomeBackGreeting(name),
            style: Theme.of(context).textTheme.titleMedium,
          ),
        ],
      ),
      actions: [
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.welcomeBackContinue),
        ),
      ],
    ),
  );
}

/// Waits for the game to exit, then asks C# to analyze the artifacts.
///
/// `launch.wait` is a native task that completes on process exit; this watcher
/// therefore adds no Dart timer. The pending report is stored in a provider so
/// the shell can show it even when the launch page was disposed or the window
/// was hidden.
Future<void> _watchLaunchExit(
  WidgetRef ref,
  CoreClient client,
  GameInstance instance,
  LaunchResult result, {
  required bool quitWhenDone,
  required bool focusMode,
}) async {
  PendingCrashReportNotifier? reports;
  try {
    reports = ref.read(pendingCrashReportProvider.notifier);
  } catch (_) {
    // The provider tree is already gone; the app is closing anyway.
  }

  var fallbackQuit = false;
  try {
    LaunchExit exit;
    do {
      exit = await client.waitForLaunchExit(
        instance.id,
        logPath: result.logPath,
      );
    } while (exit.running);

    final analysis = await client.analyzeCrash(
      instance.id,
      logPath: result.logPath,
      exitCode: exit.exitCode,
    );
    if (analysis.abnormal && !quitWhenDone) {
      reports?.show(analysis);
    }
  } catch (error, stackTrace) {
    _logPostLaunchFailure('crash diagnosis failed', error, stackTrace);
    // An older core has no `launch.wait`; keep the quit-on-exit preference
    // working through the log-tail fallback, and let that watcher do the quit.
    if (quitWhenDone) {
      unawaited(quitWhenGameExits(client, instance.id));
      fallbackQuit = true;
    }
  } finally {
    if (focusMode) {
      try {
        await client.setFocusMode(false);
      } catch (error, stackTrace) {
        _logPostLaunchFailure('cannot release focus mode', error, stackTrace);
      }
    }
    if (quitWhenDone && !fallbackQuit) await quitApplication();
  }
}

Future<void> _hideLauncherAfterStart() async {
  if (kIsWeb || !isTrayAvailable) {
    // Without a tray icon a hidden window could not be restored, so keep it
    // visible instead of trapping the user.
    _logPostLaunchFailure(
      'no tray icon, keeping the launcher visible',
      'hide skipped',
      StackTrace.current,
    );
    return;
  }
  // Failures are logged by the window owner; hiding never throws.
  await hideMainWindow();
}

void _logPostLaunchFailure(
  String message,
  Object error,
  StackTrace stackTrace,
) {
  if (!kDebugMode) return;
  debugPrint('launch action: $message: $error\n$stackTrace');
}

Future<LaunchMemoryPlan?> _readMemory(
  CoreClient client,
  String instanceId,
) async {
  try {
    return await client.getLaunchMemory(instanceId);
  } catch (_) {
    // A core without `launch.memory` still launches; the warning is optional.
    return null;
  }
}

Future<bool?> _confirmLowMemory(
  BuildContext context,
  AppLocalizations l10n,
  LaunchMemoryPlan memory,
) {
  final body = switch (memory.warningCode) {
    'SYSTEM_LOW_MEMORY' => l10n.launchMemoryWarningSystem(
      memory.systemTotalMb,
      memory.maxMb,
    ),
    'OVER_SYSTEM' || 'OVER_AVAILABLE' => l10n.launchMemoryWarningOver(
      memory.maxMb,
      memory.systemAvailableMb ?? memory.systemTotalMb,
    ),
    _ => l10n.launchMemoryWarningLow(memory.maxMb, memory.recommendedMb),
  };

  return showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(l10n.launchMemoryWarningTitle),
      content: Text(body),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(l10n.launchAnyway),
        ),
      ],
    ),
  );
}
