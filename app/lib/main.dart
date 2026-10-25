import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'app/desktop_integration.dart';
import 'app/licenses.dart';
import 'app/router.dart';
import 'stores/preferences.dart';
import 'stores/providers.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  registerAssetLicenses();

  // The startup page is a shell preference, so it must be read before the
  // router is built; a missing value keeps the router's own defaults.
  final startPage = await readStartPagePreference();
  runApp(
    ProviderScope(
      overrides: [
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
