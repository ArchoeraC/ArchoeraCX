import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../stores/providers.dart';
import '../core/core_client.dart';

/// Build and start a basic offline launch. UI code only calls this helper;
/// business decisions remain in the C# launch module.
Future<void> launchInstance(
  BuildContext context,
  WidgetRef ref,
  GameInstance instance,
) async {
  final l10n = AppLocalizations.of(context);
  final client = ref.read(coreClientProvider);
  final messenger = ScaffoldMessenger.of(context);
  messenger
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        behavior: SnackBarBehavior.floating,
        content: Text(l10n.launchStarting),
        duration: const Duration(seconds: 30),
      ),
    );

  if (client == null) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(l10n.commonCoreNotConnected),
        ),
      );
    return;
  }

  try {
    final result = await client.launchInstance(instance.id);
    if (!context.mounted) return;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(l10n.launchStarted(result.pid)),
          duration: const Duration(seconds: 3),
        ),
      );
  } catch (error) {
    if (!context.mounted) return;
    final message = switch (error) {
      CoreException(code: 'ACCOUNT_REQUIRED') => l10n.launchNoAccount,
      CoreException(code: 'AUTH_REQUIRED') => l10n.launchOfflineOnly,
      CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
        l10n.launchJavaMissing,
      _ => error.toString(),
    };
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(message),
          duration: const Duration(seconds: 4),
        ),
      );
  }
}
