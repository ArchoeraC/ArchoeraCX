import 'dart:ffi' as ffi;
import 'dart:io';

import 'accounts/accounts.dart';
import 'downloads/downloads.dart';
import 'instances/instances.dart';
import 'java/java.dart';
import 'launch/launch.dart';
import 'loaders/loader.dart';
import 'packs/modpack.dart';
import 'resources/resource.dart';
import 'settings/settings.dart';
import 'system/system.dart';
import 'system/ping.dart';
import 'versions/versions.dart';
import 'wiki/wiki.dart';
import 'core_transport.dart';

export 'accounts/accounts.dart';
export 'downloads/downloads.dart';
export 'instances/instances.dart';
export 'java/java.dart';
export 'launch/launch.dart';
export 'loaders/loader.dart';
export 'packs/modpack.dart';
export 'resources/resource.dart';
export 'settings/settings.dart';
export 'system/system.dart';
export 'system/ping.dart';
export 'versions/versions.dart';
export 'wiki/wiki.dart';
export 'core_transport.dart';

/// Event-driven Dart binding for the minimal `archoera_core` ABI.
///
/// This file is the front door of `services/core`: the class composes the FFI
/// engine ([CoreTransport]) with the domain mixins (`CoreClientJava`,
/// `CoreClientLaunch`, …) living in feature subfolders (`java/`, `launch/`,
/// `loaders/`, …). Every domain method is a real instance member of
/// [CoreClient], so callers can keep importing this single path and none of
/// the domain files. Decomposition rules:
///
/// - FFI binding, library lookup, the `runAsync` task engine, envelope
///   decoding and the `as*` coercion helpers live in [CoreTransport]
///   (`core_transport.dart`) — that file also owns [CoreException],
///   [CoreUnavailableException], [CoreStatusCodes], `noChange` and
///   `CoreDetailedProgress`.
/// - One domain = one feature folder with a `mixin CoreClientXxx on
///   CoreTransport`; the file owns its private envelope parsers and its
///   domain's model types.
/// - [EventPump] wakeups still drive installation, task and progress events;
///   there is no timer or polling loop anywhere in this binding.
final class CoreClient extends CoreTransport
    with
        CoreClientAccounts,
        CoreClientDownloads,
        CoreClientInstances,
        CoreClientJava,
        CoreClientLaunch,
        CoreClientLoader,
        CoreClientModpack,
        CoreClientPing,
        CoreClientResource,
        CoreClientSettings,
        CoreClientSystem,
        CoreClientVersions,
        CoreClientWiki {
  CoreClient._(super.library);

  /// ABI version this binding was generated against.
  static const int expectedAbiVersion = CoreTransport.expectedAbiVersion;

  /// Load `archoera_core`, verify the ABI, and initialize it with an empty
  /// configuration.
  ///
  /// Resolution order:
  /// 1. `ARCHOERACX_CORE_LIB` (debug builds only; release ignores the
  ///    environment and resolves next to the executable).
  /// 2. `build/native/<library>` relative to the current directory.
  /// 3. `../app/build/native/<library>` (when launched from the repo root).
  /// 4. The Linux release bundle path; platform bundle paths are also probed.
  static CoreClient openDefault() {
    final searchedPaths = <String>[];
    String? lastLoadError;

    for (final candidate in CoreTransport.defaultLibraryCandidates()) {
      final absolutePath = CoreTransport.tryAbsolutePath(candidate);
      if (absolutePath == null) continue;
      final file = File(absolutePath);
      searchedPaths.add(absolutePath);

      if (!file.existsSync()) continue;

      ffi.DynamicLibrary library;
      try {
        library = ffi.DynamicLibrary.open(absolutePath);
      } catch (error) {
        lastLoadError = '$absolutePath: $error';
        continue;
      }

      try {
        final client = CoreClient._(library);
        client.initialize();
        return client;
      } on CoreException {
        rethrow;
      } on CoreUnavailableException {
        rethrow;
      } catch (error) {
        throw CoreUnavailableException(
          'Failed to use archoera_core from $absolutePath: $error',
          searchedPaths: searchedPaths,
        );
      }
    }

    if (lastLoadError != null) {
      throw CoreUnavailableException(
        'Failed to load libarchoera_core ($lastLoadError).',
        searchedPaths: searchedPaths,
      );
    }

    throw CoreUnavailableException(
      'libarchoera_core not found (searched ${searchedPaths.length} locations).',
      searchedPaths: searchedPaths,
    );
  }
}
