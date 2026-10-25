import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/game_instance.dart';
import '../services/core/core_client.dart';
import '../services/core/java/java_runtime_plan.dart';
import '../services/core/packs/pack_install_result.dart';
import '../services/core/resources/resource_models.dart';
import '../services/core/downloads/task_update.dart';
import '../widgets/common/pack_safety.dart';
import '../widgets/common/toast.dart';
import 'instances.dart';
import 'preferences.dart';
import 'providers.dart';
import 'settings.dart';
import 'tasks.dart';

/// Methods that actually move bytes to disk.
///
/// The download page surfaces these in its queue strip; planning/bookkeeping
/// calls (`version.plan`, `loader.check` …) stay in the task center only.
const _downloadMethods = <String>{
  'download.execute',
  'modpack.install',
  'modpack.installLocal',
};

bool isDownloadTaskMethod(String method) => _downloadMethods.contains(method);

/// Running transfers, in task-center order.
final activeDownloadsProvider = Provider<List<TaskUpdate>>((ref) {
  final tasks = ref.watch(taskCenterProvider);
  return [
    for (final task in tasks)
      if (!task.isTerminal && isDownloadTaskMethod(task.method)) task,
  ];
});

/// Loader facet Modrinth expects for an instance.
String loaderSlug(GameLoader loader) => switch (loader) {
  GameLoader.fabric => 'fabric',
  GameLoader.forge => 'forge',
  GameLoader.neoforge => 'neoforge',
  GameLoader.quilt => 'quilt',
  _ => 'fabric',
};

/// Starts installs in the background.
///
/// Every entry point returns as soon as the native task exists; progress,
/// cancellation and history belong to the task center (`taskCenterProvider`).
/// Completion and failure are reported with toasts, so no page ever has to hold
/// a blocking dialog open.
final downloadsProvider = NotifierProvider<DownloadsNotifier, void>(
  DownloadsNotifier.new,
);

class DownloadsNotifier extends Notifier<void> {
  @override
  void build() {}

  /// Downloads the game files for [instance].
  ///
  /// Fabric/Quilt need a loader profile before the game is launchable, so the
  /// profile plan and the vanilla files are installed together. Forge/NeoForge
  /// profiles still come from the loader installer dialog; this method only
  /// fills the vanilla files and must not overwrite their merged profile with
  /// the plain vanilla JSON.
  Future<void> installGameFiles(
    GameInstance instance, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    if (instance.loader == GameLoader.fabric ||
        instance.loader == GameLoader.quilt) {
      await _installFabricLikeGameFiles(
        instance,
        client,
        successMessage: successMessage,
        failureMessage: failureMessage,
        cancelledMessage: cancelledMessage,
      );
      return;
    }

    final plan = await client.planVanillaInstall(
      root: instance.rootPath,
      versionId: instance.gameVersion,
      label: instance.gameVersion,
    );

    _detach(
      () async {
        final result = await client.executeDownload(
          plan.plan,
          label: '${instance.name} · ${instance.gameVersion}',
        );
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Game download failed (${result.status}).',
          );
        }
        // A loader instance launches from its merged profile. Writing the
        // vanilla JSON over it (the instance root can be versions/<game>) is
        // what leaves the next launch with a mismatched loader profile.
        if (instance.loader == GameLoader.vanilla) {
          await client.commitVanillaVersion(
            root: instance.rootPath,
            versionId: plan.versionId,
            versionJson: plan.versionJson,
            label: instance.gameVersion,
          );
        }
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Fabric/Quilt: fetch the loader profile, merge it with vanilla, then run
  /// one plan for the client, libraries and assets.
  Future<void> _installFabricLikeGameFiles(
    GameInstance instance,
    CoreClient client, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final shareLoaderFiles =
        ref.read(appSettingsProvider).value?.shareLoaderFiles ?? true;
    final label = '${instance.name} · ${instance.gameVersion}';
    final plan = await client.planLoaderInstall(
      root: instance.rootPath,
      kind: instance.loader.name,
      gameVersion: instance.gameVersion,
      loaderVersion: instance.loaderVersion,
      shareLoaderFiles: shareLoaderFiles,
      label: label,
    );

    _detach(
      () async {
        final result = await client.executeDownload(plan.plan, label: label);
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Game download failed (${result.status}).',
          );
        }
        await client.commitLoaderVersion(
          root: plan.installRoot.isEmpty ? instance.rootPath : plan.installRoot,
          versionId: plan.versionId,
          versionJson: plan.versionJson,
          label: instance.gameVersion,
        );
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Mod, resource pack or shader into [instance] (dependencies included).
  Future<void> installResource({
    required ResourceProject project,
    required ResourceVersion version,
    required GameInstance instance,
    required String kind,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    final plan = await client.planModrinthDownload(
      projectId: project.id,
      versionId: version.id,
      kind: kind,
      gameVersion: instance.gameVersion,
      loader: loaderSlug(instance.loader),
      shaderLoader: kind == 'shader' ? 'iris' : null,
      includeDependencies: true,
      root: instance.rootPath,
    );

    _detach(
      () async {
        final result = await client.executeDownload(plan, label: project.name);
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Download failed (${result.status}).',
          );
        }
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Modrinth modpack: the backend creates the instance, installs the loader
  /// and merges overrides. The new instance is selected when it lands.
  Future<void> installModpackFromModrinth({
    required String projectId,
    required String versionId,
    required String label,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    _detach(
      () async {
        final result = await client.installModpackFromModrinth(
          projectId: projectId,
          versionId: versionId,
          label: label,
        );
        await ref.read(instancesProvider.notifier).sync();
        ref
            .read(selectedInstanceIdProvider.notifier)
            .select(result.instance.id);
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Local `.mrpack` import: inspect first so an unreadable or invalid file
  /// fails before a queue entry appears, then install in the background.
  ///
  /// Returns the inspection so the caller can name the pack in its "queued"
  /// toast, or null when the file was rejected (already reported).
  Future<PackInspection?> installModpackFromFile(
    String path, {
    required String label,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    required String inspectFailedMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    final client = _client();
    final PackInspection inspection;
    try {
      inspection = await client.inspectModpack(path, label: label);
    } catch (error) {
      if (error is CoreException && isDestructivePackCode(error.code)) {
        final shown = await showPackBlockedDialogFromRouter(
          code: error.code,
          detail: error.message,
        );
        if (shown) return null;
      }
      final described = error is CoreException
          ? describeError?.call(error)
          : null;
      toast(
        described ?? '$inspectFailedMessage\n$error',
        type: ToastType.error,
      );
      return null;
    }

    _detach(
      () async {
        final result = await client.installModpackLocal(
          path: path,
          label: label,
        );
        await ref.read(instancesProvider.notifier).sync();
        ref
            .read(selectedInstanceIdProvider.notifier)
            .select(result.instance.id);
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
      describeError: describeError,
    );

    return inspection;
  }

  /// Forge/NeoForge loader install into [instance].
  ///
  /// Java selection and plan construction are awaited so a missing runtime or
  /// an unusable installer still fails while the caller can explain it; the
  /// transfer, the version commit and the processor repair then run as native
  /// tasks the task center owns.
  Future<void> installLoader({
    required GameInstance instance,
    required String kind,
    required String gameVersion,
    required String loaderVersion,
    required String installerPath,
    bool? shareLoaderFiles,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    final client = _client();
    final label = '${kind[0].toUpperCase()}${kind.substring(1)} $loaderVersion';
    final share =
        shareLoaderFiles ??
        ref.read(appSettingsProvider).value?.shareLoaderFiles ??
        true;

    // Forge/NeoForge processors need a JVM; fail before the large download
    // when no suitable runtime is installed.
    final java = await client.selectJava(gameVersion: gameVersion);
    final plan = await client.planLoaderInstall(
      root: instance.rootPath,
      installerPath: installerPath,
      kind: kind,
      gameVersion: gameVersion,
      loaderVersion: loaderVersion,
      shareLoaderFiles: share,
      label: label,
    );
    // Commit and repair follow the plan: the shared game root, or the
    // instance itself when sharing is off.
    final installRoot = plan.installRoot.isEmpty
        ? instance.rootPath
        : plan.installRoot;

    _detach(
      () async {
        final result = await client.executeDownload(plan.plan, label: label);
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Loader download failed (${result.status}).',
          );
        }
        await client.commitLoaderVersion(
          root: installRoot,
          versionId: plan.versionId,
          versionJson: plan.versionJson,
          label: label,
        );
        if (plan.requiresJava) {
          final generated = plan.installerPath;
          if (generated == null || generated.isEmpty) {
            throw StateError('The loader plan produced no installer to run.');
          }
          final repair = await client.repairLoader(
            installerPath: generated,
            root: installRoot,
            kind: kind,
            javaPath: java.path,
            label: label,
          );
          if (repair['ok'] != true) {
            final error = repair['error'];
            final detail = error is Map ? error['message']?.toString() : null;
            throw StateError(detail ?? 'Loader processor repair failed.');
          }
        }
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
      describeError: describeError,
    );
  }

  /// Copies [instance] into a new folder under [name], in the background.
  ///
  /// A duplicate walks the whole instance directory, which is minutes of work
  /// for a large pack, so it runs as a native task instead of holding a dialog
  /// open.
  Future<void> duplicateInstance(
    GameInstance instance, {
    required String name,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    final client = _client();
    _detach(
      () async {
        await client.duplicateInstance(
          id: instance.id,
          name: name,
          label: name,
        );
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
      describeError: describeError,
    );
  }

  /// Repairs an existing Forge/NeoForge instance (missing downloads plus a
  /// loader processor rerun) without holding a dialog open.
  Future<void> repairInstance(
    GameInstance instance, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    final client = _client();
    _detach(
      () async {
        await client.repairInstanceDependencies(
          instance.id,
          label: instance.name,
        );
        await ref.read(instancesProvider.notifier).reload();
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
      describeError: describeError,
    );
  }

  /// Installs a Mojang Java runtime (e.g. Java 21) under the data root.
  ///
  /// The plan is built first so a missing component fails before the UI claims
  /// the download started; the transfer itself runs in the background.
  Future<void> installJavaRuntime(
    int major, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
  }) async {
    final client = _client();
    JavaRuntimePlan plan;
    try {
      plan = await client.planJavaRuntime(major: major);
    } catch (error) {
      toast('$failureMessage\n$error', type: ToastType.error);
      return;
    }

    await _runDetached(
      () async {
        final result = await client.executeDownload(
          plan.plan,
          label: 'Java $major',
        );
        if (!result.ok) {
          throw StateError(
            result.stderr?.trim().isNotEmpty == true
                ? result.stderr!.trim()
                : 'Java runtime download failed (${result.status}).',
          );
        }
      },
      successMessage: successMessage,
      failureMessage: failureMessage,
      cancelledMessage: cancelledMessage,
    );
  }

  /// Runs [job] to completion without anyone awaiting it.
  ///
  /// `unawaited` keeps the analyzer honest: the task center owns the live
  /// state, so losing the future is intentional.
  void _detach(
    Future<void> Function() job, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) {
    unawaited(
      _runDetached(
        job,
        successMessage: successMessage,
        failureMessage: failureMessage,
        cancelledMessage: cancelledMessage,
        describeError: describeError,
      ),
    );
  }

  /// Same as [_detach] but the caller may await completion (for follow-up
  /// work such as rescanning for the newly installed runtime).
  ///
  /// [describeError] lets a caller map backend codes (`PACK_LOADER_UNSUPPORTED`
  /// …) to a localized sentence; returning null falls back to the generic
  /// failure message plus the raw error.
  Future<void> _runDetached(
    Future<void> Function() job, {
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) {
    return (() async {
      try {
        await job();
        toast(successMessage, type: ToastType.success);
        _notifyDesktop(successMessage);
      } on CoreException catch (error) {
        if (error.code == 'E_CANCELLED') {
          toast(cancelledMessage, type: ToastType.info);
          return;
        }
        if (isDestructivePackCode(error.code)) {
          final shown = await showPackBlockedDialogFromRouter(
            code: error.code,
            detail: error.message,
          );
          if (shown) return;
        }
        toast(
          describeError?.call(error) ?? '$failureMessage\n$error',
          type: ToastType.error,
        );
      } catch (error) {
        toast('$failureMessage\n$error', type: ToastType.error);
      }
    })();
  }

  /// Optional desktop notification; the user can switch it off in Settings.
  void _notifyDesktop(String body) {
    if (!ref.read(preferencesProvider).downloadNotifications) return;
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    unawaited(client.showSystemNotification(title: 'ArchoeraCX', body: body));
  }

  CoreClient _client() {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError('The native core is not connected.');
    }
    return client;
  }
}
