import 'dart:io';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../pages/accounts_page.dart';
import '../pages/dashboard_page.dart';
import '../pages/download_page.dart';
import '../pages/instances_page.dart';
import '../pages/java_page.dart';
import '../pages/resource_detail_page.dart';
import '../pages/settings_page.dart';
import '../pages/wiki_page.dart';
import '../services/core/resources/resource_models.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_navigator.dart';
import '../widgets/common/page_surface.dart';
import 'shell.dart';

/// Builds the app router.
///
/// [initialLocation] wins over the `ARCHOERACX_INITIAL_ROUTE` environment
/// variable, which wins over the dashboard (`/`). Invalid values fall back to
/// the dashboard so a bad preference can never leave the shell without a page.
///
/// Every page is wrapped in [AppPageSurface]: routes do not paint their own
/// background, and without a route-scoped material the pages' ink effects land
/// on the shell material and float above the next route.
GoRouter buildAppRouter({String? initialLocation}) {
  final configured =
      initialLocation ??
      Platform.environment['ARCHOERACX_INITIAL_ROUTE']?.trim();
  final location = configured != null && configured.startsWith('/')
      ? configured
      : '/';

  return GoRouter(
    navigatorKey: appNavigatorKey,
    initialLocation: location,
    routes: [
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) {
          return AppShell(navigationShell: navigationShell);
        },
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/',
                name: 'dashboard',
                builder: (context, state) =>
                    const AppPageSurface(child: DashboardPage()),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/instances',
                name: 'instances',
                builder: (context, state) =>
                    const AppPageSurface(child: InstancesPage()),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/download',
                name: 'download',
                builder: (context, state) =>
                    const AppPageSurface(child: DownloadPage()),
                routes: [
                  GoRoute(
                    path: 'project/:kind/:projectId',
                    name: 'download-project',
                    pageBuilder: (context, state) {
                      final extra = state.extra;
                      return _fadeSlidePage(
                        context,
                        state,
                        AppPageSurface(
                          child: ResourceDetailPage(
                            kind: state.pathParameters['kind'] ?? 'mod',
                            projectId: state.pathParameters['projectId'] ?? '',
                            project: extra is ResourceProject ? extra : null,
                            focusVersions:
                                state.uri.queryParameters['versions'] == '1',
                          ),
                        ),
                      );
                    },
                  ),
                ],
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/accounts',
                name: 'accounts',
                builder: (context, state) =>
                    const AppPageSurface(child: AccountsPage()),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/wiki',
                name: 'wiki',
                builder: (context, state) =>
                    const AppPageSurface(child: WikiPage()),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/settings',
                name: 'settings',
                builder: (context, state) =>
                    const AppPageSurface(child: SettingsPage()),
                routes: [
                  GoRoute(
                    path: 'java',
                    name: 'settings-java',
                    pageBuilder: (context, state) => _fadeSlidePage(
                      context,
                      state,
                      const AppPageSurface(child: JavaPage()),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ],
      ),
    ],
  );
}

/// Detail pages slide up by 8px and fade in, matching the design system's
/// page transition instead of the platform's default route animation.
///
/// Durations come from [AppTokens], which already collapse to zero when the
/// user enables "reduce motion"; the system accessibility flag is honoured the
/// same way. Only opacity and a paint transform animate, so the layout is
/// computed once and the page does not move its siblings.
Page<void> _fadeSlidePage(
  BuildContext context,
  GoRouterState state,
  Widget child,
) {
  final tokens = Theme.of(context).extension<AppTokens>();
  final reduceMotion = MediaQuery.maybeOf(context)?.disableAnimations ?? false;
  final duration = reduceMotion
      ? Duration.zero
      : tokens?.normal ?? const Duration(milliseconds: 180);
  final reverseDuration = reduceMotion
      ? Duration.zero
      : tokens?.fast ?? const Duration(milliseconds: 120);

  return CustomTransitionPage<void>(
    key: state.pageKey,
    child: child,
    transitionDuration: duration,
    reverseTransitionDuration: reverseDuration,
    transitionsBuilder: (context, animation, secondaryAnimation, child) {
      if (duration == Duration.zero) return child;
      return AnimatedBuilder(
        animation: animation,
        child: child,
        builder: (context, child) {
          final value = animation.value.clamp(0.0, 1.0);
          final curve = animation.status == AnimationStatus.reverse
              ? Curves.easeInCubic
              : Curves.easeOutCubic;
          final progress = curve.transform(value);
          return Opacity(
            key: const Key('detailRouteFade'),
            opacity: progress,
            child: Transform.translate(
              key: const Key('detailRouteSlide'),
              offset: Offset(0, 8 * (1 - progress)),
              child: child,
            ),
          );
        },
      );
    },
  );
}
