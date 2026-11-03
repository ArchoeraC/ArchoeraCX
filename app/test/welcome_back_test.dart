import 'package:archoera_cx/models/account.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/launch/welcome_back.dart';
import 'package:flutter_test/flutter_test.dart';

GameInstance _instance(String id, DateTime? lastPlayed) => GameInstance(
  id: id,
  name: id,
  gameVersion: '1.21.4',
  loader: GameLoader.vanilla,
  lastPlayed: lastPlayed,
);

Account _account(String id, AccountKind kind, {bool active = false}) =>
    Account(id: id, kind: kind, displayName: id, active: active);

void main() {
  final now = DateTime(2026, 11, 3, 12);

  group('shouldShowWelcomeBack', () {
    test('stays quiet without instances', () {
      expect(shouldShowWelcomeBack(const [], now: now), isFalse);
    });

    test('stays quiet while any instance was launched recently', () {
      final instances = [
        _instance('old', now.subtract(const Duration(days: 45))),
        _instance('fresh', now.subtract(const Duration(days: 2))),
      ];
      expect(shouldShowWelcomeBack(instances, now: now), isFalse);
    });

    test('stays quiet for an instance that was never launched', () {
      final instances = [
        _instance('old', now.subtract(const Duration(days: 45))),
        _instance('new', null),
      ];
      expect(shouldShowWelcomeBack(instances, now: now), isFalse);
    });

    test('fires when every instance is idle for more than 30 days', () {
      final instances = [
        _instance('a', now.subtract(const Duration(days: 31))),
        _instance('b', now.subtract(const Duration(days: 120))),
      ];
      expect(shouldShowWelcomeBack(instances, now: now), isTrue);
    });

    test('exactly 30 days is not "more than 30 days"', () {
      final instances = [
        _instance('a', now.subtract(const Duration(days: 30))),
      ];
      expect(shouldShowWelcomeBack(instances, now: now), isFalse);
    });
  });

  group('welcomeBackAccountName', () {
    test('prefers the current Microsoft account', () {
      final current = _account('me', AccountKind.microsoft, active: true);
      final accounts = [
        current,
        _account('other', AccountKind.microsoft),
        _account('offline', AccountKind.offline),
      ];
      expect(welcomeBackAccountName(accounts, current: current), 'me');
    });

    test('uses another Microsoft account when the current one is offline', () {
      final current = _account('local', AccountKind.offline, active: true);
      final accounts = [current, _account('ms', AccountKind.microsoft)];
      expect(welcomeBackAccountName(accounts, current: current), 'ms');
    });

    test('falls back to the current account name', () {
      final current = _account('local', AccountKind.offline, active: true);
      expect(welcomeBackAccountName([current], current: current), 'local');
    });

    test('returns null without any account', () {
      expect(welcomeBackAccountName(const []), isNull);
    });
  });
}
