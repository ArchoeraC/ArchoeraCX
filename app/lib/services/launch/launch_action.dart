import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/account.dart';
import '../../models/game_instance.dart';
import '../../stores/account_policy.dart';
import '../../stores/accounts.dart';
import '../../stores/providers.dart';
import '../core/core_client.dart';
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
  toast(l10n.launchStarting, duration: const Duration(seconds: 4));

  if (client == null) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
    return;
  }

  try {
    final result = await client.launchInstance(
      instance.id,
      mode: account?.kind == AccountKind.microsoft ? 'online' : 'offline',
      requirePremium: policy.requirePremium,
      requireMicrosoft: policy.microsoftOnly,
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
      CoreException(code: 'AUTO_REPAIR_FAILED') => l10n.launchAutoRepairFailed,
      CoreException(code: 'AUTO_REPAIR_BLOCKED') =>
        l10n.launchAutoRepairBlocked,
      _ => error.toString(),
    };
    toast(message, type: ToastType.error, duration: const Duration(seconds: 4));
  }
}
