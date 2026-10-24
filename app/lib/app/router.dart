import 'dart:io';

import 'package:go_router/go_router.dart';

import '../pages/accounts_page.dart';
import '../pages/dashboard_page.dart';
import '../pages/download_page.dart';
import '../pages/instances_page.dart';
import '../pages/java_page.dart';
import '../pages/settings_page.dart';
import '../pages/wiki_page.dart';
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
                    builder: (context, state) =>
                        const AppPageSurface(child: JavaPage()),
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
