import 'dart:io';

import 'package:go_router/go_router.dart';

import '../pages/accounts_page.dart';
import '../pages/dashboard_page.dart';
import '../pages/download_page.dart';
import '../pages/instances_page.dart';
import '../pages/java_page.dart';
import '../pages/settings_page.dart';
import '../pages/wiki_page.dart';
import 'shell.dart';

GoRouter buildAppRouter() {
  final configured = Platform.environment['ARCHOERACX_INITIAL_ROUTE']?.trim();
  final initialLocation = configured != null && configured.startsWith('/')
      ? configured
      : '/';

  return GoRouter(
    initialLocation: initialLocation,
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
                builder: (context, state) => const DashboardPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/instances',
                name: 'instances',
                builder: (context, state) => const InstancesPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/download',
                name: 'download',
                builder: (context, state) => const DownloadPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/accounts',
                name: 'accounts',
                builder: (context, state) => const AccountsPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/wiki',
                name: 'wiki',
                builder: (context, state) => const WikiPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/settings',
                name: 'settings',
                builder: (context, state) => const SettingsPage(),
                routes: [
                  GoRoute(
                    path: 'java',
                    name: 'settings-java',
                    builder: (context, state) => const JavaPage(),
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
