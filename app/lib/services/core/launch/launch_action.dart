import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:window_manager/window_manager.dart';

import '../../../app/desktop_integration.dart';
import '../../../l10n/generated/app_localizations.dart';
import '../../../models/account.dart';
import '../../../models/game_instance.dart';
import '../../../stores/account_policy.dart';
import '../../../stores/accounts.dart';
import '../../../stores/preferences.dart';
import '../../../stores/providers.dart';
import '../core_client.dart';
import 'launch_memory.dart';
import 'launch_post_actions.dart';
import '../../../widgets/common/toast.dart';

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
  if (!policy.microsoftOnly && policy.requirePremium && !policy.hasPremium) {
    toast(l10n.accountPremiumRequired, type: ToastType.error);
    return;
  }
  final client = ref.read(coreClientProvider);

  if (client == null) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
    return;
  }

  // Snapshot the desktop launch switches before the first await: the widget
  // may be gone by the time the game starts, and the window actions below must
  // still run for the launch that was requested.
  final uiPrefs = ref.read(preferencesProvider);

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

  try {
    final result = await client.launchInstance(
      instance.id,
      mode: account?.kind == AccountKind.microsoft ? 'online' : 'offline',
      requirePremium: policy.requirePremium,
      requireMicrosoft: policy.microsoftOnly,
      hardware: hardware,
      label: instance.name,
    );
    if (context.mounted) {
      toast(l10n.launchStarted(result.pid), type: ToastType.success);
    }

    // Window actions run after the success toast and never extend the launch
    // call: hiding is best effort and the exit monitor follows the log until
    // the game process ends. They also run when the page was closed mid-launch.
    final plan = postLaunchPlan(
      succeeded: true,
      hideAfterLaunch: uiPrefs.hideAfterLaunch,
      quitAfterGameExit: uiPrefs.quitAfterGameExit,
    );
    if (plan.hideWindow || plan.monitorGameExit) {
      unawaited(_applyPostLaunchPlan(client, instance.id, plan));
    }
  } catch (error) {
    if (!context.mounted) return;
    final message = switch (error) {
      CoreException(code: 'ACCOUNT_REQUIRED') => l10n.launchNoAccount,
      CoreException(code: 'AUTH_REQUIRED') => l10n.launchOnlineSessionExpired,
      CoreException(code: 'MICROSOFT_ACCOUNT_ONLY') =>
        l10n.accountMicrosoftOnly,
      CoreException(code: 'MICROSOFT_ACCOUNT_REQUIRED') =>
        l10n.launchMicrosoftAccountRequired,
      CoreException(code: 'MICROSOFT_CLIENT_ID_MISSING') =>
        l10n.launchOnlineNotConfigured,
      CoreException(code: 'ONLINE_LAUNCH_NOT_READY') =>
        l10n.launchOnlineNotReady,
      CoreException(code: 'PREMIUM_ACCOUNT_REQUIRED') =>
        l10n.accountPremiumRequired,
      CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
        l10n.launchJavaMissing,
      CoreException(code: 'LOADER_NOT_INSTALLED') =>
        l10n.launchLoaderNotInstalled,
      CoreException(code: 'AUTO_REPAIR_FAILED') => l10n.launchAutoRepairFailed,
      CoreException(code: 'AUTO_REPAIR_BLOCKED') =>
        l10n.launchAutoRepairBlocked,
      _ => error.toString(),
    };
    toast(message, type: ToastType.error, duration: const Duration(seconds: 4));
  }
}

/// Applies the desktop window actions after a successful start.
///
/// Hiding happens first, so enabling both switches hides the launcher and only
/// quits once the game process ends. The exit monitor is never awaited here.
Future<void> _applyPostLaunchPlan(
  CoreClient client,
  String instanceId,
  PostLaunchPlan plan,
) async {
  if (plan.hideWindow) await _hideLauncherAfterStart();
  if (!plan.monitorGameExit) return;
  unawaited(quitWhenGameExits(client, instanceId));
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
  try {
    await windowManager.hide();
  } catch (error, stackTrace) {
    _logPostLaunchFailure(
      'cannot hide the launcher after launch',
      error,
      stackTrace,
    );
  }
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
