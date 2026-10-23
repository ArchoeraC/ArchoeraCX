import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';

/// Asset keys of the launcher's fragment programs.
///
/// The keys must match the `flutter: shaders:` section in `pubspec.yaml`; the
/// build compiles each `.frag` with `impellerc`, so the asset is a program, not
/// source text.
abstract final class AppShaderAssets {
  static const String transition = 'shaders/transition.frag';
  static const String glow = 'shaders/glow.frag';
}

/// Fragment programs that are available to the widget tree.
///
/// Every program is optional on purpose: shaders are an enhancement, and a
/// backend that cannot compile them (software rasterizer, an engine without
/// runtime effects, the test harness) must leave the launcher with its plain
/// opacity/dye path instead of a broken first frame. Callers therefore always
/// handle `null` and keep their pre-shader fallback.
@immutable
class AppShaderPrograms {
  const AppShaderPrograms({this.transition, this.glow});

  /// No programs loaded (or loading failed).
  static const AppShaderPrograms empty = AppShaderPrograms();

  final ui.FragmentProgram? transition;
  final ui.FragmentProgram? glow;

  bool get isEmpty => transition == null && glow == null;
}

/// Loads [AppShaderAssets] once and exposes them to the tree.
///
/// Loading is asynchronous and deliberately non-fatal: one unreadable program
/// only disables the effect that consumes it. The scope is installed above the
/// router (see `ArchoeraApp`) so every page shares the same compiled programs.
class AppShaderScope extends StatefulWidget {
  const AppShaderScope({super.key, required this.child});

  final Widget child;

  /// Programs of the nearest scope, or [AppShaderPrograms.empty] when the app
  /// has no scope (tests, previews). Never null, so callers can read
  /// `.transition` directly.
  static AppShaderPrograms of(BuildContext context) =>
      maybeOf(context) ?? AppShaderPrograms.empty;

  static AppShaderPrograms? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<_AppShaderData>()?.programs;

  @override
  State<AppShaderScope> createState() => _AppShaderScopeState();
}

class _AppShaderScopeState extends State<AppShaderScope> {
  AppShaderPrograms _programs = AppShaderPrograms.empty;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final results = await Future.wait([
      _loadOne(AppShaderAssets.transition),
      _loadOne(AppShaderAssets.glow),
    ]);
    if (!mounted) return;
    setState(() {
      _programs = AppShaderPrograms(transition: results[0], glow: results[1]);
    });
  }

  static Future<ui.FragmentProgram?> _loadOne(String asset) async {
    try {
      return await ui.FragmentProgram.fromAsset(asset);
    } catch (error) {
      // The plain fallback is a supported state, so a missing program must not
      // take the app down. Debug builds say which asset degraded.
      if (kDebugMode) {
        debugPrint('ArchoeraCX: shader $asset unavailable ($error)');
      }
      return null;
    }
  }

  @override
  Widget build(BuildContext context) {
    return _AppShaderData(programs: _programs, child: widget.child);
  }
}

class _AppShaderData extends InheritedWidget {
  const _AppShaderData({required this.programs, required super.child});

  final AppShaderPrograms programs;

  @override
  bool updateShouldNotify(_AppShaderData oldWidget) =>
      oldWidget.programs != programs;
}
