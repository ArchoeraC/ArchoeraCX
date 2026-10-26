import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'app/desktop_integration.dart';
import 'app/licenses.dart';
import 'app/router.dart';
import 'app/single_instance.dart';
import 'services/core/core_client.dart';
import 'stores/preferences.dart';
import 'stores/providers.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // The core is opened before the widget tree so a second launch can forward
  // activation and exit before it shows a window or creates a tray icon and
  // widget tree. The primary reuses this same client below.
  final client = await _claimSingleInstance();

  registerAssetLicenses();

  // The startup page is a shell preference, so it must be read before the
  // router is built; a missing value keeps the router's own defaults.
  final startPage = await readStartPagePreference();
  runApp(
    ProviderScope(
      overrides: [
        if (client != null)
          coreClientProvider.overrideWith((ref) {
            ref.onDispose(client.close);
            return client;
          }),
        if (startPage != null)
          routerProvider.overrideWithValue(
            buildAppRouter(initialLocation: startPage),
          ),
      ],
      child: const ArchoeraApp(showLaunchAnimation: true),
    ),
  );

  // Tray icon and close-to-tray handling are optional desktop extras; the
  // integration swallows its own failures so startup never depends on them.
  unawaited(setupDesktopIntegration());
}

/// Claims the per-user single-instance lock and wires activation events.
///
/// The activation subscription is installed before the native listener starts,
/// so a second launch that races startup is not dropped. A secondary process
/// exits here; the primary returns its core client for the provider override.
/// A core that cannot be opened or probed leaves the normal widget path to
/// report the diagnostic instead of blocking startup.
Future<CoreClient?> _claimSingleInstance() async {
  CoreClient? client;
  try {
    client = CoreClient.openDefault();
    listenForSingleInstanceActivation(client.events, activateMainWindow);

    final claim = await client.claimSingleInstance();
    if (!claim.primary) {
      client.close();
      await exitSecondaryInstance();
      return null;
    }
    return client;
  } catch (error, stackTrace) {
    client?.close();
    debugPrint('single instance check failed: $error\n$stackTrace');
    return null;
  }
}
