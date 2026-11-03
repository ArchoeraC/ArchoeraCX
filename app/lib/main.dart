import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'app/desktop_integration.dart';
import 'app/elevation_gate.dart';
import 'app/licenses.dart';
import 'app/router.dart';
import 'app/single_instance.dart';
import 'services/core/core_client.dart';
import 'stores/preferences.dart';
import 'stores/providers.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // The core is opened first so an elevated launch can be refused before the
  // shell, the single-instance lock or the tray exist. It also lets a second
  // launch forward activation and exit before it shows a window.
  final client = _openCore();

  // Refusing here exits without touching any instance: the gate is the only UI
  // an elevated launch ever builds.
  if (!await _allowElevatedStart(client)) {
    client?.close();
    exit(0);
  }

  final primary = client == null ? null : await _claimSingleInstance(client);

  registerAssetLicenses();

  // The startup page is a shell preference, so it must be read before the
  // router is built; a missing value keeps the router's own defaults.
  final startPage = await readStartPagePreference();
  runApp(
    ProviderScope(
      overrides: [
        if (primary != null)
          coreClientProvider.overrideWith((ref) {
            ref.onDispose(primary.close);
            return primary;
          }),
        if (startPage != null)
          routerProvider.overrideWithValue(
            buildAppRouter(initialLocation: startPage),
          ),
      ],
      child: const ArchoeraApp(),
    ),
  );

  // Tray icon and close-to-tray handling are optional desktop extras; the
  // integration swallows its own failures so startup never depends on them.
  unawaited(setupDesktopIntegration());
}

/// Shows the superuser refusal gate when the native probe reports root/admin.
///
/// Returns true when startup may continue. A missing core keeps the existing
/// diagnostic path; a failed probe is not treated as elevation so a broken core
/// cannot lock the user out.
Future<bool> _allowElevatedStart(CoreClient? client) async {
  if (client == null) return true;
  try {
    final elevation = await client.getProcessElevation();
    if (elevation == null || !elevation.elevated) return true;
    return await runElevationGate(elevation);
  } catch (error, stackTrace) {
    debugPrint('superuser probe failed: $error\n$stackTrace');
    return true;
  }
}

/// Opens the native core, or null when it is missing or rejected the ABI.
CoreClient? _openCore() {
  try {
    return CoreClient.openDefault();
  } catch (error, stackTrace) {
    debugPrint('core open failed: $error\n$stackTrace');
    return null;
  }
}

/// Claims the per-user single-instance lock and wires activation events.
///
/// The activation subscription is installed before the native listener starts,
/// so a second launch that races startup is not dropped. A secondary process
/// exits here; the primary returns its core client for the provider override.
/// A core that cannot be probed leaves the normal widget path to report the
/// diagnostic instead of blocking startup.
Future<CoreClient?> _claimSingleInstance(CoreClient client) async {
  try {
    listenForSingleInstanceActivation(client.events, activateMainWindow);

    final claim = await client.claimSingleInstance();
    if (!claim.primary) {
      client.close();
      await exitSecondaryInstance();
      return null;
    }
    return client;
  } catch (error, stackTrace) {
    client.close();
    debugPrint('single instance check failed: $error\n$stackTrace');
    return null;
  }
}
