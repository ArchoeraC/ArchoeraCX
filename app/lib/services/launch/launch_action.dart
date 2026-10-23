import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/account.dart';
import '../../models/game_instance.dart';
import '../../stores/account_policy.dart';
import '../../stores/accounts.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../core/core_client.dart';
import '../core/launch_memory.dart';
import '../../widgets/common/toast.dart';

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
  // whether it is worth preferring the dedicated GPU.
  Map<String, Object?>? hardware;
  if (ref.read(appSettingsProvider).value?.gameOptimization == true) {
    try {
      hardware = await client.getSystemGpu();
    } catch (_) {
      // Probing is best effort; the launch still works without it.
    }
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
    if (!context.mounted) return;
    toast(l10n.launchStarted(result.pid), type: ToastType.success);
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
