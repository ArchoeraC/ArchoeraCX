import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../app/router.dart';
import '../services/core/core_client.dart';

/// Router instance owned by the app shell.
///
/// Feature/store providers will live next to this file as the app grows.
final routerProvider = Provider<GoRouter>((ref) => buildAppRouter());

/// Shared FFI client for UI features. Returns null when the native core is
/// unavailable so pages can render an inline error instead of crashing.
final coreClientProvider = Provider<CoreClient?>((ref) {
  try {
    final client = CoreClient.openDefault();
    ref.onDispose(client.close);
    return client;
  } catch (_) {
    return null;
  }
});
